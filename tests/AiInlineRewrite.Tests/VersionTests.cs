using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AiInlineRewrite.Tests;

/// <summary>
/// The version lives in one place, the app's csproj (CONTRIBUTING.md). It must be three-part, and the CHANGELOG must
/// have its section right under Unreleased.
/// </summary>
public class VersionTests
{
    internal static string CsprojVersion()
    {
        var doc = XDocument.Parse(RepoFiles.Read("src", "AiInlineRewrite", "AiInlineRewrite.csproj"));
        return doc.Descendants("Version").Select(v => v.Value.Trim()).Single();
    }

    [Fact]
    public void The_app_has_a_three_part_version()
        => Assert.Matches(@"^\d+\.\d+\.\d+$", CsprojVersion());

    [Fact]
    public void No_other_project_file_sets_a_version()
    {
        foreach (var csproj in Directory.EnumerateFiles(RepoFiles.Root, "*.csproj", SearchOption.AllDirectories)
                     .Where(p => !p.EndsWith("AiInlineRewrite.csproj", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("<Version>", File.ReadAllText(csproj));
        }
    }

    [Fact]
    public void The_changelog_has_Unreleased_on_top_then_a_dated_section_for_this_version()
    {
        var changelog = RepoFiles.Read("CHANGELOG.md");
        var heads = Regex.Matches(changelog, @"^## \[([^\]]+)\]", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal("Unreleased", heads[0]);
        Assert.Equal(CsprojVersion(), heads[1]);
        Assert.Matches(@"(?m)^## \[" + Regex.Escape(CsprojVersion()) + @"\] - \d{4}-\d\d-\d\d\r?$", changelog);
    }
}
