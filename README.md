# AI Inline Rewrite

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/logo-dark.png" />
  <img src="docs/logo.png" width="112" align="right" alt="AI Inline Rewrite logo: a pen nib and an ink wave inside a speech bubble, in front of a draft bubble" />
</picture>

Type in your language, get it rewritten right where you type. A small Windows tray app: write a sentence in
Portuguese or in English in any app, pause, and a bubble under the text cursor offers it in clean English, in a few
tones. Pick one with a key and it replaces what you typed. AIR for short.

It runs locally by default, with an open model in [Ollama](https://ollama.com) on your PC, or with Claude through the
Anthropic API when you would rather not run a model yourself (no video card, for one).

[![CI](https://github.com/rbritta/ai-inline-rewrite/actions/workflows/ci.yml/badge.svg)](https://github.com/rbritta/ai-inline-rewrite/actions/workflows/ci.yml)
[![release](https://img.shields.io/github/v/release/rbritta/ai-inline-rewrite?sort=semver&color=0284c7)](https://github.com/rbritta/ai-inline-rewrite/releases/latest)
[![license: MIT](https://img.shields.io/badge/license-MIT-7dd3fc.svg)](LICENSE)

<div align="center">

<img src="docs/bubble-card.png" width="500" alt="The AIR bubble in pick mode: three versions of 'i dont think we can finish this until friday', Casual, Concise and Polished, each with the words it changed struck through in red and the new ones in green; the second is selected, and the key hints up, down, Enter, 1 to 3 and Esc sit in the bar." />

</div>

[Engines](#engines) · [Install](#install) · [Use it](#use-it) · [Tones](#tones) · [Languages](#languages) ·
[Settings](#settings) · [What to expect](#what-to-expect) · [Privacy](#privacy) · [Develop](#develop) ·
[For AI agents](AGENTS.md) · [Roadmap](TODO.md)

## What it does

For people who write English at work in their second language. You type the way you think, in Portuguese or in
rough English, in Slack, Outlook, a browser, Notepad, anywhere with a text field. When you pause, AIR reads the
sentence you are typing and writes it again in English, once per tone:

- **Portuguese is translated.** "acho que a gente precisa conversar sobre o prazo amanhã" comes back as "I think we
  need to talk about the deadline tomorrow", or "we need to talk about the deadline tomorrow" in Concise.
- **English is corrected**, and the bubble shows exactly what changed: struck-through red for the words that go,
  green for the ones that come in.
- **Your shape is kept.** The version ends with the punctuation you typed (or none) and starts with the case you
  typed, so it drops into your text as if you had written it.

Then tap the **AIR key** (Right Ctrl, alone) and pick a version: it replaces your sentence in place. Or ignore it
and keep typing: the versions clear, and new ones come at the next pause. The bubble stays by your text while AIR is
on; turn AIR off in the settings or the tray menu when you want it gone, and the AIR key brings it back.

## Engines

AIR asks a language model for each version, through one of two engines:

| | Ollama (default) | Claude |
|---|---|---|
| Where the model runs | on your PC: `qwen2.5:7b` (Qwen2.5 7B, Apache 2.0) | at Anthropic: Claude Haiku 5.5 by default |
| You need | Ollama and the model (a 4.7 GB download) | internet and an Anthropic API key |
| On the [reference machine](docs/ENGINES.md#reference-machine) | 4.6 GiB of video memory; a median of 0.13 s per version on its GPU, 4.0 s on its CPU alone | not measured: live calls are [not validated yet](TODO.md#not-validated-yet) |
| Cost | free; the model holds memory while loaded | billed per token by Anthropic ([pricing](https://platform.claude.com/docs/en/about-claude/pricing)) |
| Your sentence | stays on your PC | goes to Anthropic's API |

[docs/ENGINES.md](docs/ENGINES.md) has the reference machine and what was measured on it (memory, speed, three other
Ollama models), how long Ollama keeps the model loaded and how to free it, the Claude models and setup, other
backends, Docker, and the platforms. The Claude engine was tested only against a fake Anthropic API so far.

## Install

1. **An engine**, one of:
   - **Ollama.** Install it from [ollama.com](https://ollama.com), then pull the model once, in a terminal:

     ```powershell
     ollama pull qwen2.5:7b
     ```

   - **Claude.** Create an API key in the [Claude Console](https://platform.claude.com/settings/keys) and store it as
     the user environment variable `ANTHROPIC_API_KEY`; then, after step 2, pick Claude in the settings window
     ([docs/ENGINES.md](docs/ENGINES.md#set-it-up) walks through it, `ant auth login` included).
     [Not validated yet](TODO.md#not-validated-yet) against the live API.

2. **AIR.** Download `AiInlineRewrite-vX.Y.Z-win-x64.zip` from
   [Releases](https://github.com/rbritta/ai-inline-rewrite/releases/latest), unzip it anywhere and run
   `AiInlineRewrite.exe`. It is one self-contained file: no installer, no .NET to install. The exe is not code-signed
   yet, so SmartScreen may ask you to confirm the first run.

3. **Start with Windows** (optional): right-click the tray icon and check *Start with Windows*.

Windows 11, 64-bit. Windows 10 64-bit was [not tested yet](TODO.md#not-validated-yet). Windows only: AIR is made of
Windows desktop APIs ([why](docs/ENGINES.md#platforms)). Running it a second time just opens the settings of the copy
already running.

## Use it

| You do | AIR does |
|---|---|
| Type a sentence and pause (0.7 s) | the bubble appears under the text cursor and writes one version per tone, as they stream in |
| Tap **Right Ctrl** alone (the AIR key) | pick mode: the first version is selected and a 4 s timer runs |
| **↑ ↓** | moves the selection (↑ on the first goes to the last); each key restarts the timer |
| **Enter** | applies the selected version |
| **1** to **5** | applies that version directly |
| **Esc** | clears the versions |
| Any other key, or 4 s without a key | leaves pick mode, and the key types as usual |
| Click a version | applies it |
| Drag the bubble by its bar | leaves it there (see *Follow the text cursor*) |

The keys above are taken from the app you type in only while pick mode is on; the rest of the time AIR only listens.
Right Ctrl keeps working as Ctrl in any combination (Ctrl+C, Ctrl+click): only a press and release with no other key
counts as a tap. When AIR is off, the AIR key turns it back on. Nothing uses Ctrl+Alt, which is AltGr on the ABNT2
keyboards many Brazilians use.

**Applying** deletes your sentence from the end of the field and pastes the version, then puts back whatever was on your
clipboard (text, images, files). Ctrl+Z in the app undoes it. If the field changed meanwhile (you typed after the
sentence, or moved elsewhere), AIR does not touch it, and if Windows refuses the keystrokes (the desktop got locked,
say), nothing is pasted: either way the version goes to the clipboard instead, and the bubble says Ctrl+V pastes it.

## Tones

Each tone is one version in the bubble. Turn them on or off and reorder them in the settings; the order is the key
number.

| Tone | On by default | What it does | "i dont think we can finish this until friday" |
|---|---|---|---|
| **Casual** | yes | fixes real mistakes only, keeps your slang (gonna, wanna stay) | I don't think we can finish this until Friday |
| **Concise** | yes | correct and direct: hedges and filler words go | we can't finish this until Friday |
| **Polished** | yes | fuller and courteous, in plain everyday words | I don't think we'll be able to finish this until Friday |
| **Ornate** | no | refined, literary vocabulary | I do not believe we shall be able to conclude this endeavor until Friday |
| **Diplomatic** | no | softens blunt or rude wording, keeps what you mean | I believe we may need until Friday to finish this |

Real output of `qwen2.5:7b` on the reference machine; [docs/PROMPTS.md](docs/PROMPTS.md) has ten sample sentences
through every tone, and how the prompts were tuned.

## Languages

AIR is built for Portuguese speakers who write English:

- **Portuguese is translated** into English, and the bubble says "Translated from Portuguese".
- **English is corrected**, with the changed words marked.
- **Other languages** are not tuned or tested. The prompts ask the model to translate any sentence that is not in
  English, but no other language was tried. The bubble only recognizes Portuguese: another language shows as an
  English correction (every word marked as changed), and a Spanish sentence with an accented letter is taken for
  Portuguese.
- **The versions are always in English.**

Adapting AIR to another pair (say Spanish to English, or Portuguese to Spanish) means changing the prompts, the
language guess and a few status texts; [docs/PROMPTS.md](docs/PROMPTS.md#another-language-pair) lists each place.

## Settings

Click the gear in the bubble, and the settings open beside it; or click the tray icon, and they open above the tray.

<div align="center">

<img src="docs/settings.png" width="380" alt="The settings window: AIR on, the five tones with checkboxes, key numbers and reorder arrows, Layout Card or Compact, Theme Windows, Light or Dark, the AIR key dropdown, Follow the text cursor with its arrow, and the Engine section: Ollama or Claude, the model qwen2.5:7b, and a green dot with Ready." />

</div>

- **AIR** on or off. Off, AIR stops reading what you type.
- **Tones**, as above.
- **Layout**: *Card* (the bar on top, as above) or *Compact* (a tighter list, the bar at the bottom). Clicking an
  option floats a preview beside the window.
- **Theme**: follow Windows, Light or Dark.
- **AIR key**: a tap of Right Ctrl (default) or of Right Shift, or Pause, Scroll Lock or Insert.
- **Follow the text cursor**: the bubble sits under the text cursor (filled arrow). Drag it somewhere and it stays
  there (grey outline arrow); click the arrow beside the gear to bring it back.
- **Engine**: Ollama or Claude, and the model: the models Ollama has installed, or Claude Haiku, Sonnet or Opus 5.5.
  Below them, the engine's state (a green dot and Ready, or what is wrong and the one step that fixes it). A change
  applies at once, without a restart. The Anthropic credential is never typed here; it stays in the environment
  ([Engines](#engines)). Switching in the running app is [not validated yet](TODO.md#not-validated-yet).

<div align="center">

<img src="docs/bubble-compact-dark.png" width="460" alt="The Compact layout in the dark theme: three versions in a tight list, with the bar at the bottom holding the logo, AIR, the status, the follow arrow and the gear." />

</div>

The settings live in `%AppData%\AiInlineRewrite\settings.json`. `Engine`, `OllamaModel` and `ClaudeModel` are also
in the window's Engine section; the rest have no switch there:

| Field | Default | What |
|---|---|---|
| `Engine` | `Ollama` | `Ollama` or `Claude` ([Engines](#engines)) |
| `OllamaModel` | `qwen2.5:7b` | any Ollama model you pulled |
| `OllamaEndpoint` | `http://localhost:11434` | where Ollama listens |
| `ClaudeModel` | `claude-haiku-5-5` | the Claude model, such as `claude-sonnet-5-5` or `claude-opus-5-5` |
| `DebounceMs` | `700` | the pause, in milliseconds, before AIR writes (200 to 5000) |
| `DiagnosticLog` | `false` | writes events (never what you type) to `log.txt` next to the settings, rotated at 256 KB |

Edit the file while AIR is closed. The Anthropic credential is never stored there: it stays in the environment.

## What to expect

The numbers below come from the [reference machine](docs/ENGINES.md#reference-machine) (Windows 11, a 12 GB desktop
GPU, a 24-core desktop CPU, 64 GB of RAM). Other GPUs, CPU-only laptops and Windows 10 were
[not tested yet](TODO.md#not-validated-yet).

- **Ollama on the GPU**: the ten sample sentences through all five tones, 50 versions one at a time, took 6.3 s, a
  median of 0.13 s each.
- **Ollama on the CPU alone**: a median of 4.0 s per version.
- **Claude**: tested only against a fake Anthropic API; live calls, and so their speed, are
  [not validated yet](TODO.md#not-validated-yet).
- **Where it works**: text fields whose text Windows UI Automation exposes. Where a field cannot be read, AIR stays
  quiet. Windows does not let a normal app see the keys typed into an app running as administrator, so AIR does not
  work there.
- **The sentence must be at the end of the field.** AIR rewrites the last sentence before the end of the text; a
  sentence in the middle of a long document is not offered yet.

When the engine cannot write (Ollama not running, the model missing, no Anthropic credential, a refused key, no
connection), the bubble and the settings window say so and show the one step that fixes it:

<div align="center">

<img src="docs/bubble-model-missing.png" width="440" alt="The bubble when the Ollama model is missing: 'The model qwen2.5:7b is missing' and the hint 'Run this once in a terminal: ollama pull qwen2.5:7b'." />

</div>

## Privacy

- **Local by default.** With Ollama, the only network traffic is to your own Ollama (`localhost` by default): no
  account, no cloud.
- **With Claude**, the sentence you are typing and the tone's instructions go to Anthropic's API at each pause, once
  per enabled tone, and Anthropic's terms apply to them. Nothing else from your PC is sent, and without a credential
  nothing is sent at all.
- **No telemetry**, with either engine.
- **Password fields are never read.** The bubble says so and stays empty.
- **No history.** AIR keeps the current sentence in memory while the bubble is open, nothing more. The diagnostic
  log is off by default and, when on, holds events and timings, never text.
- **Your clipboard is put back** after a version is pasted, with the formats it held.

## Develop

Windows with the [.NET 9 SDK](https://dotnet.microsoft.com/download). Built and tested on the
[reference machine](docs/ENGINES.md#reference-machine), Windows 11 x64 with the .NET SDK 9.0.315. An engine (Ollama,
or an Anthropic credential) is only needed to run the app or `tools/ToneCheck`, not to build or test: the tests drive
both engine clients through a fake HTTP handler.

```powershell
git clone https://github.com/rbritta/ai-inline-rewrite
cd ai-inline-rewrite
dotnet build AiInlineRewrite.sln
dotnet test tests\AiInlineRewrite.Tests                              # the unit tests
dotnet publish src\AiInlineRewrite -p:PublishProfile=win-x64          # -> artifacts\publish\win-x64\AiInlineRewrite.exe
dotnet run --project tools\ToneCheck -c Release                      # every tone over the sample sentences (Ollama)
dotnet run --project tools\ToneCheck -c Release -- --engine claude    # the same through Claude (bills the account)
```

CI builds, tests (with a coverage summary in the job log) and publishes the exe on Windows for every push and pull
request; a `vX.Y.Z` tag publishes a GitHub Release. See [CONTRIBUTING.md](CONTRIBUTING.md) and
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

```text
AiInlineRewrite.sln
src/AiInlineRewrite/              the app: WPF bubble and settings, WinForms tray, net9.0-windows
  AppController.cs                  the flow: typing, pause, rewrite, pick, apply
  Core/                             pure logic, unit-tested: prompts, text shaping, pick mode, AIR key, settings
  Llm/                              the engines behind IRewriteEngine: OllamaClient, ClaudeClient (Anthropic SDK)
  Native/                           keyboard and mouse hooks, SendInput, clipboard snapshot
  Automation/FocusedText.cs         the focused field through UI Automation
  Ui/                               bubble, settings window, preview, palette, icons
tests/AiInlineRewrite.Tests/      xUnit on plain net9.0, links the pure files and the engine clients
tools/                            ToneCheck, the desktop smoke test, make_logo.py, release notes and coverage for CI
design/experience.html            the HTML mockup the app follows
```

## Contributing

Issues and pull requests are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and the
[Code of Conduct](CODE_OF_CONDUCT.md) first. Found a security issue? See [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE)
