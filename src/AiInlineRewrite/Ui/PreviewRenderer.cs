using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiInlineRewrite.Core;

namespace AiInlineRewrite.Ui;

/// <summary>
/// Renders the real bubble and settings visuals to PNG files, off screen: nothing is shown and no hook is installed.
/// For reviewing the UI and for the README images: <c>AiInlineRewrite.exe --render-previews docs/img</c>.
/// </summary>
public static class PreviewRenderer
{
    private const string English = "i dont think we can finish this until friday";

    public static void RenderAll(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (bool dark in new[] { false, true })
        {
            var p = Palette.For(dark);
            string theme = dark ? "dark" : "light";
            foreach (var layout in new[] { BubbleLayout.Card, BubbleLayout.Compact })
            {
                string name = layout.ToString().ToLowerInvariant();
                Save(Bubble(p, Translated(layout)), Path.Combine(folder, $"bubble-{name}-{theme}.png"));
                Save(Bubble(p, Corrected(layout, pick: true)), Path.Combine(folder, $"bubble-{name}-pick-{theme}.png"));
            }
            Save(Bubble(p, Corrected(BubbleLayout.Card, pick: false, follow: false)), Path.Combine(folder, $"bubble-card-pinned-{theme}.png"));
            Save(Bubble(p, Writing()), Path.Combine(folder, $"bubble-card-writing-{theme}.png"));
            Save(Bubble(p, Blocked(EngineKind.Ollama, EngineState.ModelMissing, Settings.DefaultOllamaModel)), Path.Combine(folder, $"bubble-card-model-missing-{theme}.png"));
            Save(Bubble(p, Blocked(EngineKind.Claude, EngineState.NoKey, Settings.DefaultClaudeModel)), Path.Combine(folder, $"bubble-card-no-key-{theme}.png"));
            Save(SettingsContent(dark), Path.Combine(folder, $"settings-{theme}.png"));
        }
    }

    private static BubbleState Translated(BubbleLayout layout) => new()
    {
        Layout = layout,
        Rows = Samples.Rows([Tone.Casual, Tone.Concise, Tone.Polished]),
        Source = Samples.Portuguese,
        Translated = true,
        Status = "Translated from Portuguese",
    };

    private static BubbleState Corrected(BubbleLayout layout, bool pick, bool follow = true) => new()
    {
        Layout = layout,
        Rows =
        [
            new BubbleRow(Tone.Casual, "I don't think we can finish this by Friday", true),
            new BubbleRow(Tone.Concise, "we can't finish this by Friday", true),
            new BubbleRow(Tone.Polished, "I do not believe we can finish this by Friday", true),
        ],
        Source = English,
        Status = "English, corrected",
        Pick = pick,
        Selected = pick ? 1 : 0,
        PickRemainingMs = pick ? 2600 : 0,
        Follow = follow,
        Interactive = false,
    };

    private static BubbleState Writing() => new()
    {
        Rows =
        [
            new BubbleRow(Tone.Casual, "I don't think we can finish", false),
            new BubbleRow(Tone.Concise, "we can't", false),
            new BubbleRow(Tone.Polished, "I do not believe", false),
        ],
        Source = English,
        Status = "Writing…",
        Interactive = false,
    };

    // An engine state the user has to fix, with the one step that fixes it.
    private static BubbleState Blocked(EngineKind engine, EngineState state, string model) => new()
    {
        Status = EngineStatus.Status(engine, state, model),
        Hint = EngineStatus.Hint(engine, state, model),
        Interactive = false,
    };

    private static FrameworkElement Bubble(Palette p, BubbleState s) => new Border
    {
        Background = p.Desk,
        Padding = new Thickness(28),
        Child = BubbleView.Create(s, p).Root,
    };

    private static FrameworkElement SettingsContent(bool dark)
    {
        var host = new RenderHost(dark);
        var window = new SettingsWindow(host);
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.Width = window.Width; // the window's fixed width, so the help lines wrap as they do on screen
        window.Close();
        return new Border { Background = Palette.For(dark).Desk, Child = content };
    }

    private static void Save(FrameworkElement element, string path)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        const double scale = 2; // crisp at 200 %
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(element);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        png.Save(file);
    }

    private sealed class RenderHost(bool dark) : ISettingsHost
    {
        public Settings Settings { get; } = new() { Tones = Settings.DefaultTones() };
        public Palette Palette => Palette.For(dark);
        public bool WindowsIsDark => dark;
        public void SetOn(bool on) { }
        public void SetToneOn(Tone tone, bool on) { }
        public void SetOrder(IReadOnlyList<Tone> order) { }
        public void SetLayout(BubbleLayout layout) { }
        public void SetTheme(AppTheme theme) { }
        public void SetAirKey(AirKey key) { }
        public void SetFollow(bool on) { }
        public EngineState EngineState => EngineState.Ready;
        public void SetEngine(EngineKind engine) { }
        public void SetModel(string model) { }
        public Task<IReadOnlyList<string>?> InstalledModelsAsync() => Task.FromResult<IReadOnlyList<string>?>(null);
    }
}
