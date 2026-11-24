using AiInlineRewrite.Core;
using Xunit;

namespace AiInlineRewrite.Tests;

public class AirKeyDetectorTests
{
    [Fact]
    public void A_quick_press_and_release_of_Right_Ctrl_alone_is_a_tap()
    {
        var d = new AirKeyDetector(AirKey.RightCtrlTap);
        d.KeyDown(Vk.RControl, 0);
        Assert.True(d.Armed);
        Assert.True(d.KeyUp(Vk.RControl, 150));
        Assert.False(d.Armed);
    }

    [Fact]
    public void Right_Ctrl_with_another_key_is_a_chord_not_a_tap()
    {
        var d = new AirKeyDetector(AirKey.RightCtrlTap);
        d.KeyDown(Vk.RControl, 0);
        d.KeyDown(Vk.A, 50); // Ctrl+A
        d.KeyUp(Vk.A, 80);
        Assert.False(d.KeyUp(Vk.RControl, 120));
    }

    [Fact]
    public void Held_too_long_is_not_a_tap()
    {
        var d = new AirKeyDetector(AirKey.RightCtrlTap);
        d.KeyDown(Vk.RControl, 0);
        d.KeyDown(Vk.RControl, 500); // auto-repeat
        d.KeyDown(Vk.RControl, 700);
        Assert.False(d.KeyUp(Vk.RControl, 750));
    }

    [Fact]
    public void Released_after_the_limit_is_not_a_tap()
    {
        var d = new AirKeyDetector(AirKey.RightCtrlTap);
        d.KeyDown(Vk.RControl, 0);
        Assert.False(d.KeyUp(Vk.RControl, AirKeyDetector.TapMaxMs + 1));
    }

    [Fact]
    public void A_mouse_click_or_the_wheel_while_held_cancels_the_tap()
    {
        var d = new AirKeyDetector(AirKey.RightCtrlTap);
        d.KeyDown(Vk.RControl, 0);
        d.MouseInput(); // Ctrl+click, Ctrl+wheel
        Assert.False(d.KeyUp(Vk.RControl, 100));
    }

    [Fact]
    public void Another_key_already_held_cancels_the_tap()
    {
        var d = new AirKeyDetector(AirKey.RightCtrlTap);
        d.KeyDown(Vk.LShift, 0); // Shift held first
        d.KeyDown(Vk.RControl, 100);
        Assert.False(d.Armed);
        Assert.False(d.KeyUp(Vk.RControl, 200));
        d.KeyUp(Vk.LShift, 250);
        d.KeyDown(Vk.RControl, 300); // and alone again it works
        Assert.True(d.KeyUp(Vk.RControl, 400));
    }

    [Fact]
    public void A_key_whose_release_was_never_seen_stops_blocking_after_a_while()
    {
        var d = new AirKeyDetector(AirKey.RightCtrlTap);
        d.KeyDown(Vk.LShift, 0); // its key-up went to an elevated window
        d.KeyDown(Vk.RControl, 5000);
        Assert.True(d.KeyUp(Vk.RControl, 5100));
    }

    [Fact]
    public void AltGr_on_ABNT2_is_not_Right_Ctrl()
    {
        // AltGr sends a fake Left Ctrl and Right Alt; neither is the AIR key.
        var d = new AirKeyDetector(AirKey.RightCtrlTap);
        d.KeyDown(Vk.LControl, 0);
        d.KeyDown(Vk.RMenu, 0);
        Assert.False(d.KeyUp(Vk.RMenu, 80));
        Assert.False(d.KeyUp(Vk.LControl, 80));
        Assert.False(d.Armed);
    }

    [Fact]
    public void Right_Shift_tap_works_the_same_way()
    {
        var d = new AirKeyDetector(AirKey.RightShiftTap);
        d.KeyDown(Vk.RShift, 0);
        Assert.True(d.KeyUp(Vk.RShift, 100));
        Assert.False(d.KeyUp(Vk.RControl, 100));
    }

    [Theory]
    [InlineData("LeftCtrlTap", Vk.LControl)]
    [InlineData("LeftShiftTap", Vk.LShift)]
    public void Left_Ctrl_and_Left_Shift_taps_work_the_same_way(string name, int vk)
    {
        var d = new AirKeyDetector(AirKeys.Parse(name)!);
        d.KeyDown(vk, 0);
        Assert.True(d.KeyUp(vk, 100));
        d.KeyDown(vk, 1000);
        d.KeyDown(Vk.A, 1040); // a chord
        d.KeyUp(Vk.A, 1070);
        Assert.False(d.KeyUp(vk, 1100));
    }

    [Fact]
    public void AltGr_on_ABNT2_is_not_a_tap_of_Left_Ctrl()
    {
        // AltGr's fake Left Ctrl comes with Right Alt, so it is a chord.
        var d = new AirKeyDetector(AirKey.LeftCtrlTap);
        d.KeyDown(Vk.LControl, 0);
        d.KeyDown(Vk.RMenu, 0);
        Assert.False(d.KeyUp(Vk.RMenu, 80));
        Assert.False(d.KeyUp(Vk.LControl, 80));
    }

    [Fact]
    public void A_detector_for_a_shortcut_never_fires()
    {
        // Windows reports a shortcut (RegisterHotKey); the hook leaves it alone
        var d = new AirKeyDetector(AirKey.Shortcut(KeyMods.Ctrl | KeyMods.Shift, Vk.Space));
        Assert.False(d.IsKey(Vk.Space));
        d.KeyDown(Vk.Space, 0);
        Assert.False(d.Armed);
        Assert.False(d.KeyUp(Vk.Space, 50));
    }

    [Fact]
    public void A_capital_typed_with_Right_Shift_is_not_the_off_key()
    {
        var off = new AirKeyDetector(AirKeys.DefaultOffKey(AirKey.RightCtrlTap));
        off.KeyDown(Vk.RShift, 0);
        off.KeyDown(Vk.A, 40); // Shift+A
        off.KeyUp(Vk.A, 70);
        Assert.False(off.KeyUp(Vk.RShift, 110));

        off.KeyDown(Vk.RShift, 1000); // alone: a tap
        Assert.True(off.KeyUp(Vk.RShift, 1100));
    }

    [Theory]
    [InlineData(Vk.A, false, true)]
    [InlineData(Vk.Space, false, true)]
    [InlineData(Vk.Back, false, true)]
    [InlineData(Vk.Oem1, false, true)]
    [InlineData(Vk.Left, false, false)]   // navigation does not change the text
    [InlineData(Vk.Shift, false, false)]
    [InlineData(Vk.V, true, true)]        // Ctrl+V pastes
    [InlineData(Vk.A, true, false)]       // Ctrl+A selects
    public void Edits_tells_keys_that_change_the_text(int vk, bool chord, bool edits)
        => Assert.Equal(edits, Vk.Edits(vk, chord));
}
