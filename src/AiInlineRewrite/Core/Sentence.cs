using System.Text.RegularExpressions;

namespace AiInlineRewrite.Core;

/// <summary>
/// The sentence the user is typing. Typing happens at the end of the field, so it is the text after the last
/// sentence break, with the end marks the user typed. A break is a line break, or ". ! ?" followed by a space and a
/// capital letter; an end mark before a lowercase word ("corrido!! mas amanhã...", the way many people write in
/// chat) or with no space after it ("3.5", "site.com") does not end the sentence.
/// </summary>
public static partial class Sentence
{
    // "Mr. Smith" is not a break; Portuguese capitals (É, Á) start a sentence too.
    [GeneratedRegex(@"(?<!\b(?:Mr|Mrs|Ms|Dr|Prof|Sr|Sra|Jr|St|vs|etc))[.!?]+[ \t]+(?=[""“]?\p{Lu})|[\r\n]+")]
    private static partial Regex Break();

    public static string ExtractCurrent(string fullText)
    {
        if (string.IsNullOrEmpty(fullText)) return string.Empty;

        string t = fullText.TrimEnd();
        int start = 0;
        foreach (Match m in Break().Matches(t)) start = m.Index + m.Length;
        return t[start..].TrimStart();
    }

    /// <summary>
    /// How many Backspaces, pressed at the very end of the field, delete <paramref name="sentence"/> and whatever
    /// follows it (trailing spaces or line breaks): -1 when the field no longer holds the sentence. A "\r\n" line
    /// break is two characters but one Backspace.
    /// </summary>
    public static int BackspacesToReplace(string fullText, string sentence)
    {
        if (string.IsNullOrEmpty(sentence)) return -1;
        int idx = fullText.LastIndexOf(sentence, StringComparison.Ordinal);
        if (idx < 0) return -1;
        string tail = fullText[idx..];
        if (tail[sentence.Length..].Trim().Length > 0) return -1; // something was typed after it
        int crlf = 0;
        for (int i = tail.IndexOf("\r\n", StringComparison.Ordinal); i >= 0; i = tail.IndexOf("\r\n", i + 2, StringComparison.Ordinal))
            crlf++;
        return tail.Length - crlf;
    }

    /// <summary>True when there is enough to rewrite: at least two words and a letter.</summary>
    public static bool IsWorthRewriting(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return false;
        int words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return words >= 2 && sentence.Any(char.IsLetter);
    }
}
