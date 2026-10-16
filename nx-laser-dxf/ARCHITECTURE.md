# Laser DXF journal: architecture

NX Open journal (NX 2412, Teamcenter / NX Manager) that turns a 6 mm laser-cut plate into
an annotated DXF for the laser cutter.

## Decisions

### Language: C#

- NX compiles a .NET journal completely before running it, so a mistake fails straight away
  with a line number. A Python journal only fails when it reaches the bad line, which may be
  after you have picked a face. You are the only one who can run NX, so failing early saves
  round trips.
- Most NX Open reference docs and community examples are .NET. Signatures with `out`
  parameters match the docs exactly. Python returns those values as tuples, which is a common
  source of bugs that I couldn't catch without NX.
- The code sticks to **C# 5 and mscorlib/System only** (no LINQ, `HashSet`, string
  interpolation or `?.`). It should then compile with any NX journal compiler, old or new.
  The test script enforces this by compiling with `mcs -langversion:5 -noconfig`.

### Single-file journal, multi-file source

NX plays one `.cs` file. The sources are split for testing and merged by
`tools/build_journal.py` into `dist/*.cs`:

```
src/core/   pure logic: geometry, loops, hole analysis, labels, DXF, stroke font, config
src/nx/     everything that calls NX Open: selection, B-rep extraction, attributes, UI
src/entry/  the journal's Main() (global namespace, fully-qualified names)
dist/       generated journals: these are what you play in NX
stubs/      compile-only NXOpen stand-ins, so the merged journal can be type-checked here
tests/      unit tests (C#, Mono) + DXF validation (Python, ezdxf)
```

Each source file puts its `using` directives inside its own namespace block. As a result,
`src/core` never sees the `NXOpen` namespace, even in the merged file. This keeps the boundary
honest, and NX type names such as `Line`, `Arc` or `Point` can't clash with anything in core.

No Author licence and no signed DLL are needed: the journal is plain source that NX compiles
at play time.

### Write the DXF ourselves (not via a drawing and DxfdwgCreator)

The translator route is worse for this job:

- It needs a drawing sheet and view in the part. In Teamcenter that means modifying, and
  probably checking out, the item revision, or leaving a temporary drawing to clean up. The
  journal as designed is **read-only on the part**: it never modifies or saves it, so no
  check-out is needed.
- The translator's settings file controls layers and colours by object type, not
  entity by entity. Getting holes and the profile on CUT and every note on ETCH, with exact
  colours, is fragile.
- NX notes come out as TEXT/MTEXT in NX fonts. Most laser CAM can't etch those; it needs
  single-stroke vectors, so we would have to post-process the DXF anyway.
- Projected views can turn arcs into splines or polylines, depending on the settings.
- None of it can be tested outside NX.

Writing the file ourselves lets us control every entity, layer and colour, makes it
deterministic, and lets us test it with ezdxf.

### DXF version: R2000 by default, R12 available

R2000 (AC1015) is read by every current laser/nesting package. It carries explicit units
(`$INSUNITS = 4`, mm), which avoids the classic 25.4× import scaling error. It also has
LWPOLYLINE. `"dxfVersion": "R12"` writes the oldest, most widely accepted flavour instead
(POLYLINE in place of LWPOLYLINE, no units header) for older CAM software.

## Data flow

```
NX selection ──► NxBodyExtractor ──► BodyModel ──► PlateAnalyzer ──► FaceAnalysis
 (face/body)      (NX layer)        (neutral,      (frame, loops,     │
                                     JSON-able)     hole walls)       ▼
part attributes ─► AttributeEntry list ─────────► Etch text     HoleClassifier (feature data → table)
                                                       │              │
                                                       ▼              ▼
                                                  EtchPlacer ◄── LabelPlacer
                                                       │              │
                                                       └────► DxfWriter (+ StrokeFont) ──► .dxf
                                                                      └────► report + log
```

`BodyModel` describes the body's faces, edges, sampled edge geometry, features and attributes.
It can be saved to JSON. The probe journal writes one for every part you run it on, so I can
replay your real parts through the whole pipeline in tests without NX.

## NX Open APIs used

