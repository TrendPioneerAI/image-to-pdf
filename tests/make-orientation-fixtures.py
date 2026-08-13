from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent / "fixtures" / "orientation"
ROOT.mkdir(parents=True, exist_ok=True)

image = Image.new("RGB", (800, 520), "white")
draw = ImageDraw.Draw(image)
draw.rectangle((0, 0, 400, 260), fill=(236, 80, 80))
draw.rectangle((400, 0, 800, 260), fill=(85, 190, 110))
draw.rectangle((0, 260, 400, 520), fill=(75, 135, 230))
draw.rectangle((400, 260, 800, 520), fill=(242, 196, 62))
draw.rectangle((18, 18, 782, 502), outline=(20, 24, 32), width=9)
draw.polygon(((400, 42), (340, 145), (375, 145), (375, 225), (425, 225), (425, 145), (460, 145)), fill=(20, 24, 32))
font = ImageFont.truetype("arial.ttf", 42)
draw.text((34, 34), "TL", fill="white", font=font)
draw.text((690, 34), "TR", fill="white", font=font)
draw.text((34, 430), "BL", fill="white", font=font)
draw.text((690, 430), "BR", fill=(20, 24, 32), font=font)

for orientation in range(1, 9):
    exif = Image.Exif()
    exif[274] = orientation
    image.save(ROOT / f"orientation-{orientation}.jpg", "JPEG", quality=96, exif=exif)

print(ROOT)
