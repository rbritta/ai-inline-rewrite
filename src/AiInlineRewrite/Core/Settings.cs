namespace AiInlineRewrite.Core;

public enum BubbleLayout
{
    /// <summary>Each version under its tone name, the bar on top.</summary>
    Card,

    /// <summary>One line per version with the tone on the right, the bar at the bottom.</summary>
    Compact,
}

public enum AppTheme
{
    /// <summary>Follows the Windows app mode and changes with it.</summary>
    Windows,
    Light,
    Dark,
}

/// <summary>The model that writes the versions.</summary>
public enum EngineKind
{
    /// <summary>A local model in Ollama, on this PC or one you choose (<see cref="Settings.OllamaEndpoint"/>).</summary>
    Ollama,

    /// <summary>Claude, through the Anthropic API; the API key comes from the environment, never from settings.json.</summary>
    Claude,
}

public sealed class ToneSetting
{
    public Tone Tone { get; set; }

    public bool On { get; set; }
}

/// <summary>
/// Everything the user can change, saved as %AppData%\AiInlineRewrite\settings.json. The engine fields, DebounceMs
/// and DiagnosticLog have no control in the settings window; they are edited in the file (README, docs/ENGINES.md).
/// </summary>
public sealed class Settings
{
    public const string DefaultOllamaModel = "qwen2.5:7b";
    public const string DefaultOllamaEndpoint = "http://localhost:11434";
    public const string DefaultClaudeModel = "claude-haiku-5-5";
    public const int DefaultDebounceMs = 700;
    public const int MinDebounceMs = 200;
    public const int MaxDebounceMs = 5000;

    public int SchemaVersion { get; set; } = 1;

    /// <summary>AIR reads what you type and writes versions. Off, only the AIR key is listened to.</summary>
    public bool On { get; set; } = true;

    /// <summary>The tones in bubble order; the checked ones are the versions, numbered 1..n.</summary>
    public List<ToneSetting> Tones { get; set; } = DefaultTones();

    public BubbleLayout Layout { get; set; } = BubbleLayout.Card;

    public AppTheme Theme { get; set; } = AppTheme.Windows;

    public AirKey AirKey { get; set; } = AirKey.RightCtrlTap;

    /// <summary>The bubble moves with the text cursor; off, it stays at <see cref="BubbleLeft"/>, <see cref="BubbleTop"/>.</summary>
    public bool FollowCaret { get; set; } = true;

    public double? BubbleLeft { get; set; }

    public double? BubbleTop { get; set; }

    /// <summary>Which model writes the versions: a local one in Ollama (the default) or Claude.</summary>
    public EngineKind Engine { get; set; } = EngineKind.Ollama;

    /// <summary>Any model pulled in Ollama (<c>ollama pull</c>).</summary>
    public string OllamaModel { get; set; } = DefaultOllamaModel;

    /// <summary>Where Ollama listens.</summary>
    public string OllamaEndpoint { get; set; } = DefaultOllamaEndpoint;

    /// <summary>A Claude model id, such as claude-haiku-5-5 (the fastest) or claude-sonnet-5-5.</summary>
    public string ClaudeModel { get; set; } = DefaultClaudeModel;

    /// <summary>How long a pause in typing has to last before AIR writes the versions.</summary>
    public int DebounceMs { get; set; } = DefaultDebounceMs;

    /// <summary>Writes app events (never what you type) to log.txt next to this file, for bug reports.</summary>
    public bool DiagnosticLog { get; set; }

    /// <summary>The first-run tip was shown.</summary>
    public bool Welcomed { get; set; }

    /// <summary>The model of the engine in use (a method, so it is not saved in the file).</summary>
    public string ActiveModel() => Engine == EngineKind.Claude ? ClaudeModel : OllamaModel;

    public static List<ToneSetting> DefaultTones() =>
        [.. Core.Tones.All.Select(t => new ToneSetting { Tone = t, On = Core.Tones.OnByDefault(t) })];

    /// <summary>The checked tones in order: version 1 is the first.</summary>
    public IReadOnlyList<Tone> EnabledTones() => [.. Tones.Where(t => t.On).Select(t => t.Tone)];

    /// <summary>
    /// Repairs what a hand edit may have left: every tone exactly once (a missing one is added off, at the end), at
    /// least one tone on, a known engine, layout, theme and key, a sane pause, and the defaults for empty strings.
    /// </summary>
    public Settings Normalize()
    {
        var seen = new HashSet<Tone>();
        var tones = new List<ToneSetting>();
        foreach (var t in Tones ?? [])
        {
            if (t is not null && Enum.IsDefined(t.Tone) && seen.Add(t.Tone)) tones.Add(new ToneSetting { Tone = t.Tone, On = t.On });
        }
        foreach (var t in Core.Tones.All)
        {
            if (seen.Add(t)) tones.Add(new ToneSetting { Tone = t, On = false });
        }
        if (!tones.Any(t => t.On)) tones[0].On = true;
        Tones = tones;

        if (!Enum.IsDefined(Layout)) Layout = BubbleLayout.Card;
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.Windows;
        if (!Enum.IsDefined(AirKey)) AirKey = AirKey.RightCtrlTap;
        if (!Enum.IsDefined(Engine)) Engine = EngineKind.Ollama;
        OllamaModel = Trimmed(OllamaModel, DefaultOllamaModel);
        OllamaEndpoint = Trimmed(OllamaEndpoint, DefaultOllamaEndpoint);
        ClaudeModel = Trimmed(ClaudeModel, DefaultClaudeModel);
        DebounceMs = Math.Clamp(DebounceMs, MinDebounceMs, MaxDebounceMs);
        return this;
    }

    private static string Trimmed(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
