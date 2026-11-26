using AiInlineRewrite.Core;
using Xunit;

namespace AiInlineRewrite.Tests;

public class TextShapeTests
{
    [Theory]
    [InlineData("você é um idiota!", "You're an idiot!", "you're an idiot!")]          // lowercase start stays lowercase
    [InlineData("Você é", "you are", "You are")]                                     // uppercase start stays uppercase
    [InlineData("i dont think", "I don't think", "I don't think")]                   // the pronoun I stays upper
    [InlineData("see you friday", "Friday works", "Friday works")]                   // day names stay upper
    [InlineData("the meeting is in march", "March is fine", "March is fine")]        // month names stay upper
    [InlineData("air is fine", "AIR is fine", "AIR is fine")]                        // a word with more capitals stays
    [InlineData("air is fine", "iPhone is fine", "iPhone is fine")]
    [InlineData("may I come", "May I come", "may I come")]                           // May is left out on purpose
    [InlineData("123 ok", "456 Sure", "456 sure")]                                   // leading digits: the first letter of each decides
    public void The_first_letter_mirrors_what_the_user_typed(string typed, string version, string expected)
        => Assert.Equal(expected, TextShape.MatchStartCase(typed, version));

    [Theory]
    [InlineData("", "Hello")]
    [InlineData("hello", "")]
    [InlineData("!!!", "Hello")]
    public void Without_letters_nothing_changes(string typed, string version)
        => Assert.Equal(version, TextShape.MatchStartCase(typed, version));

    [Theory]
    [InlineData("we need to talk", "We need to talk.", "we need to talk")]     // nothing typed: none added
    [InlineData("we need to talk.", "We need to talk", "we need to talk.")]    // a period typed: kept
    [InlineData("can we talk?", "Can we talk.", "can we talk?")]               // the user's mark wins
    [InlineData("wow!", "Wow!!!", "wow!")]                                     // a run of marks becomes the user's one
    [InlineData("ok?", "\"Okay?\"", "okay?")]                                  // surrounding quotes go
    public void The_final_punctuation_mirrors_what_the_user_typed(string typed, string version, string expected)
        => Assert.Equal(expected, TextShape.MatchPunctuation(typed, version));

    [Theory]
    [InlineData("Corrected: I think so", "I think so")]
    [InlineData("  Polished:   I believe so  ", "I believe so")]
    [InlineData("Slang: I think so for sure", "I think so for sure")]
    [InlineData("Trendy: ngl I think so", "ngl I think so")]
    [InlineData("I think so\n\nThis version keeps the tone.", "I think so")]
    [InlineData("“I think so”", "I think so")]
    [InlineData("", "")]
    public void Clean_keeps_the_first_line_without_labels_or_quotes(string answer, string expected)
        => Assert.Equal(expected, TextShape.Clean(answer));

    [Fact]
    public void A_partial_answer_while_streaming_is_shaped_too()
        => Assert.Equal("we need", TextShape.MatchPunctuation("acho que a gente", "\"We need"));

    [Theory]
    [InlineData("I tried everything; nothing worked", "I tried everything. Nothing worked.", "I tried everything. Nothing worked")]
    [InlineData("even today, after 40 years. They still suffer prejudice", "Even today, after 40 years, they still face prejudice", "even today, after 40 years, they still face prejudice")]
    [InlineData("done. Ship it", "Done. Ship it", "done. Ship it")]
    [InlineData("nossa, hoje foi corrido!! mas amanhã a gente conversa melhor", "Wow, today was hectic!! But we can talk properly tomorrow", "wow, today was hectic!! But we can talk properly tomorrow")]
    [InlineData("ask him", "Ask Mr. Smith about it", "ask Mr. Smith about it")]
    [InlineData("v2.0 is out", "Version 2.0 is out", "version 2.0 is out")]
    public void The_answer_is_never_cut_inside_the_line(string typed, string version, string expected)
        => Assert.Equal(expected, TextShape.MatchPunctuation(typed, version));

    [Theory]
    [InlineData("I posit that we ought to engage in discourse concerning the时限问题", true)]
    [InlineData("привет", true)]
    [InlineData("I believe we ought to confer regarding the deadline", false)]
    [InlineData("café, naïve, résumé and Ångström", false)]                             // accented Latin is fine
    [InlineData("the plan costs 10 €, 20% off!", false)]
    [InlineData("", false)]
    public void HasForeignLetters_spots_a_drift_out_of_the_Latin_alphabet(string text, bool expected)
        => Assert.Equal(expected, TextShape.HasForeignLetters(text));
}
