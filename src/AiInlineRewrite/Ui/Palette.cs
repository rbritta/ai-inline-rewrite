using System.Windows.Media;

namespace AiInlineRewrite.Ui;

/// <summary>
/// The colors of one theme (design/experience.html). Controls use solid Sky, never a gradient; the gradient is only
/// in the Q mark itself.
/// </summary>
public sealed class Palette
{
    private Palette(bool dark) => Dark = dark;

    public bool Dark { get; }

    public required Brush Surface { get; init; }
    public required Brush Surface2 { get; init; }
    public required Brush Border { get; init; }
    public required Brush Text { get; init; }
    public required Brush Muted { get; init; }
    public required Brush Hover { get; init; }
    public required Brush Accent { get; init; }
    public required Brush OnAccent { get; init; }
    public required Brush KeyBg { get; init; }
    public required Brush Tint { get; init; }
    public required Brush DelFg { get; init; }
    public required Brush DelBg { get; init; }
    public required Brush InsFg { get; init; }
    public required Brush InsBg { get; init; }
    public required Brush Field { get; init; }
    public required Brush FieldBorder { get; init; }
    public required Brush Desk { get; init; }
    public required Color Shadow { get; init; }

    public static Palette Light { get; } = new(false)
    {
        Surface = B("#FFFFFF"), Surface2 = B("#F7FAFC"), Border = B("#E3E3E3"), Text = B("#1F1F1F"), Muted = B("#6B6B6B"),
        Hover = B("#140284C7"), Accent = B("#0284C7"), OnAccent = B("#FFFFFF"), KeyBg = B("#F3F3F3"), Tint = B("#F0F9FF"),
        DelFg = B("#B91C1C"), DelBg = B("#FEE2E2"), InsFg = B("#15803D"), InsBg = B("#DCFCE7"), Field = B("#FFFFFF"),
        FieldBorder = B("#CFD6DE"), Desk = B("#DBE4EE"), Shadow = Color.FromArgb(0x30, 0x02, 0x20, 0x40),
    };

    public static Palette DarkTheme { get; } = new(true)
    {
        Surface = B("#2B2B2B"), Surface2 = B("#262B31"), Border = B("#3A3A3A"), Text = B("#F3F3F3"), Muted = B("#A3A3A3"),
        Hover = B("#1F7DD3FC"), Accent = B("#7DD3FC"), OnAccent = B("#0B1220"), KeyBg = B("#383838"), Tint = B("#147DD3FC"),
        DelFg = B("#FCA5A5"), DelBg = B("#2EF87171"), InsFg = B("#86EFAC"), InsBg = B("#2E4ADE80"), Field = B("#2B2B2B"),
        FieldBorder = B("#4A4F55"), Desk = B("#18222D"), Shadow = Color.FromArgb(0x66, 0, 0, 0),
    };

    public static Palette For(bool dark) => dark ? DarkTheme : Light;

    private static SolidColorBrush B(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
