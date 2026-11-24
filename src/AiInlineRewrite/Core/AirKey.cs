namespace AiInlineRewrite.Core;

/// <summary>The modifiers of a shortcut, with the values of the MOD_ flags of RegisterHotKey.</summary>
[Flags]
public enum KeyMods
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global key of the app: the AIR key and the off key are each one of these, never the same one. A tap (Right or
/// Left Ctrl or Shift, pressed and released alone) leaves the key working in chords. A shortcut (one key held with
/// Ctrl, Alt or Win, and Shift if wanted) is registered with Windows and belongs to AIR while it runs. Nothing uses
/// Ctrl+Alt: on the Brazilian ABNT2 keyboard that is AltGr. Saved by name: "RightCtrlTap", or "Ctrl+Shift+Space".
/// </summary>
public sealed record AirKey
{
    public static readonly AirKey RightCtrlTap = new(Vk.RControl, KeyMods.None, tap: true);
    public static readonly AirKey RightShiftTap = new(Vk.RShift, KeyMods.None, tap: true);
    public static readonly AirKey LeftCtrlTap = new(Vk.LControl, KeyMods.None, tap: true);
    public static readonly AirKey LeftShiftTap = new(Vk.LShift, KeyMods.None, tap: true);

    private AirKey(int key, KeyMods mods, bool tap)
    {
        Key = key;
        Mods = mods;
        IsTap = tap;
    }

    /// <summary>The virtual-key code: the tapped key, or the shortcut's key.</summary>
    public int Key { get; }

    /// <summary>The shortcut's modifiers; none for a tap.</summary>
    public KeyMods Mods { get; }

    public bool IsTap { get; }

    /// <summary>A shortcut as recorded; <see cref="AirKeys.Problem"/> says whether it can be used.</summary>
    public static AirKey Shortcut(KeyMods mods, int key) => new(key, mods, tap: false);

    public override string ToString() => AirKeys.Name(this);
}

public static class AirKeys
{
    /// <summary>The taps the settings offer, in their order; a recorded shortcut comes after them.</summary>
    public static readonly IReadOnlyList<AirKey> Taps =
        [AirKey.RightCtrlTap, AirKey.RightShiftTap, AirKey.LeftCtrlTap, AirKey.LeftShiftTap];

    // Shortcuts every app counts on: copy, paste, cut, undo, redo, select all, save, the other copy, close, the window menu.
    private static readonly (KeyMods Mods, int Key)[] Taken =
    [
        (KeyMods.Ctrl, 'C'), (KeyMods.Ctrl, 'V'), (KeyMods.Ctrl, 'X'), (KeyMods.Ctrl, 'Z'), (KeyMods.Ctrl, 'Y'),
        (KeyMods.Ctrl, 'A'), (KeyMods.Ctrl, 'S'), (KeyMods.Ctrl, Vk.Insert), (KeyMods.Alt, Vk.F4), (KeyMods.Alt, Vk.Space),
    ];

    private static readonly Dictionary<string, int> KeysByName = BuildKeysByName();

    /// <summary>
    /// The off key when none is chosen: a tap of Right Shift, or of Right Ctrl when Right Shift is the AIR key. The off
    /// key puts AIR to sleep (the bubble closes, nothing is written) until the AIR key wakes it.
    /// </summary>
    public static AirKey DefaultOffKey(AirKey airKey) =>
        airKey == AirKey.RightShiftTap ? AirKey.RightCtrlTap : AirKey.RightShiftTap;

    /// <summary>The AIR key becomes <paramref name="key"/>; when that was the off key, the off key takes the old AIR key.</summary>
    public static (AirKey Air, AirKey Off) ChooseAir(AirKey air, AirKey off, AirKey key) => (key, key == off ? air : off);

    /// <summary>The off key becomes <paramref name="key"/>; when that was the AIR key, the AIR key takes the old off key.</summary>
    public static (AirKey Air, AirKey Off) ChooseOff(AirKey air, AirKey off, AirKey key) => (key == air ? off : air, key);

    /// <summary>
    /// Why a shortcut cannot be used, or null when it can (a tap always can). Whether another app holds it is asked
    /// of Windows, outside this class.
    /// </summary>
    public static string? Problem(AirKey key)
    {
        if (key.IsTap) return null;
        if (KeyName(key.Key) is null)
            return "That key can't be part of a shortcut: use a letter, a digit, F1 to F24, Space, or a key such as Insert, Home or an arrow.";
        if ((key.Mods & (KeyMods.Ctrl | KeyMods.Alt | KeyMods.Win)) == KeyMods.None)
            return "Hold Ctrl, Alt or Win with the key: alone, or with Shift only, it belongs to the app you type in.";
        if (key.Mods.HasFlag(KeyMods.Ctrl) && key.Mods.HasFlag(KeyMods.Alt))
            return "Ctrl+Alt is AltGr on many keyboards, the Brazilian ABNT2 among them: use another pair.";
        if (Taken.Contains((key.Mods, key.Key)))
            return $"{Label(key)} is a shortcut every app uses: record another.";
        return null;
    }

    /// <summary>What the settings show: "Tap Right Ctrl", or the shortcut as "Ctrl+Shift+Space".</summary>
    public static string Label(AirKey key)
    {
        if (key.IsTap)
        {
            return key.Key switch
            {
                Vk.RShift => "Tap Right Shift",
                Vk.LControl => "Tap Left Ctrl",
                Vk.LShift => "Tap Left Shift",
                _ => "Tap Right Ctrl",
            };
        }
        string mods = ModsLabel(key.Mods);
        return (mods.Length > 0 ? mods + "+" : "") + (KeyName(key.Key) ?? "?");
    }

    /// <summary>The modifiers in the order Windows writes them: "Win+Ctrl+Alt+Shift"; empty for none.</summary>
    public static string ModsLabel(KeyMods mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(KeyMods.Win)) parts.Add("Win");
        if (mods.HasFlag(KeyMods.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyMods.Alt)) parts.Add("Alt");
        if (mods.HasFlag(KeyMods.Shift)) parts.Add("Shift");
        return string.Join("+", parts);
    }

    /// <summary>The key in a sentence: "tap Right Ctrl", "press Ctrl+Shift+Space".</summary>
    public static string Phrase(AirKey key) =>
        key.IsTap ? "tap " + Label(key)["Tap ".Length..] : "press " + Label(key);

    /// <summary>The name saved in settings.json: "RightCtrlTap", or the shortcut's label.</summary>
    public static string Name(AirKey key) => key.IsTap
        ? key.Key switch
        {
            Vk.RShift => nameof(AirKey.RightShiftTap),
            Vk.LControl => nameof(AirKey.LeftCtrlTap),
            Vk.LShift => nameof(AirKey.LeftShiftTap),
            _ => nameof(AirKey.RightCtrlTap),
        }
        : Label(key);

    /// <summary>
    /// A saved name back into a key, ignoring case and spaces: a tap's name, or modifiers and a key joined by "+".
    /// Null for anything else, such as the Pause, ScrollLock and Insert an older settings file may hold.
    /// </summary>
    public static AirKey? Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var tap = Taps.FirstOrDefault(t => string.Equals(Name(t), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (tap is not null) return tap;
        var parts = name.Split('+');
        if (parts.Length < 2) return null;
        var mods = KeyMods.None;
        foreach (string part in parts[..^1])
        {
            KeyMods mod = Squeeze(part) switch
            {
                "ctrl" => KeyMods.Ctrl,
                "alt" => KeyMods.Alt,
                "shift" => KeyMods.Shift,
                "win" => KeyMods.Win,
                _ => KeyMods.None,
            };
            if (mod == KeyMods.None || mods.HasFlag(mod)) return null;
            mods |= mod;
        }
        return KeysByName.TryGetValue(Squeeze(parts[^1]), out int key) ? AirKey.Shortcut(mods, key) : null;
    }

    // The keys a shortcut can end in: letters, digits, F1 to F24, Space, and the editing and navigation keys.
    private static string? KeyName(int vk) => vk switch
    {
        >= Vk.A and <= Vk.Z or >= Vk.D0 and <= Vk.D9 => ((char)vk).ToString(),
        >= Vk.F1 and <= Vk.F24 => "F" + (vk - Vk.F1 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        Vk.Space => "Space",
        Vk.Insert => "Insert",
        Vk.Delete => "Delete",
        Vk.Home => "Home",
        Vk.End => "End",
        Vk.PageUp => "Page Up",
        Vk.PageDown => "Page Down",
        Vk.Left => "Left",
        Vk.Up => "Up",
        Vk.Right => "Right",
        Vk.Down => "Down",
        Vk.Pause => "Pause",
        Vk.Scroll => "Scroll Lock",
        _ => null,
    };

    private static Dictionary<string, int> BuildKeysByName()
    {
        var byName = new Dictionary<string, int>();
        for (int vk = 1; vk < 256; vk++)
        {
            if (KeyName(vk) is { } name) byName[Squeeze(name)] = vk;
        }
        return byName;
    }

    private static string Squeeze(string text) => text.Replace(" ", "", StringComparison.Ordinal).Trim().ToLowerInvariant();
}

/// <summary>The virtual-key codes the app looks at.</summary>
public static class Vk
{
    public const int Back = 0x08, Tab = 0x09, Return = 0x0D, Shift = 0x10, Control = 0x11, Menu = 0x12, Pause = 0x13;
    public const int Capital = 0x14, Escape = 0x1B, Space = 0x20, PageUp = 0x21, PageDown = 0x22, End = 0x23, Home = 0x24;
    public const int Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28;
    public const int Insert = 0x2D, Delete = 0x2E, D0 = 0x30, D1 = 0x31, D9 = 0x39, A = 0x41, Z = 0x5A;
    public const int LWin = 0x5B, RWin = 0x5C, NumPad0 = 0x60, NumPad1 = 0x61, NumPad9 = 0x69, Divide = 0x6F;
    public const int F1 = 0x70, F4 = 0x73, F24 = 0x87;
    public const int Scroll = 0x91, LShift = 0xA0, RShift = 0xA1, LControl = 0xA2, RControl = 0xA3, LMenu = 0xA4, RMenu = 0xA5;
    public const int Oem1 = 0xBA, Oem8 = 0xDF, Oem102 = 0xE2, AbntC1 = 0xC1, AbntC2 = 0xC2;
    public const int V = 0x56, X = 0x58, Y = 0x59;

    public static bool IsModifier(int vk) =>
        vk is Shift or Control or Menu or LShift or RShift or LControl or RControl or LMenu or RMenu or LWin or RWin or Capital;

    /// <summary>1..9 for the digit keys of the top row and the number pad, 0 for anything else.</summary>
    public static int Digit(int vk) =>
        vk is >= D1 and <= D9 ? vk - D0 : vk is >= NumPad1 and <= NumPad9 ? vk - NumPad0 : 0;

    /// <summary>
    /// A key that likely changes the text: letters, digits, punctuation, space, Backspace, Delete, Enter, Tab. With
    /// Ctrl, Alt or Win held, only the editing shortcuts (paste, cut, undo, redo) and the deleting keys count.
    /// </summary>
    public static bool Edits(int vk, bool chord)
    {
        if (chord) return vk is V or X or Z or Y or Back or Delete;
        return vk is Back or Delete or Return or Tab or Space
            or (>= D0 and <= D9) or (>= A and <= Z) or (>= NumPad0 and <= Divide)
            or (>= Oem1 and <= Oem8) or Oem102 or AbntC1 or AbntC2;
    }
}
