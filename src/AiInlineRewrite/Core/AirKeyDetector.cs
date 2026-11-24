namespace AiInlineRewrite.Core;

/// <summary>
/// Tells when a tap key (the AIR key or the off key) fires, from the raw key-down and key-up events of the keyboard
/// hook: on its key-up, when it went down and up alone within <see cref="TapMaxMs"/>: no other key in between, no
/// other key already held, no mouse button meanwhile, not held too long. A shortcut is not seen here: Windows reports
/// it (RegisterHotKey), so a detector made for one never fires.
/// </summary>
public sealed class AirKeyDetector
{
    public const int TapMaxMs = 600;

    // A key held this long without its key-up is assumed stuck (its key-up went to an elevated window).
    private const int StaleHeldMs = 3000;

    private readonly int _vk;
    private readonly Dictionary<int, long> _held = [];
    private bool _down;
    private long _downAt;

    public AirKeyDetector(AirKey key)
    {
        Key = key;
        _vk = key.IsTap ? key.Key : 0;
    }

    public AirKey Key { get; }

    /// <summary>True while a tap is possible: the key is down alone and nothing has cancelled it yet.</summary>
    public bool Armed { get; private set; }

    /// <summary>The tapped key itself; never true for a shortcut.</summary>
    public bool IsKey(int vk) => _vk != 0 && vk == _vk;

    /// <summary>A key went down. A tap never fires here.</summary>
    public void KeyDown(int vk, long nowMs)
    {
        if (!IsKey(vk))
        {
            Armed = false; // a chord, not a tap
            _held[vk] = nowMs;
            return;
        }
        if (_down)
        {
            // auto-repeat while held
            if (nowMs - _downAt > TapMaxMs) Armed = false;
            return;
        }
        _down = true;
        _downAt = nowMs;
        Armed = !OtherKeyHeld(nowMs);
    }

    /// <summary>A key went up. Returns true when a tap completes.</summary>
    public bool KeyUp(int vk, long nowMs)
    {
        if (!IsKey(vk))
        {
            _held.Remove(vk);
            return false;
        }
        _down = false;
        bool fired = Armed && nowMs - _downAt <= TapMaxMs;
        Armed = false;
        return fired;
    }

    /// <summary>A mouse button or the wheel while the key is down: Ctrl+click, Ctrl+wheel, not a tap.</summary>
    public void MouseInput() => Armed = false;

    private bool OtherKeyHeld(long nowMs)
    {
        foreach (var (key, at) in _held.ToList())
        {
            if (nowMs - at > StaleHeldMs) _held.Remove(key);
        }
        return _held.Count > 0;
    }
}
