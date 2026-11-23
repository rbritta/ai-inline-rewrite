# Prompts

Every tone is one prompt to the model, built in
[`src/AiInlineRewrite/Core/Prompts.cs`](../src/AiInlineRewrite/Core/Prompts.cs). Ollama gets it as one completion text
(`Prompts.For`); Claude gets the same words split the Messages way, the instructions as the system prompt
(`Prompts.Instructions`) and the sentence as the user message (`Prompts.Input`). The answer then goes through
`TextShape`, which keeps the first line, drops an echoed label or quotes, drops a second sentence the user never typed,
and gives the answer the user's own final punctuation and first-letter case. A version with letters outside the Latin
alphabet is asked for once more, with a line telling the model it left English (Ollama only, `Prompts.For` with
`retry`), and never offered if it drifts again (`TextShape.HasForeignLetters`).

## How a prompt is built

1. **The tone's role.** What the tone does and what it must not do (Casual fixes only real mistakes and keeps slang;
   Concise cuts hedges and filler but keeps the grammar; Polished reads fuller in plain words; Ornate is literary;
   Diplomatic turns an insult into a calm sentence about how the writer feels; Slang talks the way people do today;
   Trendy adds one or two pieces of the newest internet slang to a faithful version).
2. **The language line.** When `Lang.LooksPortuguese` says the sentence is Portuguese, the prompt says so outright
   and forbids keeping a Portuguese word or switching to another language. Otherwise a softer line asks to translate
   anything that is not English.
3. **The shape line.** No added final punctuation, the first letter in the user's case (the word I always a capital),
   English only, one sentence, no quotes, labels or explanation.
4. **The sentence and a label** (`Corrected:`, `Concise:`, `Polished:`, `Rewritten:`, `Diplomatic:`, `Slang:` or
   `Trendy:`) for the model to complete.

## Check a change

`tools/ToneCheck` sends ten sample sentences through every tone with the app's own prompts, engine and shaping, one
request at a time, and prints the tables below (the timings go to the error stream):

```powershell
dotnet run --project tools/ToneCheck -c Release
dotnet run --project tools/ToneCheck -c Release -- --model llama3.1:8b --endpoint http://localhost:11434
dotnet run --project tools/ToneCheck -c Release -- --engine claude --model claude-sonnet-5-5
dotnet run --project tools/ToneCheck -c Release -- --sentence "vc viu o email que te mandei ontem?" --tone Ornate
```

