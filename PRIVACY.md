# Privacy policy

AI Inline Rewrite (AIR) is a Windows app that rewrites the sentence you are typing. This page says what it reads,
where that goes and what it keeps. It applies to every copy of AIR: the Microsoft Store one and the zip from this
repository's Releases page. Last updated: 2026-11-23.

## What AIR reads

- **What you type, to know when you pause.** AIR watches the keyboard (a low-level hook) to see typing, the pause and
  the AIR key.
- **The sentence you are typing.** At a pause, AIR reads the focused text field through Windows UI Automation and
  takes the sentence at the end of it. A field marked as a password is never read.

## Where it goes

Only to the engine you choose in the settings:

- **Built-in** (the default): the model runs inside AIR, on your PC. The sentence does not leave it.
- **Ollama**: the sentence goes to Ollama, on your PC by default (`http://localhost:11434`), or to the machine you set
  in `OllamaEndpoint`.
- **Claude**: the sentence and the tone's instructions go to Anthropic's API over HTTPS, once per enabled tone, only
  when you pick Claude and give AIR an Anthropic credential. Anthropic's terms and privacy policy apply to what it
  receives.

The engine you did not choose is never contacted. AIR has no telemetry, no analytics, no ads and no account of its
own, and it sends nothing to its author. "Report a version" in the tray menu only opens a GitHub issue form in your
browser, with AIR's version and engine filled in; what goes in the issue is what you write there, and GitHub issues
are public.

## The model download

With the built-in engine, AIR needs its model once: Qwen2.5 7B Instruct (4.7 GB, Apache 2.0). If Ollama already has
`qwen2.5:7b` on your PC, AIR uses that file and downloads nothing. Otherwise it downloads the model from Hugging Face
(`huggingface.co`) when you click Download in the settings. As with any download, Hugging Face sees the request (your
IP address and the file asked for); no text you type is part of it.

## What AIR keeps

- **Settings** in `settings.json` (your tones, layout, keys and engine), in AIR's own folder in your user profile.
- **The model files**, after the download, in AIR's folder for local data.
- **A diagnostic log**, only if you turn on `DiagnosticLog`: events and timings, never what you type.

AIR keeps no history of what you type: the current sentence and its versions stay in memory while the bubble shows
them. Uninstalling AIR from the Microsoft Store removes its settings and the downloaded model.

## The clipboard

Applying a version pastes it through the clipboard, then puts back what the clipboard held before.

## Questions

Open an issue at <https://github.com/rbritta/ai-inline-rewrite/issues>, or see [SECURITY.md](SECURITY.md) for how to
report a security problem privately.
