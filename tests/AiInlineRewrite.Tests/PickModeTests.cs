using AiInlineRewrite.Core;
using Xunit;

namespace AiInlineRewrite.Tests;

public class PickModeTests
{
    private static PickMode Entered(int count = 3, long now = 1000)
    {
        var pick = new PickMode();
        pick.Enter(count, now);
        return pick;
    }

    [Fact]
    public void Entering_selects_the_first_version()
    {
        var pick = Entered();
        Assert.True(pick.Active);
        Assert.Equal(0, pick.Selected);
        Assert.Equal(3, pick.Count);
    }

    [Fact]
    public void Entering_with_no_versions_does_nothing()
    {
        var pick = new PickMode();
        pick.Enter(0, 0);
        Assert.False(pick.Active);
    }

    [Fact]
    public void Down_moves_and_wraps_from_the_last_to_the_first()
    {
        var pick = Entered();
        Assert.Equal(new PickOutcome(true, PickCommand.Moved), pick.KeyDown(Vk.Down, false, 1100));
        Assert.Equal(1, pick.Selected);
        pick.KeyDown(Vk.Down, false, 1200);
        pick.KeyDown(Vk.Down, false, 1300);
        Assert.Equal(0, pick.Selected);
    }

    [Fact]
    public void Up_on_the_first_goes_to_the_last()
    {
        var pick = Entered();
        Assert.True(pick.KeyDown(Vk.Up, false, 1100).Swallow);
        Assert.Equal(2, pick.Selected);
    }

    [Fact]
    public void Enter_applies_the_selected_version_and_is_swallowed()
    {
        var pick = Entered();
        pick.KeyDown(Vk.Down, false, 1100);
        Assert.Equal(new PickOutcome(true, PickCommand.Apply, 1), pick.KeyDown(Vk.Return, false, 1200));
        Assert.True(pick.Active); // the caller exits once it applied
    }

    [Theory]
    [InlineData(Vk.D1, 0)]
    [InlineData(Vk.D1 + 2, 2)]
    [InlineData(Vk.NumPad1 + 1, 1)]
    [InlineData(Vk.D9, 8)] // beyond the versions: the caller shakes the bubble
    public void A_digit_applies_its_version_directly(int vk, int index)
        => Assert.Equal(new PickOutcome(true, PickCommand.Apply, index), Entered().KeyDown(vk, false, 1100));

    [Fact]
    public void Esc_dismisses_and_leaves_pick_mode()
    {
        var pick = Entered();
        Assert.Equal(new PickOutcome(true, PickCommand.Dismiss), pick.KeyDown(Vk.Escape, false, 1100));
        Assert.False(pick.Active);
    }

    [Theory]
    [InlineData(Vk.A)]
    [InlineData(Vk.Space)]
    [InlineData(Vk.Left)]
    [InlineData(Vk.Back)]
    public void Any_other_key_leaves_pick_mode_and_goes_through(int vk)
    {
        var pick = Entered();
        Assert.Equal(new PickOutcome(false, PickCommand.Exit), pick.KeyDown(vk, false, 1100));
        Assert.False(pick.Active);
    }

    [Theory]
    [InlineData(Vk.Shift)]
    [InlineData(Vk.LControl)]
    [InlineData(Vk.RControl)]
    [InlineData(Vk.RMenu)]
    [InlineData(Vk.LWin)]
    [InlineData(Vk.Capital)]
    public void A_modifier_alone_does_nothing(int vk)
    {
        var pick = Entered();
        Assert.Equal(new PickOutcome(false, PickCommand.None), pick.KeyDown(vk, false, 1100));
        Assert.True(pick.Active);
    }

    [Fact]
    public void A_key_in_a_chord_leaves_pick_mode_even_a_digit()
    {
        // AltGr+Q types "/" on ABNT2: Ctrl and Alt are down, so it is not a pick key.
        var pick = Entered();
        Assert.Equal(new PickOutcome(false, PickCommand.Exit), pick.KeyDown(Vk.D1, true, 1100));
        Assert.False(pick.Active);
    }

    [Fact]
    public void The_timer_runs_out_after_four_seconds_and_each_arrow_restarts_it()
    {
        var pick = Entered(now: 0);
        Assert.False(pick.Expired(3999));
        pick.KeyDown(Vk.Down, false, 3000);
        Assert.False(pick.Expired(6999));
        Assert.Equal(1000, pick.RemainingMs(6000));
        Assert.True(pick.Expired(7000));
    }

    [Fact]
    public void Outside_pick_mode_no_key_is_touched()
    {
        var pick = new PickMode();
        Assert.Equal(new PickOutcome(false, PickCommand.None), pick.KeyDown(Vk.Return, false, 0));
        Assert.Equal(0, pick.RemainingMs(0));
        Assert.False(pick.Expired(10_000));
    }
}
