# Security

## Supported versions

Only the latest minor release gets fixes.

| Version | Supported |
|---|---|
| 0.1.x | yes |
| older | no |

## Trust model

AI Inline Rewrite reads what you type. By default it keeps that on your PC; the Claude engine sends the sentence you
are typing to Anthropic, and only when you choose it.

- **Keyboard and mouse hooks.** AIR installs a low-level keyboard hook to see typing, the pause and the AIR key, and
  a mouse hook only while a tap key is held. It takes keys away from the app you type in only in pick mode (the
  arrows, Enter, the digits and Esc), and ignores the keys it sends itself.
- **What it reads.** The focused text field, through Windows UI Automation, at a pause. A field marked as a password
  is never read. The current sentence stays in memory while the bubble shows it; nothing is written to disk.
- **Where it goes.** Only to the engine in the settings. With Ollama (the default), to its endpoint,
  `http://localhost:11434` by default; if you point `OllamaEndpoint` at another machine, your sentences travel to it
  over plain HTTP, so use a machine and a network you trust. With Claude, to the Anthropic API over HTTPS, through the
  official Anthropic SDK: the sentence and the tone's instructions, one request per enabled tone, and nothing without
  a credential. The engine not selected is never contacted; switching engines in the settings drops the old one.
- **The Anthropic credential.** Found by the Anthropic SDK, in this order: the `ANTHROPIC_API_KEY` environment
  variable, `ANTHROPIC_AUTH_TOKEN`, or a profile in `%APPDATA%\Anthropic` (where `ant auth login` keeps its sign-in).
  Environment variables reach AIR when it starts. AIR never writes the credential to `settings.json`, the log or
  anywhere else, and the settings window never asks for it. Keep a key as a user variable, not a system one, and
  revoke it in the Anthropic Console if it leaks.
- **The log.** Off by default. When `DiagnosticLog` is on, it writes events and timings (never text) to
  `%AppData%\AiInlineRewrite\log.txt`, rotated at 256 KB.
- **The clipboard.** Applying a version pastes it through the clipboard and then puts back what was there.
- **Start with Windows** writes one value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, and only when
  you check it in the tray menu.
- **Elevated apps.** AIR runs without administrator rights, so Windows keeps it from seeing keys typed into apps
  running as administrator. Do not run AIR as administrator to get around it.
- **The exe is not code-signed yet.** Download it only from this repository's Releases page, and check it as below.

## Verifying a download

Each release has, next to the zip, its SHA-256 checksum (the `.sha256` file) and a build provenance attestation: a
signed record, made by the release workflow, that this repository built the exe and the zip from the tagged commit.

- **The checksum**, in PowerShell, in the folder of both downloads (prints `True` when they match):

  ```powershell
  (Get-FileHash .\AiInlineRewrite-v0.1.0-win-x64.zip).Hash -eq (Get-Content .\AiInlineRewrite-v0.1.0-win-x64.zip.sha256).Split(' ')[0]
  ```

- **The attestation**, with the [GitHub CLI](https://cli.github.com), for the zip or the exe inside it:

  ```powershell
  gh attestation verify .\AiInlineRewrite-v0.1.0-win-x64.zip --repo rbritta/ai-inline-rewrite
  ```

## Reporting a vulnerability

Please report suspected vulnerabilities privately through
[GitHub private vulnerability reporting](https://github.com/rbritta/ai-inline-rewrite/security/advisories/new)
rather than a public issue. You can expect an initial response within a few days.
