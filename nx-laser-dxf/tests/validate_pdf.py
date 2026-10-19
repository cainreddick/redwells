#!/usr/bin/env python3
"""Validate the PDF instruction sheets written by the C# tests: they parse cleanly
(strict mode), have one landscape page of the right size, and carry the etch
instruction, part number, DXF name and hole key. Also renders PNGs for eyeballing.

Then the 1:1 PDFs of the DXF: page = plate + margin, CUT/ETCH layers, and - rendered
at 254 dpi (10 px per mm) - the drawing lands exactly where 1:1 puts it."""
import json
import pathlib
import subprocess
import sys

from PIL import Image
from pypdf import PdfReader

SIZES = {"A4": (842, 595), "A3": (1191, 842)}


def main():
    folder = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "build/test-output")
    samples = json.loads((folder / "pdf_samples.json").read_text())
    failed = 0
    for s in samples:
        problems = []
        path = folder / s["file"]
        reader = PdfReader(path, strict=True)
        if len(reader.pages) != 1:
            problems.append(f"{len(reader.pages)} pages")
        page = reader.pages[0]
        w, h = float(page.mediabox.width), float(page.mediabox.height)
        ew, eh = SIZES[s["sheet"]]
        if abs(w - ew) > 1 or abs(h - eh) > 1:
            problems.append(f"page {w:.0f}x{h:.0f}, expected {s['sheet']} landscape {ew}x{eh}")
        text = " ".join(page.extract_text().split())
        for must in ["ETCH ALL TEXT ON LAYER ETCH", s["etch"], s["dxf"], "LASER CUTTING NOTES", "VIEW FROM THE ETCH FACE"]:
            if must not in text:
                problems.append(f"missing text: {must!r}")
        for k in s["key"]:
            if " ".join(k.split()) not in text:
                problems.append(f"missing hole key line: {k!r}")
        subprocess.run(["pdftoppm", "-png", "-r", "110", "-singlefile", str(path), str(path.with_suffix(""))], check=True)
        print(("ok   " if not problems else "FAIL ") + s["file"])
        for p in problems:
            print("     " + p)
        failed += bool(problems)
    print(f"\n{len(samples) - failed}/{len(samples)} PDF sheets valid")
    return (1 if failed else 0) | check_cut(folder)


def check_cut(folder):
    samples = json.loads((folder / "cut_pdf_samples.json").read_text())
    failed = 0
    mm = 72 / 25.4
    for s in samples:
        problems = []
        path = folder / s["file"]
        reader = PdfReader(path, strict=True)
        page = reader.pages[0]
        w, h = float(page.mediabox.width) / mm, float(page.mediabox.height) / mm
        ew, eh = s["width"] + 2 * s["margin"], s["height"] + 2 * s["margin"]
        if len(reader.pages) != 1 or abs(w - ew) > 0.05 or abs(h - eh) > 0.05:
            problems.append(f"page {w:.2f} x {h:.2f} mm, expected {ew:.2f} x {eh:.2f}")
        oc = reader.trailer["/Root"].get("/OCProperties")
        names = sorted(str(g.get_object()["/Name"]) for g in oc["/OCGs"]) if oc else []
        if s["layers"] and names != ["CUT", "ETCH"]:
            problems.append(f"layers {names}, expected CUT and ETCH")
        if not s["layers"] and names:
            problems.append(f"unexpected layers {names}")
        if page.extract_text().strip():
            problems.append("has PDF text - should be vectors only")
        png = path.with_suffix("")
        subprocess.run(["pdftoppm", "-png", "-r", "254", "-gray", "-singlefile", str(path), str(png)], check=True)
        img = Image.open(str(png) + ".png").point(lambda v: 255 if v < 200 else 0)
        box = img.getbbox()  # left, top, right, bottom of everything drawn, in px (10 px = 1 mm)
        want = (s["margin"] * 10, s["margin"] * 10, (s["margin"] + s["width"]) * 10, (s["margin"] + s["height"]) * 10)
        if box is None or any(abs(a - b) > 2.5 for a, b in zip(box, want)):
            problems.append(f"drawing at {box} px, 1:1 puts it at {tuple(round(v, 1) for v in want)}")
        print(("ok   " if not problems else "FAIL ") + f"{s['file']}  ({ew:.1f} x {eh:.1f} mm, layers {names or 'none'})")
        for p in problems:
            print("     " + p)
        failed += bool(problems)
    print(f"\n{len(samples) - failed}/{len(samples)} 1:1 PDFs valid")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
