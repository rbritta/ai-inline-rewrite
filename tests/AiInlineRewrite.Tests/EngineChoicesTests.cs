using AiInlineRewrite.Core;
using Xunit;

namespace AiInlineRewrite.Tests;

public class EngineChoicesTests
{
    [Fact]
    public void The_engines_are_built_in_Ollama_then_Claude_by_their_setting_names()
    {
        Assert.Equal([("BuiltIn", "Built-in"), ("Ollama", "Ollama"), ("Claude", "Claude")], EngineChoices.Engines);
        Assert.All(EngineChoices.Engines, e => Assert.True(Enum.TryParse<EngineKind>(e.Value, out _)));
    }

    [Fact]
    public void Claude_offers_Haiku_Sonnet_and_Opus_with_Haiku_the_default()
    {
        Assert.Equal(["claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5"], EngineChoices.ClaudeOptions(Settings.DefaultClaudeModel).Select(o => o.Value));
        Assert.Equal(Settings.DefaultClaudeModel, EngineChoices.ClaudeModels[0].Value);
    }

    [Fact]
    public void A_Claude_id_typed_in_the_settings_file_is_offered_first()
    {
        var options = EngineChoices.ClaudeOptions("claude-fable-5-2");
        Assert.Equal(("claude-fable-5-2", "claude-fable-5-2"), options[0]);
        Assert.Equal(4, options.Count);
    }

    [Fact]
    public void Ollama_offers_the_installed_models_sorted_and_keeps_the_current_one_selectable()
    {
        var options = EngineChoices.OllamaOptions(["qwen2.5:7b", "gemma4:12b", "Gemma4:12B"], "qwen2.5:7b");
        Assert.Equal([("gemma4:12b", "gemma4:12b"), ("qwen2.5:7b", "qwen2.5:7b")], options); // deduped, case aside
    }

    [Fact]
    public void An_untagged_current_model_matches_its_latest_tag()
    {
        var options = EngineChoices.OllamaOptions(["llama3:latest", "qwen2.5:7b"], "llama3");
        Assert.Equal([("llama3", "llama3:latest"), ("qwen2.5:7b", "qwen2.5:7b")], options); // the value stays as set
    }

    [Fact]
    public void A_current_model_that_is_not_installed_comes_first_and_says_so()
    {
        var options = EngineChoices.OllamaOptions(["gemma4:12b"], "qwen2.5:7b");
        Assert.Equal([("qwen2.5:7b", "qwen2.5:7b (not installed)"), ("gemma4:12b", "gemma4:12b")], options);
    }

    [Fact]
    public void Without_the_list_only_the_current_model_is_offered()
        => Assert.Equal([("my-model:3b", "my-model:3b")], EngineChoices.OllamaOptions(null, "my-model:3b"));

    [Theory]
    [InlineData("""{"models":[{"name":"qwen2.5:7b","model":"qwen2.5:7b"},{"name":"gemma4:12b"}]}""", "qwen2.5:7b,gemma4:12b")]
    [InlineData("""{"models":[]}""", "")]
    [InlineData("""{"models":[{"model":"no-name"},{"name":""},{"name":3},"text"]}""", "")]
    public void InstalledModels_reads_the_names(string json, string expected)
        => Assert.Equal(expected, string.Join(",", EngineChoices.InstalledModels(json)!));

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"other":1}""")]
    [InlineData("""{"models":{}}""")]
    public void InstalledModels_on_a_strange_answer_is_null(string json)
        => Assert.Null(EngineChoices.InstalledModels(json));

    [Theory]
    [InlineData("llama3", "llama3:latest", true)]
    [InlineData("QWEN2.5:7B", "qwen2.5:7b", true)]
    [InlineData("qwen2.5:7b", "qwen2.5:14b", false)]
    public void SameModel_ignores_case_and_the_latest_tag(string a, string b, bool expected)
        => Assert.Equal(expected, EngineStatus.SameModel(a, b));
}
