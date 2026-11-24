using System.Windows.Interop;
using AiInlineRewrite.Core;

namespace AiInlineRewrite.Native;

/// <summary>
/// AIR's shortcuts, registered with Windows (RegisterHotKey) on a message-only window of the UI thread. Windows keeps
/// a registered shortcut from the app in front, reports it here once per press (no auto-repeat), and refuses one that
/// another app already holds. Taps are not shortcuts: the keyboard hook sees those (AirKeyDetector).
/// </summary>
internal sealed class GlobalHotKeys : IDisposable
{
    // an id no shortcut of AIR's uses, for asking whether a shortcut is free
    private const int ProbeId = 0xBFFF;

    private readonly HwndSource _window;
    private readonly Dictionary<int, AirKey> _held = [];

    public GlobalHotKeys()
    {
        _window = new HwndSource(new HwndSourceParameters("AIR shortcuts") { ParentWindow = new IntPtr(-3), WindowStyle = 0 }); // HWND_MESSAGE
        _window.AddHook(OnMessage);
    }

    /// <summary>A registered shortcut was pressed; its id.</summary>
    public event Action<int>? Pressed;

    /// <summary>Registers <paramref name="key"/> under <paramref name="id"/>; false when another app holds it.</summary>
    public bool Register(int id, AirKey key)
    {
        Unregister(id);
        if (key.IsTap) return false;
        if (!NativeMethods.RegisterHotKey(_window.Handle, id, (uint)key.Mods | NativeMethods.MOD_NOREPEAT, (uint)key.Key)) return false;
        _held[id] = key;
        return true;
    }

    public void Unregister(int id)
    {
        if (_held.Remove(id)) NativeMethods.UnregisterHotKey(_window.Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (int id in _held.Keys.ToList()) Unregister(id);
    }

    /// <summary>Is <paramref name="vk"/> with exactly <paramref name="mods"/> one of the registered shortcuts?</summary>
    public bool Holds(int vk, KeyMods mods) => _held.Values.Any(k => k.Key == vk && k.Mods == mods);

    /// <summary>Whether no other app holds <paramref name="key"/>: it is registered for a moment and let go.</summary>
    public bool IsFree(AirKey key)
    {
        if (!NativeMethods.RegisterHotKey(_window.Handle, ProbeId, (uint)key.Mods | NativeMethods.MOD_NOREPEAT, (uint)key.Key)) return false;
        NativeMethods.UnregisterHotKey(_window.Handle, ProbeId);
        return true;
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            handled = true;
            Pressed?.Invoke(wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.Dispose();
    }
}
