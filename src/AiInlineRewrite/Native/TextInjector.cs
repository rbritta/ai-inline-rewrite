using System.Runtime.InteropServices;
using static AiInlineRewrite.Native.NativeMethods;

namespace AiInlineRewrite.Native;

/// <summary>
/// Applies a version by synthesizing real keystrokes: Ctrl+End, then Backspace over the sentence, then Ctrl+V of the
/// clipboard (which the caller set to the version). Going through the normal input pipeline is what makes it work
/// in web editors (contenteditable), where UI Automation's SetValue and synthetic Shift+Arrow selection are ignored.
/// </summary>
public static class TextInjector
{
    private static readonly int Size = Marshal.SizeOf<INPUT>();

    /// <summary>Tags AIR's own keystrokes ("AIR!"), so the keyboard hook can tell them from anyone else's.</summary>
    public static readonly IntPtr Mark = 0x41495221;

    /// <summary>
    /// True when Windows took the paste. False when it refused the keystrokes (the desktop got locked, or input is
    /// blocked); then the field may have lost the sentence without getting the version.
    /// </summary>
    public static bool DeleteAndPaste(int deleteCount)
    {
        // Release any modifier still held (the user may still be on the AIR key), so the synthetic keys are plain.
        Send([Up(VK_MENU), Up(VK_CONTROL), Up(VK_SHIFT), Up(VK_LWIN)]);
        Thread.Sleep(60);

        // To the end of the field (Ctrl+End): the sentence AIR rewrote is the field's last one.
        Send([Down(VK_CONTROL), Down(VK_END), Up(VK_END), Up(VK_CONTROL)]);
        Thread.Sleep(35);

        if (deleteCount > 0)
        {
            var del = new INPUT[deleteCount * 2];
            for (int i = 0; i < deleteCount; i++)
            {
                del[2 * i] = Down(VK_BACK);
                del[2 * i + 1] = Up(VK_BACK);
            }
            Send(del);
            Thread.Sleep(45); // let the editor take the deletions before the paste
        }

        INPUT[] paste = [Down(VK_CONTROL), Down(VK_V), Up(VK_V), Up(VK_CONTROL)];
        return Send(paste) == paste.Length;
    }

    private static uint Send(INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Size);

    private static INPUT Down(ushort vk) => Key(vk, 0);

    private static INPUT Up(ushort vk) => Key(vk, KEYEVENTF_KEYUP);

    private static INPUT Key(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = flags, dwExtraInfo = Mark } },
    };
}
