using AiInlineRewrite.Core;
using Xunit;

namespace AiInlineRewrite.Tests;

public class AirKeyDetectorTests
{
    [Fact]
    public void A_quick_press_and_release_of_Right_Ctrl_alone_is_a_tap()
    {
        var d = new AirKeyDetector(AirKey.RightCtrlTap);
        Assert.False(d.KeyDown(Vk.RControl, 0));
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
    [InlineData(AirKey.Pause, Vk.Pause)]
    [InlineData(AirKey.ScrollLock, Vk.Scroll)]
    [InlineData(AirKey.Insert, Vk.Insert)]
    public void A_plain_key_fires_on_its_key_down_once_while_held(AirKey key, int vk)
    {
        var d = new AirKeyDetector(key);
        Assert.False(d.IsTap);
        Assert.True(d.KeyDown(vk, 0));
        Assert.False(d.KeyDown(vk, 500)); // auto-repeat
        Assert.False(d.KeyUp(vk, 600));
        Assert.True(d.KeyDown(vk, 700));
    }

    [Theory]
    [InlineData(AirKey.RightCtrlTap, Vk.RControl, true, "Tap Right Ctrl")]
    [InlineData(AirKey.RightShiftTap, Vk.RShift, true, "Tap Right Shift")]
    [InlineData(AirKey.Pause, Vk.Pause, false, "Pause")]
    [InlineData(AirKey.ScrollLock, Vk.Scroll, false, "Scroll Lock")]
    [InlineData(AirKey.Insert, Vk.Insert, false, "Insert")]
    public void Each_choice_has_its_key_kind_and_label(AirKey key, int vk, bool tap, string label)
    {
        Assert.Equal(vk, AirKeys.VirtualKey(key));
        Assert.Equal(tap, AirKeys.IsTap(key));
        Assert.Equal(label, AirKeys.Label(key));
    }

    [Fact]
    public void Nothing_uses_Ctrl_Alt()
        => Assert.DoesNotContain(AirKeys.All, k => AirKeys.Label(k).Contains("Alt", StringComparison.Ordinal));

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
