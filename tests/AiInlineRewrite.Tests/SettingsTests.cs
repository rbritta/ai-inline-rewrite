using AiInlineRewrite.Core;
using Xunit;

namespace AiInlineRewrite.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "air-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    [Fact]
    public void A_fresh_install_has_the_approved_defaults()
    {
        var s = SettingsStore.Load(_dir);
        Assert.True(s.On);
        Assert.Equal([Tone.Casual, Tone.Concise, Tone.Polished], s.EnabledTones());
        Assert.Equal([Tone.Casual, Tone.Concise, Tone.Polished, Tone.Ornate, Tone.Diplomatic, Tone.Slang, Tone.Trendy], s.Tones.Select(t => t.Tone));
        Assert.Equal(BubbleLayout.Card, s.Layout);
        Assert.Equal(AppTheme.Windows, s.Theme);
        Assert.Equal(AirKey.RightCtrlTap, s.AirKey);
        Assert.Equal(AirKey.RightShiftTap, s.OffKey);
        Assert.True(s.FollowCaret);
        Assert.False(s.DiagnosticLog);
        Assert.Equal(EngineKind.BuiltIn, s.Engine); // the model inside AIR, on this PC
        Assert.Equal("qwen2.5:7b", s.OllamaModel);
        Assert.Equal("http://localhost:11434", s.OllamaEndpoint);
        Assert.Equal("claude-haiku-5-5", s.ClaudeModel);
        Assert.Equal(700, s.DebounceMs);
        Assert.False(File.Exists(Path.Combine(_dir, SettingsStore.FileName))); // nothing written until something changes
    }

    [Theory]
    [InlineData(EngineKind.Ollama, "qwen2.5:7b")]
    [InlineData(EngineKind.Claude, "claude-haiku-5-5")]
    [InlineData(EngineKind.BuiltIn, "qwen2.5-7b-instruct-q4_k_m")]
    public void The_active_model_is_the_one_of_the_engine_in_use(EngineKind engine, string model)
        => Assert.Equal(model, new Settings { Engine = engine }.ActiveModel());

    [Fact]
    public void Claude_is_chosen_by_name_and_its_model_can_change()
    {
        var s = SettingsStore.Parse("""{ "Engine": "Claude", "ClaudeModel": "claude-sonnet-5-5" }""");
        Assert.Equal(EngineKind.Claude, s.Engine);
        Assert.Equal("claude-sonnet-5-5", s.ActiveModel());
        Assert.Equal("qwen2.5:7b", s.OllamaModel); // the other engine keeps its defaults
    }

    [Fact]
    public void A_file_from_before_the_engines_loads_and_keeps_everything_else()
    {
        // Model and Endpoint were the Ollama fields before Claude came in; unknown names are skipped, not an error.
        var s = SettingsStore.Parse("""
            { "SchemaVersion": 1, "On": true, "Model": "qwen2.5:7b", "Endpoint": "http://localhost:11434",
              "DebounceMs": 500, "Layout": "Compact", "Welcomed": true }
            """);
        Assert.Equal(EngineKind.Ollama, s.Engine);
        Assert.Equal(Settings.DefaultOllamaModel, s.OllamaModel);
        Assert.Equal(Settings.DefaultOllamaEndpoint, s.OllamaEndpoint);
        Assert.Equal(500, s.DebounceMs);
        Assert.Equal(BubbleLayout.Compact, s.Layout);
        Assert.True(s.Welcomed);
    }

    [Fact]
    public void The_settings_file_never_holds_an_API_key()
    {
        string json = SettingsStore.Serialize(new Settings { Engine = EngineKind.Claude });
        Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ActiveModel", json); // derived, not saved
    }

    [Fact]
    public void An_unreadable_settings_file_is_kept_aside_and_the_defaults_used()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), "{ broken");
        var s = SettingsStore.Load(_dir);
        Assert.True(s.On);
        Assert.Equal("{ broken", File.ReadAllText(Path.Combine(_dir, SettingsStore.UnreadableFileName)));
    }

    [Fact]
    public void An_unknown_enum_name_counts_as_unreadable()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), """{ "Engine": "Gemini" }""");
        Assert.Equal(EngineKind.BuiltIn, SettingsStore.Load(_dir).Engine);
        Assert.True(File.Exists(Path.Combine(_dir, SettingsStore.UnreadableFileName)));
    }

    [Fact]
    public void Saving_and_loading_round_trips_every_setting()
    {
        var s = new Settings
        {
            On = false,
            Layout = BubbleLayout.Compact,
            Theme = AppTheme.Dark,
            AirKey = AirKey.Shortcut(KeyMods.Ctrl | KeyMods.Shift, Vk.Space),
            OffKey = AirKey.LeftShiftTap,
            FollowCaret = false,
            BubbleLeft = 10,
            BubbleTop = 20,
            Engine = EngineKind.Claude,
            OllamaModel = "llama3.1:8b",
            OllamaEndpoint = "http://gpu-box:11434",
            ClaudeModel = "claude-opus-5-5",
            DebounceMs = 900,
            DiagnosticLog = true,
            Welcomed = true,
            Tones =
            [
                new ToneSetting { Tone = Tone.Diplomatic, On = true },
                new ToneSetting { Tone = Tone.Casual, On = true },
                new ToneSetting { Tone = Tone.Concise, On = false },
                new ToneSetting { Tone = Tone.Polished, On = false },
                new ToneSetting { Tone = Tone.Ornate, On = true },
                new ToneSetting { Tone = Tone.Slang, On = false },
                new ToneSetting { Tone = Tone.Trendy, On = true },
            ],
        };
        SettingsStore.Save(_dir, s);
        var back = SettingsStore.Load(_dir);

        Assert.Equal(SettingsStore.Serialize(s), SettingsStore.Serialize(back));
        Assert.Equal([Tone.Diplomatic, Tone.Casual, Tone.Ornate, Tone.Trendy], back.EnabledTones());
        string file = File.ReadAllText(Path.Combine(_dir, SettingsStore.FileName));
        Assert.Contains("\"Compact\"", file); // enums as names
        Assert.Contains("\"Ctrl+Shift+Space\"", file); // keys too
        Assert.Contains("\"LeftShiftTap\"", file);
        Assert.Contains("\"Claude\"", file);
        Assert.False(File.Exists(Path.Combine(_dir, SettingsStore.FileName + ".tmp"))); // the temp file was moved into place
    }

    [Fact]
    public void Normalize_repairs_a_hand_edited_file()
    {
        var s = SettingsStore.Parse("""
            { "Tones": [ { "Tone": "Ornate", "On": false }, { "Tone": "Ornate", "On": true }, { "Tone": "Casual", "On": false } ],
              "DebounceMs": 5, "OllamaModel": " ", "OllamaEndpoint": "", "ClaudeModel": "  claude-sonnet-5-5  " }
            """);
        Assert.Equal([Tone.Ornate, Tone.Casual, Tone.Concise, Tone.Polished, Tone.Diplomatic, Tone.Slang, Tone.Trendy], s.Tones.Select(t => t.Tone)); // deduped, missing ones added
        Assert.Equal([Tone.Ornate], s.EnabledTones()); // none on: the first is turned on
        Assert.Equal(Settings.MinDebounceMs, s.DebounceMs);
        Assert.Equal(Settings.DefaultOllamaModel, s.OllamaModel);
        Assert.Equal(Settings.DefaultOllamaEndpoint, s.OllamaEndpoint);
        Assert.Equal("claude-sonnet-5-5", s.ClaudeModel); // trimmed
    }

    [Fact]
    public void Normalize_caps_a_long_pause_and_survives_null_tones()
    {
        var s = new Settings { DebounceMs = 60_000, Tones = null! }.Normalize();
        Assert.Equal(Settings.MaxDebounceMs, s.DebounceMs);
        Assert.Equal([Tone.Casual], s.EnabledTones()); // every tone back, off, then the first turned on
    }

    [Fact]
    public void Normalize_replaces_values_outside_the_enums()
    {
        var s = new Settings { Layout = (BubbleLayout)9, Theme = (AppTheme)9, AirKey = null!, OffKey = AirKey.Shortcut(KeyMods.Ctrl, 'C'), Engine = (EngineKind)9 }.Normalize();
        Assert.Equal(BubbleLayout.Card, s.Layout);
        Assert.Equal(AppTheme.Windows, s.Theme);
        Assert.Equal(AirKey.RightCtrlTap, s.AirKey);
        Assert.Equal(AirKey.RightShiftTap, s.OffKey);
        Assert.Equal(EngineKind.BuiltIn, s.Engine);
    }

    [Theory]
    [InlineData("""{ "AirKey": "RightCtrlTap" }""", "RightCtrlTap", "RightShiftTap")] // from before the off key was a
    [InlineData("""{ "AirKey": "RightShiftTap" }""", "RightShiftTap", "RightCtrlTap")] // setting: the key it had
    [InlineData("""{ "AirKey": "Ctrl+Shift+Space", "OffKey": "LeftShiftTap" }""", "Ctrl+Shift+Space", "LeftShiftTap")]
    [InlineData("""{ "AirKey": "Pause" }""", "RightCtrlTap", "RightShiftTap")]          // the plain keys of older versions
    [InlineData("""{ "AirKey": "Insert", "OffKey": "ScrollLock" }""", "RightCtrlTap", "RightShiftTap")]
    [InlineData("""{ "AirKey": "Pause", "OffKey": "RightCtrlTap" }""", "RightShiftTap", "RightCtrlTap")] // a tap the off key leaves free
    [InlineData("""{ "AirKey": "Ctrl+C" }""", "RightCtrlTap", "RightShiftTap")]         // refused, written by hand
    [InlineData("""{ "AirKey": 3 }""", "RightCtrlTap", "RightShiftTap")]                // not a name
    [InlineData("""{ "AirKey": "LeftCtrlTap", "OffKey": "LeftCtrlTap" }""", "LeftCtrlTap", "RightShiftTap")] // the off key on the AIR key
    public void Keys_load_by_name_and_a_key_that_cannot_be_used_falls_back_to_a_tap(string json, string air, string off)
    {
        var s = SettingsStore.Parse(json);
        Assert.Equal(air, AirKeys.Name(s.AirKey));
        Assert.Equal(off, AirKeys.Name(s.OffKey));
    }

    [Theory]
    [InlineData("""{ "On": false }""", EngineKind.Ollama)]           // written before there was a choice: Ollama's user
    [InlineData("""{ "Engine": "Ollama" }""", EngineKind.Ollama)]
    [InlineData("""{ "Engine": "Claude" }""", EngineKind.Claude)]
    [InlineData("""{ "Engine": "BuiltIn" }""", EngineKind.BuiltIn)]
    public void A_settings_file_keeps_its_engine_and_one_without_an_engine_keeps_Ollama(string json, EngineKind expected)
        => Assert.Equal(expected, SettingsStore.Parse(json).Engine);

    [Fact]
    public void Every_tone_has_a_description()
        => Assert.All(Tones.All, t => Assert.False(string.IsNullOrWhiteSpace(Tones.Describe(t))));
}
