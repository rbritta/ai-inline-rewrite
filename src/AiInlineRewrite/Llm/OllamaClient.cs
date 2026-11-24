using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AiInlineRewrite.Core;

namespace AiInlineRewrite.Llm;

/// <summary>
/// The local engine: a model in Ollama (http://localhost:11434 by default). Streams each version token by token
/// from /api/generate, so the bubble fills in live. Nothing leaves the machine unless the endpoint is another one.
/// </summary>
public sealed class OllamaClient : IRewriteEngine
{
    /// <summary>
    /// How long Ollama keeps the model in memory after the last request (its own default is 5 minutes). Every request
    /// renews it, so the model stays loaded while you write and frees its memory half an hour after the last pause, or
    /// at once when AIR goes to sleep or quits (<see cref="ReleaseAsync"/>).
    /// </summary>
    public const string KeepAlive = "30m";

    private static readonly HttpClient Shared = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly string _endpoint;

    public OllamaClient(string model, string endpoint = Settings.DefaultOllamaEndpoint)
        : this(model, endpoint, Shared)
    {
    }

    /// <summary>With an <see cref="HttpClient"/> of the caller's (the tests pass one with a fake handler).</summary>
    internal OllamaClient(string model, string endpoint, HttpClient http)
    {
        Model = model;
        _endpoint = endpoint.TrimEnd('/');
        _http = http;
    }

    public EngineKind Kind => EngineKind.Ollama;

    public string Model { get; }

    /// <summary>
    /// A version that came back with letters of another alphabet is asked for once more, telling the model it left
    /// English: at temperature 0 the same prompt would drift the same way. What still drifts, the app does not offer.
    /// </summary>
    public async Task<string> RewriteAsync(string sentence, Tone tone, Action<string> onDelta, CancellationToken ct)
    {
        string text = await GenerateAsync(sentence, tone, retry: false, onDelta, ct);
        return TextShape.HasForeignLetters(text) ? await GenerateAsync(sentence, tone, retry: true, onDelta, ct) : text;
    }

    private async Task<string> GenerateAsync(string sentence, Tone tone, bool retry, Action<string> onDelta, CancellationToken ct)
    {
        var body = new
        {
            model = Model,
            prompt = Prompts.For(tone, sentence, retry),
            stream = true,
            keep_alive = KeepAlive,
            options = new { temperature = 0.0, num_predict = 256 },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint + "/api/generate")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException e)
        {
            throw Unreachable(e);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                string error = await resp.Content.ReadAsStringAsync(ct);
                var state = EngineStatus.FromError((int)resp.StatusCode, error);
                throw new EngineException(state, $"Ollama answered {(int)resp.StatusCode}");
            }

            try
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var acc = new StringBuilder();
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) is not null)
                {
                    if (line.Length == 0) continue;
                    ct.ThrowIfCancellationRequested();

                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var err))
                        throw new EngineException(EngineState.Failing, err.GetString() ?? "Ollama error");
                    if (root.TryGetProperty("response", out var r))
                    {
                        acc.Append(r.GetString());
                        onDelta(TextShape.MatchPunctuation(sentence, acc.ToString()));
                    }
                    if (root.TryGetProperty("done", out var d) && d.GetBoolean())
                        break;
                }
                return TextShape.MatchPunctuation(sentence, acc.ToString());
            }
            catch (Exception e) when (e is JsonException or IOException or HttpRequestException)
            {
                // a broken stream (the server stopped mid-answer, or sent something that is not NDJSON)
                throw new EngineException(EngineState.Failing, "Ollama's answer could not be read", e);
            }
        }
    }

    /// <summary>Is Ollama up, and does it have the model? Never throws.</summary>
    public async Task<EngineState> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(_endpoint + "/api/tags", ct);
            if (!resp.IsSuccessStatusCode) return EngineState.Failing;
            return EngineStatus.FromTags(await resp.Content.ReadAsStringAsync(ct), Model);
        }
        catch (HttpRequestException e)
        {
            return Unreachable(e).State;
        }
        catch (OperationCanceledException)
        {
            return EngineState.Unknown;
        }
    }

    /// <summary>
    /// The models Ollama has (/api/tags), for the model list of the settings window; null when Ollama cannot be asked
    /// within 5 seconds. Never throws.
    /// </summary>
    public async Task<IReadOnlyList<string>?> ListModelsAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var resp = await _http.GetAsync(_endpoint + "/api/tags", cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            return EngineChoices.InstalledModels(await resp.Content.ReadAsStringAsync(cts.Token));
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Loads the model into memory, with a throwaway rewrite, so the first real one is quick. Never throws.</summary>
    public async Task PreloadAsync(CancellationToken ct)
    {
        try { await RewriteAsync("This is a test", Tone.Casual, _ => { }, ct); }
        catch (Exception e) when (e is EngineException or OperationCanceledException)
        {
            // the server may still be starting; the first rewrite reports the real state
        }
    }

    /// <summary>
    /// Asks Ollama to unload the model now (no prompt, keep_alive 0) instead of half an hour after the last request.
    /// Never throws: an Ollama that is down holds nothing.
    /// </summary>
    public async Task ReleaseAsync(CancellationToken ct)
    {
        var body = new { model = Model, keep_alive = 0 };
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(_endpoint + "/api/generate", content, ct);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
        {
            // nothing to free, or no time left to ask (AIR is quitting)
        }
    }

    private static EngineException Unreachable(HttpRequestException e) =>
        e.InnerException is SocketException or IOException || e.HttpRequestError == HttpRequestError.ConnectionError
            ? new EngineException(EngineState.NotRunning, "Ollama is not reachable", e)
            : new EngineException(EngineState.Failing, e.Message, e);
}