`--sentence` and `--tone` repeat, and replace the samples and the tones: the way to reproduce a sentence a user
reported. `--dump-prompts prompts.json` writes the exact prompts the app sends to Ollama (sentence, tone, prompt)
and calls no engine, for a benchmark that sets its own options, as in [MODELS.md](MODELS.md#speed).

A prompt change comes with a new run here. Temperature is 0, so runs are close to repeatable but not exactly: on the
reference machine's GPU ([ENGINES.md](ENGINES.md#reference-machine)), two runs on the same day with the same prompts
differed in the wording of 6 of the 50 versions. Compare runs by the problems they show (another language,
cut grammar, an added sentence, a changed meaning), not word by word. The Claude engine sends no temperature, and no
Claude run has been made yet ([TODO](../TODO.md#not-validated-yet)).

Three other Ollama models went through the same run on the reference machine; their answers and timings are in
[ENGINES.md](ENGINES.md#other-models).

## What the tuning fixed (2026-11-21, qwen2.5:7b)

| Problem in the draft | Fix |
|---|---|
| Ornate drifted into Chinese in the middle of a Portuguese sentence | the prompt says "The sentence is in Portuguese" outright, and a version with non-Latin letters is never offered |
| Concise cut "I" and kept "think" ("think we need to talk...") | the shape line says the word I is always a capital: the lowercase start had made the model drop it |
| Concise later turned telegraphic ("Send file when can") | one example of the cut it wants, and "never drop the articles, pronouns or verbs a sentence needs" |
| Concise matched Casual on most samples | it now drops hedges: "we need to talk about the deadline tomorrow", "we can't finish this until Friday" |
| Diplomatic invented facts ("your suggestion seems to be misunderstood") and changed the meaning ("certainly unexpected") | an insult becomes a calm sentence about how the writer feels; every other word, name, day and fact stays |
| Diplomatic appended a second sentence | `TextShape.FirstSentence` keeps one sentence when the user typed one |
| A list of Portuguese "false friends" in every prompt made Casual answer in Spanish | dropped; `Lang` learned more Portuguese words instead (manda, arquivo, puder...) |

What is still soft: Casual and Concise agree when there is nothing to cut, Polished sometimes equals Concise on short
sentences, and "any doubt" (a Portuguese speaker's "question") becomes "any doubts" in most tones.

## What the tuning fixed (2026-11-22, qwen2.5:7b, v0.2.0)

| Problem | Fix |
|---|---|
| Ornate wrote Chinese ("新颖的俚语") for "gírias novas" (new slang) in a Portuguese question; at temperature 0 the same prompt drifts again, so the bubble kept showing an error. A line in the Ornate prompt ("every word must be an English word") did not help | a version with non-Latin letters is asked for once more with a line saying the last answer switched to another language, and the retry came back all in English. Sampling at temperature 0.5 instead fixed it in 2 of 5 seeds |
| The first Slang prompt appended a feeling the sentence did not have ("I'm totally stoked about") | "never add a new idea, feeling or reaction" |
| The first Trendy prompt, with a long list (it's giving, vibe, slay, bet, fire), stacked slang and bent the meaning ("I'm fr real deadass thinkin' you gotta work on your vibes bro" for "você é um idiota!", "I'll dm you the file" for "Me manda o arquivo") | a short list (ngl, lowkey, highkey, fr, deadass, no cap), "keeping who does what to whom exactly as written", and three examples |

What is still soft in Trendy: "fr" closes most versions, and on "acho que a gente precisa conversar sobre o prazo
amanhã" every prompt tried kept "amanhã" or garbled it ("amanhakay"); dropping the Portuguese example did not fix it
and made Trendy swap who sends the file as well. A larger model should do better; none was tried yet.

## Another language pair

The prompts and a few texts are written for Portuguese and English; the hooks, the bubble, pick mode and applying do
not care about the language. To make AIR serve another pair, change:

1. **The prompts** (`Core/Prompts.cs`): the language lines `FromPortuguese` and `FromAny`, "Answer in English only"
   in `Shape`, and the tone roles, which speak of English (Casual's slang list and contractions, Concise's example).
2. **The language guess** (`Core/Lang.cs`): `LooksPortuguese` looks for Portuguese-only accented letters and common
   Portuguese words. It decides whether the bubble shows a translation (plain text) or a correction (a word diff), and
   which language line the prompt gets.
3. **The output check** (`TextShape.HasForeignLetters`): a version with letters outside the Latin alphabet is never
   offered. A target language written in another script (Cyrillic, Greek, Japanese) needs this check changed.
4. **The texts**: the status lines in `AppController` ("Translated from Portuguese", "English, corrected", "Type in
   Portuguese or English.", "this version left English"), the sample sentence of the settings preview
   (`Samples` in `Ui/SettingsWindow.cs`) and the tone descriptions in `Core/Tone.cs`.
5. **The checks**: the samples in `tools/ToneCheck`, a run here, and the tests (`LangTests`, `PromptsTests`).

The engines need no change: they send whatever the prompts say. Whether the model follows the new prompts in the new
language is what the ToneCheck run shows.

## Before: the draft prompts

Model `qwen2.5:7b`, temperature 0, eight samples.

#### acho que a gente precisa conversar sobre o prazo amanhã

| Tone | Version |
|---|---|
| Casual | I think we need to talk about the deadline tomorrow |
| Concise | think we need to talk about the deadline tomorrow |
| Polished | I think we need to have a conversation about the deadline tomorrow |
| Ornate | I posit that we ought to engage in discourse concerning the时限问题明日须详谈 |
| Diplomatic | I believe we should discuss the deadline tomorrow |

#### você é um idiota!

| Tone | Version |
|---|---|
| Casual | you are an idiot! |
| Concise | you are an idiot! |
| Polished | you are an idiot! |
| Ornate | you are a fool! |
| Diplomatic | you may not have intended it, but your suggestion seems to be misunderstood or misinterpreted! |

#### i dont think we can finish this until friday

| Tone | Version |
|---|---|
| Casual | I don't think we can finish this until Friday |
| Concise | we can't finish this until Friday |
| Polished | I don't think we'll be able to finish this until Friday |
| Ornate | I do not believe we shall be able to conclude this endeavor until Friday |
| Diplomatic | I don't believe we will be able to complete this until Friday |

#### can you send me the report until tomorrow?

| Tone | Version |
|---|---|
| Casual | can you send me the report by tomorrow? |
| Concise | can you send me the report by tomorrow? |
| Polished | could you send me the report by tomorrow? |
| Ornate | may you convey to me the document in question by tomorrow? |
| Diplomatic | could you send me the report by tomorrow? |

#### we gonna need more time to fix this bug

| Tone | Version |
|---|---|
| Casual | we're gonna need more time to fix this bug |
| Concise | we are going to need more time to fix this bug |
| Polished | we are going to need more time to fix this bug |
| Ornate | we shall require a greater allocation of time to efficaciously address and remediate this vexing bug |
| Diplomatic | we may require additional time to address this bug |

#### o cliente reclamou de novo, isso é ridículo

| Tone | Version |
|---|---|
| Casual | the customer complained again, that's ridiculous |
| Concise | the customer complained again, this is ridiculous |
| Polished | the customer complained again, that is indeed ridiculous |
| Ornate | the client has once again voiced his disapproval, an assertion that is, indeed, preposterous |
| Diplomatic | the client has expressed their concerns again, which is certainly unexpected |

#### please let me know if you have any doubt.

| Tone | Version |
|---|---|
| Casual | please let me know if you have any doubts. |
| Concise | please let me know if you have any doubts. |
| Polished | please let me know if you have any doubts at all. |
| Ornate | please inform me if you harbor any uncertainty. |
| Diplomatic | please let me know if you have any questions. |

#### Me manda o arquivo quando puder

| Tone | Version |
|---|---|
| Casual | Send me the file when you can |
| Concise | Send the file when you can |
| Polished | Could you send me the file when you have a chance |
| Ornate | When you are able, please transmit the document to me |
| Diplomatic | Could you send me the file when you have a moment |

## After: the prompts in v0.1.0

Model `qwen2.5:7b` in Ollama 0.40.16, temperature 0, ten samples (two more Portuguese ones), 50 versions in 6.3 s on
the [reference machine](ENGINES.md#reference-machine)'s 12 GB GPU, none outside English. Run on 2026-11-21 with the
code of this release.

#### acho que a gente precisa conversar sobre o prazo amanhã

| Tone | Version |
|---|---|
| Casual | I think we need to talk about the deadline tomorrow |
| Concise | we need to talk about the deadline tomorrow |
| Polished | I think we need to have a conversation about the deadline tomorrow |
| Ornate | I believe that we must engage in a discourse concerning the timeframe tomorrow |
| Diplomatic | I feel we need to discuss the deadline tomorrow |

#### você é um idiota!

| Tone | Version |
|---|---|
| Casual | you're an idiot! |
| Concise | you are an idiot! |
| Polished | you are an idiot! |
| Ornate | you are a fool! |
| Diplomatic | I feel this isn't constructive! |

#### i dont think we can finish this until friday

| Tone | Version |
|---|---|
| Casual | I don't think we can finish this until Friday |
| Concise | we can't finish this until Friday |
| Polished | I don't think we'll be able to finish this until Friday |
| Ornate | I do not believe we shall be able to conclude this endeavor until Friday |
| Diplomatic | I believe we may need until Friday to finish this |

#### can you send me the report until tomorrow?

| Tone | Version |
|---|---|
| Casual | can you send me the report by tomorrow? |
| Concise | can you send me the report by tomorrow? |
| Polished | could you send me the report by tomorrow? |
| Ornate | could you kindly transmit the report to me by tomorrow? |
| Diplomatic | can you send me the report by tomorrow? |

#### we gonna need more time to fix this bug

| Tone | Version |
|---|---|
| Casual | we're gonna need more time to fix this bug |
| Concise | we are going to need more time to fix this bug |
| Polished | we are going to need more time to fix this bug |
| Ornate | we are going to require a greater allocation of time in order to adequately address and resolve this particular bug |
| Diplomatic | I believe we will need some additional time to fix this bug |

#### o cliente reclamou de novo, isso é ridículo

| Tone | Version |
|---|---|
| Casual | the customer complained again, that's ridiculous |
| Concise | the customer complained again, that is ridiculous |
| Polished | the customer complained again, and that is really ridiculous |
| Ornate | the client once more voiced his complaint, an assertion undeniably preposterous |
| Diplomatic | I find the client's repeated complaint to be unreasonable |

#### please let me know if you have any doubt.

| Tone | Version |
|---|---|
| Casual | please let me know if you have any doubts. |
| Concise | please let me know if you have any doubt. |
| Polished | please let me know if you have any doubts at all. |
| Ornate | kindly inform me if you harbor any uncertainty or query. |
| Diplomatic | please let me know if you have any doubts. |

#### Me manda o arquivo quando puder

| Tone | Version |
|---|---|
| Casual | Send me the file when you can |
| Concise | Send me the file when you can |
| Polished | Could you send me the file when you have the chance |
| Ornate | When you are able, I beseech you to convey the document to me |
| Diplomatic | I would appreciate it if you could send me the file when you have a moment |

#### não sei se vai dar tempo de entregar tudo hoje

| Tone | Version |
|---|---|
| Casual | I don't know if there'll be time to deliver everything today |
| Concise | I don't know if we'll have time to deliver everything today |
| Polished | I'm not sure if there will be enough time to deliver everything today |
| Ornate | I am uncertain whether there will be sufficient time to ensure the timely submission of all items today |
| Diplomatic | I am concerned we might not have enough time to deliver everything today |

#### obrigado pela ajuda, você salvou meu dia

| Tone | Version |
|---|---|
| Casual | thanks for the help, you saved my day |
| Concise | thank you for the help, you saved my day |
| Polished | thank you for your help, you really made my day |
| Ornate | I thank you for your assistance, for you have rescued my day |
| Diplomatic | I appreciate your help, you made my day |

## After: the prompts in v0.2.0

Model `qwen2.5:7b` in Ollama 0.40.16, temperature 0, the ten samples through the seven tones, 70 versions in 9.4 s on
the [reference machine](ENGINES.md#reference-machine)'s 12 GB GPU, none outside English (no retry was needed). Run
on 2026-11-22 with the code of this release. Only the Slang and Trendy prompts are new; the other five are the
v0.1.0 prompts.

#### acho que a gente precisa conversar sobre o prazo amanhã

| Tone | Version |
|---|---|
| Casual | I think we need to talk about the deadline tomorrow |
| Concise | we need to talk about the deadline tomorrow |
| Polished | I think we need to have a conversation about the deadline tomorrow |
| Ornate | I believe that we must engage in a discourse concerning the timeframe tomorrow |
| Diplomatic | I feel we need to discuss the deadline tomorrow |
| Slang | I think we gotta chat about the deadline tomorrow |
| Trendy | I think we need to chat about the deadline amanhakay fr |

#### você é um idiota!

| Tone | Version |
|---|---|
| Casual | you're an idiot! |
| Concise | you are an idiot! |
| Polished | you are an idiot! |
| Ornate | you are a fool! |
| Diplomatic | I feel this isn't constructive! |
| Slang | you're a total idiot! |
| Trendy | you're a real idiot fr! |

#### i dont think we can finish this until friday

| Tone | Version |
|---|---|
| Casual | I don't think we can finish this until Friday |
| Concise | we can't finish this until Friday |
| Polished | I don't think we'll be able to finish this until Friday |
| Ornate | I do not believe we shall be able to conclude this endeavor until Friday |
| Diplomatic | I believe we may need until Friday to finish this |
| Slang | I don't think we can finish this until Friday |
| Trendy | I don't think we can finish this until Friday fr |

#### can you send me the report until tomorrow?

| Tone | Version |
|---|---|
| Casual | can you send me the report by tomorrow? |
| Concise | can you send me the report by tomorrow? |
| Polished | could you send me the report by tomorrow? |
| Ornate | could you kindly transmit the report to me by tomorrow? |
| Diplomatic | can you send me the report by tomorrow? |
| Slang | can you send me the report by tomorrow, for sure? |
| Trendy | can you send me the report until tomorrow, fr? |

#### we gonna need more time to fix this bug

| Tone | Version |
|---|---|
| Casual | we're gonna need more time to fix this bug |
| Concise | we are going to need more time to fix this bug |
| Polished | we are going to need more time to fix this bug |
| Ornate | we are going to require a greater allocation of time in order to adequately address and resolve this particular bug |
| Diplomatic | I believe we will need some additional time to fix this bug |
| Slang | we're gonna need more time to fix this bug |
| Trendy | ngl we gonna need more time to fix this bug fr |

#### o cliente reclamou de novo, isso é ridículo

| Tone | Version |
|---|---|
| Casual | the customer complained again, that's ridiculous |
| Concise | the customer complained again, that is ridiculous |
| Polished | the customer complained again, and that is really ridiculous |
| Ornate | the client once more voiced his complaint, an assertion undeniably preposterous |
| Diplomatic | I find the client's repeated complaint to be unreasonable |
| Slang | the customer's complaining again, like that's super ridiculous |
| Trendy | I'm fr the customer complained again, that's so ridiculous |

#### please let me know if you have any doubt.

| Tone | Version |
|---|---|
| Casual | please let me know if you have any doubts. |
| Concise | please let me know if you have any doubt. |
| Polished | please let me know if you have any doubts at all. |
| Ornate | kindly inform me if you harbor any uncertainty or query. |
| Diplomatic | please let me know if you have any doubts. |
| Slang | please let me know if you got any doubt. |
| Trendy | ngl, just let me know if you have any doubts fr. |

#### Me manda o arquivo quando puder

| Tone | Version |
|---|---|
| Casual | Send me the file when you can |
| Concise | Send me the file when you can |
| Polished | Could you send me the file when you have a moment |
| Ornate | When you are able, I beseech you to convey the document to me |
| Diplomatic | I would appreciate it if you could send me the file when you have a moment |
| Slang | Send me the file when you can |
| Trendy | Send me the file when you can fr |

#### não sei se vai dar tempo de entregar tudo hoje

| Tone | Version |
|---|---|
| Casual | I don't know if there'll be time to deliver everything today |
| Concise | I don't know if we'll have time to deliver everything today |
| Polished | I'm not sure if there will be enough time to deliver everything today |
| Ornate | I am uncertain whether there shall be sufficient time to deliver all of it today |
| Diplomatic | I am concerned we might not have enough time to deliver everything today |
| Slang | I don't know if we'll gonna have time to get everything done today |
| Trendy | I don't know if I'll have time to get everything done today fr |

#### obrigado pela ajuda, você salvou meu dia

| Tone | Version |
|---|---|
| Casual | thanks for the help, you saved my day |
| Concise | thank you for the help, you saved my day |
| Polished | thank you for your help, you saved my day |
| Ornate | thankful for your assistance, you have rescued my day |
| Diplomatic | I appreciate your help, you made my day |
| Slang | thanks for the help, you totally saved my day |
| Trendy | ngl, you really saved my day, fr |
