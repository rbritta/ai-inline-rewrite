namespace AiInlineRewrite.Tests;

/// <summary>The repository's own files, found from the test assembly's folder (the folder holding the .sln).</summary>
internal static class RepoFiles
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "AiInlineRewrite.sln")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? throw new InvalidOperationException("AiInlineRewrite.sln not found above " + AppContext.BaseDirectory);
        }
    }

    public static string Path(params string[] parts)
        => System.IO.Path.Combine(Root, System.IO.Path.Combine(parts));

    public static string Read(params string[] parts)
        => File.ReadAllText(Path(parts));
}
