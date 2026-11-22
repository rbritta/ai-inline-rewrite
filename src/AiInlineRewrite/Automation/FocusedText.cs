using System.Windows;
using System.Windows.Automation;

namespace AiInlineRewrite.Automation;

/// <param name="FullText">The field's text; empty for a password field.</param>
/// <param name="CaretPx">The caret, or the field's bottom left when the caret is unknown, in physical screen pixels.</param>
/// <param name="IsPassword">A password field: AIR never reads it.</param>
public readonly record struct FocusSnapshot(string FullText, Rect CaretPx, bool IsPassword);

/// <summary>
/// Reads the focused text field through UI Automation: its text and where the caret is. Works in Win32, WinForms,
/// WPF and UWP text controls and in most browser inputs; where a provider is missing it reports nothing and AIR
/// waits. Password fields are reported as such and never read.
/// </summary>
public static class FocusedText
{
    public static bool TryRead(out FocusSnapshot snapshot)
    {
        snapshot = default;
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el is null) return false;

            if (el.Current.IsPassword)
            {
                snapshot = new FocusSnapshot(string.Empty, Rect.Empty, true);
                return true;
            }

            string? text = ReadText(el);
            if (text is null) return false;

            Rect caret = ReadCaretPx(el, out bool hasCaret);
            if (!hasCaret) caret = FieldBottomLeft(el);
            snapshot = new FocusSnapshot(text, caret, false);
            return true;
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return false;
        }
    }

    private static string? ReadText(AutomationElement el)
    {
        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            return ((ValuePattern)vp).Current.Value ?? string.Empty;

        if (el.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
            return ((TextPattern)tp).DocumentRange.GetText(-1) ?? string.Empty;

        return null;
    }

    private static Rect ReadCaretPx(AutomationElement el, out bool hasCaret)
    {
        hasCaret = false;
        try
        {
            if (el.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
            {
                var ranges = ((TextPattern)tp).GetSelection();
                if (ranges is { Length: > 0 })
                {
                    Rect[] rects = ranges[0].GetBoundingRectangles();
                    if (rects.Length > 0)
                    {
                        var r = rects[^1];
                        hasCaret = true;
                        // A zero-width caret still gives a usable x, y and line height.
                        return new Rect(r.Right, r.Y, 0, r.Height > 0 ? r.Height : 18);
                    }
                }
            }
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // a provider without a usable selection
        }
        return Rect.Empty;
    }

    // Without a caret, the bubble goes under the field's left edge.
    private static Rect FieldBottomLeft(AutomationElement el)
    {
        var b = el.Current.BoundingRectangle;
        return b.IsEmpty ? Rect.Empty : new Rect(b.Left + 8, b.Bottom - 22, 0, 18);
    }
}
