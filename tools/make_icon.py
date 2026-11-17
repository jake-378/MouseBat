from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "res" / "mousebat.ico"
BASE = 512
SCALE = 2
N = BASE * SCALE

image = Image.new("RGBA", (N, N), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)

def box(values):
    return tuple(int(value * SCALE) for value in values)

def rounded(values, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(
        box(values),
        radius=radius * SCALE,
        fill=fill,
        outline=outline,
        width=width * SCALE,
    )

# A dark rounded tile, with an outlined gaming mouse and a bright battery badge.
rounded((18, 18, 494, 494), 112, "#141B29", "#344258", 10)
rounded((48, 40, 298, 470), 122, "#253247", "#E7F3FF", 16)
draw.line(
    (153 * SCALE, 48 * SCALE, 153 * SCALE, 222 * SCALE),
    fill="#C9E8FF",
    width=14 * SCALE,
)
rounded((131, 125, 175, 216), 22, "#78D8FF")

# Battery badge overlaps the lower-right of the mouse so both shapes read as one mark.
rounded((260, 292, 458, 424), 30, "#101722", "#78D8FF", 16)
rounded((455, 333, 484, 385), 9, "#78D8FF")
bolt = [
    (365, 308),
    (325, 358),
    (354, 358),
    (338, 406),
    (394, 348),
    (362, 348),
]
draw.polygon([(x * SCALE, y * SCALE) for x, y in bolt], fill="#C7F36B")

# Supersampling keeps the small Windows taskbar and shortcut icon sizes crisp.
image = image.resize((BASE, BASE), Image.Resampling.LANCZOS)
OUT.parent.mkdir(parents=True, exist_ok=True)
image.save(OUT.with_suffix(".png"), format="PNG")
image.save(OUT, format="ICO", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print(OUT)
