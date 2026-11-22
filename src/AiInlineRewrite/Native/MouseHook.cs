namespace AiInlineRewrite.Native;

/// <summary>
/// A low-level mouse hook installed only while a tap key is down (at most 600 ms), to notice Ctrl+click and
/// Ctrl+wheel, which are not taps. It never runs the rest of the time, so it cannot slow the pointer down.
/// </summary>
public sealed class MouseHook : IDisposable
{
    private readonly NativeMethods.LowLevelProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    public MouseHook()
    {
        _proc = HookCallback;
    }

    /// <summary>A button went down or the wheel turned.</summary>
    public event Action? Input;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN
                or NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_MOUSEWHEEL or NativeMethods.WM_MOUSEHWHEEL)
            {
                try { Input?.Invoke(); }
                catch (Exception) { /* never break the hook chain */ }
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
