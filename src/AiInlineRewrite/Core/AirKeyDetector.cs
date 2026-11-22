namespace AiInlineRewrite.Core;

/// <summary>
/// Tells when the AIR key fires, from the raw key-down and key-up events of the keyboard hook.
/// A tap key fires on its key-up when it went down and up alone within <see cref="TapMaxMs"/>: no other key in
/// between, no other key already held, no mouse button meanwhile, not held too long. A plain key fires on its
/// key-down (auto-repeat ignored).
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
        _vk = AirKeys.VirtualKey(key);
        IsTap = AirKeys.IsTap(key);
    }

    public AirKey Key { get; }

    public bool IsTap { get; }

    /// <summary>True while a tap is possible: the key is down alone and nothing has cancelled it yet.</summary>
    public bool Armed { get; private set; }

    public bool IsAirKey(int vk) => vk == _vk;

    /// <summary>A key went down. Returns true when a plain AIR key fires.</summary>
    public bool KeyDown(int vk, long nowMs)
    {
        if (vk != _vk)
        {
            Armed = false; // a chord, not a tap
            _held[vk] = nowMs;
            return false;
        }
        if (_down)
        {
            // auto-repeat while held
            if (IsTap && nowMs - _downAt > TapMaxMs) Armed = false;
            return false;
        }
        _down = true;
        _downAt = nowMs;
        if (!IsTap) return true;
        Armed = !OtherKeyHeld(nowMs);
        return false;
    }

    /// <summary>A key went up. Returns true when a tap completes.</summary>
    public bool KeyUp(int vk, long nowMs)
    {
        if (vk != _vk)
        {
            _held.Remove(vk);
            return false;
        }
        _down = false;
        bool fired = IsTap && Armed && nowMs - _downAt <= TapMaxMs;
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
