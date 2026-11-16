"""Recolor the existing shop frame to the approved four quality palettes."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import colorsys

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/AssetRaw/UI/UISprites/MysteriousStore/shopIcon'
SOURCE = OUT / 'Shop_BuffIconBG.png'
PALETTES = {
    'Green': (0.274, 1.38, 0.99),
    'Blue': (0.558, 1.40, 0.99),
    'Purple': (0.757, 1.40, 0.97),
    'Orange': (0.075, 2.15, 1.16),
}

def build():
    source = Image.open(SOURCE).convert('RGBA')
    outline_alpha = source.getchannel('A').point(lambda a: 255 if a > 100 else 0).filter(ImageFilter.MaxFilter(13))
    for quality, (hue, saturation, value) in PALETTES.items():
        pixels = []
        for y in range(512):
            for x in range(512):
                r, g, b, a = source.getpixel((x, y))
                h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
                panel = 44 <= x <= 468 and 44 <= y <= 468 and a > 60 and 0.43 <= h <= 0.64 and s > 0.10
                corner = (45 <= x <= 103 or 409 <= x <= 467) and (45 <= y <= 103 or 409 <= y <= 467)
                inlay = corner and a > 60 and 0.045 <= h <= 0.15 and s > 0.42 and v > 0.5
                if panel:
                    rgb = colorsys.hsv_to_rgb((hue + (h - 0.54) * 0.10) % 1, min(0.68, max(0.18, s * saturation)), min(0.99, v * value))
                    r, g, b = [round(c * 255) for c in rgb]
                elif inlay:
                    r, g, b = [round(c * 255) for c in colorsys.hsv_to_rgb(hue, min(0.75, s * 0.95), v)]
                pixels.append((r, g, b, a))
        plate = Image.new('RGBA', source.size)
        plate.putdata(pixels)
        result = Image.new('RGBA', source.size)
        shadow = Image.new('RGBA', source.size, (39, 29, 22, 0))
        shadow.putalpha(outline_alpha.filter(ImageFilter.GaussianBlur(4)).point(lambda a: round(a * 0.23)))
        result.alpha_composite(shadow, (1, 5))
        outline = Image.new('RGBA', source.size, (57, 43, 33, 255))
        outline.putalpha(outline_alpha)
        result.alpha_composite(outline)
        result.alpha_composite(plate)
        result.save(OUT / f'Shop_BuffFrame{quality}.png')
    badge = Image.new('RGBA', (128, 64))
    draw = ImageDraw.Draw(badge)
    draw.rounded_rectangle((2, 2, 125, 61), radius=19, fill=(61, 39, 28, 242), outline=(244, 219, 168, 255), width=4)
    badge.resize((96, 48), Image.Resampling.LANCZOS).save(OUT / 'Shop_BuffLevelBadge.png')
    print('Generated four shop quality frames and the held-buff level badge.')

if __name__ == '__main__':
    build()
