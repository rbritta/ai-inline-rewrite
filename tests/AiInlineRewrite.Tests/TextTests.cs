using AiInlineRewrite.Core;
using Xunit;

namespace AiInlineRewrite.Tests;

public class SentenceTests
{
    [Theory]
    [InlineData("Hi there. I dont think so", "I dont think so")]          // the sentence being typed
    [InlineData("Hi there. I don't think so.", "I don't think so.")]       // a finished one, with its period
    [InlineData("éé, talvez seja melhor não alimentar expectativas!! mas nada impede de continuar curtindo",
        "éé, talvez seja melhor não alimentar expectativas!! mas nada impede de continuar curtindo")] // a lowercase word goes on
    [InlineData("ok. vamos lá", "ok. vamos lá")]
    [InlineData("Sim. É isso mesmo", "É isso mesmo")]                      // a Portuguese capital starts one
    [InlineData("o valor é 3.5 mil e o site.com", "o valor é 3.5 mil e o site.com")] // no space after the mark
    [InlineData("Talk to Mr. Smith today", "Talk to Mr. Smith today")]
    [InlineData("Done! \"Next one\" please", "\"Next one\" please")]
    [InlineData("First line\nsecond line here", "second line here")]
    [InlineData("Are you there?   ", "Are you there?")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void ExtractCurrent_takes_the_last_sentence(string field, string expected)
        => Assert.Equal(expected, Sentence.ExtractCurrent(field));

    [Theory]
    [InlineData("ok", false)]
    [InlineData("ok then", true)]
    [InlineData("12 34", false)]
    [InlineData("", false)]
    public void IsWorthRewriting_needs_two_words_and_a_letter(string s, bool expected)
        => Assert.Equal(expected, Sentence.IsWorthRewriting(s));

    [Theory]
    [InlineData("Hi. we gonna go", "we gonna go", 11)]
    [InlineData("Hi. we gonna go  ", "we gonna go", 13)]       // trailing spaces go too
    [InlineData("Hi.\r\nwe gonna go\r\n", "we gonna go", 12)]  // a CRLF is one Backspace
    [InlineData("we gonna go", "we gonna go", 11)]
    [InlineData("we gonna go and more", "we gonna go", -1)]    // typed after it: not safe to replace
    [InlineData("something else", "we gonna go", -1)]          // gone from the field
    [InlineData("anything", "", -1)]
    public void BackspacesToReplace_counts_what_to_delete_from_the_end(string field, string sentence, int expected)
        => Assert.Equal(expected, Sentence.BackspacesToReplace(field, sentence));
}

public class WordDiffTests
{
    [Fact]
    public void Removed_words_come_before_the_added_ones_around_the_shared_words()
    {
        var runs = WordDiff.Compute("i dont think we can finish this until friday", "I don't think we can finish this by Friday");
        Assert.Equal(
        [
            new DiffRun(DiffKind.Removed, "i dont"),
            new DiffRun(DiffKind.Added, "I don't"),
            new DiffRun(DiffKind.Same, "think we can finish this"),
            new DiffRun(DiffKind.Removed, "until friday"),
            new DiffRun(DiffKind.Added, "by Friday"),
        ], runs);
    }

    [Fact]
    public void A_case_fix_counts_as_a_change()
        => Assert.Equal([new DiffRun(DiffKind.Removed, "hello"), new DiffRun(DiffKind.Added, "Hello")], WordDiff.Compute("hello", "Hello"));

    [Theory]
    [InlineData("same words here", "same  words   here", true)]
    [InlineData("same words here", "same words there", false)]
    [InlineData("", "", true)]
    public void Unchanged_ignores_spacing_only(string a, string b, bool expected)
        => Assert.Equal(expected, WordDiff.Unchanged(a, b));

    [Fact]
    public void Only_added_words_when_the_typed_text_is_empty()
        => Assert.Equal([new DiffRun(DiffKind.Added, "new text")], WordDiff.Compute("", "new text"));
}

public class LangTests
{
    [Theory]
    [InlineData("acho que a gente precisa conversar sobre o prazo amanhã", true)]
    [InlineData("você é um idiota!", true)]           // an accented letter
    [InlineData("eu quero isso", true)]               // two stopwords
    [InlineData("Me manda o arquivo quando puder", true)] // no accent, but manda, arquivo, quando, puder
    [InlineData("i dont think we can finish this", false)]
    [InlineData("the que in the queue", false)]       // one stopword is not enough
    [InlineData("", false)]
    public void LooksPortuguese_is_conservative(string s, bool expected)
        => Assert.Equal(expected, Lang.LooksPortuguese(s));
}

public class EngineStatusTests
{
    private const string Tags = """{ "models": [ { "name": "qwen2.5:7b", "model": "qwen2.5:7b" }, { "name": "llama3:latest" } ] }""";

