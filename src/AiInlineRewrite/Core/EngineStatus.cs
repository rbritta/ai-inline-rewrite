using System.Text.Json;

namespace AiInlineRewrite.Core;

public enum EngineState
{
    Unknown,
    Ready,

    /// <summary>Nothing answers: Ollama is not installed or not started, or Claude cannot be reached.</summary>
    NotRunning,

    /// <summary>The engine answers but does not know the model.</summary>
    ModelMissing,

    /// <summary>Claude only: no API key in the environment.</summary>
    NoKey,

    /// <summary>Claude only: the API key was refused (wrong, revoked, or without access).</summary>
    KeyRefused,

    /// <summary>The engine answered with an error, or is too busy to answer.</summary>
    Failing,

    /// <summary>Built-in only: the model is being downloaded; it gets ready by itself when the download ends.</summary>
    Downloading,
}

/// <summary>Turns what an engine answers into a state, and a state into the two lines the bubble shows for it.</summary>
public static class EngineStatus
{
    /// <summary>States the app cannot write in until the user fixes something; the bubble keeps showing them.</summary>
    public static bool Blocks(EngineState state) =>
        state is EngineState.NotRunning or EngineState.ModelMissing or EngineState.NoKey or EngineState.KeyRefused
            or EngineState.Downloading;

    /// <summary>Ollama: ready when the /api/tags answer lists the model ("qwen2.5" means "qwen2.5:latest").</summary>
    public static EngineState FromTags(string tagsJson, string model)
    {
        try
        {
            using var doc = JsonDocument.Parse(tagsJson);
            if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
                return EngineState.Failing;
            foreach (var m in models.EnumerateArray())
            {
                foreach (string field in new[] { "name", "model" })
                {
                    if (m.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String && SameModel(v.GetString()!, model))
                        return EngineState.Ready;
                }
            }
            return EngineState.ModelMissing;
        }
        catch (JsonException)
        {
            return EngineState.Failing;
        }
    }

    /// <summary>Ollama: an error answer of /api/generate, a missing model or something else.</summary>
    public static EngineState FromError(int statusCode, string body) =>
        statusCode == 404 || body.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? EngineState.ModelMissing
            : EngineState.Failing;

    /// <summary>
    /// Claude: the state for an HTTP status of the Anthropic API. No request goes out without a credential (that is
    /// <see cref="EngineState.NoKey"/> before any call), so a 401 or 403 means the credential was refused.
    /// </summary>
    public static EngineState FromClaudeStatus(int statusCode) => statusCode switch
    {
        401 or 403 => EngineState.KeyRefused,
        404 => EngineState.ModelMissing,
        _ => EngineState.Failing,
    };

    /// <summary>The status line in the bubble's bar.</summary>
    public static string Status(EngineKind engine, EngineState state, string model) => (engine, state) switch
    {
        (EngineKind.BuiltIn, EngineState.ModelMissing) => "The model is not on this PC yet",
        (EngineKind.BuiltIn, EngineState.Downloading) => "Downloading the model",
        (EngineKind.BuiltIn, EngineState.NotRunning or EngineState.Failing) => "The model could not run",
        (EngineKind.Claude, EngineState.NotRunning) => "Claude cannot be reached",
        (EngineKind.Claude, EngineState.ModelMissing) => $"The model {model} does not exist",
        (EngineKind.Claude, EngineState.NoKey) => "No Anthropic API key",
        (EngineKind.Claude, EngineState.KeyRefused) => "The Anthropic API key was refused",
        (EngineKind.Claude, EngineState.Failing) => "Claude is not answering",
        (_, EngineState.NotRunning) => "Ollama is not running",
        (_, EngineState.ModelMissing) => $"The model {model} is missing",
        (_, EngineState.Failing) => "Ollama is not answering",
        _ => "Ready",
    };

    /// <summary>The one step that fixes it, shown where the versions go.</summary>
    public static string Hint(EngineKind engine, EngineState state, string model) => (engine, state) switch
    {
        (EngineKind.BuiltIn, EngineState.ModelMissing) => $"Download it once in the settings ({LocalModel.DownloadSizeText}).",
        (EngineKind.BuiltIn, EngineState.Downloading) => "AIR is ready by itself when it ends; the settings show how far it is.",
        (EngineKind.BuiltIn, EngineState.NotRunning or EngineState.Failing) => "Close programs that use a lot of memory, then type again.",
        (EngineKind.Claude, EngineState.NotRunning) => "Check the internet connection, then type again.",
        (EngineKind.Claude, EngineState.ModelMissing) => "Choose another model in the settings, such as Claude Haiku 5.5.",
        (EngineKind.Claude, EngineState.NoKey) => "Set the ANTHROPIC_API_KEY environment variable, then restart AIR.",
        (EngineKind.Claude, EngineState.KeyRefused) => "Check ANTHROPIC_API_KEY in the Anthropic Console, then restart AIR.",
        (EngineKind.Claude, EngineState.Failing) => "Wait a moment and type again; if it goes on, check your Anthropic account.",
        (_, EngineState.NotRunning) => "Start Ollama (ollama.com), then type again.",
        (_, EngineState.ModelMissing) => $"Run this once in a terminal: ollama pull {model}",
        (_, EngineState.Failing) => "Check that Ollama works, then type again.",
        _ => "",
    };

    /// <summary>Ollama: the same model, case aside, with no tag meaning :latest ("llama3" is "llama3:latest").</summary>
    public static bool SameModel(string a, string b) => string.Equals(WithTag(a), WithTag(b), StringComparison.OrdinalIgnoreCase);

    private static string WithTag(string model) => model.Contains(':') ? model : model + ":latest";
}
