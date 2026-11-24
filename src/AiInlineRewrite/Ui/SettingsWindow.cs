using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using AiInlineRewrite.Core;
using AiInlineRewrite.Llm;

namespace AiInlineRewrite.Ui;

/// <summary>What the settings window changes; the app applies and saves each change.</summary>
public interface ISettingsHost
{
    Settings Settings { get; }

    /// <summary>The palette of the app's current theme.</summary>
    Palette Palette { get; }

    /// <summary>Is Windows in dark mode? (for the "Windows" theme preview).</summary>
    bool WindowsIsDark { get; }

    void SetOn(bool on);

    void SetToneOn(Tone tone, bool on);

    void SetOrder(IReadOnlyList<Tone> order);

    void SetLayout(BubbleLayout layout);

    void SetTheme(AppTheme theme);

    /// <summary>Sets the AIR key; when that was the off key, the off key takes the old AIR key.</summary>
    void SetAirKey(AirKey key);

    /// <summary>Sets the off key; when that was the AIR key, the AIR key takes the old off key.</summary>
    void SetOffKey(AirKey key);

    void SetFollow(bool on);

    /// <summary>The state of the engine in use (Unknown while it is being checked), for the Engine section.</summary>
    EngineState EngineState { get; }

    /// <summary>Switches the engine. It applies at once: the other engine is dropped and the new one checked.</summary>
    void SetEngine(EngineKind engine);

    /// <summary>Sets the model of the engine in use; it applies at once too.</summary>
    void SetModel(string model);

    /// <summary>The models Ollama has, while Ollama is the engine; null otherwise, or when Ollama cannot be asked.</summary>
    Task<IReadOnlyList<string>?> InstalledModelsAsync();

    /// <summary>How far the built-in model's download is; null when none is running.</summary>
    DownloadProgress? ModelDownload { get; }

    /// <summary>Why the last download stopped, when it failed; null otherwise.</summary>
    string? ModelDownloadError { get; }

    void StartModelDownload();

    /// <summary>Stops the download; what came in is kept, and the next start resumes from there.</summary>
    void CancelModelDownload();
}

/// <summary>The pages of the settings window, one per item of its menu.</summary>
public enum SettingsPage
{
    /// <summary>AIR on or off, the AIR key and the off key.</summary>
    General,

    /// <summary>Which tones are versions, and their order.</summary>
    Tones,

    /// <summary>Layout, theme, and following the text cursor.</summary>
    Bubble,

    /// <summary>The engine, its model, and its state.</summary>
    Engine,
}

/// <summary>
/// The settings window: a sober 40 px title bar with the loose Q, "AI Inline Rewrite · Settings" and an X; below it, a
/// menu on the left and the page it picks on the right, the same height for every page. General: AIR on/off, the AIR
/// key and the off key. Tones: check, order by the arrows or by dragging, one line each. Bubble: Layout and Theme (a
/// click floats a preview beside the window), and Follow the text cursor with its arrow. Engine: Built-in, Ollama or
/// Claude, the model, and the engine's state with the one step that fixes it (its color also marks the menu item).
/// </summary>
public sealed class SettingsWindow : Window
{
    private const double Pad = 24;
    private const double NavWidth = 144;
    private const double PageWidth = 384;

    private static readonly Thickness PageMargin = new(16, 4, 16, 16);

    private static readonly (SettingsPage Page, string Label)[] Pages =
        [(SettingsPage.General, "General"), (SettingsPage.Tones, "Tones"), (SettingsPage.Bubble, "Bubble"), (SettingsPage.Engine, "Engine")];

    private readonly ISettingsHost _host;
    private readonly (Rect Bubble, Rect Area)? _beside;
    private readonly PreviewWindow _preview = new();
    private readonly DispatcherTimer _previewTimer = new();
    private readonly Dictionary<SettingsPage, NavItem> _nav = [];
    private SettingsPage _page;
    private ScrollViewer? _scroll;
    private FrameworkElement? _previewSection;
    private bool _previewLeaving;
    private bool _placed;
    private Toggle? _onToggle;
    private Toggle? _followToggle;
    private Border? _followIconHost;
    private Border? _keysBody;
    private Ellipse? _engineDot;

    // the Engine section: filled again when the engine, the model or Ollama's model list changes; the status line
    // alone when the engine's state does
    private Border? _engineBody;
    private Border? _engineStatus;
    private IReadOnlyList<string>? _installed;
    private bool _askingModels;

    // dragging a tone row
    private StackPanel? _toneList;
    private Tone? _dragTone;
    private Point _dragFrom;
    private bool _dragMoved;

