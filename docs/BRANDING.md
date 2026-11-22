# Branding

## Name

**AI Inline Rewrite** is the name: it says what the app does, without promising a speed (that depends on the PC) or
a coverage (that depends on the app you type in), and it is not tied to one language. **AIR** is the short name, used
in the bubble, the tray tooltip and the docs, never as the main name or the repository name (Adobe AIR, MacBook Air
and the Go tool `air` already use it).

Tagline: *Type in your language, get it rewritten right where you type.*

## The mark

**The Q**: a pen nib, with its slit and breather hole, over an ink wave, in the Sky gradient. It is the app icon, the
tray icon (grey while AIR is off) and the glyph in the bubble's bar and the settings title bar. Inside the app it is
drawn as vector geometry (`Ui/Icons.cs`); the `.ico` files come from the same geometry.

**The PQ2 logo**: a draft speech bubble in outline behind, and the rewrite bubble in front, filled with the Sky
gradient and holding the whole Q in white. The draft and the rewrite: the whole product in one mark. It is used in
the README, the release page and the GitHub preview image.

The logo is never put inside a tile: either the PQ2 bubbles or the loose Q with its gradient.

## Palette

| Part | Hex |
|---|---|
| Sky gradient, light end | `#7DD3FC` |
| Sky gradient, deep end | `#0284C7` |
| Controls, light theme (switches, checkboxes, segments, the selection) | `#0284C7`, solid |
| Controls, dark theme | `#7DD3FC`, solid, with `#0B1220` on it |
| Glyph on the gradient | white |
| The Q while AIR is off | `#8B8F96` |

Controls are solid Sky, never a gradient; the gradient belongs to the mark.

## Regenerate

```bash
pip install pillow
python tools/make_logo.py
```

This overwrites, in place:

- `src/AiInlineRewrite/Resources/app.ico`: the Q, 16 to 256 px, the exe and the tray while AIR is on
- `src/AiInlineRewrite/Resources/tray-off.ico`: the Q in grey, 16 to 48 px, the tray while AIR is off
- `docs/logo.png` and `docs/logo-dark.png`: PQ2, 512 px, for light and dark backgrounds
- `docs/social-preview.png`: 1280 × 640, the GitHub preview image (Settings > Social preview)

The geometry is authored in a 64-unit grid (the same one as `Ui/Icons.cs`) and supersampled, so the mark stays crisp
from 512 px down to 16 px.

## Screenshots

The images of the bubble and the settings in `docs/` are rendered by the app itself, offscreen:

```powershell
AiInlineRewrite.exe --render-previews out
```
