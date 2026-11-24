using System.Diagnostics;
using System.IO;
using System.Text;
using AiInlineRewrite.Core;
using AiInlineRewrite.Llm;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace AiInlineRewrite.Local;

/// <summary>
/// The built-in engine: <see cref="LocalModel"/> run inside AIR's own process by llama.cpp (LLamaSharp), with no
/// Ollama and nothing sent anywhere. On a GPU through Vulkan (NVIDIA, AMD and Intel drivers ship it), else on the
/// CPU. The prompt is wrapped the way Ollama wraps it for the same model (<see cref="Prompts.Chat"/>), and decoding is
/// greedy, as Ollama's is at temperature 0, so the answers match the ones the prompts were tuned on.
/// </summary>
public sealed class BuiltInEngine : IRewriteEngine
{
    /// <summary>
    /// How long the model stays in memory after the last version, as with Ollama (<see cref="OllamaClient.KeepAlive"/>);
    /// AIR asleep or quitting frees it at once (<see cref="ReleaseAsync"/>).
    /// </summary>
    public static readonly TimeSpan KeepLoaded = TimeSpan.FromMinutes(30);

    private const int MaxTokens = 256;
    private const uint ContextSize = 4096;

    // One model in memory for the whole app, and one version at a time: a context is not shared between threads, and
    // the GPU writes one version faster than it writes two at once.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Lock ConfigLock = new();
    private static bool _configured;
    private static LLamaWeights? _weights;
    private static ModelParams? _params;
    private static string? _loadedFrom;
    private static Timer? _idle;

    private readonly Func<bool> _downloading;
    private readonly string _directory;
    private readonly Func<string?> _ollamaModels;

    /// <param name="downloading">Is the model being downloaded now? (the app's download, <see cref="ModelDownloader"/>)</param>
    public BuiltInEngine(Func<bool> downloading)
        : this(downloading, LocalModel.DefaultDirectory, () => LocalModel.OllamaModelsDirectory(Environment.GetEnvironmentVariable))
    {
    }

    public BuiltInEngine(Func<bool> downloading, string directory, Func<string?> ollamaModels)
    {
        _downloading = downloading;
        _directory = directory;
        _ollamaModels = ollamaModels;
    }

    public EngineKind Kind => EngineKind.BuiltIn;

    public string Model => LocalModel.Id;

    /// <summary>The file it loads: AIR's own download, else Ollama's copy; null when the model is not on this PC.</summary>
    public string? ModelPath => LocalModel.Find(_directory, _ollamaModels());

    public Task<EngineState> CheckAsync(CancellationToken ct) => Task.FromResult(
        _downloading() ? EngineState.Downloading : ModelPath is null ? EngineState.ModelMissing : EngineState.Ready);

    /// <summary>Loads the model and writes one throwaway version, so the first real one is quick. Never throws.</summary>
    public async Task PreloadAsync(CancellationToken ct)
    {
        try { await RewriteAsync("This is a test", Tone.Casual, _ => { }, ct); }
        catch (Exception e) when (e is EngineException or OperationCanceledException)
        {
            // the first real version reports the state
        }
    }

