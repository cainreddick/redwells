#!/usr/bin/env python3
"""Builds the image fixtures for the PDF logo tests: a placeholder logo (PNG with
transparency, and JPEG) and one small PNG per colour type / bit depth, each with the
pixels PIL decodes as raw RGBA next to it (<name>.rgba) for the C# decoder to match."""
import pathlib

from PIL import Image, ImageDraw

here = pathlib.Path(__file__).parent / "images"
here.mkdir(exist_ok=True)

# Placeholder logo, 600 x 140: a ring and a word, on a transparent background.
logo = Image.new("RGBA", (600, 140), (0, 0, 0, 0))
d = ImageDraw.Draw(logo)
d.ellipse((10, 10, 130, 130), outline=(20, 40, 90, 255), width=14)
d.rectangle((50, 60, 90, 80), fill=(20, 40, 90, 255))
for i, x in enumerate(range(170, 590, 70)):
    d.rectangle((x, 35, x + 50, 105), fill=(20, 40, 90, 255) if i % 2 == 0 else (60, 120, 200, 255))
logo.save(here / "test_logo.png")
logo.convert("RGB").save(here / "test_logo.jpg", quality=90)

# Decoder variants: a 13 x 7 gradient (odd width exercises the sub-byte packing).
base = Image.new("RGBA", (13, 7))
for y in range(7):
    for x in range(13):
        base.putpixel((x, y), (x * 19, y * 36, (x * y * 7) % 256, 255 if (x + y) % 3 else (x * 20) % 256))
variants = {
    "rgba8": base,
    "rgb8": base.convert("RGB"),
    "grey8": base.convert("L"),
    "greyalpha8": base.convert("LA"),
    "palette8": base.convert("RGB").quantize(colors=200),
    "palette4": base.convert("RGB").quantize(colors=16),
    "mono1": base.convert("1"),
    "palettealpha": base.quantize(colors=64, method=Image.Quantize.FASTOCTREE),
    "grey16": base.convert("L").point(lambda v: v * 257, "I"),
}
for name, im in variants.items():
    path = here / f"{name}.png"
    if name == "palette4":
        im.save(path, bits=4)
    elif name == "grey16":
        im.convert("I;16").save(path)
    else:
        im.save(path)
    back = Image.open(path)
    if name == "grey16":  # PIL clips 16-bit grey when converting to RGBA; take the high byte instead
        grey = bytes(v >> 8 for v in back.get_flattened_data())
        (here / f"{name}.rgba").write_bytes(b"".join(bytes((g, g, g, 255)) for g in grey))
    else:
        (here / f"{name}.rgba").write_bytes(back.convert("RGBA").tobytes())
print("image fixtures written to", here)
