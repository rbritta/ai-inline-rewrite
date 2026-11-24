# AGENTS.md

For AI coding agents working on this repository.

## What this is

AI Inline Rewrite (AIR for short) is a Windows tray app. While you type in any app, it reads the sentence in the
focused field at a pause (UI Automation), asks a model for one English version per tone (a local Ollama model by
default, or Claude through the Anthropic SDK), and shows them in a bubble under the text cursor. A tap of the AIR key
opens pick mode; the chosen version replaces the sentence in place (Backspaces and a paste through the clipboard).
.NET 11, WPF for the bubble and the settings, WinForms for the tray icon, Windows only.

## Layout

| Path | What |
|---|---|
| `src/AiInlineRewrite/AppController.cs` | the flow: hooks, typing and the pause, rewrite, pick mode, apply, tray, settings |
| `src/AiInlineRewrite/Core/` | pure logic, linked into the tests: `Prompts`, `TextShape`, `Sentence`, `WordDiff`, `Lang`, `PickMode`, `AirKeyDetector`, `AirKey`, `Settings`, `SettingsStore`, `EngineStatus`, `EngineChoices`, `LocalModel`, `Tone`, `Log` |
| `src/AiInlineRewrite/Llm/` | the engines behind `IRewriteEngine`: `OllamaClient` (streaming `/api/generate`, `/api/tags` check and model list), `ClaudeClient` (Anthropic SDK, streaming Messages, Models API check); `RewriteEngines.Create` makes the one in the settings; `ModelDownloader` (the built-in model from Hugging Face) |
| `src/AiInlineRewrite/Local/` | the built-in engine: `BuiltInEngine` (LLamaSharp, llama.cpp on Vulkan or the CPU) and `BuiltInCheck` (`--check-builtin`) |
| `src/AiInlineRewrite/Native/` | keyboard and mouse hooks, recorded shortcuts (`GlobalHotKeys`), `SendInput` paste, clipboard snapshot, Start with Windows |
| `src/AiInlineRewrite/Automation/FocusedText.cs` | the focused field's text, caret and password flag |
| `src/AiInlineRewrite/Ui/` | the code-built bubble and settings window, preview, palette, vector icons, offscreen renderer |
| `tests/AiInlineRewrite.Tests/` | xUnit on plain `net11.0`; it **links** the `Core` and `Llm` files, with no reference to the app; `FakeHttp` stands in for Ollama and the Anthropic API |
| `tools/` | `ToneCheck` (every tone over sample sentences, either engine), `smoke/` (drives the real app on the desktop), `make_logo.py` (`--msix` for the package icons), `pack-msix.ps1`, `release-notes.ps1`, `coverage-summary.ps1` |
| `packaging/` | the MSIX manifest (Store package) and its icons, packed by `tools/pack-msix.ps1` |
| `design/experience.html` | the HTML mockup the app follows |
| `docs/` | architecture, engines, AI collaboration, branding, prompts, and the screenshots |

## Commands

```powershell
dotnet build AiInlineRewrite.sln
dotnet test tests\AiInlineRewrite.Tests                               # the unit tests
dotnet run --project tools\ToneCheck -c Release                       # needs Ollama with the model
dotnet run --project tools\ToneCheck -c Release -- --engine claude     # needs an Anthropic credential; bills the account
src\AiInlineRewrite\bin\Debug\net11.0-windows10.0.19041.0\AiInlineRewrite.exe --render-previews out   # PNGs, nothing shown
src\AiInlineRewrite\bin\Debug\net11.0-windows10.0.19041.0\AiInlineRewrite.exe --check-builtin out.md  # built-in engine, every tone
dotnet publish src\AiInlineRewrite -p:PublishProfile=win-x64           # the release folder
.\tools\pack-msix.ps1                                                  # the Store package (MSIX)
.\tools\release-notes.ps1 -Tag v0.1.0                                 # the notes of a release
```

## Invariants

- Only pure, dependency-free logic goes in `Core/`; anything that touches WPF, WinForms, Win32 or HTTP stays out of
  it. The engine clients in `Llm/` use only HttpClient and the Anthropic SDK. A new `Core` or `Llm` file is added to
  the `<Compile Include>` list of the test project.
- The hook takes keys from the app only in pick mode (`PickMode.KeyDown` returns `Swallow`), ignores injected
  events, and never uses Ctrl+Alt (AltGr on ABNT2 keyboards); a recorded shortcut goes to Windows (RegisterHotKey),
  never with Ctrl+Alt (`AirKeys.Problem`). Right Ctrl keeps working as Ctrl: only a press and
  release with no other key and no mouse input counts as a tap (`AirKeyDetector`).
- Nothing the user types is written to disk: the log holds events only and is off by default; password fields are
  never read.
- The only network traffic is to the engine selected in the settings (the Ollama endpoint, or the Anthropic API),
  plus the built-in model's download from Hugging Face when the user clicks Download.
  The other engine is never created. Tests never call a live engine.
- The Anthropic credential is found by the SDK (environment variables or an `ant auth login` profile); it is never a
  setting, never typed in the settings window, never logged, and without it nothing is sent.
- The docs state as fact only what was verified, and measurements name the reference machine
  (`docs/ENGINES.md`); anything else is marked "Not validated yet" with an entry in `TODO.md`.
- Applying a version restores the clipboard, and never edits a field that changed since the version was written
  (`Sentence.BackspacesToReplace` returns -1; the version is copied instead).
- A prompt change comes with a `tools/ToneCheck` run in `docs/PROMPTS.md`.
- The version lives only in `<Version>` of `src/AiInlineRewrite/AiInlineRewrite.csproj`, and `CHANGELOG.md` has its
  section under `Unreleased`; `VersionTests` checks both.
- English only, made-up text only, no em-dashes or en-dashes. `.editorconfig`: 4 spaces for C#, PowerShell, Python
  and XAML, 2 elsewhere, LF (PowerShell and the .sln CRLF).
