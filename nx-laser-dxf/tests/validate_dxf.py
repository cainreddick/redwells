#!/usr/bin/env python3
"""Validate the sample DXFs written by the C# tests with ezdxf.

Checks: file loads, audit finds no errors, layers/colours, units (R2000),
entity counts, extents, and that every open CUT contour closes (each LINE/ARC/
open-polyline end point meets another one).
"""
import json
import math
import pathlib
import sys

import ezdxf

TOL = 1e-4


def endpoints(e):
    t = e.dxftype()
    if t == "LINE":
        return [tuple(e.dxf.start)[:2], tuple(e.dxf.end)[:2]]
    if t == "ARC":
        c, r = e.dxf.center, e.dxf.radius
        pts = []
        for a in (e.dxf.start_angle, e.dxf.end_angle):
            pts.append((c[0] + r * math.cos(math.radians(a)), c[1] + r * math.sin(math.radians(a))))
        return pts
    if t == "LWPOLYLINE" and not e.closed:
        p = list(e.get_points("xy"))
        return [p[0], p[-1]]
    if t == "POLYLINE" and not e.is_closed:
        p = [v.dxf.location for v in e.vertices]
        return [tuple(p[0])[:2], tuple(p[-1])[:2]]
    return []


def check(folder, s):
    problems = []
    path = folder / s["file"]
    doc = ezdxf.readfile(path)
    auditor = doc.audit()
    if auditor.has_errors:
        problems += [f"audit: {err.message}" for err in auditor.errors]
    expected_ver = {"R2000": "AC1015", "R12": "AC1009"}[s["version"]]
    if doc.dxfversion != expected_ver:
        problems.append(f"version {doc.dxfversion} != {expected_ver}")
    if s["version"] == "R2000" and doc.header.get("$INSUNITS") != 4:
        problems.append("$INSUNITS is not 4 (mm)")
    for name, colour in (("CUT", 7), ("ETCH", 5)):
        if name not in doc.layers:
            problems.append(f"layer {name} missing")
        elif doc.layers.get(name).color != colour:
            problems.append(f"layer {name} colour {doc.layers.get(name).color} != {colour}")

    msp = doc.modelspace()
    ents = list(msp)
    if len(ents) != s["entities"]:
        problems.append(f"{len(ents)} entities, expected {s['entities']}")
    n_circles = len(msp.query("CIRCLE"))
    n_arcs = len(msp.query("ARC"))
    n_text = len(msp.query("TEXT"))
    if n_text != s.get("texts", 0):
        problems.append(f"{n_text} TEXT entities, expected {s.get('texts', 0)}")
    if n_circles != s["circles"]:
        problems.append(f"{n_circles} circles, expected {s['circles']}")
    if n_arcs != s["arcs"]:
        problems.append(f"{n_arcs} arcs, expected {s['arcs']}")

    # extents: everything inside [0,W]x[0,H], touching both corners
    from ezdxf import bbox
    ext = bbox.extents(ents)
    if not (abs(ext.extmin.x) < TOL and abs(ext.extmin.y) < TOL):
        problems.append(f"extents min {ext.extmin} is not the origin")
    if not (abs(ext.extmax.x - s["width"]) < 1e-3 and abs(ext.extmax.y - s["height"]) < 1e-3):
        problems.append(f"extents max {ext.extmax} != ({s['width']}, {s['height']})")

    # layers: geometry on CUT, text on ETCH (as polylines or TEXT)
    pts = []
    etch = 0
    for e in ents:
        if e.dxf.layer == "ETCH":
            etch += 1
            if e.dxftype() not in ("LWPOLYLINE", "POLYLINE", "TEXT"):
                problems.append(f"{e.dxftype()} on ETCH")
            if e.dxf.color != 5:
                problems.append(f"ETCH entity colour {e.dxf.color}")
            continue
        if e.dxf.layer != "CUT":
            problems.append(f"{e.dxftype()} on layer {e.dxf.layer}")
        pts += endpoints(e)
    if etch < s.get("minEtch", 0):
        problems.append(f"only {etch} ETCH entities, expected at least {s['minEtch']}")
    for i, p in enumerate(pts):
        if sum(1 for j, q in enumerate(pts) if j != i and math.dist(p, q) < TOL) != 1:
            problems.append(f"open contour end at {p}")
            break
    return problems


def main():
    folder = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "build/test-output")
    samples = json.loads((folder / "samples.json").read_text())
    failed = 0
    for s in samples:
        problems = check(folder, s)
        print(("ok   " if not problems else "FAIL ") + s["file"])
        for p in problems:
            print("     " + p)
        failed += bool(problems)
    print(f"\n{len(samples) - failed}/{len(samples)} DXF samples valid")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
