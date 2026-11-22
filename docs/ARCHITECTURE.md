# Architecture

AI Inline Rewrite is one process: a WPF application with no main window, a WinForms tray icon, two low-level hooks
and one engine client (Ollama over HTTP, or Claude through the Anthropic SDK). Everything runs on the UI thread except
the UI Automation read and the engine calls, which are awaited.

```text
keyboard hook ──> AppController ──(pause)──> FocusedText (UI Automation) ──> Sentence.ExtractCurrent
      │                 │                                                          │
      │ AIR key, pick   │                                                          v
      v                 │                                IRewriteEngine (Ollama or Claude), one request per tone
 AirKeyDetector         │                                                          │ streamed, shaped by TextShape
 PickMode               v                                                          v
                  BubbleWindow <──────────────── rows (tone, text, done, error) ───┘
                        │
                        └─(apply)──> TextInjector: Ctrl+End, Backspace × n, Ctrl+V; ClipboardSnapshot restores
```

## The flow

1. **Typing.** `KeyboardHook` (WH_KEYBOARD_LL) passes every key event to `AppController.OnKey`. A key that edits
   text (`Vk.Edits`: letters, digits, punctuation, Space, Tab, Enter, Backspace, Delete, and Ctrl with V, X, Z, Y,
   Backspace or Delete) clears the rows
   and restarts the pause timer (`DebounceMs`, 700 ms). Navigation keys and shortcuts do not.
2. **The pause.** `FocusedText.TryRead` asks UI Automation for the focused element on a worker thread: its text
   (ValuePattern, else TextPattern), the caret rectangle (the TextPattern selection) and `IsPassword`. A password
   field stops here. `Sentence.ExtractCurrent` takes the last sentence; `IsWorthRewriting` wants two words and a
   letter. The same sentence as last time restores the cached rows instead of asking again.
3. **Rewrite.** `Lang.LooksPortuguese` decides how the rows render (a translation as plain text, a correction as a
   `WordDiff` against what was typed) and which language line the prompts get. One `IRewriteEngine.RewriteAsync` per
   enabled tone runs at once (see *Engines* below); every partial answer goes through `TextShape.MatchPunctuation`
   before it is shown. A version with non-Latin letters becomes an error row, and a version the model declines (a
   Claude refusal) says so in its row while the others go on. Typing again cancels the whole generation.
4. **Pick.** The AIR key comes from `AirKeyDetector`: a tap key (Right Ctrl, Right Shift) fires on release when it
   was pressed alone, within 600 ms, with no other key held and no mouse input (a mouse hook is installed only while
   the key is down, to catch Ctrl+click and Ctrl+wheel); a plain key (Pause, Scroll Lock, Insert) fires on key down
   and is swallowed. `PickMode` is a pure state machine: it says, for each key, whether the hook swallows it and what
   to do (move, apply, dismiss, exit). Only in pick mode does the hook take keys from the app.
5. **Apply.** `Sentence.BackspacesToReplace` checks that the field still ends with the sentence the version was
   written for (a CRLF counts as one Backspace). If it does, `TextInjector` releases any held modifier, sends
   Ctrl+End, the Backspaces and Ctrl+V through `SendInput`, with the version on the clipboard; `ClipboardSnapshot`
   took every clipboard format first and puts them back after. If it does not, the version goes to the clipboard and
   the bubble says so. The hook ignores injected events, so the paste does not restart the pause.

## Engines

`AppController` talks to one `IRewriteEngine` (`Llm/IRewriteEngine.cs`), created by `RewriteEngines.Create` from
`Settings.Engine`; only that engine exists in the process, so with Claude nothing ever reaches Ollama and the other way
round. An engine streams a version (`RewriteAsync`), says whether it can write now without spending tokens
(`CheckAsync`, when it is created) and gets ready (`PreloadAsync`). A new engine or model from the settings window
takes effect at once (`AppController.SwitchEngine`): the versions in flight are cancelled, the cached ones dropped,
the new engine created and checked; a check that finishes after another switch is ignored. A failure the user can fix is an `EngineException` with an
`EngineState`; `EngineStatus` turns it into the status line and the one-step hint, per engine.

