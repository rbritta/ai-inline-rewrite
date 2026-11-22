using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AiInlineRewrite.Core;

namespace AiInlineRewrite.Ui;

/// <param name="Tone">The tone this version is written in.</param>
/// <param name="Text">The version so far (it streams in).</param>
/// <param name="Done">The model finished this version.</param>
/// <param name="Error">The model could not write it; <paramref name="Text"/> says why.</param>
public sealed record BubbleRow(Tone Tone, string Text, bool Done, bool Error = false);

/// <summary>Everything the bubble shows at one moment.</summary>
public sealed class BubbleState
{
    public BubbleLayout Layout { get; init; } = BubbleLayout.Card;

    public IReadOnlyList<BubbleRow> Rows { get; init; } = [];

    /// <summary>What the user typed, for the diff of English corrections.</summary>
    public string Source { get; init; } = "";

    /// <summary>A translation: the versions show as plain text, not as a diff against the Portuguese.</summary>
    public bool Translated { get; init; }

    public string Status { get; init; } = "Ready";

    public string Hint { get; init; } = "Type in Portuguese or English.";

    public bool Pick { get; init; }

    public int Selected { get; init; }

    public long PickRemainingMs { get; init; }

    public bool Follow { get; init; } = true;

    /// <summary>False in the settings preview: no hover, no clicks.</summary>
    public bool Interactive { get; init; } = true;
}

/// <summary>What the bubble's controls do; any of them may be left out.</summary>
public sealed class BubbleActions
{
    public Action<int>? RowClick { get; init; }
    public Action? Gear { get; init; }
    public Action? Follow { get; init; }

    /// <summary>The mouse went down on the bar (not on a button): the start of a drag.</summary>
    public Action<FrameworkElement, MouseButtonEventArgs>? BarDown { get; init; }
}

/// <summary>
/// Builds the bubble in its two layouts (design/experience.html): Card, each version under its tone name with the
/// bar on top; Compact, one line per version with the tone on the right and the bar at the bottom. The bar holds the
/// Q, "AIR", the status, the follow arrow and the gear. The settings preview builds it too, in any theme.
/// </summary>
public sealed class BubbleView
{
    private static readonly FontFamily Ui = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    private readonly Palette _p;
    private readonly BubbleState _s;
    private readonly BubbleActions _a;
    private readonly TranslateTransform _shake = new();

    private BubbleView(BubbleState state, Palette palette, BubbleActions? actions)
    {
        _s = state;
        _p = palette;
        _a = actions ?? new BubbleActions();
        Root = Build();
        Root.RenderTransform = _shake;
    }

    public FrameworkElement Root { get; }

    public static BubbleView Create(BubbleState state, Palette palette, BubbleActions? actions = null) => new(state, palette, actions);

    /// <summary>A short side-to-side shake: there is nothing to pick, or that version is not ready.</summary>
    public void Shake()
    {
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(300) };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-4, KeyTime.FromPercent(.25)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromPercent(.75)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        _shake.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private FrameworkElement Build()
    {
        bool card = _s.Layout == BubbleLayout.Card;
        var stack = new StackPanel();
        if (card) stack.Children.Add(Bar(card));
        else stack.Children.Add(new Border { Height = 4 });

        if (_s.Rows.Count == 0)
            stack.Children.Add(HintRow(card));
        else
            for (int i = 0; i < _s.Rows.Count; i++) stack.Children.Add(card ? CardRow(i) : CompactRow(i));

        if (!card) stack.Children.Add(Bar(card));
        stack.Children.Add(Timer());

        double radius = card ? 8 : 10;
        var body = new Border
        {
            Width = card ? 440 : 430,
            Background = _p.Surface,
            BorderBrush = _s.Pick ? _p.Accent : _p.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius),
            Child = stack,
        };
        // Children are clipped to the rounded corners.
        stack.SizeChanged += (_, e) => stack.Clip = new RectangleGeometry(new Rect(e.NewSize), radius - 1, radius - 1);
        TextOptions.SetTextFormattingMode(body, TextFormattingMode.Display);

