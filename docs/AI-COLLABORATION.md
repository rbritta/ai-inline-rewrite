# AI collaboration

AIR puts a language model next to your typing, so it keeps one clear boundary:

> **The model writes versions. Only you apply one.**

Nothing the model answers reaches your text on its own. It shows up in the bubble, and stays there until you pick it
with the AIR key or a click; keep typing and it is gone. What you send is always something you chose.

## What the model sees

- **One sentence at a time**: the sentence you are typing, at a pause. Not the rest of the field, not the app, not
  who you are writing to.
- **One prompt per tone**, written in [`Core/Prompts.cs`](../src/AiInlineRewrite/Core/Prompts.cs) and shown in full,
  with real answers, in [PROMPTS.md](PROMPTS.md).
- **Never a password field.**

By default the model runs in your own Ollama; with the Claude engine, the sentence and the tone's instructions go to
Anthropic's API ([ENGINES.md](ENGINES.md)). Either way AIR keeps no history of what it asked or what came back.

## Why the answers get guard rails

A small model on a PC does not always follow instructions ([PROMPTS.md](PROMPTS.md) shows where `qwen2.5:7b` did
not), so AIR checks every answer before it is offered, whatever the engine (`Core/TextShape.cs`):

- it keeps the first line, without an echoed label ("Corrected:") or quotes;
- it keeps one sentence when you typed one;
- it ends with the punctuation you typed, or none, and starts with the case you typed;
- it is never offered when it holds letters outside the Latin alphabet (a model asked for literary English once
  drifted into Chinese mid-sentence).

And the bubble shows, for an English sentence, exactly which words the version changed, so a rewrite that went too
far is visible before you apply it.

## Applying is careful too

AIR replaces the sentence only when the field still ends with it. If you typed on, or moved to another field, the
version goes to the clipboard instead and the bubble tells you. Your clipboard is put back after the paste, and the
app's own Ctrl+Z undoes it.

## Building it with AI agents

This repository was built with an AI coding agent working from design mockups that the author approved one by one; the
one the app follows is `design/experience.html`. [AGENTS.md](../AGENTS.md) gives any agent the layout, the commands and
the invariants above, and [CONTRIBUTING.md](../CONTRIBUTING.md) asks for a `tools/ToneCheck` run with every prompt
change, so a person reviews what the model actually answers, not only the prompt text.
