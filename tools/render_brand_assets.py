from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1] / "src" / "BTHaven.App" / "Assets"
SCALE = 4
NAVY = (11, 32, 54, 255)
BLUE = (30, 141, 255, 255)
CYAN = (118, 231, 255, 255)
WHITE = (248, 253, 255, 255)


def load_font(size: int, semibold: bool = False):
    candidates = [
        Path(r"C:\Windows\Fonts\segoeuisb.ttf") if semibold else Path(r"C:\Windows\Fonts\segoeui.ttf"),
        Path(r"C:\Windows\Fonts\segoeui.ttf"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size)
    return ImageFont.load_default()


def mark(size: int) -> Image.Image:
    high = size * SCALE
    image = Image.new("RGBA", (high, high), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    radius = int(high * 0.23)
    draw.rounded_rectangle((0, 0, high - 1, high - 1), radius=radius, fill=NAVY)

    center = high / 2
    outer = int(high * 0.34)
    inner = int(high * 0.27)
    draw.ellipse((center - outer, center - outer, center + outer, center + outer), outline=(118, 231, 255, 38), width=max(2, int(high * 0.035)))
    draw.ellipse((center - inner, center - inner, center + inner, center + inner), outline=(118, 231, 255, 60), width=max(1, int(high * 0.008)))

    line_width = max(3, int(high * 0.05))
    x = center
    top = int(high * 0.21)
    bottom = int(high * 0.79)
    right = int(high * 0.70)
    left = int(high * 0.30)
    mid = int(high * 0.50)
    points = [
        (x, top), (right, int(high * 0.39)), (x, mid),
        (right, int(high * 0.61)), (x, bottom),
    ]
    draw.line([(x, top), (x, bottom)], fill=CYAN, width=line_width, joint="curve")
    draw.line([points[0], points[1], points[2], points[3], points[4]], fill=BLUE, width=line_width, joint="curve")
    draw.line([(x, mid), (left, int(high * 0.34))], fill=CYAN, width=line_width, joint="curve")
    draw.line([(x, mid), (left, int(high * 0.66))], fill=CYAN, width=line_width, joint="curve")

    node_radius = max(3, int(high * 0.035))
    for px, py, color in [
        (x, top, WHITE), (right, int(high * 0.39), WHITE),
        (right, int(high * 0.61), WHITE), (x, bottom, WHITE),
        (left, int(high * 0.34), (166, 243, 255, 255)),
        (left, int(high * 0.66), (166, 243, 255, 255)),
    ]:
        draw.ellipse((px - node_radius, py - node_radius, px + node_radius, py + node_radius), fill=color)

    return image.resize((size, size), Image.Resampling.LANCZOS)


def save_icon(name: str, size: int):
    image = mark(size)
    image.save(ROOT / name)


def save_wide():
    width, height = 620, 300
    image = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((0, 0, width - 1, height - 1), radius=34, fill=NAVY)
    icon = mark(220)
    image.alpha_composite(icon, (34, 40))
    draw.text((290, 90), "BTHaven", font=load_font(54, True), fill=WHITE)
    draw.text((294, 155), "Bluetooth hub for Windows", font=load_font(21), fill=(166, 243, 255, 255))
    image.save(ROOT / "Wide310x150Logo.scale-200.png")


def save_splash():
    width, height = 1240, 600
    image = Image.new("RGBA", (width, height), NAVY)
    icon = mark(360)
    image.alpha_composite(icon, (108, 120))
    draw = ImageDraw.Draw(image)
    draw.text((545, 220), "BTHaven", font=load_font(82, True), fill=WHITE)
    draw.text((552, 320), "Open Bluetooth hub for Windows", font=load_font(30), fill=(166, 243, 255, 255))
    image.save(ROOT / "SplashScreen.scale-200.png")


ROOT.mkdir(parents=True, exist_ok=True)
save_icon("Square150x150Logo.scale-200.png", 300)
save_icon("Square44x44Logo.scale-200.png", 88)
save_icon("Square44x44Logo.targetsize-24_altform-unplated.png", 24)
save_icon("Square44x44Logo.targetsize-48_altform-lightunplated.png", 48)
save_icon("LockScreenLogo.scale-200.png", 48)
save_icon("StoreLogo.png", 50)
mark(256).save(ROOT / "AppIcon.ico", format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
save_wide()
save_splash()
print(f"Generated BTHaven app assets in {ROOT}")