| Purpose | API | Notes |
|---|---|---|
| Select face or body | `UI.SelectionManager.SelectTaggedObject` with `Selection.MaskTriple(UF_solid_type, 0, UF_UI_SEL_FEATURE_ANY_FACE / _BODY)` | Works in an assembly; occurrences are mapped to the component part with `.Prototype` |
| Face type | `Face.SolidFaceType` (`Planar`, `Cylindrical`, `Conical`) | |
| Topology | `Body.GetFaces/GetEdges`, `Face.GetEdges/GetBody`, `Edge.GetFaces` | Loops are built by chaining edge end points (pure code), not from UF loop lists |
| Edge geometry | `UFSession.Modl.AskCurveProps(tag, t, …)` (point at a normalised parameter) | Lines: 2 points. Arcs: 5 points, then a 3-point circle fit, cross-checked against NX's curvature radius. Other edges: adaptive sampling. End points are checked against `Edge.GetVertices` |
| Edge type / length | `Edge.SolidEdgeType`, `Edge.GetLength` | |
| Features | `Body.GetFeatures`, `BodyFeature.GetFaces`, `Feature.FeatureType/GetFeatureName` | Maps each hole's wall faces to the feature that made them |
| Hole feature data | `FeatureCollection.CreateHolePackageBuilder(hole)`, read the properties, `Destroy()` (never committed) | Called by reflection until step 1 confirms the property names (hole type, form, thread size) |
| Attributes | `Part.GetUserAttributes()` → `NXObject.AttributeInformation` (`Title`, `Category`, `Type`, `StringValue`, …) | Alias/unset members read by reflection; step 1 logs which members exist |
| Config location | `Session.ExecutingJournal` (via reflection), fallback env var `LASER_DXF_CONFIG` | Finds the JSON next to the journal |
| WCS option | `Part.WCS.CoordinateSystem.Orientation` | X axis hint |
| Pick-a-point option | `UI.SelectionManager.SelectScreenPosition` / point dialog | Etch position |
| Output | `Session.ListingWindow`, `NXMessageBox`, `System.IO` | Overwrite prompt through `NXMessageBox` (Question) |
| Options dialog | `System.Windows.Forms` (pre-filled from the config) | Can be switched off in the config |

All of these work from an unsigned journal. Two things need checking on your seat, and step 1
does so: the UF calls (`AskCurveProps`), and reading the Hole builder.

## Core algorithms

**Frame.** Z is the selected face's *outward* normal. The normal from the face's loop is
flipped if any body material lies in front of the face. X follows the longest straight edge
of the outer loop (or the WCS X, projected onto the face), and Y = Z × X. If the plate lies
below that edge, X and Y are both negated. That is a 180° rotation, never a mirror. The origin
is the bottom-left corner of the outer profile's exact bounding box, including arc extremes.
The frame is always right-handed with Z pointing at the viewer, so the DXF shows the plate as
seen from the selected face.

**Loops.** The face's edges are chained by matching end points. The loop with the largest area
is the outer profile, and all others are holes or cut-outs.

**Hole analysis.** For each inner loop, the hole's wall faces are flood-filled from the loop.
The fill stops at the far face of the plate (a planar face parallel to the selected one, at
depth = thickness).
- *Through*: the fill reached the far face. Blind holes and pockets don't, so they are left
  out of the DXF and logged.
- *Cut diameter*: the narrowest circle coaxial with the hole anywhere in its walls. For a
  countersink or counterbore this is the bore, not the opening.
- *Countersink*: a conical wall face. *Counterbore*: a parallel step face part-way through.
- *Feature*: the NX feature(s) that created the wall faces.

**Classification (step 3).** First the Hole feature data: threaded → thread size such as
`M8`, countersunk → `CS`, mapped through the config. Then the diameter table:
`{label, diameter, tolerance, cutDiameter?}`. Countersinks found from geometry also get `CS`
when no feature says otherwise. Anything else gets no label.

**Label placement (step 4).** Each label's box size comes from the stroke-font metrics (also
used for TEXT mode). It is tried in this order: right, above, left, below, then the four
diagonals, each at radius + gap. A position is rejected if it overlaps a hole (plus
clearance), another label, or the outer profile (plus edge clearance). If none fit, the
position with the least overlap is used and a warning is logged.

**Part number (step 5).**
- *Text*: built from attributes using the config format (`{ID}-{REV}`, revision optionally as a
  letter: 1→A … 26→Z, 27→AA). If `ID` is missing, `Part_Number` is used unless it equals
  `model-plain-1`, then the part name. A missing revision is dropped with a warning.
- *Position*: the largest clear spot inside the profile, away from holes, labels and edges,
  found by searching for the point furthest from every obstacle. The text box must fit at
  0°, otherwise at 90°.
