# TODO

## Not validated yet

What the docs mark as "Not validated yet". Each item is built, but was not checked the way the docs would need to
state it as fact.

- **Claude, live.** The Claude engine was tested only against a fake Anthropic API (the unit tests). Not yet: a real
  key, `tools/ToneCheck --engine claude` (its answers next to `qwen2.5:7b`'s, and its speed), and the app with Claude
  as the engine.
- **`ant auth login`.** The Anthropic SDK reads a profile from `%APPDATA%\Anthropic` when no key is set; that was read
  in the SDK's code and checked in the tests with a made-up profile. A real sign-in with the Anthropic CLI, then AIR
  using it, was not tried.
- **Another model, picked in the running app.** Switching engines there passed the desktop smoke test on the
  reference machine on 2026-11-22: Ollama to Claude and back, the status line and the bubble following at once with
  no restart, Claude's models in the list, and a fresh sample corrected after switching back (Claude in its
  no-credential state, since live Claude is the first item). Picking another model from the list, for either
  engine, was not run.
- **Other configurations.** Every measurement comes from the reference machine (docs/ENGINES.md): Windows 11, a 12 GB
  desktop GPU, a 24-core desktop CPU, 64 GB of RAM. Not tested: other GPUs (and how much video memory the default
  model needs on them), CPU-only laptops, a full bubble of three tones on the CPU, and Windows 10.
- **Ollama in Docker.** The steps in docs/ENGINES.md follow the documentation of the `ollama/ollama` image and were not
  run; AIR was tested against a native Ollama only.

## Roadmap

Roughly in the order it would be picked up. Nothing here is promised; an issue moves an item up.

- Code-signed releases, so Windows names the publisher instead of an unknown one.
- A setting for how long Ollama keeps the model loaded (AIR asks for 30 minutes), and unloading it when AIR is
  turned off or quits.
- A smaller default model for PCs without a GPU, chosen with `tools/ToneCheck` on a CPU, and the first-run hint
  pointing to it.
- Thinking models in Ollama (`gemma4:12b`, `qwen3-vl:8b`): they spend the 256 tokens AIR allows per answer on
  thinking and answer nothing, and `"think": false` did not stop `qwen3-vl:8b` (docs/ENGINES.md, Other models).
- `tools/ToneCheck` counting empty answers, so a model that answers nothing does not pass as "0 left English".
- A list of the apps AIR was checked in (Slack, Outlook, Teams, the browsers), with what works in each.
- Rewrite a sentence in the middle of a field, not only the last one before the end of the text.
- Two more layouts from the design round: a speech bubble with a tail pointing at the text cursor, and pills.
- More engines behind `IRewriteEngine`: OpenAI-compatible servers (LM Studio, llama.cpp, vLLM) and other cloud APIs,
  opt-in, with the privacy note next to the switch.
- Other language pairs: the prompts, the language guess and a few texts are written for Portuguese and English
  ([docs/PROMPTS.md](docs/PROMPTS.md#another-language-pair)); nothing else in the app is.
- Hide the bubble while a full-screen app (a game, a presentation) has the focus.
- Per-app rules: turn AIR off in apps where it gets in the way.
- winget and a signed exe, so SmartScreen does not ask on the first run.
- "any doubt" (a Portuguese speaker's "any question") is still corrected to "any doubts" by most tones.
