using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AiInlineRewrite.Native;

namespace AiInlineRewrite.Ui;

/// <summary>
/// The card that floats beside the settings window when a Layout or Theme option is clicked: the bubble in that
/// layout or theme, with the user's tones, and one line explaining the option. It never takes the focus.
/// </summary>
public sealed class PreviewWindow : Window
{
    private const double Pad = 24;
    private const double CardWidth = 484;

    private readonly Canvas _canvas = new();
    private readonly TranslateTransform _slide = new();

    public PreviewWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "AI Inline Rewrite preview";
        Content = new Border { Padding = new Thickness(Pad), Child = _canvas };
    }

    /// <summary>
    /// Shows <paramref name="bubble"/> with the explanation, its arrow pointing at <paramref name="sectionCenterY"/>,
    /// on the left of <paramref name="settings"/> (on its right when there is no room on the left).
    /// </summary>
    public void Present(BubbleState bubble, Palette bubblePalette, Palette chrome, Inline[] explanation, Rect settings, double sectionCenterY,
        (Rect Bubble, Rect Area)? beside = null)
    {
        var view = BubbleView.Create(bubble, bubblePalette);
        var stage = new Border
        {
            Background = bubblePalette.Desk,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 16, 10, 16),
            Child = new Border { HorizontalAlignment = HorizontalAlignment.Center, Child = view.Root },
        };
        var text = new TextBlock { FontFamily = Controls.Ui, FontSize = 12.5, Foreground = chrome.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 9, 4, 2), LineHeight = 18 };
        text.Inlines.AddRange(explanation);
        var stack = new StackPanel();
        stack.Children.Add(stage);
        stack.Children.Add(text);
        var card = new Border
        {
            Width = CardWidth,
            Background = chrome.Surface,
            BorderBrush = chrome.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Direction = 270, Opacity = chrome.Dark ? .45 : .22 },
        };
        card.Measure(new Size(CardWidth, double.PositiveInfinity));
        double h = card.DesiredSize.Height;

        // Left of the settings window when it fits; right of it when the bubble is on its left, so the bubble shows.
        var area = beside?.Area ?? SystemParameters.WorkArea;
        bool left = settings.Left - CardWidth - 12 >= area.Left;
        if (beside is { } b && b.Bubble.Left < settings.Left && settings.Right + 12 + CardWidth <= area.Right) left = false;
        double cardLeft = left ? settings.Left - CardWidth - 12 : settings.Right + 12;
        double cardTop = Math.Max(area.Top + 8, Math.Min(sectionCenterY - h / 2, area.Bottom - h - 8));

        // The arrow: a turned square on the side facing the settings window, at the section's height.
        var tip = new Border
        {
            Width = 12,
            Height = 12,
            Background = chrome.Surface,
            BorderBrush = chrome.Border,
            BorderThickness = left ? new Thickness(0, 1, 1, 0) : new Thickness(1, 0, 0, 1),
            RenderTransformOrigin = new Point(.5, .5),
            RenderTransform = new RotateTransform(45),
        };
        double tipTop = Math.Max(12, Math.Min(sectionCenterY - cardTop - 6, h - 24));

        _canvas.Children.Clear();
        _canvas.Width = CardWidth;
        _canvas.Height = h;
        Canvas.SetLeft(card, 0);
        Canvas.SetTop(card, 0);
        _canvas.Children.Add(card);
        Canvas.SetLeft(tip, left ? CardWidth - 6 : -6);
        Canvas.SetTop(tip, tipTop);
        _canvas.Children.Add(tip);

        Left = cardLeft - Pad;
        Top = cardTop - Pad;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        _slide.X = left ? 8 : -8;
        _canvas.RenderTransform = _slide;
        var dur = TimeSpan.FromMilliseconds(180);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur));
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dur));
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        fade.Completed += (_, _) =>
        {
            if (Opacity == 0) Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (msg != NativeMethods.WM_MOUSEACTIVATE) return IntPtr.Zero;
            handled = true;
            return NativeMethods.MA_NOACTIVATE;
        });
    }
}
