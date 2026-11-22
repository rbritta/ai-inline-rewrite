using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AiInlineRewrite.Native;
using WinForms = System.Windows.Forms;

namespace AiInlineRewrite.Ui;

/// <summary>
/// The floating window that holds the bubble. It never takes the focus (clicking it leaves the caret in the app
/// being typed in), stays on top, has no taskbar button, and is dragged by its bar.
/// </summary>
public sealed class BubbleWindow : Window
{
    /// <summary>Transparent room around the bubble for its shadow.</summary>
    public const double Pad = 20;

    private readonly System.Windows.Controls.Border _host = new() { Margin = new Thickness(Pad) };
    private BubbleView? _view;
    private Rect _anchorPx = Rect.Empty;

    private bool _dragging;
    private bool _moved;
    private Point _dragStartPx;
    private double _startLeft, _startTop;

    public BubbleWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        Title = "AI Inline Rewrite";
        Content = _host;
        SizeChanged += (_, _) => Reposition();
        MouseMove += OnDragMove;
        MouseLeftButtonUp += (_, _) => EndDrag();
        LostMouseCapture += (_, _) => EndDrag();
    }

    /// <summary>The first move of a drag: the bubble stops following the text cursor.</summary>
    public event Action? DragStarted;

    /// <summary>A drag ended; the bubble's top-left corner, in DIPs.</summary>
    public event Action<double, double>? Dropped;

    public bool IsDragging => _dragging;

    /// <summary>The bubble's top-left corner (without the shadow room), in DIPs.</summary>
    public Point BubbleTopLeft => new(Left + Pad, Top + Pad);

    /// <summary>The bubble (without the shadow room) and the work area of its monitor, in DIPs.</summary>
    public (Rect Bubble, Rect Area) Bounds()
    {
        var bubble = new Rect(Left + Pad, Top + Pad, Math.Max(0, ActualWidth - 2 * Pad), Math.Max(0, ActualHeight - 2 * Pad));
        var s = Scale();
        var px = WinForms.Screen.FromPoint(new System.Drawing.Point((int)(bubble.Left * s.X), (int)(bubble.Top * s.Y))).WorkingArea;
        return (bubble, new Rect(px.Left / s.X, px.Top / s.Y, px.Width / s.X, px.Height / s.Y));
    }

    public BubbleView Render(BubbleState state, Palette palette, BubbleActions actions)
    {
        _view = BubbleView.Create(state, palette, actions);
        _host.Child = _view.Root;
        return _view;
    }

    public void Shake() => _view?.Shake();

    /// <summary>Sits under the caret (above it when there is no room below), at every render.</summary>
    public void Anchor(Rect caretPx, bool glide = false)
    {
        _anchorPx = caretPx;
        Reposition(glide);
    }

    /// <summary>Stays at a fixed place (Follow the text cursor off).</summary>
    public void PlaceAt(double left, double top, bool glide = false)
    {
        _anchorPx = Rect.Empty;
        MoveTo(left - Pad, top - Pad, glide);
    }

    public void StartDrag(MouseButtonEventArgs e)
    {
        _dragging = true;
        _moved = false;
        _dragStartPx = PointToScreen(e.GetPosition(this));
        _startLeft = Left;
        _startTop = Top;
        Mouse.Capture(this);
        e.Handled = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return NativeMethods.MA_NOACTIVATE;
        }
        return IntPtr.Zero;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var now = PointToScreen(e.GetPosition(this));
        var scale = Scale();
        double dx = (now.X - _dragStartPx.X) / scale.X, dy = (now.Y - _dragStartPx.Y) / scale.Y;
        if (!_moved && Math.Abs(dx) + Math.Abs(dy) < 3) return;
        if (!_moved)
        {
            _moved = true;
            _anchorPx = Rect.Empty;
            DragStarted?.Invoke();
        }
        Left = _startLeft + dx;
        Top = _startTop + dy;
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        if (Mouse.Captured == this) Mouse.Capture(null);
        if (_moved) Dropped?.Invoke(Left + Pad, Top + Pad);
    }

    private Vector Scale()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Vector(dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1, dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1);
    }

    // Under the caret like design/experience.html: 18 px left of it, 10 px below its line; above when no room.
    private void Reposition(bool glide = false)
    {
        if (_dragging || _anchorPx.IsEmpty) return;
        var s = Scale();
        var screen = WinForms.Screen.FromPoint(new System.Drawing.Point((int)_anchorPx.Left, (int)_anchorPx.Top)).WorkingArea;
        double w = ActualWidth > 0 ? ActualWidth - 2 * Pad : 440, h = ActualHeight > 0 ? ActualHeight - 2 * Pad : 80;

        double area = screen.Left / s.X, areaTop = screen.Top / s.Y, areaRight = screen.Right / s.X, areaBottom = screen.Bottom / s.Y;
        double left = _anchorPx.Left / s.X - 18;
        double top = _anchorPx.Bottom / s.Y + 10;
        if (top + h > areaBottom - 4) top = _anchorPx.Top / s.Y - h - 8;
        left = Math.Max(area + 4, Math.Min(left, areaRight - w - 4));
        top = Math.Max(areaTop + 4, Math.Min(top, areaBottom - h - 4));
        MoveTo(left - Pad, top - Pad, glide);
    }

    private void MoveTo(double left, double top, bool glide)
    {
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        if (!glide || !IsVisible)
        {
            Left = left;
            Top = top;
            return;
        }
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(250);
        var ax = new DoubleAnimation(left, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var ay = new DoubleAnimation(top, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        ax.Completed += (_, _) => Left = left;
        ay.Completed += (_, _) => Top = top;
        BeginAnimation(LeftProperty, ax);
        BeginAnimation(TopProperty, ay);
    }
}
