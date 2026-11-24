using AiInlineRewrite.Core;
using Xunit;

namespace AiInlineRewrite.Tests;

public class AirKeyTests
{
    private static AirKey Key(string name) => AirKeys.Parse(name) ?? throw new ArgumentException(name);

    [Theory]
    [InlineData("RightCtrlTap", Vk.RControl, "Tap Right Ctrl")]
    [InlineData("RightShiftTap", Vk.RShift, "Tap Right Shift")]
    [InlineData("LeftCtrlTap", Vk.LControl, "Tap Left Ctrl")]
    [InlineData("LeftShiftTap", Vk.LShift, "Tap Left Shift")]
    public void Each_tap_has_its_key_label_and_name(string name, int vk, string label)
    {
        var key = Key(name);
        Assert.True(key.IsTap);
        Assert.Equal(vk, key.Key);
        Assert.Equal(label, AirKeys.Label(key));
        Assert.Equal(name, AirKeys.Name(key));
        Assert.Null(AirKeys.Problem(key));
    }

    [Fact]
    public void The_settings_offer_the_four_taps_in_order()
        => Assert.Equal([AirKey.RightCtrlTap, AirKey.RightShiftTap, AirKey.LeftCtrlTap, AirKey.LeftShiftTap], AirKeys.Taps);

    [Theory]
    [InlineData("Ctrl+Shift+Space")]
    [InlineData("Win+Shift+K")]
    [InlineData("Alt+F9")]
    [InlineData("Ctrl+Page Down")]
    [InlineData("Ctrl+Shift+5")]
    [InlineData("Win+Ctrl+Alt+Shift+F24")]
    public void A_shortcut_is_saved_by_its_label_and_read_back(string name)
    {
        var key = Key(name);
        Assert.False(key.IsTap);
        Assert.Equal(name, AirKeys.Name(key));
        Assert.Equal(name, AirKeys.Label(key));
        Assert.Equal(key, AirKeys.Parse(AirKeys.Name(key)));
    }

    [Theory]
    [InlineData("ctrl + shift + space", "Ctrl+Shift+Space")] // any case and spacing
    [InlineData("Shift+Ctrl+Space", "Ctrl+Shift+Space")]     // any order: written the Windows way
    [InlineData("Ctrl+PageDown", "Ctrl+Page Down")]
    [InlineData("rightctrltap", "RightCtrlTap")]
    public void Reading_a_name_forgives_case_spaces_and_order(string written, string name)
        => Assert.Equal(name, AirKeys.Name(Key(written)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Pause")]           // the plain keys of older versions
    [InlineData("ScrollLock")]
    [InlineData("Insert")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Hyper+A")]
    [InlineData("Ctrl+Semicolon")]  // a key a shortcut cannot end in
    public void Anything_else_reads_as_no_key(string? name) => Assert.Null(AirKeys.Parse(name));

    [Theory]
    [InlineData("Ctrl+Shift+Space", false)]
    [InlineData("Win+Shift+K", false)]
    [InlineData("Alt+F9", false)]
    [InlineData("Ctrl+Shift+C", false)] // only Ctrl+C itself is the copy
    [InlineData("Shift+F9", true)]      // no Ctrl, Alt or Win
    [InlineData("Ctrl+Alt+K", true)]    // AltGr on ABNT2
    [InlineData("Ctrl+C", true)]
    [InlineData("Ctrl+V", true)]
    [InlineData("Ctrl+Z", true)]
    [InlineData("Ctrl+S", true)]
    [InlineData("Ctrl+Insert", true)]
    [InlineData("Alt+F4", true)]
    [InlineData("Alt+Space", true)]
    public void A_shortcut_is_refused_when_it_would_take_typing_AltGr_or_a_shortcut_every_app_uses(string name, bool refused)
        => Assert.Equal(refused, AirKeys.Problem(Key(name)) is not null);

    [Fact]
    public void A_shortcut_ending_in_a_key_it_cannot_use_is_refused()
    {
        var key = AirKey.Shortcut(KeyMods.Ctrl, Vk.Oem1);
        Assert.NotNull(AirKeys.Problem(key));
        Assert.Equal("Ctrl+?", AirKeys.Label(key));
    }

    [Fact]
    public void Nothing_uses_Ctrl_Alt()
    {
        Assert.DoesNotContain(AirKeys.Taps, k => AirKeys.Label(k).Contains("Alt", StringComparison.Ordinal));
        Assert.NotNull(AirKeys.Problem(AirKey.Shortcut(KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift, Vk.Space)));
    }

    [Theory]
    [InlineData("RightCtrlTap", "tap Right Ctrl")]
    [InlineData("Ctrl+Shift+Space", "press Ctrl+Shift+Space")]
    public void The_tray_says_how_to_use_the_key(string name, string phrase) => Assert.Equal(phrase, AirKeys.Phrase(Key(name)));

    [Theory]
    [InlineData("RightCtrlTap", "RightShiftTap")]
    [InlineData("LeftCtrlTap", "RightShiftTap")]
    [InlineData("Ctrl+Shift+Space", "RightShiftTap")]
    [InlineData("RightShiftTap", "RightCtrlTap")] // never the AIR key itself
    public void The_default_off_key_is_a_tap_of_Right_Shift_unless_that_is_the_AIR_key(string air, string off)
    {
        Assert.Equal(Key(off), AirKeys.DefaultOffKey(Key(air)));
        Assert.NotEqual(Key(air), AirKeys.DefaultOffKey(Key(air)));
    }

    [Theory]
    [InlineData("LeftCtrlTap", "LeftCtrlTap", "RightShiftTap")]           // a free key: the off key stays
    [InlineData("Ctrl+Shift+Space", "Ctrl+Shift+Space", "RightShiftTap")]
    [InlineData("RightShiftTap", "RightShiftTap", "RightCtrlTap")]        // the off key's: the two swap
    [InlineData("RightCtrlTap", "RightCtrlTap", "RightShiftTap")]         // the same again: nothing changes
    public void Choosing_the_AIR_key_never_leaves_both_on_one_key(string key, string air, string off)
    {
        var chosen = AirKeys.ChooseAir(AirKey.RightCtrlTap, AirKey.RightShiftTap, Key(key));
        Assert.Equal((Key(air), Key(off)), chosen);
        Assert.NotEqual(chosen.Air, chosen.Off);
    }

    [Theory]
    [InlineData("LeftShiftTap", "RightCtrlTap", "LeftShiftTap")]          // a free key: the AIR key stays
    [InlineData("Alt+F9", "RightCtrlTap", "Alt+F9")]
    [InlineData("RightCtrlTap", "RightShiftTap", "RightCtrlTap")]         // the AIR key's: the two swap
    [InlineData("RightShiftTap", "RightCtrlTap", "RightShiftTap")]        // the same again: nothing changes
    public void Choosing_the_off_key_never_leaves_both_on_one_key(string key, string air, string off)
    {
        var chosen = AirKeys.ChooseOff(AirKey.RightCtrlTap, AirKey.RightShiftTap, Key(key));
        Assert.Equal((Key(air), Key(off)), chosen);
        Assert.NotEqual(chosen.Air, chosen.Off);
    }
}
