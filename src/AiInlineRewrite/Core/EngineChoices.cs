using System.Text.Json;

namespace AiInlineRewrite.Core;

/// <summary>What the settings window offers in its Engine section: the engines and, for each, the models.</summary>
public static class EngineChoices
{
    /// <summary>The engines, as the settings window names them.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> Engines =
        [(nameof(EngineKind.Ollama), "Ollama"), (nameof(EngineKind.Claude), "Claude")];

    /// <summary>The Claude models the window offers, fastest first.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> ClaudeModels =
    [
        (Settings.DefaultClaudeModel, "Claude Haiku 5.5"),
        ("claude-sonnet-5-5", "Claude Sonnet 5.5"),
        ("claude-opus-5-5", "Claude Opus 5.5"),
    ];

    /// <summary>The Claude models, with the current one first when it is another id (typed in settings.json).</summary>
    public static IReadOnlyList<(string Value, string Label)> ClaudeOptions(string current) =>
        ClaudeModels.Any(m => m.Value == current) ? ClaudeModels : [(current, current), .. ClaudeModels];

    /// <summary>
    /// The Ollama models: the installed ones by name, with the current one first, marked, when it is not installed.
    /// Without the list (Ollama could not be asked), only the current one, as it is in the settings.
    /// </summary>
    public static IReadOnlyList<(string Value, string Label)> OllamaOptions(IReadOnlyList<string>? installed, string current)
    {
        if (installed is null) return [(current, current)];
        var options = installed
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(name => (Value: EngineStatus.SameModel(name, current) ? current : name, Label: name))
            .ToList();
        if (!installed.Any(name => EngineStatus.SameModel(name, current)))
            options.Insert(0, (current, current + " (not installed)"));
        return options;
    }

    /// <summary>The model names in an /api/tags answer of Ollama; null when it is not one.</summary>
    public static IReadOnlyList<string>? InstalledModels(string tagsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(tagsJson);
            if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
                return null;
            var names = new List<string>();
            foreach (var m in models.EnumerateArray())
            {
                if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(n.GetString()))
                    names.Add(n.GetString()!);
            }
            return names;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
