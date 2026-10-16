#!/usr/bin/env python3
"""Validate the PDF instruction sheets written by the C# tests: they parse cleanly
(strict mode), have one landscape page of the right size, and carry the etch
instruction, part number, DXF name and hole key. Also renders PNGs for eyeballing."""
import json
import pathlib
import subprocess
import sys

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
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
