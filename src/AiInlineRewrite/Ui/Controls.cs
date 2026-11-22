using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AiInlineRewrite.Ui;

/// <summary>
/// The settings window's controls, drawn to the design instead of restyling the stock WPF ones: solid Sky when
/// on or selected, no gradients. Each is focusable and works from the keyboard (Space or Enter, arrows).
/// </summary>
internal static class Controls
{
    public static readonly FontFamily Ui = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static Style FocusRing(Palette p)
    {
        var style = new Style();
        var rect = new FrameworkElementFactory(typeof(Rectangle));
        rect.SetValue(Shape.StrokeProperty, p.Accent);
        rect.SetValue(Shape.StrokeThicknessProperty, 2.0);
        rect.SetValue(Rectangle.RadiusXProperty, 6.0);
        rect.SetValue(Rectangle.RadiusYProperty, 6.0);
        rect.SetValue(FrameworkElement.MarginProperty, new Thickness(-3));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate { VisualTree = rect }));
        return style;
    }

    public static TextBlock Text(string text, double size, Brush color, FontWeight? weight = null) => new()
    {
        Text = text,
        FontFamily = Ui,
        FontSize = size,
        Foreground = color,
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>
    /// A click (mouse up over the element that got the mouse down) or Space/Enter when focused; on a
    /// <see cref="DrawnButton"/>, also UI Automation's Invoke or Toggle (screen readers, tools/smoke).
    /// </summary>
    public static void OnActivate(UIElement el, Action<bool> action)
    {
        if (el is DrawnButton drawn) drawn.Press ??= () => action(true);
        bool pressed = false;
        el.MouseLeftButtonDown += (_, e) =>
        {
            pressed = true;
            e.Handled = true;
        };
        el.MouseLeftButtonUp += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            e.Handled = true;
            if (el.IsMouseOver) action(false);
        };
        el.MouseLeave += (_, _) => pressed = false;
        el.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter)
            {
                e.Handled = true;
                action(true);
            }
        };
    }
}

/// <summary>
/// UI Automation for the drawn controls. WPF leaves Borders and panels out of the automation tree, so without this a
/// screen reader (and tools/smoke) would not see the bubble's buttons or the settings' switches at all. Each control
/// shows up with its name and its kind, and can be pressed (Invoke) or, when it has an on/off state, toggled.
/// </summary>
internal sealed class DrawnPeer(FrameworkElement owner, AutomationControlType kind, Func<Action?> press, Func<bool>? isOn)
    : FrameworkElementAutomationPeer(owner), IInvokeProvider, IToggleProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => kind;

    protected override string GetClassNameCore() => Owner.GetType().Name;

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;

    public override object GetPattern(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.Toggle when isOn is not null => this,
        PatternInterface.Invoke when isOn is null && press() is not null => this,
        _ => base.GetPattern(patternInterface),
    };

    public void Invoke() => press()?.Invoke();

    public ToggleState ToggleState => isOn?.Invoke() == true ? ToggleState.On : ToggleState.Off;

    public void Toggle() => press()?.Invoke();
}

/// <summary>A Border that UI Automation sees as a named control of <see cref="Kind"/> (a button by default).</summary>
internal class DrawnButton : Border
{
    public AutomationControlType Kind { get; init; } = AutomationControlType.Button;

    /// <summary>What a click does; <see cref="Controls.OnActivate"/> fills it in.</summary>
    public Action? Press { get; set; }

    /// <summary>For a control with an on/off state: the state, exposed as the Toggle pattern.</summary>
    public Func<bool>? IsOn { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new DrawnPeer(this, Kind, () => Press, IsOn);
}

/// <summary>The on/off switch with its "On"/"Off" label.</summary>
internal sealed class Toggle : StackPanel
{
    protected override AutomationPeer OnCreateAutomationPeer() => new DrawnPeer(this, AutomationControlType.Button, () => Flip, () => IsOn);

    private void Flip()
    {
        IsOn = !IsOn;
        Paint(animate: true);
        Changed?.Invoke(IsOn);
    }

    private readonly Palette _p;
    private readonly Border _track;
    private readonly Ellipse _knob;
    private readonly TranslateTransform _slide = new();
    private readonly TextBlock _label;

