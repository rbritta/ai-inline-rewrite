using AiInlineRewrite.Core;
using Xunit;

namespace AiInlineRewrite.Tests;

/// <summary>The tray's "Report a version" opens the repository's issue form with the version and engine filled in.</summary>
public class ReportLinkTests
{
    [Fact]
    public void The_link_opens_the_issue_form_with_the_version_and_the_engine()
        => Assert.Equal(
            "https://github.com/rbritta/ai-inline-rewrite/issues/new?template=inappropriate_version.yml"
            + "&version=0.3.0&engine=BuiltIn%2C%20qwen2.5-7b-instruct-q4_k_m",
            ReportLink.For("0.3.0", EngineKind.BuiltIn, LocalModel.Id));

    [Theory]
    [InlineData(EngineKind.Ollama, "llama3.2:3b", "Ollama%2C%20llama3.2%3A3b")]
    [InlineData(EngineKind.Claude, "claude-haiku-5-5", "Claude%2C%20claude-haiku-5-5")]
    [InlineData(EngineKind.Ollama, "a&b=c", "Ollama%2C%20a%26b%3Dc")]
    public void The_engine_and_model_are_escaped(EngineKind engine, string model, string expected)
        => Assert.EndsWith("&engine=" + expected, ReportLink.For("1.0.0", engine, model));

    [Fact]
    public void The_form_exists_and_has_the_fields_the_link_fills()
    {
        string form = RepoFiles.Read(".github", "ISSUE_TEMPLATE", ReportLink.Template);
        Assert.Contains("id: version", form);
        Assert.Contains("id: engine", form);
    }
}
