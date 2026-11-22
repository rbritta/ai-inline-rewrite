using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using AiInlineRewrite.Core;

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

    void SetAirKey(AirKey key);

    void SetFollow(bool on);

    /// <summary>The state of the engine in use (Unknown while it is being checked), for the Engine section.</summary>
    EngineState EngineState { get; }

    /// <summary>Switches the engine. It applies at once: the other engine is dropped and the new one checked.</summary>
    void SetEngine(EngineKind engine);

    /// <summary>Sets the model of the engine in use; it applies at once too.</summary>
    void SetModel(string model);

    /// <summary>The models Ollama has, while Ollama is the engine; null otherwise, or when Ollama cannot be asked.</summary>
    Task<IReadOnlyList<string>?> InstalledModelsAsync();
}

/// <summary>
/// The settings window (design/experience.html): a sober 40 px title bar with the loose Q, "AI Inline Rewrite ·
/// Settings" and an X; then AIR on/off, Tones (check, order by the arrows or by dragging, one line each), Layout and
/// Theme (a click floats a preview beside the window), the AIR key, Follow the text cursor with its arrow, and the
/// Engine: Ollama or Claude, the model, and the engine's state with the one step that fixes it.
/// </summary>
public sealed class SettingsWindow : Window
{
    private const double Pad = 24;
    private const double Width0 = 384;

    private readonly ISettingsHost _host;
    private readonly (Rect Bubble, Rect Area)? _beside;
    private readonly PreviewWindow _preview = new();
    private readonly DispatcherTimer _previewTimer = new();
    private ScrollViewer? _scroll;
    private FrameworkElement? _previewSection;
    private bool _previewLeaving;
    private bool _placed;
    private Toggle? _onToggle;
    private Toggle? _followToggle;
    private Border? _followIconHost;

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
    public SettingsWindow(ISettingsHost host, (Rect Bubble, Rect Area)? beside = null)
    {
        _host = host;
        _beside = beside;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = Width0 + 2 * Pad;
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

    /// <summary>Builds the content again (after a theme change), keeping the scroll position.</summary>
    public void Rebuild()
    {
        double scroll = _scroll?.VerticalOffset ?? 0;
        HidePreview();
        var p = _host.Palette;
        MaxHeight = SystemParameters.WorkArea.Height - 24 + 2 * Pad;

        var body = new StackPanel { Margin = new Thickness(16, 4, 16, 16) };
        body.Children.Add(OnSection(p, first: true));
        body.Children.Add(TonesSection(p));
        body.Children.Add(ChoiceSection(p, "style", "Layout",
            [(nameof(BubbleLayout.Card), "Card"), (nameof(BubbleLayout.Compact), "Compact")], _host.Settings.Layout.ToString()));
        body.Children.Add(ChoiceSection(p, "theme", "Theme",
            [(nameof(AppTheme.Windows), "Windows"), (nameof(AppTheme.Light), "Light"), (nameof(AppTheme.Dark), "Dark")], _host.Settings.Theme.ToString()));
        body.Children.Add(KeySection(p));
        body.Children.Add(FollowSection(p));
        body.Children.Add(EngineSection(p));

        _scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        var dock = new DockPanel();
        var title = TitleBar(p);
        DockPanel.SetDock(title, Dock.Top);
        dock.Children.Add(title);
        dock.Children.Add(_scroll);

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
    public void SyncEngine() => FillEngineStatus(_host.Palette);

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

    private FrameworkElement KeySection(Palette p)
    {
        var stack = new StackPanel();
        stack.Children.Add(H3(p, "AIR key"));
        stack.Children.Add(Help(p, "Opens pick mode: then ↑ ↓ and Enter, a number, or Esc. A tap is a press and release with no other key."));
        var options = AirKeys.All.Select(k => (k.ToString(), AirKeys.Label(k))).ToList();
        var select = new Dropdown(p, options, _host.Settings.AirKey.ToString(), "AIR key");
        select.Chosen += v => _host.SetAirKey(Enum.Parse<AirKey>(v));
        stack.Children.Add(select);
        return Section(p, false, stack);
    }

    private FrameworkElement ChoiceSection(Palette p, string kind, string title, IReadOnlyList<(string, string)> options, string selected)
    {
        var stack = new StackPanel();
        stack.Children.Add(H3(p, title));
        var seg = new Segmented(p, options, selected, title) { Margin = new Thickness(0, 8, 0, 0) };
        var section = Section(p, false, stack);
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

    private FrameworkElement EngineSection(Palette p)
    {
        _engineBody = new Border();
        FillEngine(p, focus: null);
        return Section(p, false, _engineBody);
    }

    private void FillEngine(Palette p, string? focus)
    {
        if (_engineBody is null) return;
        var s = _host.Settings;
        var stack = new StackPanel();
        stack.Children.Add(H3(p, "Engine"));
        stack.Children.Add(Help(p, "The model that writes the versions. Ollama runs it on this PC; Claude sends the sentence you are typing to Anthropic's API."));

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

        var options = s.Engine == EngineKind.Claude
            ? EngineChoices.ClaudeOptions(s.ClaudeModel)
            : EngineChoices.OllamaOptions(_installed, s.OllamaModel);
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
            Fill = state == EngineState.Ready ? p.InsFg : EngineStatus.Blocks(state) ? p.DelFg : p.Muted,
            Margin = new Thickness(1, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(dot, Dock.Left);
        line.Children.Add(dot);
        line.Children.Add(Controls.Text(status, 12.5, p.Text, FontWeights.SemiBold));

        var stack = new StackPanel();
        stack.Children.Add(line);
        if (hint.Length > 0)
        {
            var help = Help(p, hint, inLine: true);
            help.Margin = new Thickness(18, 2, 0, 0);
            stack.Children.Add(help);
        }
        _engineStatus.Child = stack;
    }

    // ---------- tones ----------

    private FrameworkElement TonesSection(Palette p)
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
        return Section(p, false, stack);
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
        _ => "I think we need to talk about the deadline tomorrow",
    };
}
