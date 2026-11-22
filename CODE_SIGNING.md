# Code signing policy

**Status: the releases are not code-signed yet.** Signing is planned through the SignPath Foundation's program for
open source projects. Until it starts, check each download as [SECURITY.md](SECURITY.md#verifying-a-download) shows:
every release carries the zip's SHA-256 checksum and a build provenance attestation. When signing starts, this page
and the release notes will say so.

## What gets signed

Only `AiInlineRewrite.exe` as the release workflow ([`.github/workflows/release.yml`](.github/workflows/release.yml))
builds it from a tagged commit of this repository, after the build's unit tests pass. Nothing built anywhere else is
signed, and each release is signed only after one of the approvers below approves it by hand.

## Roles

| Role | Members |
|---|---|
| Authors (commit to this repository) | [rbritta](https://github.com/rbritta) |
| Reviewers (review every change before a release) | [rbritta](https://github.com/rbritta) |
| Approvers (approve each signing request) | [rbritta](https://github.com/rbritta) |

Every member uses multi-factor authentication on GitHub, and will on the signing service.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user.
With the default engine, Ollama on the same PC, what you type stays on your PC. The Claude engine, only when you pick
it in the settings, sends the sentence you are typing to Anthropic's API; [README.md](README.md#privacy) and
[SECURITY.md](SECURITY.md#trust-model) have the details.

## What AIR changes on your PC

The folder `%AppData%\AiInlineRewrite` (the settings, and the log when it is on), and one value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` only while *Start with Windows* is checked.
[README.md](README.md#install) says how to uninstall it.
