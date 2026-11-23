# Models compared

Eight Ollama models measured on the reference machine ([ENGINES.md](ENGINES.md#reference-machine): an RTX 5070 with
12 GB, a 24-core CPU and 64 GB of RAM) on 2026-11-23, with the app's own prompts: quality judged blind, speed on the
GPU and on the CPU alone, size, video memory and license. The last part sets three ways of running the model side by
side: Ollama (what AIR uses), llama.cpp loaded inside a .NET process (a probe, not part of AIR), and Claude.

## Results in brief

- **Quality.** In a blind test of 896 versions, `qwen2.5:7b` (the default) and `gemma3:4b` scored highest (5.33 and
  5.32 out of 6). The other six scored 0.4 to 0.7 lower. The 3B and 4B models lost the sentence's meaning in 9% to 18%
  of their versions, against 3% for `qwen2.5:7b` and 1% for `gemma3:4b`.
- **`qwen2.5:7b` on the GPU.** 0.15 s a version, 0.44 s for three tones, and 4.6 GiB of video memory.
- **`qwen2.5:7b` on the CPU alone.** 2.1 s a version with 8 threads and 3.2 s with 4; three tones in a row took 6 to
  10 s. The smaller models took 0.5 to 1.8 s a version with 8 threads.
- **llama.cpp inside a .NET process.** LLamaSharp on .NET 11 loaded the same model file and gave the same answers in
  17 of 18 cases. It took 0.44 s a version on the GPU (Vulkan) and 3.5 s on the CPU with 8 threads. Its libraries
  take about 75 MB, against 3.2 GB for this machine's Ollama install.
- **Claude.** Not run. Estimated from the measured token counts: about $0.0001 to $0.0003 per pause with Claude Haiku
  5.5, or $0.60 to $1.60 for 200 pauses a day over 30 days. It needs an Anthropic API key, and the sentence goes to
  Anthropic.

## Quality

**How it was judged.** `tools/ToneCheck` ran 16 sentences through all 7 tones on every model, using the app's prompts,
engine and shaping. The sentences were the 10 samples plus 6 made-up chat messages (slang, abbreviations, "kkkk").
That gave 112 versions per model and 896 in all. They were shuffled and judged blind: the judge saw the sentence, the
tone and the version, never the model. The rubric scored three things from 0 to 2: meaning kept (0 if reversed,
invented or largely lost), natural English, and the tone asked for. The judge was one AI model (Claude) in a single
pass. With 16 sentences, treat a difference under about 0.2 as noise.

| Model | Score (0 to 6) | Meaning (0 to 2) | English (0 to 2) | Tone (0 to 2) | All three at 2 | Meaning lost (0) |
|---|---|---|---|---|---|---|
| `qwen2.5:7b` (the default) | 5.33 | 1.85 | 1.83 | 1.65 | 54% | 3% |
| `gemma3:4b` | 5.32 | 1.84 | 1.80 | 1.68 | 54% | 1% |
| `phi4-mini` | 4.90 | 1.57 | 1.70 | 1.63 | 42% | 12% |
| `qwen3:4b-instruct` | 4.89 | 1.69 | 1.62 | 1.58 | 38% | 9% |
| `llama3.2:3b` | 4.86 | 1.51 | 1.71 | 1.64 | 40% | 18% |
| `gemma3:1b` | 4.73 | 1.70 | 1.66 | 1.38 | 30% | 4% |
| `qwen2.5:1.5b` | 4.71 | 1.68 | 1.74 | 1.29 | 33% | 10% |
| `qwen2.5:3b` | 4.61 | 1.55 | 1.58 | 1.47 | 40% | 15% |

By tone (score out of 6, 16 versions each):

| Model | Casual | Concise | Polished | Ornate | Diplomatic | Slang | Trendy |
|---|---|---|---|---|---|---|---|
| `qwen2.5:7b` | 5.81 | 5.81 | 5.12 | 5.44 | 5.50 | 4.88 | 4.75 |
| `gemma3:4b` | 5.38 | 5.50 | 5.38 | 5.62 | 5.31 | 5.06 | 5.00 |
| `phi4-mini` | 4.69 | 5.44 | 5.00 | 5.00 | 5.31 | 4.12 | 4.75 |
| `qwen3:4b-instruct` | 5.19 | 5.50 | 4.62 | 4.31 | 4.50 | 4.44 | 5.69 |
| `llama3.2:3b` | 4.88 | 4.56 | 5.38 | 5.06 | 5.12 | 4.06 | 4.94 |
| `gemma3:1b` | 5.44 | 5.50 | 4.81 | 4.06 | 4.81 | 4.00 | 4.50 |
| `qwen2.5:1.5b` | 5.31 | 5.38 | 4.88 | 4.56 | 4.94 | 4.00 | 3.94 |
| `qwen2.5:3b` | 5.25 | 5.69 | 4.31 | 5.00 | 3.94 | 4.62 | 3.44 |

What the low scores were:

- **`qwen2.5:7b`** lost the meaning 3 times. In Diplomatic, "vc viu o email que te mandei ontem?" became "I saw the
  email you sent yesterday", and "to atrasado uns 10 min" became "the meeting was delayed". In Slang, it added "my bad
  for not texting sooner".
- **`gemma3:4b`** lost it once: "to atrasado" became "he was about 10 minutes late".
- **The smaller models** made these mistakes again and again:
  - read "nem me fala" as "don't talk to me" and "puxado" as "pulled";
  - turned "Me manda o arquivo" into "I'll send you the file";
  - moved Friday to Saturday or to tomorrow;
  - dropped the "not" in "i dont think we can finish this until friday".
- **The worst versions** came from the smaller models:
  - `qwen2.5:3b` answered one Ornate version almost all in Portuguese. One Trendy version kept the Portuguese with
    slang stuck in, and moved Friday to "hoje".
  - `gemma3:1b` gave advice about the sentence instead of a rewrite.
  - `qwen2.5:1.5b` refused once ("I'm sorry, but I can't assist with that request") and dropped the "not" from the
    Friday sentence.
- **Slang and Trendy** were the weakest tones for every model. Most low marks there were a "fr" or "ngl" tacked on
  the end of a plain sentence.

No model left English in a way the app's retry (`TextShape.HasForeignLetters`) would catch, and none answered empty.

## Speed

A small script sent the app's exact prompts to Ollama one at a time (`tools/ToneCheck --dump-prompts` writes them). It
used 6 sentences in the three default tones (Casual, Concise, Polished), with each model loaded alone and warm. "3
tones" is the three versions of one sentence back to back, which is what you wait for until the last one shows.
The CPU runs set Ollama's `num_gpu: 0` and limited it to 8 or 4 threads of the 24-core desktop CPU. A laptop's cores
are slower, so read those columns as a best case for a laptop.

| Model | GPU: 1 version | GPU: 3 tones | CPU, 8 threads: 1 version | CPU, 8 threads: 3 tones | CPU, 4 threads: 1 version | CPU, 4 threads: 3 tones |
|---|---|---|---|---|---|---|
| `qwen2.5:7b` | 0.15 s | 0.44 s | 2.08 s | 6.16 s | 3.21 s | 9.70 s |
| `gemma3:4b` | 0.13 s | 0.38 s | 1.75 s | 5.21 s | 2.97 s | 8.70 s |
| `qwen3:4b-instruct` | 0.11 s | 0.33 s | 1.27 s | 3.94 s | 1.69 s | 5.19 s |
| `phi4-mini` | 0.09 s | 0.31 s | 1.07 s | 3.17 s | 1.44 s | 4.36 s |
| `llama3.2:3b` | 0.09 s | 0.28 s | 1.09 s | 3.18 s | 1.53 s | 4.70 s |
| `qwen2.5:3b` | 0.10 s | 0.29 s | 0.95 s | 2.77 s | 1.46 s | 4.39 s |
| `gemma3:1b` | 0.07 s | 0.23 s | 0.84 s | 2.48 s | 1.19 s | 3.60 s |
| `qwen2.5:1.5b` | 0.06 s | 0.17 s | 0.49 s | 1.45 s | 0.71 s | 2.38 s |

Medians. The slowest single version was under 0.3 s on the GPU for every model. On the CPU it was up to 1.6 times the
median: `qwen2.5:7b` peaked at 3.25 s with 8 threads and 5.13 s with 4. The app asks for the enabled tones at once.
Whether Ollama answers them in parallel depends on its own settings, which this test did not change, so the
3-tone column is the most you wait.

## Size, memory and license

| Model | Parameters (4-bit) | Download | Video memory added | License |
|---|---|---|---|---|
| `qwen2.5:7b` | 7.6B | 4.7 GB | 4,705 MiB | Apache 2.0 |
| `gemma3:4b` | 3.9B, plus a 0.4B vision part AIR does not use | 3.4 GB | 3,815 MiB | Gemma Terms of Use |
| `qwen3:4b-instruct` | 4.0B | 2.5 GB | 3,203 MiB | Apache 2.0 |
| `phi4-mini` | 3.8B | 2.5 GB | 3,125 MiB | MIT |
| `llama3.2:3b` | 3.2B | 2.0 GB | 2,636 MiB | Llama 3.2 Community License |
| `qwen2.5:3b` | 3.1B | 1.9 GB | 2,240 MiB | Qwen Research License (non-commercial) |
| `gemma3:1b` | 1.0B | 0.8 GB | 1,029 MiB | Gemma Terms of Use |
| `qwen2.5:1.5b` | 1.5B | 1.0 GB | 1,258 MiB | Apache 2.0 |

"Video memory added" is what `nvidia-smi` showed in use after the run, minus before it, with the app's 4096-token
context. `ollama show <model> --license` prints each license. The Gemma terms let you redistribute the model but pass
a use policy on to everyone who gets it. The Llama license asks for a "Built with Llama" notice. The Qwen Research
License rules out commercial use.

## Three ways to run the model

| | Ollama (AIR's engine) | llama.cpp inside a .NET process (probe) | Claude (AIR's other engine) |
|---|---|---|---|
| On disk besides the model | the Ollama app: 3.2 GB here (CUDA 12 and 13, ROCm and Vulkan) | about 75 MB (LLamaSharp, the Vulkan runtime and four CPU builds, Windows x64 only) | nothing |
| The model | 4.7 GB, pulled by Ollama | the same 4.7 GB GGUF file | at Anthropic |
| `qwen2.5:7b`, GPU | 0.15 s a version (CUDA) | 0.44 s a version (Vulkan), 1.30 s for 3 tones | not measured |
| `qwen2.5:7b`, CPU, 8 threads | 2.08 s a version | 3.49 s a version, 10.5 s for 3 tones | not measured |
| Cost | free | free | billed per token (below) |
| The sentence | stays on the PC | stays on the PC | goes to Anthropic |

**The llama.cpp probe.** The probe was a .NET 11 console app with LLamaSharp 0.30.0. That version ships builds only
for .NET 10 and newer and for .NET Standard 2.0, and its native-library options are only in the .NET 10 build. The
probe used the `LLamaSharp.Backend.Vulkan.Windows` and `LLamaSharp.Backend.Cpu`
packages. It loaded the same GGUF file Ollama stores for `qwen2.5:7b` and put all 29 layers on the GPU through
Vulkan. Loading took 2.3 s. It wrapped the app's prompts the way Ollama does for this model and decoded greedily, as
AIR does at temperature 0. Of its 18 answers, 17 were word for word Ollama's. The other was a Polished version
ending "when you have the chance" instead of "when you have a moment".

It was slower than Ollama in two places. Its first token took 0.19 s, against 0.03 s for Ollama's whole prompt.
It then generated about 45 tokens a second, against Ollama's 116 with CUDA. The probe created a new context for
every answer (LLamaSharp's stateless executor), so part of the gap is the probe, not the engine. This test did not
separate that from Vulkan against CUDA, and LLamaSharp's CUDA package was not tried.

**Claude's cost.** These are estimates from measured token counts; no live call was made. A default-tone prompt came
to 225 to 268 tokens with Qwen's tokenizer, and Claude's tokenizer may count more, so take up to 1.3 times that. An
answer was 13 tokens on average (26 at most). AIR asks for low effort, which may add a few thinking tokens; take 0 to
100 more. At Claude Haiku 5.5's $0.10 per million input tokens and $0.50 per million output tokens, a pause with
three tones costs about $0.0001 to $0.0003. At 200 pauses a day for 30 days, that is $0.60 to $1.60.

## What this does not show

- **One machine.** Laptops were not tested; the thread limits only stand in for one. The video memory figures are
  this card's, and other cards and drivers may differ a little.
- **One judge, one pass, 16 sentences.** The quality scores rank the models; they are not a precise measure.
- **Claude was not run.** Its quality and speed are unknown here, and its cost is an estimate.
- **The llama.cpp probe was a probe, not the app.** It did not keep one context between answers, did not answer
  tones in parallel (one batch with three sequences), and did not try CUDA.
- **The scripts and raw results** (the speed script, the judge's rubric and scores, the probe's source) are not in
  the repository.
