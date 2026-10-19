"""Generate About/Preview.png and About/thumb.png.

LaunchPad needs both to register the mod. Run from the repo root:

    python tools/make_images.py

Needs Pillow. The picture is a blast door in three positions: open and armed, losing power,
and closed.
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONT_DIRS = [Path("C:/Windows/Fonts"), Path("/usr/share/fonts/truetype/dejavu")]

BG_TOP = (22, 27, 36)
BG_BOTTOM = (12, 15, 21)
FRAME = (70, 78, 92)
DOOR = (120, 130, 145)
DOOR_DARK = (88, 96, 110)
AMBER = (255, 165, 0)
RED = (255, 84, 72)
GREEN = (96, 214, 130)
TEXT = (232, 236, 242)
MUTED = (150, 160, 175)


def font(size, bold=False):
    names = (
        ["segoeuib.ttf", "arialbd.ttf", "DejaVuSans-Bold.ttf"]
        if bold
        else ["segoeui.ttf", "arial.ttf", "DejaVuSans.ttf"]
    )
    for d in FONT_DIRS:
        for n in names:
            if (d / n).exists():
                return ImageFont.truetype(str(d / n), size)
    return ImageFont.load_default()


def gradient(w, h):
    img = Image.new("RGB", (w, h))
    px = img.load()
    for y in range(h):
        k = y / max(1, h - 1)
        row = tuple(round(a + (b - a) * k) for a, b in zip(BG_TOP, BG_BOTTOM))
        for x in range(w):
            px[x, y] = row
    return img


def draw_door(d, x, y, w, h, open_fraction, light):
    """A doorway with two leaves that part by open_fraction (0 closed, 1 open)."""
    d.rectangle([x - 10, y - 10, x + w + 10, y + h + 10], fill=FRAME)
    d.rectangle([x, y, x + w, y + h], fill=(8, 10, 14))
    gap = round(w / 2 * open_fraction)
    leaf = w // 2 - gap
    if leaf > 0:
        d.rectangle([x, y, x + leaf, y + h], fill=DOOR, outline=DOOR_DARK, width=4)
        d.rectangle([x + w - leaf, y, x + w, y + h], fill=DOOR, outline=DOOR_DARK, width=4)
    r = max(6, w // 14)
    cx = x + w // 2
    d.ellipse([cx - r, y - 38 - r, cx + r, y - 38 + r], fill=light)


def render(width, height):
    img = gradient(width, height)
    d = ImageDraw.Draw(img)
    scale = width / 1280
    pw, ph = round(200 * scale), round(290 * scale)
    gapx = round(110 * scale)
    total = 3 * pw + 2 * gapx
    x0 = (width - total) // 2
    y0 = round(270 * scale)
    steps = [(1.0, GREEN, "Open, armed"), (0.45, AMBER, "Power lost"), (0.0, RED, "Closed")]
    for i, (frac, colour, label) in enumerate(steps):
        x = x0 + i * (pw + gapx)
        draw_door(d, x, y0, pw, ph, frac, colour)
        f = font(round(28 * scale))
        tw = d.textlength(label, font=f)
        d.text((x + (pw - tw) / 2, y0 + ph + round(34 * scale)), label, font=f, fill=MUTED)
        if i < 2:
            ax = x + pw + round(24 * scale)
            ay = y0 + ph // 2
            d.polygon(
                [(ax, ay - 14), (ax + gapx - round(48 * scale), ay - 14),
                 (ax + gapx - round(48 * scale), ay - 28),
                 (ax + gapx - round(34 * scale), ay),
                 (ax + gapx - round(48 * scale), ay + 28),
                 (ax + gapx - round(48 * scale), ay + 14), (ax, ay + 14)],
                fill=FRAME,
            )
    title = font(round(64 * scale), bold=True)
    sub = font(round(30 * scale))
    d.text((round(70 * scale), round(48 * scale)), "Fail-Closed Blast Doors", font=title, fill=TEXT)
    d.text((round(72 * scale), round(128 * scale)),
           "Open slowly, charge the accumulator, close when the power goes.",
           font=sub, fill=MUTED)
    return img


def main():
    about = ROOT / "About"
    about.mkdir(exist_ok=True)
    render(1280, 720).save(about / "Preview.png", optimize=True)
    render(640, 360).save(about / "thumb.png", optimize=True)


if __name__ == "__main__":
    main()
