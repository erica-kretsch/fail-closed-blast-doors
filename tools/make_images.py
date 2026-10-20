"""Generate About/Preview.png and About/thumb.png.

LaunchPad needs both to register the mod. Run from the repo root:

    python tools/make_images.py

Needs Pillow. The picture is a blast door in four states: open and armed, losing power,
closed, then opening again under power. The door follows the game's model: an octagonal
opening in a light frame, two leaves with hazard-striped inner edges, and rounded status
light bars set into the frame (long at the top and bottom, short at the sides).
"""

from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONT_DIRS = [Path("C:/Windows/Fonts"), Path("/usr/share/fonts/truetype/dejavu")]

BG_TOP = (22, 27, 36)
BG_BOTTOM = (12, 15, 21)
FRAME = (178, 184, 192)
FRAME_EDGE = (120, 126, 136)
OPENING = (6, 8, 12)
LEAF = (222, 225, 229)
LEAF_EDGE = (150, 156, 166)
HAZARD_YELLOW = (222, 178, 36)
HAZARD_BLACK = (28, 28, 32)
ARROW = (70, 78, 92)
TEXT = (232, 236, 242)
MUTED = (150, 160, 175)

# The status light colours the game shows: green while the door moves, white when idle and
# powered, off without power, and amber (this mod's default RechargeColor) while the
# accumulator recharges.
MOVING_GREEN = (120, 240, 120)
IDLE_WHITE = (240, 242, 246)
LIGHT_OFF = (74, 80, 92)
AMBER = (255, 165, 0)


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


def octagon(x0, y0, x1, y1, c):
    return [(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c),
            (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)]


def draw_door(img, x, y, size, open_fraction, light):
    """A square doorway whose leaves part by open_fraction (0 closed, 1 open)."""
    w = h = size
    m = round(w * 0.10)  # frame thickness
    chamfer = round(w * 0.13)
    d = ImageDraw.Draw(img)
    d.polygon(octagon(x - m, y - m, x + w + m, y + h + m, chamfer + m // 2),
              fill=FRAME, outline=FRAME_EDGE)
    opening = octagon(x, y, x + w, y + h, chamfer)
    d.polygon(opening, fill=OPENING)

    # The leaves are drawn on their own layer and clipped to the opening.
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    cx = x + w // 2
    band = max(6, round(w * 0.07))  # the hazard-striped edge of each leaf
    # Fully open, each leaf and its band are clear of the opening.
    shift = round((w / 2 + band) * open_fraction)
    left_edge, right_edge = cx - shift, cx + shift
    ld.rectangle([x - w, y, left_edge - band, y + h], fill=LEAF)
    ld.rectangle([right_edge + band, y, x + 2 * w, y + h], fill=LEAF)
    for x0, x1 in ((left_edge - band, left_edge), (right_edge, right_edge + band)):
        ld.rectangle([x0, y, x1, y + h], fill=HAZARD_YELLOW)
        step = band * 2
        for yy in range(y - step, y + h + step, step):
            ld.polygon([(x0, yy), (x1, yy - band), (x1, yy - band + step // 2),
                        (x0, yy + step // 2)], fill=HAZARD_BLACK)
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).polygon(opening, fill=255)
    layer.putalpha(ImageChops.multiply(layer.getchannel("A"), mask))
    img.paste(layer, (0, 0), layer)
    ImageDraw.Draw(img).line(opening + [opening[0]], fill=LEAF_EDGE, width=2)

    # The status lights: long bars top and bottom, short bars at the sides, set into the frame.
    t = max(5, round(w * 0.05))
    long_len, short_len = round(w * 0.78), round(h * 0.22)
    cy = y + h // 2
    bars = [
        (cx - long_len // 2, y - m // 2 - t // 2, cx + long_len // 2, y - m // 2 + t // 2),
        (cx - long_len // 2, y + h + m // 2 - t // 2, cx + long_len // 2, y + h + m // 2 + t // 2),
        (x - m // 2 - t // 2, cy - short_len // 2, x - m // 2 + t // 2, cy + short_len // 2),
        (x + w + m // 2 - t // 2, cy - short_len // 2, x + w + m // 2 + t // 2, cy + short_len // 2),
    ]
    if light != LIGHT_OFF:
        # Blur the shape as a mask and paint the light colour through it, so the halo is the
        # light's colour rather than a blend with transparent black.
        halo = Image.new("L", img.size, 0)
        hd = ImageDraw.Draw(halo)
        for b in bars:
            hd.rounded_rectangle(b, radius=t // 2, fill=200)
        halo = halo.filter(ImageFilter.GaussianBlur(max(3, t)))
        img.paste(Image.new("RGB", img.size, light), (0, 0), halo)
    d = ImageDraw.Draw(img)
    for b in bars:
        d.rounded_rectangle(b, radius=t // 2, fill=light)
    return m


def render(width, height):
    img = gradient(width, height)
    scale = width / 1280
    size = round(196 * scale)
    gapx = round(96 * scale)
    total = 4 * size + 3 * gapx
    x0 = (width - total) // 2
    y0 = round(255 * scale)
    # The point is the close on power loss; the slow, charging open comes after it.
    steps = [(1.0, IDLE_WHITE, "Open, armed"), (0.45, LIGHT_OFF, "Power lost"),
             (0.0, LIGHT_OFF, "Closed"), (0.5, MOVING_GREEN, "Opening, 10 s")]
    for i, (frac, colour, label) in enumerate(steps):
        x = x0 + i * (size + gapx)
        m = draw_door(img, x, y0, size, frac, colour)
        d = ImageDraw.Draw(img)
        f = font(round(28 * scale))
        tw = d.textlength(label, font=f)
        d.text((x + (size - tw) / 2, y0 + size + m + round(24 * scale)), label, font=f, fill=MUTED)
        if i < len(steps) - 1:
            # Clear of both frames, which stand out a tenth of the door's width.
            ax = x + size + m + round(9 * scale)
            tip = x + size + gapx - m - round(9 * scale)
            head = tip - round(16 * scale)
            ay = y0 + size // 2
            h1, h2 = round(8 * scale), round(17 * scale)
            d.polygon(
                [(ax, ay - h1), (head, ay - h1), (head, ay - h2), (tip, ay),
                 (head, ay + h2), (head, ay + h1), (ax, ay + h1)],
                fill=ARROW,
            )

    d = ImageDraw.Draw(img)
    title = font(round(64 * scale), bold=True)
    sub = font(round(30 * scale))
    d.text((round(70 * scale), round(48 * scale)), "Fail-Closed Blast Doors", font=title, fill=TEXT)
    d.text((round(72 * scale), round(128 * scale)),
           "Close when the power goes. Reopen slowly, charging the accumulator.",
           font=sub, fill=MUTED)
    # The recharge colour is the one players will not have seen, so say what it means.
    note = font(round(26 * scale))
    r = round(9 * scale)
    nx, ny = round(72 * scale), height - round(70 * scale)
    d.rounded_rectangle([nx, ny + r // 2, nx + 4 * r, ny + r + r // 2], radius=r // 2, fill=AMBER)
    d.text((nx + 4 * r + round(14 * scale), ny - round(6 * scale)),
           "Amber status light: an open door is recharging its accumulator.",
           font=note, fill=MUTED)
    return img


def main():
    about = ROOT / "About"
    about.mkdir(exist_ok=True)
    render(1280, 720).save(about / "Preview.png", optimize=True)
    render(640, 360).save(about / "thumb.png", optimize=True)


if __name__ == "__main__":
    main()
