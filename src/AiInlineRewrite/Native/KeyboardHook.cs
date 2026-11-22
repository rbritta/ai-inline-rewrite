using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AiInlineRewrite.Native;

/// <param name="Vk">The virtual-key code, left and right modifiers told apart (VK_RCONTROL, VK_RSHIFT...).</param>
/// <param name="Down">Pressed, not released.</param>
/// <param name="Injected">Synthesized by a program (including AIR's own paste), not typed.</param>
/// <param name="TimeMs">When the hook saw it (Environment.TickCount64, milliseconds since Windows started).</param>
/// <param name="Own">Synthesized by AIR itself (its paste carries <see cref="TextInjector.Mark"/>).</param>
public readonly record struct KeyEvent(int Vk, bool Down, bool Injected, long TimeMs, bool Own = false);

/// <summary>
/// The global low-level keyboard hook. It sees key codes only: it never reads or stores text (the text comes from
/// the focused field, through UI Automation, at a pause). The handler returns true to swallow a key, which AIR
/// does only for the AIR key when it is a plain key, and for the pick keys while pick mode is on.
/// The callback runs on the thread that installed the hook (the UI thread), so it must return quickly.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    public KeyboardHook()
    {
        _proc = HookCallback; // keep the delegate alive for the hook's lifetime
    }

    public Func<KeyEvent, bool>? Handler { get; set; }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "The keyboard hook could not be installed");
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Handler is { } handler)
        {
            int msg = (int)wParam;
            bool down = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            bool up = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
            if (down || up)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                bool injected = (data.flags & NativeMethods.LLKHF_INJECTED) != 0;
                var e = new KeyEvent((int)data.vkCode, down, injected, Environment.TickCount64, injected && data.dwExtraInfo == TextInjector.Mark);
                try
                {
                    if (handler(e)) return 1; // swallowed: the app never sees the key
                }
                catch (Exception)
                {
                    // never break the hook chain
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
