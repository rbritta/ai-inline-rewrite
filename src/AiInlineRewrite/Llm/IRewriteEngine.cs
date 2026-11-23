using AiInlineRewrite.Core;

namespace AiInlineRewrite.Llm;

/// <summary>
/// A model that writes the versions. The app talks only to this interface, so it never touches the engine it does
/// not use: with Claude selected no request goes to Ollama, and the other way round.
/// </summary>
public interface IRewriteEngine
{
    EngineKind Kind { get; }

    string Model { get; }

    /// <summary>
    /// Streams the <paramref name="tone"/> version of <paramref name="sentence"/>. <paramref name="onDelta"/> gets the
    /// version so far, already shaped (<see cref="TextShape.MatchPunctuation"/>). Returns the final version.
    /// </summary>
    /// <exception cref="EngineException">The engine cannot write it; the state says why.</exception>
    /// <exception cref="VersionDeclinedException">The model declined this one version.</exception>
    Task<string> RewriteAsync(string sentence, Tone tone, Action<string> onDelta, CancellationToken ct);

    /// <summary>Can it write now (reachable, a key, the model)? Costs no tokens. Never throws.</summary>
    Task<EngineState> CheckAsync(CancellationToken ct);

    /// <summary>Gets ready so the first rewrite is quick (Ollama loads the model). Never throws.</summary>
    Task PreloadAsync(CancellationToken ct);
}

/// <summary>The engine failed in a way the bubble can explain (<see cref="EngineStatus"/>).</summary>
public sealed class EngineException(EngineState state, string message, Exception? inner = null) : Exception(message, inner)
{
    public EngineState State { get; } = state;
}

/// <summary>The model declined to write this version (Claude's refusal stop reason); the other tones go on.</summary>
public sealed class VersionDeclinedException() : Exception("The model declined to write this version");

public static class RewriteEngines
{
    /// <summary>
    /// Makes the built-in engine. The app sets it: the engine runs llama.cpp, which the tests and tools/ToneCheck do not
    /// carry, so this file stays free of it.
    /// </summary>
    public static Func<IRewriteEngine>? BuiltIn { get; set; }

    /// <summary>The engine the settings choose. Only that one is created.</summary>
    public static IRewriteEngine Create(Settings settings) => settings.Engine switch
    {
        EngineKind.Claude => new ClaudeClient(settings.ClaudeModel),
        EngineKind.BuiltIn => BuiltIn?.Invoke() ?? throw new NotSupportedException("The built-in engine runs only in the app"),
        _ => new OllamaClient(settings.OllamaModel, settings.OllamaEndpoint),
    };
}