- **`OllamaClient`**: `POST /api/generate` with the whole prompt (`Prompts.For`), temperature 0, `num_predict` 256 and
  `keep_alive` 30 minutes, read as NDJSON until `done`. `CheckAsync` reads `/api/tags` and looks for the model, and
  `ListModelsAsync` reads the same list for the settings window; `PreloadAsync` rewrites a made-up sentence so the
  model is loaded before the first pause. Ollama runs the tones in
  parallel or queues them, depending on its own settings.
- **`ClaudeClient`**: the official Anthropic SDK (`Anthropic` on NuGet), `Messages.CreateStreaming` with the tone's
  instructions as the system prompt (`Prompts.Instructions`), the sentence as the one user message (`Prompts.Input`),
  `max_tokens` 1024 and low effort, and no sampling parameters, which current models do not take. Text deltas are
  shaped as they come; a `refusal` stop reason declines that one version. The SDK finds the credential (`ANTHROPIC_API_KEY`,
  `ANTHROPIC_AUTH_TOKEN`, or a profile in `%APPDATA%\Anthropic` such as `ant auth login` saves); without any, the
  client sends nothing and reports `NoKey`, and a profile it cannot read is a refused key. `CheckAsync` looks the model up in the Models API (no
  tokens); `PreloadAsync` does nothing. SDK errors map to states: 401 or 403 a refused credential, 404 a missing model, a
  connection failure "cannot be reached", anything else (429, 5xx, a timeout) "not answering", which the next pause
  retries.

Both are tested without a network: `FakeHttp` scripts the answers (NDJSON for Ollama, server-sent events and error
bodies for the Anthropic API) and records the requests.

## The bubble

`BubbleWindow` is a borderless, transparent, topmost WPF window with `WS_EX_NOACTIVATE` and `WS_EX_TOOLWINDOW`, and
it answers `WM_MOUSEACTIVATE` with `MA_NOACTIVATE`: clicking it never takes the focus from the field you type in.
`BubbleView.Create` builds the whole visual tree from a `BubbleState` (layout, rows, status, pick selection, timer,
follow state) on every render; renders are coalesced on the dispatcher. It sits 10 px under the caret and flips above
when there is no room; dragging the bar (a manual drag with mouse capture, since the window cannot activate) turns
Follow off and saves the position.

`SettingsWindow` is code-built too and talks to the app through `ISettingsHost`. Choosing a layout or a theme floats
a `PreviewWindow` beside it, rendered by the same `BubbleView`. The Engine section takes its choices from
`EngineChoices` (Ollama's installed models, asked through the host; the three Claude models) and redraws its status
line when the app reports a new engine state (`SyncEngine`). `Palette` holds the light and dark brushes;
`WindowsTheme` reads `AppsUseLightTheme` and follows its changes. `Icons` draws the Q, the gear, the follow arrow and
the rest as vector geometry, so they stay sharp at any scale.

`PreviewRenderer` renders the bubble in every layout and state, and the settings window, to PNG with
`RenderTargetBitmap`, without showing a window: `AiInlineRewrite.exe --render-previews <folder>`.

## Settings and state

`Settings` is a plain class saved as JSON (enums by name) in `%AppData%\AiInlineRewrite\settings.json`, written
atomically (a temp file, then a move) 300 ms after the last change. `Normalize` repairs a hand-edited file (duplicate
or missing tones, no tone on, an out-of-range pause). An unreadable file is kept aside as `settings.unreadable.json`
and the defaults are used. The Anthropic credential is never a setting: the SDK finds it outside AIR.

One copy runs at a time: a named mutex, and a named event that a second launch sets so the running copy opens its
settings.

## Why the tests link files

The app targets `net9.0-windows` with WPF and WinForms. The tests target plain `net9.0` and compile the `Core` files
into their own assembly, with the two engine clients in `Llm`, so they need no desktop runtime and every piece of logic
in `Core` is reachable without a window or a hook. Everything that decides something (which text, which key, which
version, which settings) lives in `Core`; the rest only wires it to Windows.
