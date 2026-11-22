namespace AiInlineRewrite.Core;

public enum PickCommand
{
    /// <summary>Not in pick mode, or a modifier: nothing to do.</summary>
    None,

    /// <summary>The selection moved; the timer restarted.</summary>
    Moved,

    /// <summary>Apply the version at <see cref="PickOutcome.Index"/>. Pick mode stays on until the caller applies it.</summary>
    Apply,

    /// <summary>Esc: close the suggestions.</summary>
    Dismiss,

    /// <summary>A key that is not a pick key: pick mode closed and the key goes through to the app.</summary>
    Exit,
}

/// <param name="Swallow">True when the key belongs to pick mode and must not reach the app.</param>
/// <param name="Command">What the caller does next.</param>
/// <param name="Index">The version to apply, from 0, for <see cref="PickCommand.Apply"/>; -1 otherwise.</param>
public readonly record struct PickOutcome(bool Swallow, PickCommand Command, int Index = -1);

/// <summary>
/// Pick mode, as pure logic: the AIR key selects the first version; Down and Up move (Up on the first goes to the
/// last, Down on the last to the first) and restart the <see cref="TimeoutMs"/> timer; Enter applies the selected
/// version, 1..9 apply directly, Esc closes, and any other key closes pick mode and goes through to the app.
/// Modifiers alone do nothing; a key pressed with Ctrl, Alt or Win (AltGr included) leaves pick mode.
/// </summary>
public sealed class PickMode
{
    public const int TimeoutMs = 4000;

    public bool Active { get; private set; }

    public int Selected { get; private set; }

    public int Count { get; private set; }

    /// <summary>When the timer last (re)started.</summary>
    public long TimerStart { get; private set; }

    public void Enter(int count, long nowMs)
    {
        if (count <= 0) return;
        Active = true;
        Count = count;
        Selected = 0;
        TimerStart = nowMs;
    }

    public void Exit() => Active = false;

    public bool Expired(long nowMs) => Active && nowMs - TimerStart >= TimeoutMs;

    /// <summary>How long until the timer runs out, for the bar that shrinks under the bubble.</summary>
    public long RemainingMs(long nowMs) => Active ? Math.Max(0, TimeoutMs - (nowMs - TimerStart)) : 0;

    public PickOutcome KeyDown(int vk, bool chord, long nowMs)
    {
        if (!Active || Vk.IsModifier(vk)) return new(false, PickCommand.None);
        if (chord)
        {
            Exit();
            return new(false, PickCommand.Exit);
        }

        int digit = Vk.Digit(vk);
        if (digit > 0) return new(true, PickCommand.Apply, digit - 1);

        switch (vk)
        {
            case Vk.Down:
                Selected = (Selected + 1) % Count;
                TimerStart = nowMs;
                return new(true, PickCommand.Moved);
            case Vk.Up:
                Selected = (Selected - 1 + Count) % Count;
                TimerStart = nowMs;
                return new(true, PickCommand.Moved);
            case Vk.Return:
                return new(true, PickCommand.Apply, Selected);
            case Vk.Escape:
                Exit();
                return new(true, PickCommand.Dismiss);
            default:
                Exit();
                return new(false, PickCommand.Exit);
        }
    }
}