    /// <param name="host">The app: the settings to edit and what to do when they change.</param>
    /// <param name="beside">Opened from the gear: the bubble and its monitor's work area, so the window opens beside
    /// it. Null (the tray, a second launch): above the tray.</param>
    /// <param name="page">The page it opens on.</param>
    public SettingsWindow(ISettingsHost host, (Rect Bubble, Rect Area)? beside = null, SettingsPage page = SettingsPage.General)
    {
        _host = host;
        _beside = beside;
        _page = page;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = NavWidth + PageWidth + 2 * Pad;
        ShowInTaskbar = true;
        Title = "AI Inline Rewrite Settings";
        Topmost = true;
        _previewTimer.Tick += (_, _) => HidePreview();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Ctrl+Tab and Ctrl+Shift+Tab go round the pages
                e.Handled = true;
                int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                int i = Array.FindIndex(Pages, x => x.Page == _page);
                ShowPage(Pages[(i + step + Pages.Length) % Pages.Length].Page);
            }
        };
        PreviewMouseMove += (_, _) => TrackPreviewMouse();
        MouseLeave += (_, _) => TrackPreviewMouse();
        _preview.MouseMove += (_, _) => TrackPreviewMouse();
        _preview.MouseLeave += (_, _) => TrackPreviewMouse();
        LocationChanged += (_, _) => HidePreview();
        Closed += (_, _) =>
        {
            _previewTimer.Stop();
            _preview.Close();
        };
        SizeChanged += (_, _) => PlaceOnce();
        Rebuild();
    }

    /// <summary>Builds the content again (after a theme change), keeping the page and its scroll position.</summary>
    public void Rebuild()
    {
        double scroll = _scroll?.VerticalOffset ?? 0;
        HidePreview();
        var p = _host.Palette;
        MaxHeight = SystemParameters.WorkArea.Height - 24 + 2 * Pad;

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Height = PageHeight(p) };
        FillPage(p);
        var main = new DockPanel();
        var nav = Nav(p);
        DockPanel.SetDock(nav, Dock.Left);
        main.Children.Add(nav);
        main.Children.Add(_scroll);

        var dock = new DockPanel();
        var title = TitleBar(p);
        DockPanel.SetDock(title, Dock.Top);
        dock.Children.Add(title);
        dock.Children.Add(main);

        var frame = new Border
        {
            Background = p.Surface,
            BorderBrush = p.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = dock,
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 14, Direction = 270, Color = Color.FromRgb(2, 32, 64), Opacity = p.Dark ? .55 : .30 },
        };
        dock.SizeChanged += (_, e) => dock.Clip = new RectangleGeometry(new Rect(e.NewSize), 11, 11);
        TextOptions.SetTextFormattingMode(frame, TextFormattingMode.Display);
        Content = new Border { Padding = new Thickness(Pad), Child = frame };
        _scroll.ScrollToVerticalOffset(scroll);
    }

    /// <summary>Reflects a change made outside the window (the follow arrow, the AIR key turning AIR on).</summary>
    public void Sync()
    {
        _onToggle?.Set(_host.Settings.On);
        _followToggle?.Set(_host.Settings.FollowCaret);
        if (_followIconHost is not null)
            _followIconHost.Child = Center(Icons.FollowArrow(_host.Settings.FollowCaret ? _host.Palette.Accent : _host.Palette.Muted, _host.Settings.FollowCaret, 18));
    }

    /// <summary>Reflects a new state of the engine (its check finished, or a version failed).</summary>
    public void SyncEngine()
    {
        FillEngineStatus(_host.Palette);
        PaintEngineDot(_host.Palette);
    }

    /// <summary>Shows <paramref name="page"/> on the right, from its top.</summary>
    public void ShowPage(SettingsPage page)
    {
        if (page == _page || _scroll is null) return;
        HidePreview();
        _page = page;
        FillPage(_host.Palette);
        _scroll.ScrollToVerticalOffset(0);
        PaintNav(_host.Palette);
    }

    // Every page is as tall as Tones, the tallest, so the window keeps its size from page to page; a screen too short
    // for it gets a shorter page, which scrolls. (The frame's two borders come off the page's width.)
    private double PageHeight(Palette p)
    {
        var tones = new StackPanel { Margin = PageMargin };
        tones.Children.Add(TonesSection(p, first: true));
        tones.Measure(new Size(PageWidth - 2, double.PositiveInfinity));
        double room = SystemParameters.WorkArea.Height - 24 - 42;
        return Math.Max(200, Math.Min(Math.Ceiling(tones.DesiredSize.Height), room));
    }

    private void FillPage(Palette p)
    {
        var body = new StackPanel { Margin = PageMargin };
        switch (_page)
        {
            case SettingsPage.Tones:
                body.Children.Add(TonesSection(p, first: true));
                break;
            case SettingsPage.Bubble:
                body.Children.Add(ChoiceSection(p, "style", "Layout",
                    [(nameof(BubbleLayout.Card), "Card"), (nameof(BubbleLayout.Compact), "Compact")], _host.Settings.Layout.ToString(), first: true));
                body.Children.Add(ChoiceSection(p, "theme", "Theme",
                    [(nameof(AppTheme.Windows), "Windows"), (nameof(AppTheme.Light), "Light"), (nameof(AppTheme.Dark), "Dark")], _host.Settings.Theme.ToString()));
                body.Children.Add(FollowSection(p));
                break;
            case SettingsPage.Engine:
                body.Children.Add(EngineSection(p, first: true));
                break;
            default:
                body.Children.Add(OnSection(p, first: true));
                body.Children.Add(KeysSection(p));
                break;
        }
        _scroll!.Content = body;
    }

    // ---------- the menu ----------

    private sealed record NavItem(DrawnButton Button, Border Pill, Border Icon, TextBlock Label);

    private Border Nav(Palette p)
    {
        _nav.Clear();
        _engineDot = null;
        var list = new StackPanel();
        foreach (var (page, label) in Pages) list.Children.Add(NavButton(p, page, label));
        PaintNav(p);
        return new Border
        {
            Width = NavWidth,
            Background = p.Surface2,
            BorderBrush = p.Border,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(8, 10, 8, 10),
            Child = list,
        };
    }

    private DrawnButton NavButton(Palette p, SettingsPage page, string label)
    {
        var dock = new DockPanel { LastChildFill = true };
        var pill = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(pill, Dock.Left);
        dock.Children.Add(pill);
        var icon = new Border { Width = 16, Height = 16, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        if (page == SettingsPage.Engine)
        {
            // the engine's state at a glance, in the colors of the Engine page's status line
            var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dot, Dock.Right);
            dock.Children.Add(dot);
            _engineDot = dot;
        }
        var text = Controls.Text(label, 13, p.Text);
        text.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(text);

        var button = new DrawnButton
        {
            Kind = AutomationControlType.TabItem,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4, 0, 8, 0),
            Margin = new Thickness(0, 0, 0, 2),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Focusable = true,
            FocusVisualStyle = Controls.FocusRing(p),
            Child = dock,
        };
        AutomationProperties.SetName(button, label);
        button.MouseEnter += (_, _) => { if (_page != page) button.Background = p.KeyBg; };
        button.MouseLeave += (_, _) => { if (_page != page) button.Background = Brushes.Transparent; };
        Controls.OnActivate(button, _ => ShowPage(page));
        button.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down)) return;
            e.Handled = true;
            int i = Array.FindIndex(Pages, x => x.Page == page) + (e.Key == Key.Down ? 1 : -1);
            if (i < 0 || i >= Pages.Length) return;
            ShowPage(Pages[i].Page);
            _nav[Pages[i].Page].Button.Focus();
        };
        _nav[page] = new NavItem(button, pill, icon, text);
        return button;
    }

    private void PaintNav(Palette p)
    {
        foreach (var (page, item) in _nav)
        {
            bool on = page == _page;
            item.Button.Background = on ? p.Hover : Brushes.Transparent;
            item.Pill.Background = on ? p.Accent : Brushes.Transparent;
            item.Icon.Child = NavIcon(page, on ? p.Accent : p.Muted);
            item.Label.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetItemStatus(item.Button, on ? "Selected" : "");
        }
        PaintEngineDot(p);
    }

    private static FrameworkElement NavIcon(SettingsPage page, Brush color) => page switch
    {
        SettingsPage.Tones => Icons.Lines(color),
        SettingsPage.Bubble => Icons.SpeechBubble(color),
        SettingsPage.Engine => Icons.Chip(color),
        _ => Icons.Power(color),
    };

    private void PaintEngineDot(Palette p)
    {
        if (_engineDot is null) return;
        var state = _host.EngineState;
        _engineDot.Fill = StateBrush(p, state);
        _engineDot.ToolTip = state == EngineState.Unknown
            ? "Checking…"
            : EngineStatus.Status(_host.Settings.Engine, state, _host.Settings.ActiveModel());
    }

    // green ready, the accent while downloading (progress, not a fault), red to fix, grey checking or busy
    private static Brush StateBrush(Palette p, EngineState state) => state switch
    {
        EngineState.Ready => p.InsFg,
        EngineState.Downloading => p.Accent,
        _ when EngineStatus.Blocks(state) => p.DelFg,
        _ => p.Muted,
    };

    // ---------- placement: beside the bubble (from the gear), else above the tray ----------

    private void PlaceOnce()
    {
        if (_placed || ActualHeight <= 0) return;
        _placed = true;
        if (_beside is { } b)
        {
            PlaceBeside(b.Bubble, b.Area);
            return;
        }
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth + Pad - 12;
        Top = Math.Max(area.Top, area.Bottom - ActualHeight + Pad - 12);
    }

    // To the right of the bubble, its tops aligned; to the left when the right has no room; inside the work area.
    private void PlaceBeside(Rect bubble, Rect area)
    {
        const double Gap = 12, Edge = 4;
        double w = ActualWidth - 2 * Pad, h = ActualHeight - 2 * Pad;
        double left = bubble.Right + Gap;
        if (left + w > area.Right - Edge) left = bubble.Left - Gap - w;
        left = Math.Max(area.Left + Edge, Math.Min(left, area.Right - w - Edge));
        double top = Math.Max(area.Top + Edge, Math.Min(bubble.Top, area.Bottom - h - Edge));
        Left = left - Pad;
        Top = top - Pad;
    }

    // ---------- title bar ----------

    private FrameworkElement TitleBar(Palette p)
    {
        var dock = new DockPanel { LastChildFill = true };
        var close = new DrawnButton
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = Center(Icons.Close(p.Muted)),
            ToolTip = "Close",
            Focusable = true,
            FocusVisualStyle = Controls.FocusRing(p),
        };
        AutomationProperties.SetName(close, "Close");
        close.MouseEnter += (_, _) => close.Background = p.Hover;
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
        Controls.OnActivate(close, _ => Close());
        DockPanel.SetDock(close, Dock.Right);
        dock.Children.Add(close);

        var q = Icons.Q(18);
        q.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(q);
        var title = new TextBlock { FontFamily = Controls.Ui, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        title.Inlines.Add(new Run("AI Inline Rewrite") { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = p.Text });
        title.Inlines.Add(new Run("  ·  ") { FontSize = 12, Foreground = p.Muted });
        title.Inlines.Add(new Run("Settings") { FontSize = 12, Foreground = p.Muted });
        dock.Children.Add(title);

        var bar = new Border
        {
            Height = 40,
            Padding = new Thickness(12, 0, 6, 0),
            BorderBrush = p.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.Transparent,
            Child = dock,
        };
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        return bar;
    }

    // ---------- sections ----------

    private static Border Section(Palette p, bool first, UIElement child) => new()
    {
        Padding = new Thickness(0, 14, 0, 12),
        BorderBrush = p.Border,
        BorderThickness = first ? default : new Thickness(0, 1, 0, 0),
        Child = child,
    };

    private static TextBlock H3(Palette p, string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontFamily = Controls.Ui,
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        Foreground = p.Accent,
        Margin = new Thickness(0, 0, 0, 2),
    };

    private static TextBlock Help(Palette p, string text, bool inLine = false)
    {
        var tb = Controls.Text(text, 12, p.Muted);
        tb.LineHeight = 17;
        tb.Margin = inLine ? default : new Thickness(0, 0, 0, 9);
        return tb;
    }

    // A title and help on the left, a control on the right, optionally an icon column first.
    private static DockPanel Line(Palette p, string title, string help, FrameworkElement control, FrameworkElement? icon = null)
    {
        var dock = new DockPanel { LastChildFill = true };
        control.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(control, Dock.Right);
        dock.Children.Add(control);
        if (icon is not null)
        {
            icon.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(H3(p, title));
        text.Children.Add(Help(p, help, inLine: true));
        dock.Children.Add(text);
        return dock;
    }

    private FrameworkElement OnSection(Palette p, bool first)
    {
        _onToggle = new Toggle(p, _host.Settings.On, "AIR on or off");
        _onToggle.Changed += on => _host.SetOn(on);
        return Section(p, first, Line(p, "AIR", "Off: AIR stops reading what you type. The AIR key turns it back on.", _onToggle));
    }

    private FrameworkElement FollowSection(Palette p)
    {
        bool on = _host.Settings.FollowCaret;
        _followToggle = new Toggle(p, on, "Follow the text cursor");
        _followToggle.Changed += v => _host.SetFollow(v);
        // The same arrow as beside the gear, centered in its own column on the left of the option.
        _followIconHost = new Border { Width = 26, Child = Center(Icons.FollowArrow(on ? p.Accent : p.Muted, on, 18)), VerticalAlignment = VerticalAlignment.Stretch };
        return Section(p, false, Line(p, "Follow the text cursor",
            "Off: the bubble stays where you leave it. The same arrow beside the gear switches it.", _followToggle, _followIconHost));
    }

    // The AIR key and the off key, filled again when either changes: taking the other's key swaps the two.
    private Border KeysSection(Palette p)
    {
        _keysBody = new Border();
        FillKeys(p, focus: null);
        return _keysBody;
    }

    private void FillKeys(Palette p, string? focus)
    {
        if (_keysBody is null) return;
        var s = _host.Settings;
        var options = AirKeys.All.Select(k => (k.ToString(), AirKeys.Label(k))).ToList();
        var air = new Dropdown(p, options, s.AirKey.ToString(), "AIR key");
        air.Chosen += v =>
        {
            _host.SetAirKey(Enum.Parse<AirKey>(v));
            FillKeys(_host.Palette, "air");
        };
        var off = new Dropdown(p, options, s.OffKey.ToString(), "Off key");
        off.Chosen += v =>
        {
            _host.SetOffKey(Enum.Parse<AirKey>(v));
            FillKeys(_host.Palette, "off");
        };
        var stack = new StackPanel();
        stack.Children.Add(KeySection(p, "AIR key", "Opens pick mode: then ↑ ↓ and Enter, a number, or Esc. A tap is a press and release with no other key.", air));
        stack.Children.Add(KeySection(p, "Off key", "Puts AIR to sleep: the bubble closes and AIR stops reading until the AIR key wakes it. Choosing the AIR key here swaps the two.", off));
        _keysBody.Child = stack;
        if (focus == "air") air.Focus();
        else if (focus == "off") off.Focus();
    }

    private static Border KeySection(Palette p, string title, string help, Dropdown select)
    {
        var stack = new StackPanel();
        stack.Children.Add(H3(p, title));
        stack.Children.Add(Help(p, help));
        stack.Children.Add(select);
        return Section(p, false, stack);
    }

    private FrameworkElement ChoiceSection(Palette p, string kind, string title, IReadOnlyList<(string, string)> options, string selected, bool first = false)
    {
        var stack = new StackPanel();
        stack.Children.Add(H3(p, title));
        var seg = new Segmented(p, options, selected, title) { Margin = new Thickness(0, 8, 0, 0) };
        var section = Section(p, first, stack);
        seg.Picked += (value, _) =>
        {
            if (kind == "style") _host.SetLayout(Enum.Parse<BubbleLayout>(value));
            else _host.SetTheme(Enum.Parse<AppTheme>(value));
            // A theme change rebuilds this window; the preview finds the section again by its tag.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ShowPreview(kind, value));
        };
        section.Tag = kind;
        stack.Children.Add(seg);
        return section;
    }

    // ---------- engine ----------

    private FrameworkElement EngineSection(Palette p, bool first)
    {
        _engineBody = new Border();
        FillEngine(p, focus: null);
        return Section(p, first, _engineBody);
    }

    private void FillEngine(Palette p, string? focus)
    {
        if (_engineBody is null) return;
        var s = _host.Settings;
        var stack = new StackPanel();
        stack.Children.Add(H3(p, "Engine"));
        stack.Children.Add(Help(p, "The model that writes the versions. Built-in runs it inside AIR and Ollama runs it in Ollama, both on this PC; Claude sends the sentence you are typing to Anthropic's API."));

        var engines = new Segmented(p, EngineChoices.Engines, s.Engine.ToString(), "Engine") { Margin = new Thickness(0, 8, 0, 0) };
        engines.Picked += (value, _) =>
        {
            var engine = Enum.Parse<EngineKind>(value);
            if (engine == _host.Settings.Engine) return;
            bool hadFocus = engines.IsKeyboardFocusWithin;
            _host.SetEngine(engine);
            _installed = null; // asked again: Ollama may have started, or got a model, meanwhile
            FillEngine(_host.Palette, hadFocus ? "engine" : null);
        };
        stack.Children.Add(engines);

        var options = s.Engine switch
        {
            EngineKind.Claude => EngineChoices.ClaudeOptions(s.ClaudeModel),
            EngineKind.BuiltIn => EngineChoices.BuiltInModels,
            _ => EngineChoices.OllamaOptions(_installed, s.OllamaModel),
        };
        var model = new Dropdown(p, options, s.ActiveModel(), "Model") { Margin = new Thickness(0, 8, 0, 0) };
        model.Chosen += value =>
        {
            bool hadFocus = model.IsKeyboardFocusWithin;
            _host.SetModel(value);
            FillEngine(_host.Palette, hadFocus ? "model" : null);
        };
        stack.Children.Add(model);

        _engineStatus = new Border { Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(_engineStatus);
        FillEngineStatus(p);
        _engineBody.Child = stack;

        if (focus == "engine") engines.Focus();
        else if (focus == "model") model.Focus();
        if (s.Engine == EngineKind.Ollama && _installed is null && !_askingModels) _ = AskModelsAsync();
    }

    // Ollama's installed models, for the model list; until they come (or when Ollama cannot be asked), the list holds
    // the model in the settings.
    private async Task AskModelsAsync()
    {
        _askingModels = true;
        try
        {
            var models = await _host.InstalledModelsAsync();
            if (models is null || _host.Settings.Engine != EngineKind.Ollama) return;
            _installed = models;
            FillEngine(_host.Palette, focus: null);
        }
        finally
        {
            _askingModels = false;
        }
    }

    // A dot (green ready, red to fix, grey checking or busy), the engine's status, and the step that fixes it.
    private void FillEngineStatus(Palette p)
    {
        if (_engineStatus is null) return;
        var s = _host.Settings;
        var state = _host.EngineState;
        string model = s.ActiveModel();
        string status = state == EngineState.Unknown ? "Checking…" : EngineStatus.Status(s.Engine, state, model);
        string hint = EngineStatus.Hint(s.Engine, state, model);

        var line = new DockPanel();
        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = StateBrush(p, state),
            Margin = new Thickness(1, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(dot, Dock.Left);
        line.Children.Add(dot);
        line.Children.Add(Controls.Text(status, 12.5, p.Text, FontWeights.SemiBold));

        var stack = new StackPanel();
        stack.Children.Add(line);
        if (s.Engine == EngineKind.BuiltIn && (_host.ModelDownload is not null || state == EngineState.ModelMissing))
        {
            stack.Children.Add(DownloadBlock(p));
        }
        else if (hint.Length > 0)
        {
            var help = Help(p, hint, inLine: true);
            help.Margin = new Thickness(18, 2, 0, 0);
            stack.Children.Add(help);
        }
        _engineStatus.Child = stack;
    }

    // The built-in model's download: what it is and a Download button; while it runs, a bar, how far, and Cancel.
    private FrameworkElement DownloadBlock(Palette p)
    {
        var stack = new StackPanel { Margin = new Thickness(18, 4, 0, 0) };
        if (_host.ModelDownload is { } progress)
        {
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = p.KeyBg, Margin = new Thickness(0, 4, 0, 6) };
            var fill = new Border { CornerRadius = new CornerRadius(3), Background = p.Accent, HorizontalAlignment = HorizontalAlignment.Left };
            track.Child = fill;
            track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * progress.Fraction;
            fill.Width = track.ActualWidth * progress.Fraction;
            stack.Children.Add(track);
            string text = progress.Checking
                ? "Checking the file…"
                : string.Create(CultureInfo.InvariantCulture, $"{progress.Done / 1e9:0.00} of {progress.Total / 1e9:0.0} GB · {progress.Fraction:0%}")
                  + (progress.Retrying ? " · the connection dropped, trying again" : "");
            stack.Children.Add(Help(p, text, inLine: true));
            var cancel = ActionButton(p, "Cancel", primary: false, () => _host.CancelModelDownload());
            cancel.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(cancel);
            return stack;
        }

        stack.Children.Add(Help(p, $"{LocalModel.Name} ({LocalModel.License}), {LocalModel.DownloadSizeText} from Hugging Face, once. It runs on this PC; what you type never leaves it.", inLine: true));
        if (_host.ModelDownloadError is { } error)
        {
            var why = Controls.Text(error + ".", 12, p.DelFg);
            why.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(why);
        }
        var download = ActionButton(p, _host.ModelDownloadError is null ? $"Download ({LocalModel.DownloadSizeText})" : "Try again", primary: true,
            () => _host.StartModelDownload());
        download.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(download);
        return stack;
    }

    private static DrawnButton ActionButton(Palette p, string label, bool primary, Action action)
    {
        var b = new DrawnButton
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = primary ? p.Accent : p.KeyBg,
            Cursor = Cursors.Hand,
            Focusable = true,
            FocusVisualStyle = Controls.FocusRing(p),
            Child = Controls.Text(label, 12.5, primary ? p.OnAccent : p.Text, FontWeights.SemiBold),
        };
        AutomationProperties.SetName(b, label);
        b.MouseEnter += (_, _) => b.Opacity = .88;
        b.MouseLeave += (_, _) => b.Opacity = 1;
        Controls.OnActivate(b, _ => action());
        return b;
    }

    // ---------- tones ----------

    private FrameworkElement TonesSection(Palette p, bool first)
    {
        var stack = new StackPanel();
        stack.Children.Add(H3(p, "Tones"));
        stack.Children.Add(Help(p, "Each checked tone is one version in the bubble, in this order; its number is its key. Reorder with the arrows or by dragging."));
        _toneList = new StackPanel();
        var frame = new Border
        {
            BorderBrush = p.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Background = p.Surface2,
            Child = _toneList,
        };
        _toneList.SizeChanged += (_, e) => _toneList.Clip = new RectangleGeometry(new Rect(e.NewSize), 9, 9);
        FillTones(p);
        stack.Children.Add(frame);
        return Section(p, first, stack);
    }

    private void FillTones(Palette p)
    {
        if (_toneList is null) return;
        _toneList.Children.Clear();
        var tones = _host.Settings.Tones;
        var enabled = _host.Settings.EnabledTones().ToList();
        for (int i = 0; i < tones.Count; i++)
            _toneList.Children.Add(ToneRow(p, tones[i], i, tones.Count, enabled));
    }

    private Border ToneRow(Palette p, ToneSetting t, int index, int count, List<Tone> enabled)
    {
        var dock = new DockPanel { LastChildFill = true };

        var grip = Icons.Grip(p.Muted);
        grip.Opacity = .7;
        grip.Margin = new Thickness(2, 0, 8, 0);
        grip.VerticalAlignment = VerticalAlignment.Center;
        grip.ToolTip = "Drag to reorder";
        DockPanel.SetDock(grip, Dock.Left);
        dock.Children.Add(grip);

        var check = new Check(p, t.On, $"{t.Tone} on")
        {
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = (t.On ? "Hide " : "Show ") + t.Tone,
        };
        check.Changed += on =>
        {
            _host.SetToneOn(t.Tone, on);
            FillTones(p);
        };
        DockPanel.SetDock(check, Dock.Left);
        dock.Children.Add(check);

        var num = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(6),
            Background = p.Hover,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = t.On ? 1 : .45,
            Child = new TextBlock
            {
                Text = t.On ? (enabled.IndexOf(t.Tone) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "·",
                FontFamily = Controls.Mono,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = p.Accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        DockPanel.SetDock(num, Dock.Left);
        dock.Children.Add(num);

        var moves = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        moves.Children.Add(MoveButton(p, t.Tone, -1, index > 0));
        moves.Children.Add(MoveButton(p, t.Tone, 1, index < count - 1));
        DockPanel.SetDock(moves, Dock.Right);
        dock.Children.Add(moves);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Controls.Text(t.Tone.ToString(), 13, p.Text, FontWeights.SemiBold);
        name.Opacity = t.On ? 1 : .45;
        info.Children.Add(name);
        var desc = Controls.Text(Tones.Describe(t.Tone), 11.5, p.Muted);
        desc.LineHeight = 15.5;
        info.Children.Add(desc);
        dock.Children.Add(info);

        var row = new Border
        {
            Child = dock,
            Padding = new Thickness(4, 8, 8, 8),
            Background = p.Surface,
            BorderBrush = p.Border,
            BorderThickness = index == 0 ? default : new Thickness(0, 1, 0, 0),
            Tag = t.Tone,
            Cursor = Cursors.SizeNS,
        };
        row.MouseEnter += (_, _) => { if (_dragTone is null) row.Background = p.Tint; };
        row.MouseLeave += (_, _) => { if (_dragTone is null) row.Background = p.Surface; };
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInside(d, check, moves)) return;
            _dragTone = t.Tone;
            _dragFrom = e.GetPosition(_toneList);
            _dragMoved = false;
            _toneList!.CaptureMouse();
            _toneList.MouseMove -= OnToneDragMove;
            _toneList.MouseMove += OnToneDragMove;
            _toneList.MouseLeftButtonUp -= OnToneDrop;
            _toneList.MouseLeftButtonUp += OnToneDrop;
            _toneList.LostMouseCapture -= OnToneDragLost;
            _toneList.LostMouseCapture += OnToneDragLost;
            e.Handled = true;
        };
        return row;
    }

    private static bool IsInside(DependencyObject d, params UIElement[] parents)
    {
        for (var cur = d; cur is not null; cur = cur is Visual ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
        {
            if (parents.Contains(cur)) return true;
        }
        return false;
    }

    private Border MoveButton(Palette p, Tone tone, int step, bool enabled)
    {
        bool up = step < 0;
        var b = new DrawnButton
        {
            Width = 26,
            Height = 19,
            CornerRadius = new CornerRadius(6),
            BorderBrush = p.Border,
            BorderThickness = new Thickness(1),
            Background = p.Surface,
            Margin = new Thickness(0, up ? 0 : 3, 0, 0),
            Child = Center(Icons.Chevron(p.Muted, up)),
            Opacity = enabled ? 1 : .3,
            ToolTip = enabled ? (up ? "Move up" : "Move down") : null,
            Focusable = enabled,
            FocusVisualStyle = Controls.FocusRing(p),
        };
        AutomationProperties.SetName(b, $"Move {tone} {(up ? "up" : "down")}");
        if (!enabled) return b;
        b.Cursor = Cursors.Hand;
        b.MouseEnter += (_, _) =>
        {
            b.BorderBrush = p.Accent;
            b.Background = p.Hover;
            b.Child = Center(Icons.Chevron(p.Accent, up));
        };
        b.MouseLeave += (_, _) =>
        {
            b.BorderBrush = p.Border;
            b.Background = p.Surface;
            b.Child = Center(Icons.Chevron(p.Muted, up));
        };
        Controls.OnActivate(b, _ =>
        {
            var order = _host.Settings.Tones.Select(x => x.Tone).ToList();
            int i = order.IndexOf(tone), j = i + step;
            if (j < 0 || j >= order.Count) return;
            (order[i], order[j]) = (order[j], order[i]);
            _host.SetOrder(order);
            FillTones(p);
        });
        return b;
    }

    private static FrameworkElement Center(FrameworkElement fe)
    {
        fe.HorizontalAlignment = HorizontalAlignment.Center;
        fe.VerticalAlignment = VerticalAlignment.Center;
        return fe;
    }

    private void OnToneDragMove(object sender, MouseEventArgs e)
    {
        if (_dragTone is null || _toneList is null) return;
        var at = e.GetPosition(_toneList);
        if (!_dragMoved && Math.Abs(at.Y - _dragFrom.Y) < 4) return;
        _dragMoved = true;
        var p = _host.Palette;
        int target = DropIndex(at.Y, out bool after);
        for (int i = 0; i < _toneList.Children.Count; i++)
        {
            var row = (Border)_toneList.Children[i];
            bool dragged = (Tone)row.Tag == _dragTone;
            row.Opacity = dragged ? .35 : 1;
            row.Background = p.Surface;
            row.BorderBrush = i == target ? p.Accent : p.Border;
            row.BorderThickness = i == target
                ? (after ? new Thickness(0, i == 0 ? 0 : 1, 0, 3) : new Thickness(0, 3, 0, 0))
                : (i == 0 ? default : new Thickness(0, 1, 0, 0));
        }
    }

    private int DropIndex(double y, out bool after)
    {
        after = false;
        double top = 0;
        for (int i = 0; i < _toneList!.Children.Count; i++)
        {
            var row = (FrameworkElement)_toneList.Children[i];
            double h = row.ActualHeight;
            if (y < top + h || i == _toneList.Children.Count - 1)
            {
                after = y > top + h / 2;
                return i;
            }
            top += h;
        }
        return 0;
    }

    private void OnToneDrop(object sender, MouseButtonEventArgs e)
    {
        if (_dragTone is not { } moved || _toneList is null) return;
        bool didMove = _dragMoved;
        int target = DropIndex(e.GetPosition(_toneList).Y, out bool after);
        _dragTone = null;
        _toneList.ReleaseMouseCapture();
        if (didMove)
        {
            var order = _host.Settings.Tones.Select(x => x.Tone).ToList();
            var targetTone = (Tone)((Border)_toneList.Children[target]).Tag;
            if (targetTone != moved)
            {
                order.Remove(moved);
                int at = order.IndexOf(targetTone) + (after ? 1 : 0);
                order.Insert(at, moved);
                _host.SetOrder(order);
            }
        }
        FillTones(_host.Palette);
    }

    private void OnToneDragLost(object sender, MouseEventArgs e)
    {
        if (_dragTone is null) return;
        _dragTone = null;
        FillTones(_host.Palette);
    }

    // ---------- the floating preview ----------

    private void ShowPreview(string kind, string value)
    {
        if (!IsVisible || Content is not Border outer) return;
        var section = FindSection(outer, kind);
        if (section is null) return;
        var s = _host.Settings;
        var layout = kind == "style" ? Enum.Parse<BubbleLayout>(value) : s.Layout;
        var theme = kind == "theme" ? Enum.Parse<AppTheme>(value) : s.Theme;
        bool dark = theme == AppTheme.Dark || (theme == AppTheme.Windows && _host.WindowsIsDark);
        var state = new BubbleState
        {
            Layout = layout,
            Rows = Samples.Rows(s.EnabledTones()),
            Source = Samples.Portuguese,
            Translated = true,
            Status = "Translated from Portuguese",
            Follow = s.FollowCaret,
            Interactive = false,
        };
        var dpi = VisualTreeHelper.GetDpi(this);
        var top = section.PointToScreen(new Point(0, 0));
        var bottom = section.PointToScreen(new Point(0, section.ActualHeight));
        double centerY = (top.Y + bottom.Y) / 2 / dpi.DpiScaleY;
        var frame = new Rect(Left + Pad, Top + Pad, ActualWidth - 2 * Pad, ActualHeight - 2 * Pad);
        _preview.Present(state, Palette.For(dark), _host.Palette, Explain(kind, value), frame, centerY, _beside);
        _previewSection = section;
        _previewLeaving = false;
        // Chosen by keyboard, or the mouse never moves again: it goes away on its own after 5 s.
        ArmPreviewHide(5000);
    }

    private static FrameworkElement? FindSection(DependencyObject root, string kind)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Tag: string tag } fe && tag == kind) return fe;
            if (FindSection(child, kind) is { } found) return found;
        }
        return null;
    }

    private Inline[] Explain(string kind, string value)
    {
        (string Name, string Tail) text = (kind, value) switch
        {
            ("style", "Card") => ("Card", " · each version under its tone name, and the bar with AIR, the status and the gear on top. The easiest to read."),
            ("style", _) => ("Compact", " · one line per version with the tone on the right, and the bar at the bottom. Covers less of the screen."),
            ("theme", "Windows") => ("Windows", $" · follows the Windows mode, {(_host.WindowsIsDark ? "dark" : "light")} right now, and changes with it."),
            ("theme", "Light") => ("Light", " · always light, whatever mode Windows is in."),
            _ => ("Dark", " · always dark, whatever mode Windows is in."),
        };
        return [new Run(text.Name) { FontWeight = FontWeights.SemiBold, Foreground = _host.Palette.Text }, new Run(text.Tail)];
    }

    private void TrackPreviewMouse()
    {
        if (_previewSection is null) return;
        if (_previewSection.IsMouseOver || _preview.IsMouseOver)
        {
            _previewTimer.Stop();
            _previewLeaving = false;
        }
        else if (!_previewLeaving)
        {
            _previewLeaving = true;
            ArmPreviewHide(1200);
        }
    }

    private void ArmPreviewHide(int ms)
    {
        _previewTimer.Stop();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(ms);
        _previewTimer.Start();
    }

    private void HidePreview()
    {
        _previewTimer.Stop();
        _previewSection = null;
        _preview.Dismiss();
    }
}

/// <summary>The sample the settings preview shows: a Portuguese sentence and what each tone makes of it.</summary>
public static class Samples
{
    public const string Portuguese = "acho que a gente precisa conversar sobre o prazo amanhã";

    public static IReadOnlyList<BubbleRow> Rows(IReadOnlyList<Tone> tones) =>
        [.. tones.Select(t => new BubbleRow(t, Version(t), Done: true))];

    private static string Version(Tone tone) => tone switch
    {
        Tone.Concise => "we need to discuss the deadline tomorrow",
        Tone.Polished => "I believe we should discuss the deadline tomorrow",
        Tone.Ornate => "I believe it would be prudent for us to discuss the deadline tomorrow",
        Tone.Diplomatic => "would it be possible for us to talk about the deadline tomorrow",
        Tone.Slang => "I think we gotta talk about the deadline tomorrow",
        Tone.Trendy => "ngl we lowkey gotta talk about the deadline tomorrow",
        _ => "I think we need to talk about the deadline tomorrow",
    };
}
