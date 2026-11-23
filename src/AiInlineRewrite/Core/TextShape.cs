using System.Text.RegularExpressions;

namespace AiInlineRewrite.Core;

/// <summary>
/// Shapes the model's answer to what the user typed: one clean sentence, the user's own final punctuation and the
/// user's own first-letter case. The prompts ask for both, and this enforces them, since a small local model does
/// not always follow.
/// </summary>
public static partial class TextShape
{
    // Labels a model sometimes echoes from the prompt ("Corrected: ...").
    [GeneratedRegex(@"^\s*(Corrected|Concise|Polished|Rewritten|Diplomatic|Slang|Trendy|Formal|Sentence|Answer|Output)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex EchoedLabel();

    // Kept upper whatever the user typed: the pronoun I, and day and month names ("May" left out: it is also a verb).
    [GeneratedRegex(@"^(I\b|(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday|January|February|March|April|June|July|August|September|October|November|December)\b)")]
    private static partial Regex KeepUpper();

    // The end of a sentence followed by another one: an end mark, spaces, then a capital (or a quote and a capital).
    // Common abbreviations are not an end ("Mr. Smith", "e.g. This").
    [GeneratedRegex(@"(?<!\b(?:Mr|Mrs|Ms|Dr|Prof|Sr|Jr|St|vs|etc|e\.g|i\.e))[.!?]+(?=\s+[""“]?[A-Z])")]
    private static partial Regex SentenceBreak();

    // An end mark inside what the user typed, before more words in any case ("corrido!! mas amanhã"): the user wrote
    // more than one sentence, so the answer may hold more than one too.
    [GeneratedRegex(@"[.!?]+\s+[""“]?\p{L}")]
    private static partial Regex InnerEnd();

    private static bool IsEnd(char c) => c is '.' or '!' or '?';

    /// <summary>
    /// Does the answer hold a letter outside the Latin alphabet (Chinese, Cyrillic, Greek...)? A small model asked
    /// for literary English sometimes drifts into another language mid-sentence; such a version is never offered.
    /// </summary>
    public static bool HasForeignLetters(string text)
    {
        foreach (char c in text)
        {
            if (!char.IsLetter(c)) continue;
            bool latin = c <= '\u024F' || c is >= '\u1E00' and <= '\u1EFF';
            if (!latin) return true;
        }
        return false;
    }

    /// <summary>
    /// The first non-empty line of the answer, without an echoed label or surrounding quotes. While streaming, a
    /// partial answer goes through here too, so it never shows a label or a stray quote.
    /// </summary>
    public static string Clean(string answer)
    {
        if (string.IsNullOrEmpty(answer)) return string.Empty;
        string line = answer.TrimStart();
        int nl = line.IndexOfAny(['\r', '\n']);
        if (nl >= 0) line = line[..nl];
        line = EchoedLabel().Replace(line, "", 1).Trim();
        if (line.Length > 0 && line[0] is '"' or '“') line = line[1..];
        if (line.Length > 0 && line[^1] is '"' or '”') line = line[..^1];
        return line.Trim();
    }

    /// <summary>
    /// The answer up to the end of its first sentence, when the user typed a single sentence: a model sometimes
    /// appends a second one ("...by tomorrow? I feel it's important...") that the user never wrote.
    /// </summary>
    public static string FirstSentence(string original, string text)
    {
        if (InnerEnd().IsMatch(original.Trim())) return text;
        var m = SentenceBreak().Match(text);
        return m.Success ? text[..(m.Index + m.Length)] : text;
    }

    /// <summary>
    /// Mirrors the user's final punctuation exactly: whatever terminal ".", "!" or "?" the original ends with (or
    /// none), the answer ends the same way. Then mirrors the case of the first letter (<see cref="MatchStartCase"/>).
    /// </summary>
    public static string MatchPunctuation(string original, string text)
    {
        string o = original.TrimEnd();
        char? want = o.Length > 0 && IsEnd(o[^1]) ? o[^1] : null;

        string t = FirstSentence(original, Clean(text));
        int end = t.Length;
        while (end > 0 && IsEnd(t[end - 1])) end--;
        string core = t[..end].TrimEnd();
        return MatchStartCase(original, want is null ? core : core + want.Value);
    }

    /// <summary>
    /// The first letter of the answer takes the case of the user's first letter. Kept upper anyway: the pronoun I,
    /// words with more capitals (AIR, iPhone) and day or month names.
    /// </summary>
    public static string MatchStartCase(string original, string text)
    {
        int oi = IndexOfLetter(original), ti = IndexOfLetter(text);
        if (oi < 0 || ti < 0) return text;

        char oc = original[oi];
        string rest = text[ti..];
        int space = rest.IndexOfAny([' ', '\t', '\r', '\n']);
        string word = space < 0 ? rest : rest[..space];

        char head;
        if (oc == char.ToUpperInvariant(oc))
            head = char.ToUpperInvariant(text[ti]);
        else if (KeepUpper().IsMatch(rest) || word.Skip(1).Any(char.IsUpper))
            head = text[ti];
        else
            head = char.ToLowerInvariant(text[ti]);
        return text[..ti] + head + text[(ti + 1)..];
    }

    private static int IndexOfLetter(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (char.IsLetter(s[i])) return i;
        return -1;
    }
}
