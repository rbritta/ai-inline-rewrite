using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Credentials;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using AiInlineRewrite.Core;

namespace AiInlineRewrite.Llm;

/// <summary>
/// The cloud engine: Claude through the Anthropic API, with the official Anthropic SDK. The sentence and the tone's
/// instructions go to Anthropic. The credential is the SDK's own: ANTHROPIC_API_KEY, else ANTHROPIC_AUTH_TOKEN, else a
/// profile in the Anthropic config folder (%APPDATA%\Anthropic, where `ant auth login` keeps one). AIR never stores or
/// logs it, and without any credential nothing is sent at all. Each version streams in like Ollama's.
/// </summary>
public sealed class ClaudeClient : IRewriteEngine
{
    /// <summary>Room for a one-sentence answer, with some to spare for the model's thinking.</summary>
    public const int MaxTokens = 1024;

    private readonly AnthropicClient _client;
    private readonly Lazy<bool> _credentialElsewhere;

    /// <summary>
    /// A client with no credential of its own, so the SDK looks for one on first use. Environment variables are the
    /// ones AIR started with, so a new key needs a restart; a profile is looked for again whenever the engine is made.
    /// </summary>
    public ClaudeClient(string model)
        : this(model, new AnthropicClient { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(30) }, ProfileOrFederation)
    {
    }

    /// <summary>
    /// With a client of the caller's (the tests pass one with a fake handler and a made-up key or none), and what
    /// tells whether the SDK will find a credential outside the client (a profile); nothing by default.
    /// </summary>
    internal ClaudeClient(string model, AnthropicClient client, Func<bool>? credentialElsewhere = null)
    {
        Model = model;
        _client = client;
        _credentialElsewhere = new Lazy<bool>(credentialElsewhere ?? (() => false));
    }

    public EngineKind Kind => EngineKind.Claude;

    public string Model { get; }

    public async Task<string> RewriteAsync(string sentence, Tone tone, Action<string> onDelta, CancellationToken ct)
    {
        // Without a credential the SDK would still send the request (and the sentence) to be refused; it stays here.
        if (!HasCredential) throw new EngineException(EngineState.NoKey, "No Anthropic credential");

        // Low effort keeps a one-sentence rewrite fast on every current model (Haiku, Sonnet, Opus); no sampling
        // parameters, which current models do not take.
        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = MaxTokens,
            System = Prompts.Instructions(tone, sentence),
            OutputConfig = new OutputConfig { Effort = Effort.Low },
            Messages = [new UserMessageParam { Content = Prompts.Input(sentence) }],
        };

        var acc = new StringBuilder();
        try
        {
            await foreach (var ev in _client.Messages.CreateStreaming(parameters, ct).WithCancellation(ct))
            {
                if (ev.TryPickContentBlockDelta(out var block) && block.Delta.TryPickText(out var text))
                {
                    acc.Append(text.Text);
                    onDelta(TextShape.MatchPunctuation(sentence, acc.ToString()));
                }
                else if (ev.TryPickDelta(out var delta) && delta.Delta.StopReason == StopReason.Refusal)
                {
                    throw new VersionDeclinedException();
                }
            }
        }
        catch (Exception e) when (Translate(e) is { } engine)
        {
            throw engine;
        }
        return TextShape.MatchPunctuation(sentence, acc.ToString());
    }

    /// <summary>
    /// Looks the model up in the Models API, which is not billed: that checks the network, the key and the model id
    /// without spending a token. Never throws.
    /// </summary>
    public async Task<EngineState> CheckAsync(CancellationToken ct)
    {
        if (!HasCredential) return EngineState.NoKey;
        try
        {
            await _client.Models.Retrieve(Model, cancellationToken: ct);
            return EngineState.Ready;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return EngineState.Unknown;
        }
        catch (Exception e) when (Translate(e) is { } engine)
        {
            return engine.State;
        }
    }

    /// <summary>Nothing to load: the model runs at Anthropic.</summary>
    public Task PreloadAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Nothing to free either: no model in this PC's memory.</summary>
    public Task ReleaseAsync(CancellationToken ct) => Task.CompletedTask;

    // What an SDK or network failure means for the bubble; null for what is not an engine failure (cancellation).
    private EngineException? Translate(Exception e) => e switch
    {
        // A profile the SDK could not use (its token file gone, a broken config): the credential, not the network.
        _ when e is FileNotFoundException or WorkloadIdentityException || e.InnerException is FileNotFoundException or WorkloadIdentityException
            => new EngineException(EngineState.KeyRefused, "The Anthropic credential could not be used", e),
        AnthropicApiException api => new EngineException(
            EngineStatus.FromClaudeStatus((int)api.StatusCode), $"Anthropic answered {(int)api.StatusCode}", e),
        AnthropicIOException or HttpRequestException or IOException
            => new EngineException(EngineState.NotRunning, "The Anthropic API is not reachable", e),
        TaskCanceledException { InnerException: TimeoutException } or TimeoutException
            => new EngineException(EngineState.Failing, "The Anthropic API did not answer in time", e),
        AnthropicException => new EngineException(EngineState.Failing, "The Anthropic API answer could not be read", e),
        _ => null,
    };

    private bool HasCredential =>
        !string.IsNullOrWhiteSpace(_client.ApiKey) || !string.IsNullOrWhiteSpace(_client.AuthToken) || _credentialElsewhere.Value;

    /// <summary>
    /// Will the SDK find a credential with no key or token set: a profile in the Anthropic config folder (the one
    /// `ant auth login` writes), or workload identity federation variables? The SDK's own lookup
    /// (<see cref="AnthropicCredentials.Resolve"/>), which reads files and variables and sends nothing. A profile that is
    /// there but broken counts: the request then says what is wrong with it.
    /// </summary>
    internal static bool ProfileOrFederation()
    {
        try
        {
            return AnthropicCredentials.Resolve() is not null;
        }
        catch (Exception e) when (e is AnthropicException or ArgumentException or IOException or JsonException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