        // The shadow sits on a twin behind, so the text itself keeps ClearType.
        var shadow = new Border
        {
            Background = _p.Surface,
            CornerRadius = new CornerRadius(radius),
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Direction = 270, Color = _p.Shadow.WithAlpha(255), Opacity = _p.Shadow.A / 255.0 },
        };
        var grid = new Grid();
        grid.Children.Add(shadow);
        grid.Children.Add(body);
        return grid;
    }

    private FrameworkElement Bar(bool card)
    {
        var dock = new DockPanel { LastChildFill = true };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bool follow = _s.Follow;
        tools.Children.Add(IconButton(Icons.FollowArrow(follow ? _p.Accent : _p.Muted, follow), 22,
            follow ? "Follows the text cursor · click to keep the bubble here" : "Stays here · click to follow the text cursor", _a.Follow,
            "Follow the text cursor", () => follow));
        tools.Children.Add(IconButton(Icons.GearIcon(_p.Muted), 24, "Settings", _a.Gear, "Settings"));
        DockPanel.SetDock(tools, Dock.Right);
        dock.Children.Add(tools);

        var q = Icons.Q(card ? 16 : 12);
        q.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(q);
        dock.Children.Add(new TextBlock
        {
            Text = "AIR",
            FontFamily = Ui,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = _p.Text,
            Margin = new Thickness(card ? 8 : 6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        dock.Children.Add(Status());

        var bar = new Border
        {
            Child = dock,
            Background = Brushes.Transparent, // so the whole bar takes the mouse for dragging
            Padding = card ? new Thickness(12, 7, 8, 7) : new Thickness(12, 3, 6, 3),
            BorderBrush = _p.Border,
            BorderThickness = card ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0),
            Margin = card ? default : new Thickness(0, 4, 0, 0),
        };
        if (_s.Interactive)
        {
            bar.Cursor = Cursors.SizeAll;
            bar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && FindButton(d) is not null) return;
                _a.BarDown?.Invoke(bar, e);
            };
        }
        return bar;
    }

    private static FrameworkElement? FindButton(DependencyObject d)
    {
        for (var cur = d; cur is not null; cur = cur is Visual ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
        {
            if (cur is FrameworkElement { Tag: "button" } fe) return fe;
        }
        return null;
    }

    private TextBlock Status()
    {
        var tb = new TextBlock
        {
            FontFamily = Ui,
            FontSize = 12,
            Foreground = _p.Muted,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        tb.Inlines.Add(new Run("· "));
        if (_s.Pick)
        {
            int n = _s.Rows.Count;
            tb.Inlines.Add(Kbd("↑"));
            tb.Inlines.Add(Kbd("↓"));
            tb.Inlines.Add(new Run(" "));
            tb.Inlines.Add(Kbd("Enter"));
            tb.Inlines.Add(new Run(" · "));
            tb.Inlines.Add(Kbd("1"));
            if (n > 1)
            {
                tb.Inlines.Add(new Run("-"));
                tb.Inlines.Add(Kbd(n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            tb.Inlines.Add(new Run(" · "));
            tb.Inlines.Add(Kbd("Esc"));
            // the keycaps are drawn apart from the text, so a screen reader would hear only "· · - ·"
            string numbers = n > 1 ? $"1-{n.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "1";
            System.Windows.Automation.AutomationProperties.SetName(tb, $"· ↑ ↓ Enter · {numbers} · Esc");
        }
        else
        {
            tb.Inlines.Add(new Run(_s.Status));
        }
        return tb;
    }

    private InlineUIContainer Kbd(string key) => new(new Border
    {
        Background = _p.KeyBg,
        BorderBrush = _p.Border,
        BorderThickness = new Thickness(1, 1, 1, 2),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(4, 0, 4, 0),
        Margin = new Thickness(1, 0, 1, 0),
        Child = new TextBlock { Text = key, FontFamily = Mono, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = _p.Text },
    })
    { BaselineAlignment = BaselineAlignment.Center };

    private FrameworkElement IconButton(FrameworkElement icon, double width, string tip, Action? click, string name, Func<bool>? isOn = null)
    {
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var b = new DrawnButton
        {
            Press = _s.Interactive ? click : null,
            IsOn = isOn,
            Tag = "button",
            Width = width,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Child = icon,
            ToolTip = _s.Interactive ? tip : null,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        if (_s.Interactive)
        {
            b.Cursor = Cursors.Hand;
            b.MouseEnter += (_, _) => b.Background = _p.Hover;
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += (_, e) => e.Handled = true;
            b.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                click?.Invoke();
            };
        }
        return b;
    }

    private Border KeyChip(int i) => new()
    {
        MinWidth = 20,
        Height = 20,
        Padding = new Thickness(5, 0, 5, 0),
        CornerRadius = new CornerRadius(4),
        Background = _s.Pick ? _p.Accent : _p.KeyBg,
        BorderBrush = _s.Pick ? Brushes.Transparent : _p.Border,
        BorderThickness = new Thickness(1, 1, 1, 2),
        VerticalAlignment = VerticalAlignment.Top,
        Child = new TextBlock
        {
            Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontFamily = Mono,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = _s.Pick ? _p.OnAccent : _p.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private Border RowShell(int i, UIElement content, Thickness padding)
    {
        bool sel = _s.Pick && i == _s.Selected;
        var r = _s.Rows[i];
        var row = new DrawnButton
        {
            Kind = System.Windows.Automation.Peers.AutomationControlType.ListItem,
            Press = _s.Interactive ? () => _a.RowClick?.Invoke(i) : null,
            Child = content,
            Padding = padding,
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = sel ? _p.Accent : Brushes.Transparent,
            Background = sel ? _p.Hover : Brushes.Transparent,
        };
        System.Windows.Automation.AutomationProperties.SetName(row, $"{i + 1} {r.Tone}: {r.Text}");
        System.Windows.Automation.AutomationProperties.SetItemStatus(row, sel ? "Selected" : "");
        if (_s.Interactive)
        {
            row.Cursor = Cursors.Hand;
            row.MouseEnter += (_, _) => row.Background = _p.Hover;
            row.MouseLeave += (_, _) => row.Background = sel ? _p.Hover : Brushes.Transparent;
            row.MouseLeftButtonDown += (_, e) => e.Handled = true;
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _a.RowClick?.Invoke(i);
            };
        }
        return row;
    }

    private FrameworkElement CardRow(int i)
    {
        var r = _s.Rows[i];
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var chip = KeyChip(i);
        chip.Margin = new Thickness(0, 2, 10, 0);
        grid.Children.Add(chip);

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = r.Tone.ToString().ToUpperInvariant(),
            FontFamily = Ui,
            FontSize = 10.5,
            Foreground = _p.Muted,
            Margin = new Thickness(0, 0, 0, 1),
        });
        text.Children.Add(VersionText(r));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return RowShell(i, grid, new Thickness(9, 9, 12, 9));
    }

    private FrameworkElement CompactRow(int i)
    {
        var r = _s.Rows[i];
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chip = KeyChip(i);
        chip.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(chip);
        var text = VersionText(r);
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var tone = new TextBlock
        {
            Text = r.Tone.ToString(),
            FontFamily = Ui,
            FontSize = 11,
            Foreground = _p.Muted,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tone, 2);
        grid.Children.Add(tone);
        return RowShell(i, grid, new Thickness(9, 6, 12, 6));
    }

    private TextBlock VersionText(BubbleRow r)
    {
        var tb = new TextBlock { FontFamily = Ui, FontSize = 14, Foreground = _p.Text, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        if (r.Error)
        {
            tb.Foreground = _p.Muted;
            tb.FontStyle = FontStyles.Italic;
            tb.Text = r.Text;
            return tb;
        }
        if (!r.Done || _s.Translated)
        {
            tb.Inlines.Add(new Run(r.Text));
        }
        else
        {
            bool first = true;
            foreach (var run in WordDiff.Compute(_s.Source, r.Text))
            {
                if (!first) tb.Inlines.Add(new Run(" "));
                first = false;
                var inline = new Run(run.Text);
                if (run.Kind == DiffKind.Removed)
                {
                    inline.Foreground = _p.DelFg;
                    inline.Background = _p.DelBg;
                    inline.TextDecorations = TextDecorations.Strikethrough;
                }
                else if (run.Kind == DiffKind.Added)
                {
                    inline.Foreground = _p.InsFg;
                    inline.Background = _p.InsBg;
                }
                tb.Inlines.Add(inline);
            }
        }
        if (!r.Done) tb.Inlines.Add(new Run("▍") { Foreground = _p.Accent });
        return tb;
    }

    private FrameworkElement HintRow(bool card) => new Border
    {
        Padding = card ? new Thickness(12, 9, 12, 9) : new Thickness(12, 6, 12, 6),
        Child = new TextBlock { Text = _s.Hint, FontFamily = Ui, FontSize = 14, Foreground = _p.Muted, TextWrapping = TextWrapping.Wrap },
    };

    private FrameworkElement Timer()
    {
        var bar = new Border { Height = 2, Background = _s.Pick ? _p.Accent : Brushes.Transparent };
        if (_s.Pick && _s.PickRemainingMs > 0)
        {
            var scale = new ScaleTransform(_s.PickRemainingMs / (double)PickMode.TimeoutMs, 1);
            bar.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(_s.PickRemainingMs)));
        }
        return bar;
    }
}

internal static class ColorExtensions
{
    public static Color WithAlpha(this Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