    public Toggle(Palette p, bool on, string name)
    {
        _p = p;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Focusable = true;
        Cursor = Cursors.Hand;
        FocusVisualStyle = Controls.FocusRing(p);
        AutomationProperties.SetName(this, name);
        _knob = new Ellipse { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), RenderTransform = _slide };
        _knob.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = .3 };
        _track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), Child = _knob };
        _label = new TextBlock { FontFamily = Controls.Ui, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = p.Text, MinWidth = 24, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Children.Add(_track);
        Children.Add(_label);
        IsOn = on;
        Paint(animate: false);
        Controls.OnActivate(this, _ => Flip());
    }

    public event Action<bool>? Changed;

    public bool IsOn { get; private set; }

    public void Set(bool on)
    {
        if (on == IsOn) return;
        IsOn = on;
        Paint(animate: true);
    }

    private void Paint(bool animate)
    {
        _track.Background = IsOn ? _p.Accent : _p.FieldBorder;
        _knob.Fill = IsOn ? _p.OnAccent : Brushes.White;
        _label.Text = IsOn ? "On" : "Off";
        double x = IsOn ? 18 : 0;
        if (animate) _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() });
        else
        {
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.X = x;
        }
        AutomationProperties.SetItemStatus(this, IsOn ? "On" : "Off");
    }
}

/// <summary>The 18 px checkbox of a tone.</summary>
internal sealed class Check : DrawnButton
{
    private readonly Palette _p;

    public Check(Palette p, bool on, string name)
    {
        _p = p;
        Kind = AutomationControlType.CheckBox;
        IsOn = () => IsChecked;
        Width = 18;
        Height = 18;
        CornerRadius = new CornerRadius(5);
        BorderThickness = new Thickness(1.5);
        Focusable = true;
        Cursor = Cursors.Hand;
        FocusVisualStyle = Controls.FocusRing(p);
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(this, name);
        IsChecked = on;
        Paint();
        Controls.OnActivate(this, _ =>
        {
            IsChecked = !IsChecked;
            Paint();
            Changed?.Invoke(IsChecked);
        });
    }

    public event Action<bool>? Changed;

    public bool IsChecked { get; private set; }

    private void Paint()
    {
        Background = IsChecked ? _p.Accent : _p.Field;
        BorderBrush = IsChecked ? Brushes.Transparent : _p.FieldBorder;
        Child = IsChecked ? new Viewbox { Width = 15, Height = 15, Child = Icons.Check(_p.OnAccent) } : null;
    }
}

/// <summary>A row of options, the chosen one in solid Sky (Layout, Theme).</summary>
internal sealed class Segmented : DrawnButton
{
    private readonly Palette _p;
    private readonly List<(string Value, Border Cell, TextBlock Label)> _cells = [];

    public Segmented(Palette p, IReadOnlyList<(string Value, string Label)> options, string selected, string name)
    {
        _p = p;
        Kind = AutomationControlType.Group;
        Background = p.KeyBg;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(3);
        Focusable = true;
        FocusVisualStyle = Controls.FocusRing(p);
        AutomationProperties.SetName(this, name);
        var grid = new UniformGrid { Rows = 1 };
        foreach (var (value, label) in options)
        {
            var text = new TextBlock { Text = label, FontFamily = Controls.Ui, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center };
            var cell = new DrawnButton { Kind = AutomationControlType.RadioButton, CornerRadius = new CornerRadius(8), Padding = new Thickness(0, 6, 0, 6), Margin = new Thickness(1.5, 0, 1.5, 0), Child = text, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            cell.IsOn = () => Selected == value;
            AutomationProperties.SetName(cell, label);
            cell.MouseEnter += (_, _) => { if (value != Selected) cell.Background = p.Hover; };
            cell.MouseLeave += (_, _) => { if (value != Selected) cell.Background = Brushes.Transparent; };
            Controls.OnActivate(cell, _ => Pick(value, byKeyboard: false));
            _cells.Add((value, cell, text));
            grid.Children.Add(cell);
        }
        Child = grid;
        Selected = selected;
        Paint();
        KeyDown += (_, e) =>
        {
            int i = _cells.FindIndex(c => c.Value == Selected);
            if (e.Key is Key.Right or Key.Down) i = Math.Min(_cells.Count - 1, i + 1);
            else if (e.Key is Key.Left or Key.Up) i = Math.Max(0, i - 1);
            else if (e.Key is Key.Space or Key.Enter) { }
            else return;
            e.Handled = true;
            Pick(_cells[i].Value, byKeyboard: true);
        };
    }

    /// <summary>An option was clicked or chosen with the keyboard (true), even the one already selected.</summary>
    public event Action<string, bool>? Picked;

    public string Selected { get; private set; }

    private void Pick(string value, bool byKeyboard)
    {
        Selected = value;
        Paint();
        Picked?.Invoke(value, byKeyboard);
    }

    private void Paint()
    {
        foreach (var (value, cell, label) in _cells)
        {
            bool on = value == Selected;
            cell.Background = on ? _p.Accent : Brushes.Transparent;
            label.Foreground = on ? _p.OnAccent : _p.Text;
            label.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            cell.Effect = on ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = .18 } : null;
        }
    }
}

