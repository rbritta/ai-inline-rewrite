namespace AiInlineRewrite.Core;

/// <summary>
/// The prompt each tone sends to the model. Every prompt asks for English, translates Portuguese (or any other
/// language) in the same register, keeps the user's final punctuation and first-letter case, and asks for the
/// sentence only. <see cref="TextShape"/> enforces the punctuation and the case again on the answer, because a
/// small local model does not always follow the instruction.
/// Ollama gets one completion prompt (<see cref="For"/>: the instructions, the sentence, then the tone's label to
/// continue). Claude gets the same words split the Messages way: <see cref="Instructions"/> as the system prompt and
/// <see cref="Input"/> as the user message.
/// tools/ToneCheck runs the sample sentences of docs/PROMPTS.md through every tone; a prompt change comes with a run.
/// </summary>
public static class Prompts
{
    // Said outright when the sentence looks Portuguese: told only "if it is not English", a 7B model sometimes
    // answered in a third language (Ornate drifted into Chinese, docs/PROMPTS.md).
    private const string FromPortuguese =
        " The sentence is in Portuguese: translate it into natural English in this same style instead of copying " +
        "it. Never keep a Portuguese word and never switch to any other language.";

    private const string FromAny =
        " If the sentence is written in Portuguese, or in any language other than English, translate it into " +
        "natural English in this same style instead of copying it or leaving it unchanged.";

    // "the word I stays a capital": without it, a lowercase start made the model drop the I ("think we need...").
    private const string Shape =
        " Do not add a period or any final punctuation the user did not already type. Keep the first letter in " +
        "the same case the user typed it, but the word I is always a capital. Answer in English only, as one " +
        "sentence with no quotes, no labels and no explanation. Output ONLY the rewritten sentence.";

    private const string Casual =
        "You are an English writing coach for someone writing casually. Fix only real mistakes: spelling, verb " +
        "tense and agreement, word order, wrong prepositions, missing apostrophes in contractions (dont -> don't, " +
        "im -> I'm), and capitalize the word I. Keep the casual style: never change acceptable slang or casual " +
        "contractions like wanna, gonna, gotta, kinda, gimme; they are correct. Do not make it more formal, do not " +
        "rephrase, do not upgrade the vocabulary. If there are no real mistakes, repeat it unchanged.";

    // The example keeps a 7B model from cutting grammar words ("Send file when can") instead of filler.
    private const string Concise =
        "You are an English editor. Rewrite the sentence in correct, direct English with fewer words: remove " +
        "hedges and filler such as I think, I guess, really, just, kind of, at all, and say it the shortest " +
        "natural way (for example, \"I think we should maybe talk about this later\" becomes \"we should talk " +
        "about this later\"). Use normal grammar: never drop the articles, pronouns or verbs a sentence needs. " +
        "Expand slang (gonna -> going to, wanna -> want to). Keep the facts and the request; never add anything.";

    private const string Polished =
        "You are an English editor. Rewrite the sentence in correct, natural, professional English with fuller " +
        "phrasing than a terse version: you may add a connecting word or a short clarifying phrase so it reads " +
        "smoothly and courteously. Use plain, everyday vocabulary; do NOT use ornate, archaic or literary words. " +
        "Expand casual contractions and slang. Keep the words the user chose for people and things, and keep the " +
        "exact meaning.";

    private const string Ornate =
        "You are an English editor with a literary ear. Rewrite the sentence in elegant, refined formal English " +
        "with rich vocabulary, as it would read in well-written literary prose, at most about twice as long as the " +
        "original. Keep the exact meaning and never invent new facts.";

    // A 7B model sometimes appends a second sentence here; TextShape.FirstSentence drops it.
    private const string Diplomatic =
        "You are an English editor. Rewrite the sentence so it can be sent to a colleague or a client. If it " +
        "insults someone, replace the insult with a short, calm sentence about how the writer feels, starting " +
        "with I. Replace harsh words such as ridiculous, stupid or useless with milder ones. Keep every other " +
        "word, name, day and fact as it is and only fix mistakes. Never add a new clause, request, offer or " +
        "thanks, and do not apologize for the user.";

    /// <summary>
    /// The whole prompt as one completion text, for Ollama's /api/generate: the instructions, the sentence, and the
    /// label the answer is written after ("Corrected:").
    /// </summary>
    public static string For(Tone tone, string sentence) =>
        Instructions(tone, sentence) + "\n\n" + Input(sentence) + "\n" + Label(tone) + ":";

    /// <summary>
    /// What the model must do for <paramref name="tone"/>: the role, the language line (which depends on whether the
    /// sentence looks Portuguese) and the shape rules. Claude gets this as its system prompt.
    /// </summary>
    public static string Instructions(Tone tone, string sentence)
    {
        string role = tone switch
        {
            Tone.Concise => Concise,
            Tone.Polished => Polished,
            Tone.Ornate => Ornate,
            Tone.Diplomatic => Diplomatic,
            _ => Casual,
        };
        return role + (Lang.LooksPortuguese(sentence) ? FromPortuguese : FromAny) + Shape;
    }

    /// <summary>The sentence as the model reads it. Claude gets this as the user message.</summary>
    public static string Input(string sentence) => "Sentence: " + sentence;

    // The word the answer follows in the completion prompt; TextShape.Clean removes it if a model echoes it.
    private static string Label(Tone tone) => tone switch
    {
        Tone.Concise => "Concise",
        Tone.Polished => "Polished",
        Tone.Ornate => "Rewritten",
        Tone.Diplomatic => "Diplomatic",
        _ => "Corrected",
    };
}
