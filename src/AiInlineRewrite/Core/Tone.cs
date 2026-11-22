namespace AiInlineRewrite.Core;

/// <summary>The versions AIR can write. The settings decide which are on and in what order.</summary>
public enum Tone
{
    Casual,
    Concise,
    Polished,
    Ornate,
    Diplomatic,

    /// <summary>Today's everyday spoken slang.</summary>
    Slang,

    /// <summary>The newest internet slang.</summary>
    Trendy,
}

public static class Tones
{
    /// <summary>Every tone, in the default order.</summary>
    public static readonly IReadOnlyList<Tone> All =
        [Tone.Casual, Tone.Concise, Tone.Polished, Tone.Ornate, Tone.Diplomatic, Tone.Slang, Tone.Trendy];

    /// <summary>On after a fresh install; the others wait until the user turns them on.</summary>
    public static bool OnByDefault(Tone tone) => tone is Tone.Casual or Tone.Concise or Tone.Polished;

    /// <summary>The one line the settings show under each tone.</summary>
    public static string Describe(Tone tone) => tone switch
    {
        Tone.Casual => "Fixes real mistakes and keeps your casual style; gonna and wanna stay.",
        Tone.Concise => "Correct and direct: hedges and filler words go.",
        Tone.Polished => "Formal, with fuller phrasing in plain everyday words.",
        Tone.Ornate => "Refined, literary vocabulary for very formal text.",
        Tone.Diplomatic => "Softens blunt or rude wording into polite English; keeps what you mean.",
        Tone.Slang => "Everyday slang, the way people talk today: gonna, my bad, no worries.",
        Tone.Trendy => "The newest internet slang: lowkey, ngl, no cap, it's giving.",
        _ => "",
    };
}
