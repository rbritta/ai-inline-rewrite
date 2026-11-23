namespace AiInlineRewrite.Core;

/// <summary>
/// The tray's "Report a version": the GitHub issue form for a version that was offensive, harmful or otherwise
/// inappropriate, with AIR's version and engine filled in. Never what the user typed: the issue is public, so the
/// user decides what to paste. The Microsoft Store asks apps with live generative AI for a way to report its output.
/// </summary>
public static class ReportLink
{
    public const string Repository = "https://github.com/rbritta/ai-inline-rewrite";
    public const string Template = "inappropriate_version.yml";

    /// <summary>The issue form's address with the version and the engine and model as query values.</summary>
    public static string For(string version, EngineKind engine, string model) =>
        $"{Repository}/issues/new?template={Template}"
        + $"&version={Uri.EscapeDataString(version)}"
        + $"&engine={Uri.EscapeDataString($"{engine}, {model}")}";
}