- An option lets you pick the point yourself.

**DXF writer.** Entities: LINE, ARC, CIRCLE, and LWPOLYLINE (R12: POLYLINE) for sampled edges,
plus TEXT or stroke-font polylines. Arcs and circles stay true arcs. Layers and colours come
from the config. Numbers are written in invariant culture (UK/EU locale settings can't turn
`.` into `,`). The R2000 output includes a full table/handle structure, so strict readers
accept it.

**Stroke font.** A built-in single-line font covering A–Z, 0–9 and `- _ . / ( ) # + ,`. Glyphs
are polylines on a 6-unit grid scaled to the text height, with fixed advance widths, so label
boxes are exact.

## Config (`LaserDxf.config.json`, next to the journal)

```jsonc
{
  "outputFolder": "C:/Laser DXF",          // forward slashes or doubled backslashes
  "fileNameFormat": "{ID}-{REV}.dxf",
  "askBeforeOverwrite": true,
  "showOptionsDialog": true,
  "geometry": { "tolerance": 0.01, "chordTolerance": 0.02, "xAlign": "LongestEdge" },  // or "WCS"
  "dxf": {
    "version": "R2000",                     // or "R12"
    "textMode": "Stroke",                   // or "Text"
    "cutLayer":  { "name": "CUT",  "color": 7 },
    "etchLayer": { "name": "ETCH", "color": 5 }
  },
  "labels": {
    "textHeight": 3, "gap": 1.5, "holeClearance": 1.0, "edgeClearance": 2.0,
    "useHoleFeatures": true,
    "threadLabelFormat": "{THREAD}",        // "M8 x 1.25" -> "M8"
    "countersinkLabel": "CS",
    "table": [
      { "label": "R6", "diameter": 5.8, "tolerance": 0.05, "cutDiameter": null },
      { "label": "M8", "diameter": 5.5, "tolerance": 0.05, "cutDiameter": null }
    ]
  },
  "etch": {
    "format": "{ID}-{REV}", "revisionAsLetter": true,
    "textHeight": 5, "minTextHeight": 3, "clearance": 3,
    "placement": "Auto", "allowRotate90": true      // or "Pick"
  },
  "attributes": {
    "id":         { "title": "ID",          "category": "MCAD" },
    "revision":   { "title": "Revision",    "category": "MCAD Revision" },
    "fallbackId": { "title": "Part_Number", "category": "Lentus Attributes", "ignoreValues": ["model-plain-1"] },
    "name":       { "title": "DB_PART_NAME",  "category": "MCAD" },
    "material":   { "title": "Polar_Material", "category": "MCAD Revision" },
    "thickness":  { "title": "Matl_Thk",    "category": "Materials" }
  },
  "batch": { "requiredThickness": 6, "thicknessTolerance": 0.1, "writeCsv": true }
}
```

Attribute titles in NX's dialog appear as `Alias (Title)`, for example `Name (DB_PART_NAME)`.
The lookup matches either the title or the alias, within the given category. Step 1 dumps every
attribute with its real title and category, so the defaults above get checked against your
parts.

## Phase 2 (designed for, not built)

- **Batch.** Walk the displayed assembly (`ComponentAssembly.RootComponent`, `GetChildren()`
  recursively), de-duplicate by prototype part, and load each part fully. For every solid
  body, run the same `BodyModel → FaceAnalysis` pipeline with automatic face picking (already
  implemented: largest planar face, countersink side preferred). Everything after extraction
  takes a `BodyModel` plus attributes, so batch mode is a loop around the existing code.
- **Thickness check.** Read `Matl_Thk` (Materials) and compare it with
  `requiredThickness`, skipping the plate if it differs. The measured thickness
  (`FaceAnalysis.Thickness`) is compared with the attribute, with a warning if the attribute is
  missing or doesn't match.
- **CSV.** One row per exported plate: `ID, Revision, Name, Material, Thickness, DXF path`,
  written next to the DXFs with the same invariant formatting and proper quoting.

## Build steps

1. **Probe**: select a face, dump edges, loops, holes, features and attributes. *(this delivery)*
2. Plain profile DXF (CUT layer) + config file.
3. Hole classification (feature data → table, cut diameter override).
4. Hole labels with collision-avoiding placement.
5. Part-number etch (attributes, auto placement / pick point), options dialog, report + log.

Each step ships as `dist/*.cs` plus a note on what to check and what to send back.
