# Contributing

Thanks for your interest in improving AI Inline Rewrite.

## Set up, build and test

Windows with the [.NET 9 SDK](https://dotnet.microsoft.com/download) (built and tested with 9.0.315 on Windows 11
x64). An engine, [Ollama](https://ollama.com) with a model pulled or an Anthropic API key, is needed to run the app and
`tools/ToneCheck`, not to build or test.

```powershell
git clone https://github.com/rbritta/ai-inline-rewrite
cd ai-inline-rewrite
dotnet build AiInlineRewrite.sln
dotnet test tests\AiInlineRewrite.Tests
```

- The app is `net9.0-windows` (WPF for the bubble and the settings, WinForms for the tray icon). The tests are a
  plain `net9.0` xUnit project that **links** the pure source files under `src/AiInlineRewrite/Core` and the engine
  clients under `src/AiInlineRewrite/Llm` (no reference to the app or to the desktop runtime). The engine tests answer
  from `FakeHttp`, a scripted handler: no Ollama, no Anthropic key, no network. They also check the version and the
  CHANGELOG, and run `tools/release-notes.ps1`.
- Coverage, as CI prints it:

  ```powershell
  dotnet test tests\AiInlineRewrite.Tests --collect "Code Coverage;Format=cobertura" --results-directory TestResults
  .\tools\coverage-summary.ps1 -Path TestResults
  ```

- The exe, as a release ships it: `dotnet publish src\AiInlineRewrite -p:PublishProfile=win-x64` writes
  `artifacts\publish\win-x64\AiInlineRewrite.exe`.
- The bubble and the settings window can be checked without showing them: `AiInlineRewrite.exe --render-previews
  <folder>` renders every layout and state, in light and dark, to PNG files and exits. The screenshots in `docs/`
  come from it.
- The smoke test drives the real app on the real desktop: `python tools\smoke\smoke_test.py <exe> <folder>` types
  the samples into a text box of its own, then checks the AIR key, pick mode (arrows, Enter, a number, Esc, another
  key, a chord, the 4 s close), Ctrl+Z, the drag and the follow arrow, the settings, and the engine picker
  (switched to Claude, the settings and the bubble show the no-key state at once; back on Ollama, a fresh sample is
  corrected), 23 checks in all, and saves screenshots. The engine picker checks need Ollama as the engine and no
  Anthropic credential on the PC; otherwise they print SKIP and the run has 18. It takes over the keyboard and mouse
  for about 3 minutes, so run it on an unlocked desktop nobody is using; it stops if the focus leaves its windows or
  another window covers the point it is about to click, and puts your AIR settings back at the end. Anything else
  that breaks the run ends it as one FAIL naming the last check that passed, with the traceback in `error.txt` in the
  folder. It starts the
  app with `--accept-injected`, the switch that lets synthesized keystrokes drive AIR (without it, only real typing
  does).
- Beyond that, check by hand in real apps (Notepad, a browser, a chat app), with the AIR key and each tone.

CI builds, tests and publishes the exe on Windows for every push and pull request.

## Prompts

A change to `Core/Prompts.cs` or to `Core/TextShape.cs` comes with a run of `tools/ToneCheck` on the default engine
in [docs/PROMPTS.md](docs/PROMPTS.md), so the review sees what every tone answers before and after:

```powershell
dotnet run --project tools\ToneCheck -c Release > tonecheck.md
```

A 7B model is sensitive to wording: one sentence added to a prompt can change the answers of every tone. Run all the
samples, not only the one you were fixing.

## Pull requests

- Behavior-affecting changes come with a unit test when the logic is pure (text shaping, pick mode, the AIR key,
  settings) or in an engine client (against `FakeHttp`, never a live engine); a bug fix with a test that fails
  without it. UI changes come with a `--render-previews` screenshot.
- Add a line under `Unreleased` in [CHANGELOG.md](CHANGELOG.md).
- Made-up text only in tests, docs and screenshots.

## Releasing

The version lives in one place, `<Version>` in `src/AiInlineRewrite/AiInlineRewrite.csproj`; `VersionTests` checks
it is three-part and that `CHANGELOG.md` has its section right under `Unreleased`.

Bump `<Version>`, move the `Unreleased` notes under `## [X.Y.Z] - YYYY-MM-DD`, push, wait for CI, then push a
`vX.Y.Z` tag. The release workflow builds, tests and publishes the exe, zips it with the README and the license, and
publishes a GitHub Release with the CHANGELOG section of that version as its notes (`tools/release-notes.ps1`
refuses a tag that does not name the version).

## Style

- Follow the `.editorconfig` (4-space indent for C#, PowerShell, Python and XAML, 2 elsewhere, LF; PowerShell
  scripts and the solution file CRLF; file-scoped namespaces). Match the surrounding code.
- Keep commit messages in the imperative mood with a short summary line.
- Do not use em-dashes or en-dashes in code, comments, UI text or docs.
