using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfPath = System.Windows.Shapes.Path;

namespace AiInlineRewrite.Ui;

/// <summary>
/// The app's vector glyphs, drawn in code so they stay sharp at any DPI: the Q mark (pen nib and ink wave, the
/// same drawing tools/make_logo.py renders into the .ico files), the gear, the follow arrow and the small controls.
/// </summary>
public static class Icons
{
    public static readonly Color SkyLight = Color.FromRgb(0x7D, 0xD3, 0xFC);
    public static readonly Color SkyDeep = Color.FromRgb(0x02, 0x84, 0xC7);

    private static readonly Geometry QNib = BuildNib();
    private static readonly Geometry QWave = Freeze(Geometry.Parse("M10 57 Q17 51 24 57 T38 57 T52 57"));
    private static readonly Geometry Gear = Freeze(Geometry.Parse("F0 " + GearPath()));
    private static readonly Geometry Arrow = Freeze(Geometry.Parse("M20 4 L4 10.6 L10.9 13.1 L13.4 20 Z"));

    /// <summary>The Q mark: the Sky gradient, or one flat color (the grey "off" look).</summary>
    public static FrameworkElement Q(double size, Brush? flat = null)
    {
        Brush fill = flat ?? SkyGradient();
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 64, 64))));
        group.Children.Add(new GeometryDrawing(fill, null, QNib));
        group.Children.Add(new GeometryDrawing(null, RoundPen(fill, 3.8), QWave));
        group.Freeze();
        return new Image { Source = new DrawingImage(group), Width = size, Height = size, SnapsToDevicePixels = true };
    }

    /// <summary>The gear that opens the settings.</summary>
    public static FrameworkElement GearIcon(Brush fill, double size = 16) =>
        Box(24, size, new WpfPath { Data = Gear, Fill = fill });

    /// <summary>The follow arrow: filled while the bubble follows the text cursor, an outline while it stays put.</summary>
    public static FrameworkElement FollowArrow(Brush color, bool filled, double size = 16) =>
        Box(24, size, new WpfPath
        {
            Data = Arrow,
            Fill = filled ? color : Brushes.Transparent,
            Stroke = color,
            StrokeThickness = 2.1,
            StrokeLineJoin = PenLineJoin.Round,
        });

    public static FrameworkElement Chevron(Brush color, bool up) =>
        Stroke(10, 10, up ? "M2 6.5 L5 3.5 L8 6.5" : "M2 3.5 L5 6.5 L8 3.5", color, 1.6);

    public static FrameworkElement Close(Brush color) => Stroke(12, 12, "M2 2 L10 10 M10 2 L2 10", color, 1.6);

    public static FrameworkElement Check(Brush color) => Stroke(18, 18, "M4.8 9.4 L7.8 12.3 L13.4 6.4", color, 2);

    public static FrameworkElement Grip(Brush color)
    {
        var canvas = new Canvas { Width = 10, Height = 16 };
        foreach (double y in new[] { 3.0, 8.0, 13.0 })
        {
            foreach (double x in new[] { 3.0, 7.0 })
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 2.6, Height = 2.6, Fill = color };
                Canvas.SetLeft(dot, x - 1.3);
                Canvas.SetTop(dot, y - 1.3);
                canvas.Children.Add(dot);
            }
        }
        return canvas;
    }

    public static LinearGradientBrush SkyGradient()
    {
        var brush = new LinearGradientBrush(SkyLight, SkyDeep, new Point(6, 4), new Point(58, 60))
        {
            MappingMode = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    private static FrameworkElement Stroke(double w, double h, string data, Brush color, double thickness)
    {
        var canvas = new Canvas { Width = w, Height = h };
        canvas.Children.Add(new WpfPath
        {
            Data = Geometry.Parse(data),
            Stroke = color,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });
        return canvas;
    }

    // A glyph drawn on a units x units canvas, scaled to size.
    private static FrameworkElement Box(double units, double size, UIElement content)
    {
        var canvas = new Canvas { Width = units, Height = units };
        canvas.Children.Add(content);
        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
    }

    private static Pen RoundPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }

    // The nib: its outline less the slit and the breather hole, turned 40 degrees around (32, 31).
    private static Geometry BuildNib()
    {
        var outline = Geometry.Parse("M32 56 L21 33 Q21 15 32 6 Q43 15 43 33 Z");
        var slit = new LineGeometry(new Point(32, 35), new Point(32, 53))
            .GetWidenedPathGeometry(new Pen(Brushes.Black, 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var hole = new EllipseGeometry(new Point(32, 31), 3.6, 3.6);
        var holes = Geometry.Combine(slit, hole, GeometryCombineMode.Union, null);
        var nib = Geometry.Combine(outline, holes, GeometryCombineMode.Exclude, null);
        var turned = new PathGeometry();
        turned.AddGeometry(nib);
        turned.Transform = new RotateTransform(40, 32, 31);
        return Freeze(turned.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute));
    }

    // Eight teeth around a ring with a hole (even-odd), like design/experience.html.
    private static string GearPath()
    {
        const double outer = 10, inner = 7.4;
        var pts = new List<(double Angle, double Radius)>();
        for (int k = 0; k < 8; k++)
        {
            double c = k * Math.PI / 4;
            pts.Add((c - .38, inner));
            pts.Add((c - .2, outer));
            pts.Add((c + .2, outer));
            pts.Add((c + .38, inner));
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pts.Count; i++)
        {
            double x = 12 + pts[i].Radius * Math.Cos(pts[i].Angle), y = 12 + pts[i].Radius * Math.Sin(pts[i].Angle);
            sb.Append(i == 0 ? "M" : " L").Append(x.ToString("0.##", inv)).Append(' ').Append(y.ToString("0.##", inv));
        }
        sb.Append(" Z M15 12 A3 3 0 1 0 9 12 A3 3 0 1 0 15 12 Z");
        return sb.ToString();
    }

    private static Geometry Freeze(Geometry g)
    {
        g.Freeze();
        return g;
    }
}
