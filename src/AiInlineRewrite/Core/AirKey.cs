namespace AiInlineRewrite.Core;

/// <summary>
/// A global key of the app: the AIR key and the off key are each one of these, never the same one. A tap key (Right
/// Ctrl, Right Shift) counts only when pressed and released alone, so the key keeps working in chords; a plain key
/// (Pause, Scroll Lock, Insert) is given up to AIR entirely. Nothing uses Ctrl+Alt: on the Brazilian ABNT2 keyboard
/// that is AltGr.
/// </summary>
public enum AirKey
{
    RightCtrlTap,
    RightShiftTap,
    Pause,
    ScrollLock,
    Insert,
}

public static class AirKeys
{
    public static readonly IReadOnlyList<AirKey> All =
        [AirKey.RightCtrlTap, AirKey.RightShiftTap, AirKey.Pause, AirKey.ScrollLock, AirKey.Insert];

    /// <summary>The virtual-key code a low-level keyboard hook reports for the key.</summary>
    public static int VirtualKey(AirKey key) => key switch
    {
        AirKey.RightShiftTap => Vk.RShift,
        AirKey.Pause => Vk.Pause,
        AirKey.ScrollLock => Vk.Scroll,
        AirKey.Insert => Vk.Insert,
        _ => Vk.RControl,
    };

    public static bool IsTap(AirKey key) => key is AirKey.RightCtrlTap or AirKey.RightShiftTap;

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

    public static string Label(AirKey key) => key switch
    {
        AirKey.RightShiftTap => "Tap Right Shift",
        AirKey.Pause => "Pause",
        AirKey.ScrollLock => "Scroll Lock",
        AirKey.Insert => "Insert",
        _ => "Tap Right Ctrl",
    };
}

/// <summary>The virtual-key codes the app looks at.</summary>
public static class Vk
{
    public const int Back = 0x08, Tab = 0x09, Return = 0x0D, Shift = 0x10, Control = 0x11, Menu = 0x12, Pause = 0x13;
    public const int Capital = 0x14, Escape = 0x1B, Space = 0x20, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28;
    public const int Insert = 0x2D, Delete = 0x2E, D0 = 0x30, D1 = 0x31, D9 = 0x39, A = 0x41, Z = 0x5A;
    public const int LWin = 0x5B, RWin = 0x5C, NumPad0 = 0x60, NumPad1 = 0x61, NumPad9 = 0x69, Divide = 0x6F;
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