    /// <summary>Drops the model from memory now, once a version being written lets go of it. Never throws.</summary>
    public async Task ReleaseAsync(CancellationToken ct)
    {
        try
        {
            await Gate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try { Unload(); }
        finally { Gate.Release(); }
    }

    /// <summary>As with Ollama: a version that left English is asked for once more, saying so.</summary>
    public async Task<string> RewriteAsync(string sentence, Tone tone, Action<string> onDelta, CancellationToken ct)
    {
        string text = await GenerateAsync(sentence, tone, retry: false, onDelta, ct);
        return TextShape.HasForeignLetters(text) ? await GenerateAsync(sentence, tone, retry: true, onDelta, ct) : text;
    }

    private async Task<string> GenerateAsync(string sentence, Tone tone, bool retry, Action<string> onDelta, CancellationToken ct)
    {
        if (_downloading()) throw new EngineException(EngineState.Downloading, "The model is being downloaded");
        string path = ModelPath ?? throw new EngineException(EngineState.ModelMissing, "The model is not on this PC");

        await Gate.WaitAsync(ct);
        try
        {
            var (weights, parameters) = await LoadAsync(path, ct);
            var executor = new StatelessExecutor(weights, parameters);
            var inference = new InferenceParams
            {
                MaxTokens = MaxTokens,
                AntiPrompts = [.. Prompts.ChatStops],
                SamplingPipeline = new GreedySamplingPipeline(),
            };
            var acc = new StringBuilder();
            await foreach (string piece in executor.InferAsync(Prompts.Chat(tone, sentence, retry), inference, ct))
            {
                acc.Append(piece);
                onDelta(TextShape.MatchPunctuation(sentence, WithoutStops(acc)));
            }
            return TextShape.MatchPunctuation(sentence, WithoutStops(acc));
        }
        catch (Exception e) when (e is not (OperationCanceledException or EngineException))
        {
            Log.Write("built-in engine: " + e.GetType().Name);
            throw new EngineException(EngineState.Failing, "The built-in engine could not write the version", e);
        }
        finally
        {
            Touch();
            Gate.Release();
        }
    }

    private static string WithoutStops(StringBuilder acc)
    {
        string text = acc.ToString();
        foreach (string stop in Prompts.ChatStops)
        {
            int at = text.IndexOf(stop, StringComparison.Ordinal);
            if (at >= 0) text = text[..at];
        }
        return text;
    }

    // Loads the model once (all layers on the GPU, then fewer when the video memory is too small, then none).
    // Called inside the gate.
    private static async Task<(LLamaWeights, ModelParams)> LoadAsync(string path, CancellationToken ct)
    {
        if (_weights is not null && _params is not null && _loadedFrom == path) return (_weights, _params);
        Unload();
        Configure();
        foreach (int layers in new[] { 999, 16, 0 })
        {
            var parameters = new ModelParams(path)
            {
                ContextSize = ContextSize,
                GpuLayerCount = layers,
                Threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
                BatchSize = 512,
            };
            try
            {
                var watch = Stopwatch.StartNew();
                _weights = await LLamaWeights.LoadFromFileAsync(parameters, ct);
                _params = parameters;
                _loadedFrom = path;
                Log.Write($"built-in engine: loaded {Path.GetFileName(path)} in {watch.Elapsed.TotalSeconds:0.0} s, GPU layers {layers}");
                return (_weights, _params);
            }
            catch (Exception e) when (e is not OperationCanceledException && layers > 0)
            {
                Log.Write($"built-in engine: loading with GPU layers {layers} failed ({e.GetType().Name}), trying fewer");
            }
        }
        throw new EngineException(EngineState.Failing, "The model could not be loaded");
    }

    // The native library: Vulkan when the PC has a Vulkan device, else the CPU build for its instruction set.
    private static void Configure()
    {
        lock (ConfigLock)
        {
            if (_configured) return;
            _configured = true;
            try
            {
                // what it loaded on and its problems; not its progress dots (LLamaLogLevel.Continue) or its chatter
                NativeLibraryConfig.All.WithVulkan(true).WithAutoFallback(true).WithLogCallback((level, message) =>
                {
                    if (level is LLamaLogLevel.Warning or LLamaLogLevel.Error
                        || message.Contains("using device") || message.Contains("offloaded"))
                        Log.Write("llama.cpp: " + message.TrimEnd());
                });
            }
            catch (InvalidOperationException)
            {
                // already loaded with the same choice
            }
        }
    }

    private static void Touch()
    {
        _idle ??= new Timer(_ => UnloadIfIdle());
        _idle.Change(KeepLoaded, Timeout.InfiniteTimeSpan);
    }

    private static void UnloadIfIdle()
    {
        if (!Gate.Wait(0)) return; // writing now: the next Touch sets the timer again
        try { Unload(); }
        finally { Gate.Release(); }
    }

    private static void Unload()
    {
        if (_weights is null) return;
        _weights.Dispose();
        _weights = null;
        _params = null;
        _loadedFrom = null;
        Log.Write("built-in engine: model unloaded");
    }
}
