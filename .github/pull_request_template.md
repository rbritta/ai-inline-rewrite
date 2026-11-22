## What changed

<!-- One or two sentences. Link the issue it closes, if any. -->

## Why

<!-- The problem it solves, or what it makes possible. -->

## How it was tested

<!-- The unit tests added or changed, and what you checked by hand (the apps you typed in, the tones, the AIR key). -->

## Checklist

- [ ] Builds and the tests pass (`dotnet build AiInlineRewrite.sln`, then `dotnet test tests\AiInlineRewrite.Tests`)
- [ ] Unit tests cover any pure logic or engine client that changed (text shaping, pick mode, AIR key, settings, Ollama, Claude); a bug fix comes with a test that fails without it
- [ ] A prompt change comes with a `tools/ToneCheck` run in `docs/PROMPTS.md`
- [ ] `CHANGELOG.md` has a line under `Unreleased`
- [ ] README or AGENTS.md describe the change, when a feature or a setting changed
- [ ] Made-up text only; no em-dashes or en-dashes; follows `.editorconfig`
