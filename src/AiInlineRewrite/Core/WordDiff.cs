namespace AiInlineRewrite.Core;

public enum DiffKind
{
    Same,
    Removed,
    Added,
}

/// <summary>A run of words; the bubble puts one space between runs.</summary>
public readonly record struct DiffRun(DiffKind Kind, string Text);

/// <summary>
/// Word-level diff of what the user typed against a version: the words the version drops (shown struck through in
/// red) and the words it adds (shown in green), around the words both share. Within a change, the removed words
/// come first, then the added ones.
/// </summary>
public static class WordDiff
{
    private static readonly char[] Space = [' ', '\t', '\r', '\n'];

    public static IReadOnlyList<DiffRun> Compute(string typed, string version)
    {
        string[] a = typed.Split(Space, StringSplitOptions.RemoveEmptyEntries);
        string[] b = version.Split(Space, StringSplitOptions.RemoveEmptyEntries);
        int n = a.Length, m = b.Length;

        // Longest common subsequence of words, compared exactly (a case fix counts as a change).
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var runs = new List<DiffRun>();
        var removed = new List<string>();
        var added = new List<string>();
        void Flush()
        {
            if (removed.Count > 0) runs.Add(new DiffRun(DiffKind.Removed, string.Join(' ', removed)));
            if (added.Count > 0) runs.Add(new DiffRun(DiffKind.Added, string.Join(' ', added)));
            removed.Clear();
            added.Clear();
        }

        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && a[x] == b[y])
            {
                Flush();
                if (runs.Count > 0 && runs[^1].Kind == DiffKind.Same)
                    runs[^1] = new DiffRun(DiffKind.Same, runs[^1].Text + " " + a[x]);
                else
                    runs.Add(new DiffRun(DiffKind.Same, a[x]));
                x++;
                y++;
            }
            else if (x < n && (y == m || lcs[x + 1, y] >= lcs[x, y + 1]))
            {
                removed.Add(a[x++]);
            }
            else
            {
                added.Add(b[y++]);
            }
        }
        Flush();
        return runs;
    }

    /// <summary>True when the version only differs from what was typed in spacing.</summary>
    public static bool Unchanged(string typed, string version) =>
        Compute(typed, version).All(r => r.Kind == DiffKind.Same);
}