    [Theory]
    [InlineData("qwen2.5:7b", EngineState.Ready)]
    [InlineData("llama3", EngineState.Ready)]           // no tag means :latest
    [InlineData("QWEN2.5:7B", EngineState.Ready)]
    [InlineData("qwen2.5:14b", EngineState.ModelMissing)]
    public void FromTags_finds_the_model(string model, EngineState expected)
        => Assert.Equal(expected, EngineStatus.FromTags(Tags, model));

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "other": 1 }""")]
    public void FromTags_on_a_strange_answer_is_failing(string json)
        => Assert.Equal(EngineState.Failing, EngineStatus.FromTags(json, "qwen2.5:7b"));

    [Theory]
    [InlineData(404, """{"error":"model 'x' not found"}""", EngineState.ModelMissing)]
    [InlineData(500, """{"error":"model 'x' not found, try pulling it first"}""", EngineState.ModelMissing)]
    [InlineData(500, """{"error":"out of memory"}""", EngineState.Failing)]
    public void FromError_tells_a_missing_model(int code, string body, EngineState expected)
        => Assert.Equal(expected, EngineStatus.FromError(code, body));

    [Theory]
    [InlineData(EngineState.NotRunning, "Ollama is not running", "Start Ollama")]
    [InlineData(EngineState.ModelMissing, "The model qwen2.5:7b is missing", "ollama pull qwen2.5:7b")]
    [InlineData(EngineState.Failing, "Ollama is not answering", "Check that Ollama works")]
    public void Every_Ollama_problem_says_what_to_do(EngineState state, string status, string hint)
    {
        Assert.Equal(status, EngineStatus.Status(EngineKind.Ollama, state, "qwen2.5:7b"));
        Assert.Contains(hint, EngineStatus.Hint(EngineKind.Ollama, state, "qwen2.5:7b"));
    }

    [Theory]
    [InlineData(EngineState.NotRunning, "Claude cannot be reached", "internet connection")]
    [InlineData(EngineState.ModelMissing, "The model claude-x does not exist", "Choose another model in the settings")]
    [InlineData(EngineState.NoKey, "No Anthropic API key", "Set the ANTHROPIC_API_KEY environment variable")]
    [InlineData(EngineState.KeyRefused, "The Anthropic API key was refused", "Check ANTHROPIC_API_KEY")]
    [InlineData(EngineState.Failing, "Claude is not answering", "check your Anthropic account")]
    public void Every_Claude_problem_says_what_to_do_and_never_mentions_Ollama(EngineState state, string status, string hint)
    {
        Assert.Equal(status, EngineStatus.Status(EngineKind.Claude, state, "claude-x"));
        Assert.Contains(hint, EngineStatus.Hint(EngineKind.Claude, state, "claude-x"));
        Assert.DoesNotContain("Ollama", EngineStatus.Status(EngineKind.Claude, state, "claude-x") + EngineStatus.Hint(EngineKind.Claude, state, "claude-x"));
    }

    [Theory]
    [InlineData(EngineKind.Ollama)]
    [InlineData(EngineKind.Claude)]
    public void Ready_says_ready_with_no_hint(EngineKind engine)
    {
        Assert.Equal("Ready", EngineStatus.Status(engine, EngineState.Ready, "m"));
        Assert.Equal("", EngineStatus.Hint(engine, EngineState.Ready, "m"));
    }

    [Theory]
    [InlineData(401, EngineState.KeyRefused)]  // a key went out (none means no request) and was refused
    [InlineData(403, EngineState.KeyRefused)]
    [InlineData(404, EngineState.ModelMissing)]
    [InlineData(429, EngineState.Failing)]     // rate limited: the next pause tries again
    [InlineData(529, EngineState.Failing)]     // overloaded
    public void FromClaudeStatus_tells_a_refused_key_from_a_missing_model(int code, EngineState expected)
        => Assert.Equal(expected, EngineStatus.FromClaudeStatus(code));

    [Theory]
    [InlineData(EngineState.NotRunning, true)]
    [InlineData(EngineState.ModelMissing, true)]
    [InlineData(EngineState.NoKey, true)]
    [InlineData(EngineState.KeyRefused, true)]
    [InlineData(EngineState.Failing, false)]   // may pass: the next pause tries again
    [InlineData(EngineState.Ready, false)]
    [InlineData(EngineState.Unknown, false)]
    public void Blocks_holds_the_states_only_the_user_can_fix(EngineState state, bool expected)
        => Assert.Equal(expected, EngineStatus.Blocks(state));
}

public class PromptsTests
{
    [Theory]
    [MemberData(nameof(AllTones))]
    public void Every_prompt_holds_the_sentence_and_the_shared_rules(Tone tone)
    {
        string p = Prompts.For(tone, "acho que sim");
        Assert.Contains("Sentence: acho que sim\n", p);
        Assert.Contains("translate it into natural English", p);
        Assert.Contains("Do not add a period", p);
        Assert.Contains("Keep the first letter in the same case", p);
        Assert.Contains("Answer in English only", p);
        Assert.EndsWith(":", p);
    }

    [Theory]
    [MemberData(nameof(AllTones))]
    public void Only_the_retry_tells_the_model_it_left_English(Tone tone)
    {
        Assert.DoesNotContain("switched to another language", Prompts.For(tone, "acho que sim"));
        Assert.Contains("switched to another language", Prompts.For(tone, "acho que sim", retry: true));
    }

    [Fact]
    public void Casual_no_longer_capitalizes_the_first_letter()
    {
        string p = Prompts.For(Tone.Casual, "x y");
        Assert.DoesNotContain("first letter of the sentence", p);
        Assert.Contains("capitalize the word I", p);
    }

    [Fact]
    public void The_prompts_differ_per_tone()
        => Assert.Equal(Tones.All.Count, Tones.All.Select(t => Prompts.For(t, "x y")).Distinct().Count());

    [Theory]
    [InlineData("acho que a gente precisa conversar", true)]
    [InlineData("Me manda o arquivo quando puder", true)]
    [InlineData("i dont think we can finish this", false)]
    public void Portuguese_is_named_outright_so_the_model_does_not_drift(string sentence, bool portuguese)
    {
        string p = Prompts.For(Tone.Ornate, sentence);
        Assert.Equal(portuguese, p.Contains("The sentence is in Portuguese", StringComparison.Ordinal));
        Assert.Equal(!portuguese, p.Contains("If the sentence is written in Portuguese, or in any language", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AllTones))]
    public void The_completion_prompt_is_the_instructions_the_sentence_and_the_label(Tone tone)
    {
        const string sentence = "we gonna go";
        string p = Prompts.For(tone, sentence);
        Assert.StartsWith(Prompts.Instructions(tone, sentence) + "\n\n" + Prompts.Input(sentence) + "\n", p);
        Assert.Equal("Sentence: we gonna go", Prompts.Input(sentence));
        Assert.DoesNotContain("Sentence:", Prompts.Instructions(tone, sentence)); // Claude's system prompt holds no user text
    }

    [Fact]
    public void Braces_in_the_sentence_are_kept_verbatim()
        => Assert.Contains("Sentence: use {0} and {name}\n", Prompts.For(Tone.Concise, "use {0} and {name}"));

    public static TheoryData<Tone> AllTones()
    {
        var data = new TheoryData<Tone>();
        foreach (var t in Tones.All) data.Add(t);
        return data;
    }
}

public sealed class LogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "air-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Log.Configure(Path.Combine(_dir, "log.txt"), false);
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    [Fact]
    public void Off_by_default_nothing_is_written_and_on_it_rotates()
    {
        string path = Path.Combine(_dir, "log.txt");
        Log.Configure(path, false);
        Log.Write("event");
        Assert.False(File.Exists(path));

        Log.Configure(path, true);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, new string('x', (int)Log.MaxBytes));
        Log.Write("after rotation");
        Assert.True(File.Exists(Log.RotatedPath(path)));
        Assert.Contains("after rotation", File.ReadAllText(path));
        Assert.True(new FileInfo(path).Length < 200);
    }
}
