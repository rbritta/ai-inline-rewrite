# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **A built-in engine, now the default.** AIR runs Qwen2.5 7B Instruct by itself through LLamaSharp (llama.cpp): on
  the GPU through Vulkan when there is one, on the CPU otherwise. Nothing else to install: the settings download the
  model once (4.7 GB, Qwen's official files on Hugging Face, tried again by itself when the connection drops or stalls,
  resumed where it stopped, and checked against their SHA-256),
  or AIR uses Ollama's `qwen2.5:7b` when it is already on the PC. On the reference machine it takes a median of 0.45 s
  a version. The downloaded file is the Qwen team's own 4-bit file, not Ollama's: the two write different versions of
  two sentences in three, and scored the same in a blind check (`docs/MODELS.md`). It frees the model after 30 minutes
  without use.
- On the first run with the built-in engine and no model yet, AIR opens the settings at the download.
- A Microsoft Store package: the MSIX manifest and icons in `packaging/`, and `tools/pack-msix.ps1`. Each release also
  keeps an unsigned package. In the package, "Start with Windows" is a Windows startup task.
- "Report a version" in the tray menu: it opens an issue form on GitHub for a version that was offensive, harmful
  or otherwise wrong, with AIR's version and engine filled in.
- `PRIVACY.md`: what AIR reads, where it goes with each engine, and what it keeps.
- `AiInlineRewrite.exe --check-builtin file.md`: every tone over sample sentences through the built-in engine, with
  the time of each version.
- `docs/MODELS.md`: eight Ollama models compared on the reference machine (quality judged blind over 896 versions,
  speed on the GPU and on the CPU alone, size, video memory, license), and llama.cpp inside a .NET process measured
  next to Ollama.
- `tools/ToneCheck --dump-prompts file.json` writes the exact prompts the app sends to Ollama, without calling it.

### Changed
- The app, the tests and the tools moved from .NET 9 to .NET 11 (`net11.0-windows10.0.19041.0`, `net11.0`); building
  needs the .NET 11 SDK, and AIR needs Windows 10 version 2004 or later.
- The release zip holds a folder (`AiInlineRewrite.exe` and its files) instead of one exe: a single-file .NET 11 WPF
  app failed to start. It stays self-contained, so running it still needs no .NET install.
- `Engine` defaults to `BuiltIn`. A settings file written before keeps its engine, and one with no `Engine` keeps
  Ollama, so an existing install goes on as it was.

## [0.2.1] - 2026-11-22

### Fixed
- An end mark before a lowercase word no longer ends the sentence: in a sentence such as "nossa, hoje foi
  corrido!! mas amanhã a gente conversa melhor", AIR rewrote only the part after "!!". A new sentence now
  starts after a line break, or after ". ! ?" followed by a space and a capital letter (Portuguese capitals too), and
  "3.5" or "site.com" no longer split a sentence. An answer keeps both parts when the user typed both.
- The desktop smoke test failed at the start when the user had left AIR asleep or off: it now starts AIR awake and
  puts the user's settings back at the end.

## [0.2.0] - 2026-11-22

### Added
- Two slang tones, off by default: Slang, everyday slang the way people talk today (gonna, my bad, no worries), and
  Trendy, the newest internet slang (ngl, lowkey, fr, no cap). On `qwen2.5:7b` Trendy leans on "fr" and once kept a
  garbled Portuguese word; docs/PROMPTS.md has the run.
- A tap of Right Shift alone puts AIR to sleep: the bubble closes and nothing is written until the AIR key wakes it
  (the same as turning AIR off in the tray). A capital typed with Right Shift is not a tap. When Right Shift is the
  AIR key, a tap of Right Ctrl does it.
- `tools/ToneCheck` takes `--sentence` and `--tone`, to run a sentence a user reported through the tones it failed in.
- The desktop smoke test checks the Right Shift tap, a capital with Right Shift, and waking with the AIR key.

### Fixed
- A version that drifted into another alphabet mid-sentence (Ornate wrote Chinese for "gírias novas" in a
  Portuguese question) showed only an error. The Ollama engine now asks once more, telling the model it left
  English; what still drifts is not offered, as before.

## [0.1.0] - 2026-11-22

The first public version.

### Added
- A bubble under the text cursor that writes the sentence you are typing in English, once per tone, as the model
  streams it. Portuguese is translated; English is corrected, with the changed words shown struck through in red and
  the new ones in green.
- Two engines, chosen in the settings window or in `settings.json`: Ollama, the default, with `qwen2.5:7b` running
  on your PC; and Claude through the Anthropic API with the official Anthropic SDK, `claude-haiku-5-5` by default,
  the credential found by the SDK (`ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, or an `ant auth login` profile) and
  never stored. Only the selected engine is ever contacted. The Claude engine was tested against a fake Anthropic API
  only; live calls are not validated yet (TODO.md).
- Five tones: Casual, Concise and Polished on by default, Ornate and Diplomatic off. Each can be turned on or off
  and reordered; the order is the key number. The prompts were tuned on `qwen2.5:7b` (docs/PROMPTS.md).
- Every answer is checked before it is offered: it keeps the user's final punctuation and first-letter case, one
  sentence when the user typed one, and a version with letters outside the Latin alphabet is never offered.
- The AIR key: a tap of Right Ctrl alone by default (Right Ctrl keeps working in every combination), or a tap of
  Right Shift, or Pause, Scroll Lock or Insert. It opens pick mode: ↑ ↓ and Enter, a number, or Esc, with a 4 s
  timer; any other key leaves pick mode and types. Keys are taken from the app only while pick mode is on. When AIR
  is off, the AIR key turns it on. Nothing uses Ctrl+Alt (AltGr on ABNT2 keyboards).
- Applying a version replaces the sentence in place and puts the clipboard back with the formats it held; when the
  field changed meanwhile, or Windows refuses the keystrokes, the version is copied to the clipboard instead.
- The settings window: AIR on or off, tones, Layout (Card or Compact) and Theme (Windows, Light or Dark) with a
  floating preview, the AIR key, Follow the text cursor, and the Engine: Ollama or Claude, the model (Ollama's
  installed models, or Claude Haiku, Sonnet and Opus 5.5) and the engine's state with the step that fixes it; a
  change applies at once. The gear in the bubble opens it beside the bubble; the tray icon, above the tray.
- Follow the text cursor: on by default; dragging the bubble leaves it there, and the arrow beside the gear brings
  it back.
- Clear states when the engine cannot write (Ollama not running, the model missing, no Anthropic credential, a
  refused key, no connection), each with the one step that fixes it, in the bubble and in the settings window.
- Password fields are never read. The diagnostic log is off by default, never holds what you type, and rotates at
  256 KB.
- One copy runs at a time; starting it again opens the settings of the running copy.
- Start with Windows, from the tray menu.
- Screen readers see the bubble and the settings: every button, switch, checkbox, option and version has a name and
  can be pressed or toggled through UI Automation.
- The Q icon and the PQ2 logo, generated by `tools/make_logo.py`.
- `tools/ToneCheck`, which sends sample sentences through every tone on either engine and times each version; its
  runs are in docs/PROMPTS.md.
- `tools/smoke`, a smoke test that drives the real app on the desktop, the engine picker included (CONTRIBUTING.md).
- A self-contained single-file exe for Windows x64, built and released by CI on a `vX.Y.Z` tag, with the zip's
  SHA-256 checksum and a build provenance attestation to verify it (SECURITY.md).

[Unreleased]: https://github.com/rbritta/ai-inline-rewrite/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/rbritta/ai-inline-rewrite/releases/tag/v0.1.0
