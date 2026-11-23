#!/usr/bin/env python3
"""
Generate the AI Inline Rewrite icons and logo from code, with no external design tool.

The Q mark: a pen nib (with its slit and breather hole) over an ink wave, in the Sky gradient
(#7DD3FC to #0284C7). It is the app icon, the tray icon and the glyph in the bubble's bar.

The PQ2 logo: a draft speech bubble in outline behind, and the rewrite bubble in front, filled
with the Sky gradient and holding the whole Q in white. Used in the README, the release page and
the GitHub preview image.

The geometry is the one of src/AiInlineRewrite/Ui/Icons.cs, authored in a 64-unit grid and
supersampled, so the mark stays crisp from 512 px down to 16 px.

Usage:
    pip install pillow
    python tools/make_logo.py

Outputs (overwritten in place):
    src/AiInlineRewrite/Resources/app.ico       the Q, 16 to 256 px: the exe and the tray while AIR is on
    src/AiInlineRewrite/Resources/tray-off.ico  the Q in grey, 16 to 48 px: the tray while AIR is off
    docs/logo.png                               PQ2, 512 px, for light backgrounds
    docs/logo-dark.png                          PQ2, 512 px, for dark backgrounds
    docs/social-preview.png                     1280x640, the GitHub preview image

    python tools/make_logo.py --msix

writes only the Microsoft Store package's images (the Q), into packaging/Assets: Square44x44Logo (with the
16 to 256 px target sizes, plated and unplated), Square150x150Logo, Wide310x150Logo and StoreLogo.
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, "src", "AiInlineRewrite", "Resources")
DOCS = os.path.join(ROOT, "docs")

SKY_LIGHT = (0x7D, 0xD3, 0xFC)
SKY_DEEP = (0x02, 0x84, 0xC7)
GREY = (0x8B, 0x8F, 0x96)
WHITE = (0xFF, 0xFF, 0xFF)

SS = 16  # supersampling: pixels per grid unit at the master size (64 units -> 1024 px)


# ---------- geometry helpers (grid units) ----------

def quad(p0, c, p1, steps=24):
    """Points of a quadratic Bezier from p0 to p1 with control c."""
    pts = []
    for i in range(1, steps + 1):
        t = i / steps
        x = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * c[0] + t * t * p1[0]
        y = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * c[1] + t * t * p1[1]
        pts.append((x, y))
    return pts


def nib_outline():
    """M32 56 L21 33 Q21 15 32 6 Q43 15 43 33 Z"""
    pts = [(32, 56), (21, 33)]
    pts += quad((21, 33), (21, 15), (32, 6))
    pts += quad((32, 6), (43, 15), (43, 33))
    return pts


def wave_points():
    """M10 57 q7 -6 14 0 t14 0 t14 0, in absolute terms."""
    pts = [(10, 57)]
    pts += quad((10, 57), (17, 51), (24, 57))
    pts += quad((24, 57), (31, 63), (38, 57))
    pts += quad((38, 57), (45, 51), (52, 57))
    return pts


def rotate(p, deg, cx, cy):
    a = math.radians(deg)
    x, y = p[0] - cx, p[1] - cy
    return (cx + x * math.cos(a) - y * math.sin(a), cy + x * math.sin(a) + y * math.cos(a))


def scale_about(p, s, cx, cy):
    return (cx + (p[0] - cx) * s, cy + (p[1] - cy) * s)


# ---------- drawing on a mask ----------

def to_px(pts, k, off=(0, 0)):
    return [((x + off[0]) * k, (y + off[1]) * k) for x, y in pts]


def stroke(draw, pts, width, fill):
    """A polyline with round caps and joins."""
    draw.line(pts, fill=fill, width=int(round(width)))
    r = width / 2
    for x, y in pts:
        draw.ellipse([x - r, y - r, x + r, y + r], fill=fill)


def q_mask(size_units, k, transform, nib_scale=1.0, wave_scale=1.0, wave_width=3.8, wave_center=(31, 57)):
    """The Q as an 'L' mask: the nib less its slit and hole, and the wave. transform maps grid units."""
    w = int(size_units[0] * k)
    h = int(size_units[1] * k)
    mask = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(mask)

    def place_nib(p):
        p = rotate(p, 40, 32, 31)
        p = scale_about(p, nib_scale, 32, 31)
        return transform(p)

    d.polygon([(x * k, y * k) for x, y in map(place_nib, nib_outline())], fill=255)
    # holes: the slit (32,35)-(32,53) and the breather hole at (32,31), turned with the nib
    s = transform((1, 0))[0] - transform((0, 0))[0]  # the transform's own scale
    slit = [place_nib((32, 35 + i * 0.5)) for i in range(0, 37)]
    stroke(d, [(x * k, y * k) for x, y in slit], 2.6 * s * nib_scale * k, 0)
    cx, cy = place_nib((32, 31))
    r = 3.6 * s * nib_scale * k
    d.ellipse([cx * k - r, cy * k - r, cx * k + r, cy * k + r], fill=0)

    def place_wave(p):
        return transform(scale_about(p, wave_scale, *wave_center))

    stroke(d, [(x * k, y * k) for x, y in map(place_wave, wave_points())], wave_width * s * wave_scale * k, 255)
    return mask


def gradient(w, h, k, p0=(6, 4), p1=(58, 60), c0=SKY_LIGHT, c1=SKY_DEEP, off=(0, 0)):
    """The Sky gradient along p0 -> p1 (grid units), like SVG gradientUnits=userSpaceOnUse."""
    img = Image.new("RGB", (w, h))
    px = img.load()
    ax, ay = (p0[0] + off[0]) * k, (p0[1] + off[1]) * k
    bx, by = (p1[0] + off[0]) * k, (p1[1] + off[1]) * k
    dx, dy = bx - ax, by - ay
    length2 = dx * dx + dy * dy
    # one row of t values per y is enough to fill quickly
    for y in range(h):
        for x in range(w):
            t = ((x - ax) * dx + (y - ay) * dy) / length2
            t = 0 if t < 0 else 1 if t > 1 else t
            px[x, y] = tuple(int(round(c0[i] + (c1[i] - c0[i]) * t)) for i in range(3))
    return img


def paint(mask, fill):
    """An RGBA image: fill (a color or an RGB image) with the mask as its alpha (no dark fringe at the edges)."""
    layer = fill.convert("RGBA") if isinstance(fill, Image.Image) else Image.new("RGBA", mask.size, tuple(fill) + (255,))
    layer.putalpha(mask)
    return layer


# ---------- the Q (app and tray icon) ----------

def render_q(flat=None):
    k = SS
    mask = q_mask((64, 64), k, lambda p: p)
    fill = flat if flat else gradient(64 * k, 64 * k, k)
    return paint(mask, fill)


def to_icon(master, px, margin_frac=0.04):
    """Crop to the mark, center it on a square with a small margin, then downscale (full-bleed icon)."""
    box = master.getbbox()
    cropped = master.crop(box)
    w, h = cropped.size
    side = int(round(max(w, h) * (1 + 2 * margin_frac)))
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.alpha_composite(cropped, ((side - w) // 2, (side - h) // 2))
    return canvas.resize((px, px), Image.LANCZOS)


def save_ico(master, path, sizes):
    images = [to_icon(master, s) for s in sizes]
    images[-1].save(path, format="ICO", sizes=[(s, s) for s in sizes], append_images=images[:-1])


# ---------- PQ2 (the logo) ----------

def render_pq2(dark=False):
    k = SS
    w = h = 64 * k
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    draft_color = SKY_LIGHT if dark else SKY_DEEP

    # the draft bubble: outline, its tail and two text lines, at half opacity
    draft = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(draft)
    d.rounded_rectangle([5 * k, 4 * k, 41 * k, 30 * k], radius=9 * k, outline=255, width=3 * k)
    stroke(d, [(12 * k, 30 * k), (9 * k, 38 * k), (19 * k, 30 * k)], 3 * k, 255)
    stroke(d, [(12 * k, 12 * k), (32 * k, 12 * k)], 3 * k, 255)
    stroke(d, [(12 * k, 20 * k), (22 * k, 20 * k)], 3 * k, 255)
    draft = draft.point(lambda v: v // 2)
    out.alpha_composite(paint(draft, draft_color))

    # the rewrite bubble in front, filled with the gradient
    front = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(front)
    d.rounded_rectangle([20 * k, 22 * k, 60 * k, 52 * k], radius=10 * k, fill=255)
    d.polygon([(47 * k, 50 * k), (54 * k, 60 * k), (38 * k, 51 * k)], fill=255)
    grad = gradient(w, h, k)
    out.alpha_composite(paint(front, grad))

    # the whole Q in white inside it: at 46 %, the nib 15 % and the ink line 8 % smaller, centered in the body
    inner = q_mask((64, 64), k, lambda p: (25.74 + p[0] * 0.46, 19.22 + p[1] * 0.46),
                   nib_scale=0.85, wave_scale=0.92, wave_width=6)
    out.alpha_composite(paint(inner, WHITE))
    return out


def crop_square(img, margin_frac=0.05):
    box = img.getbbox()
    c = img.crop(box)
    w, h = c.size
    side = int(round(max(w, h) * (1 + 2 * margin_frac)))
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.alpha_composite(c, ((side - w) // 2, (side - h) // 2))
    return canvas


def font(bold, size):
    names = ["segoeuib.ttf" if bold else "segoeui.ttf", "arialbd.ttf" if bold else "arial.ttf"]
    for n in names:
        for folder in (os.path.join(os.environ.get("WINDIR", "C:\\Windows"), "Fonts"), ""):
            try:
                return ImageFont.truetype(os.path.join(folder, n) if folder else n, size)
            except OSError:
                continue
    return ImageFont.load_default()


def render_social(pq2_dark):
    w, h = 1280, 640
    img = Image.new("RGBA", (w, h), (0x0B, 0x12, 0x20, 255))
    logo = crop_square(pq2_dark).resize((340, 340), Image.LANCZOS)
    img.alpha_composite(logo, (110, 150))
    d = ImageDraw.Draw(img)
    d.text((500, 205), "AI Inline Rewrite", font=font(True, 76), fill=(0xF3, 0xF4, 0xF6))
    d.text((502, 318), "Type in your language, get it", font=font(False, 40), fill=(0xC7, 0xD2, 0xDE))
    d.text((502, 370), "rewritten right where you type.", font=font(False, 40), fill=(0xC7, 0xD2, 0xDE))
    d.text((502, 450), "Windows · local AI with Ollama · MIT", font=font(False, 28), fill=(0x7D, 0xD3, 0xFC))
    bar = gradient(w, 10, 1, p0=(0, 0), p1=(w, 0))
    img.paste(bar, (0, h - 10))
    return img


def centered(master, w, h, mark):
    """The Q, mark px tall, centered on a transparent w x h tile."""
    tile = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    q = to_icon(master, mark, margin_frac=0)
    tile.alpha_composite(q, ((w - mark) // 2, (h - mark) // 2))
    return tile


def write_msix_assets():
    assets = os.path.join(ROOT, "packaging", "Assets")
    os.makedirs(assets, exist_ok=True)
    q = render_q()
    to_icon(q, 44).save(os.path.join(assets, "Square44x44Logo.png"))
    for s in (16, 24, 32, 48, 256):
        to_icon(q, s).save(os.path.join(assets, f"Square44x44Logo.targetsize-{s}.png"))
        to_icon(q, s).save(os.path.join(assets, f"Square44x44Logo.targetsize-{s}_altform-unplated.png"))
    centered(q, 150, 150, 96).save(os.path.join(assets, "Square150x150Logo.png"))
    centered(q, 310, 150, 96).save(os.path.join(assets, "Wide310x150Logo.png"))
    to_icon(q, 50).save(os.path.join(assets, "StoreLogo.png"))
    print("wrote packaging/Assets")


def main():
    if "--msix" in sys.argv:
        write_msix_assets()
        return
    os.makedirs(RES, exist_ok=True)
    os.makedirs(DOCS, exist_ok=True)

    q = render_q()
    save_ico(q, os.path.join(RES, "app.ico"), [16, 20, 24, 32, 40, 48, 64, 128, 256])
    save_ico(render_q(flat=GREY), os.path.join(RES, "tray-off.ico"), [16, 20, 24, 32, 40, 48])

    light = render_pq2(dark=False)
    dark = render_pq2(dark=True)
    crop_square(light).resize((512, 512), Image.LANCZOS).save(os.path.join(DOCS, "logo.png"))
    crop_square(dark).resize((512, 512), Image.LANCZOS).save(os.path.join(DOCS, "logo-dark.png"))
    render_social(dark).convert("RGB").save(os.path.join(DOCS, "social-preview.png"))
    print("wrote Resources/app.ico, Resources/tray-off.ico, docs/logo.png, docs/logo-dark.png, docs/social-preview.png")


if __name__ == "__main__":
    main()