/// <summary>A select box: the current choice and a chevron, opening a list under it.</summary>
internal sealed class Dropdown : DrawnButton
{
    private readonly Palette _p;
    private readonly IReadOnlyList<(string Value, string Label)> _options;
    private readonly TextBlock _text;
    private readonly Popup _popup;

    public Dropdown(Palette p, IReadOnlyList<(string Value, string Label)> options, string selected, string name)
    {
        _p = p;
        _options = options;
        Selected = selected;
        Kind = AutomationControlType.ComboBox;
        Background = p.Field;
        BorderBrush = p.FieldBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(10, 7, 11, 7);
        Focusable = true;
        Cursor = Cursors.Hand;
        FocusVisualStyle = Controls.FocusRing(p);
        AutomationProperties.SetName(this, name);
        var dock = new DockPanel();
        var chev = Icons.Chevron(p.Accent, up: false);
        chev.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(chev, Dock.Right);
        dock.Children.Add(chev);
        _text = new TextBlock { FontFamily = Controls.Ui, FontSize = 13, Foreground = p.Text, VerticalAlignment = VerticalAlignment.Center };
        dock.Children.Add(_text);
        Child = dock;
        _popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
        Paint();
        Controls.OnActivate(this, _ => Open());
        KeyDown += (_, e) =>
        {
            int i = Index();
            if (e.Key == Key.Down) i = Math.Min(_options.Count - 1, i + 1);
            else if (e.Key == Key.Up) i = Math.Max(0, i - 1);
            else return;
            e.Handled = true;
            Choose(_options[i].Value);
        };
    }

    public event Action<string>? Chosen;

    public string Selected { get; private set; }

    private int Index() => Math.Max(0, _options.ToList().FindIndex(o => o.Value == Selected));

    private void Paint()
    {
        _text.Text = _options.FirstOrDefault(o => o.Value == Selected).Label ?? "";
        AutomationProperties.SetItemStatus(this, _text.Text);
    }

    private void Choose(string value)
    {
        _popup.IsOpen = false;
        if (value == Selected) return;
        Selected = value;
        Paint();
        Chosen?.Invoke(value);
    }

    private void Open()
    {
        var list = new StackPanel();
        foreach (var (value, label) in _options)
        {
            bool on = value == Selected;
            var row = new DrawnButton
            {
                Kind = AutomationControlType.ListItem,
                Press = () => Choose(value),
                Padding = new Thickness(10, 7, 10, 7),
                CornerRadius = new CornerRadius(6),
                Background = on ? _p.Hover : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = label, FontFamily = Controls.Ui, FontSize = 13, Foreground = on ? _p.Accent : _p.Text, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal },
            };
            row.MouseEnter += (_, _) => row.Background = _p.Hover;
            row.MouseLeave += (_, _) => row.Background = on ? _p.Hover : Brushes.Transparent;
            AutomationProperties.SetName(row, label);
            row.MouseLeftButtonUp += (_, _) => Choose(value);
            list.Children.Add(row);
        }
        _popup.Child = new Border
        {
            Width = ActualWidth,
            Background = _p.Surface,
            BorderBrush = _p.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = list,
        };
        _popup.IsOpen = true;
    }
}
