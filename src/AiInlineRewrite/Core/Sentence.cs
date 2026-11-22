namespace AiInlineRewrite.Core;

/// <summary>
/// The sentence the user is typing. Typing happens at the end of the field, so it is the text after the last
/// sentence terminator; except right after a finished sentence (". ! ?"), where it is that whole sentence with its
/// punctuation, not the empty tail after it.
/// </summary>
public static class Sentence
{
    private static readonly char[] Terminators = ['.', '!', '?', '\n', '\r'];
    private static readonly char[] EndPunct = ['.', '!', '?'];

    public static string ExtractCurrent(string fullText)
    {
        if (string.IsNullOrEmpty(fullText)) return string.Empty;

        string t = fullText.TrimEnd();
        if (t.Length == 0) return string.Empty;

        // Finished sentence: keep it whole, with its trailing . ! or ?
        if (Array.IndexOf(EndPunct, t[^1]) >= 0)
        {
            int from = t.Length - 2;
            int prev = from >= 0 ? t.LastIndexOfAny(Terminators, from) : -1;
            return t[(prev + 1)..].TrimStart();
        }

        // In-progress sentence: the fragment after the last terminator.
        int cut = t.LastIndexOfAny(Terminators);
        string current = cut >= 0 ? t[(cut + 1)..] : t;
        return current.TrimStart();
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
