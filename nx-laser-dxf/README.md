# NX Laser DXF

NX Open journal for NX 2412 (Teamcenter / NX Manager) that exports a laser-cut plate as an
annotated DXF:
- cut profile and holes on the `CUT` layer
- hole labels (`R6`, `M8`, `CS`, …) and the part number on the `ETCH` layer

The design is described in [ARCHITECTURE.md](ARCHITECTURE.md).

## Status

| Step | Journal | State |
|---|---|---|
| 1. Select a face, dump edge data | `dist/LaserDxfProbe.cs` | done: tested in NX 2412 |
| 2. Plain profile DXF + config | `dist/LaserDxf.cs` | done: tested in NX 2412 |
| 3. Hole classification | `dist/LaserDxf.cs` | tested in NX 2412 (table); Hole-feature path being fixed |
| 4. Hole labels | `dist/LaserDxf.cs` | tested in NX 2412 |
| 5. Part number etch, options dialog, pick-a-point | `dist/LaserDxf.cs` | etch tested; dialog and pick **ready to test** |
| 6. Preview & adjust window | `dist/LaserDxf.cs` | **ready to test** |
| 7. Batch export from an assembly (one DXF per plate, CSV) | `dist/LaserDxfBatch.cs` | **ready to test** |
| 8. NX drawing-sheet layout + export from the sheet | | planned |

## Running it

1. Copy **both** `dist/LaserDxf.cs` and `dist/LaserDxf.config.json` into the same folder,
   for example `C:\nx_settings\`. The journal looks for the config next to itself, or you can
   set the environment variable `LASER_DXF_CONFIG`.
2. Open a plate part and play `LaserDxf.cs` (Tools > Journal > Play).
3. Select the plate face (or the body).
4. The options dialog opens, pre-filled from the config: orientation (longest edge or WCS),
   part-number position (automatic or pick a point), text output (vectors or DXF TEXT),
   DXF version and output folder. Your choices apply to this run only. Set
   `"showOptionsDialog": false` to skip the dialog.
5. The **preview window** shows the finished plate (unless switched off). Click a label or
   the part number to select it. To select several, **Ctrl+click** them or **drag a box**
   around them on empty plate (`Ctrl+A` selects every hole label, `Esc` deselects). Then:
   - drag any selected item, or use the arrow keys (0.5 mm, or 5 mm with Shift); all
     selected items move together
   - `R` turns each 90°, `Del` deletes them
   - **Edit text / size...** (`F2` or double-click) changes the text and its height in mm.
     With several items selected it sets the same height on all of them, e.g. smaller text
     for a small part
   - **R6** / **M8** buttons (one per row of your label table) drop a label by hand: press
     one and click on the plate. The label is centred where you click; Shift-click drops
     several, and Esc stops. A label dropped next to a hole becomes that hole's label: it is
     counted in the report, and the table's `cutDiameter` for it applies
   - *Reset all* undoes every edit
   - the mouse wheel zooms, right-drag pans, and *Fit view* (Home) shows the whole plate

   Text that crosses the cut line, a hole or other text is boxed in **red**; text that is too
   close is boxed in **orange**. *Export DXF* warns if anything is still flagged. Nothing is
   written until you click it, and *Cancel* writes nothing (it asks first if you've made
   changes).
6. The DXF is written to `<outputFolder>\{ID}-{REV}.dxf`. If the file already exists, you're
   asked before it's overwritten.
   - **CUT** layer (colour 7): the profile and the through holes, as true lines, arcs and
     circles. Countersunk and counterbored holes are cut at the bore.
   - **ETCH** layer (colour 5): the hole labels and the part number. By default they are
     single-stroke vectors; set `"textMode": "Text"` for DXF TEXT entities.
7. A **PDF instruction sheet** for the laser shop is written next to the DXF
   (`{ID}-{REV}.pdf`, switch off in the options dialog or with `pdf.enabled`). It shows:
   - the plate as cut, viewed from the etch face, at a standard scale (A4, or A3 for large
     plates), with all etching in **blue** exactly as in the DXF, including preview edits.
     Etch text too small to read at the sheet's scale is printed larger, so it stays legible
   - numbered laser-cutting notes, including a bold red "ETCH ALL TEXT ON LAYER ETCH ... do
     not omit any etching". The notes are editable in `pdf.notes`
   - a hole key built from the labels actually on the plate, e.g. `R6 x12 - 6 mm reamed dowel
     hole - ream after cutting (laser cut Ø5.8)`, using each label table row's `description`
   - a title block: company, description, part number, revision, material, thickness, a
     blank QTY box, scale, sheet size, drawn by, date and the DXF file name

   If the DXF or PDF already exists you're asked once, for both files. If the PDF is open in a
   viewer it can't be overwritten; that's reported as a warning.
8. The listing window shows the part number and where it came from, the hole counts, what was
   labelled and why, where the part number went, any warnings, and the output path. A
   detailed log is appended to `<outputFolder>\_logs\`.

## Batch export from an assembly

`dist/LaserDxfBatch.cs` (keep it in the same folder as `LaserDxf.cs` and the config) exports
several plates in one go:

1. Open the jig assembly and play `LaserDxfBatch.cs`.
2. Select **one face on each plate**: the face to view it from, i.e. the etch/countersink side.
   You can also select plate bodies, in which case the face is picked automatically. Plates
   can be anywhere in the assembly. Picking a part twice, or a part that's used several
   times, still gives one DXF.
3. The options dialog appears once and applies to every plate.
4. Every plate is read first. Then:
   - **Thickness rule:** only plates whose `Matl_Thk` equals `batch.requiredThickness`
     (default 6) are exported; others are skipped with the reason. A missing `Matl_Thk`, or
     one that disagrees with the measured thickness, only gives a warning. Set
     `requiredThickness` to 0 to export any thickness.
   - **Overwrite:** one question covers every file that already exists (No = skip those
     plates).
   - **Duplicates:** two plates that would get the same file name are reported; only the
     first is written.
5. The **preview** opens for each plate in turn ("plate 2 of 5"), with the same editing
   tools, plus **Export DXF**, **Skip plate** and **Cancel batch**. Each DXF is written as
   soon as you approve it. Cancel batch keeps the plates already written.
6. The listing shows one line per plate (exported / skipped / failed, with reasons and
   warnings). `LaserDxf_batch_<date-time>.csv` is written next to the DXFs, with columns
   ID, Revision, Name, Material, Thickness and DXF path. Per-plate details go to the daily
   log.

Batch mode writes DXFs only (no PDF sheets). Pick-a-point placement for the part number isn't
available in batch mode; move the part number in the preview instead.

### How holes are labelled

For each round through hole, in this order:
1. **NX Hole feature**: a threaded hole gives its thread size (`M8 x 1.25` → `M8`, format
   `labels.threadLabelFormat`), and a countersunk hole gives `CS`.
2. **Countersink geometry**: a cone in the hole wall gives `CS`, even when the hole was
   extruded.
3. **Diameter table** (`labels.table`): the modelled bore diameter ± tolerance gives the
   label. A row's `cutDiameter`, if set, replaces the hole diameter in the DXF.

Holes matching none of these get no label, and the listing says why for each one, e.g.
`made by Extrude(5) (no Hole feature); dia 9 is not in the label table`. Holes copied by a
Pattern or Mirror feature are traced back to the Hole feature they came from. Each label is tried right of the hole, then above,
left, below, then on the diagonals. The first spot clear of other holes, other labels and the
plate edge is used. If there's no clear spot, the least bad one is used and a warning is
listed.

### Part number

The text comes from `etch.format`, default `{ID}-{REV}`, e.g. `S180-06068-47005-1`. Set
`revisionAsLetter` for `-A` style revisions. If the revision attribute is missing, the
revision in the Teamcenter part name (`.../1`) is used. It is
placed in the clearest spot on the plate: horizontal if it fits, otherwise turned 90°,
otherwise shrunk down to `etch.minTextHeight`.

If the ID or revision attribute can't be found, the listing prints every attribute exactly
as NX Open reports it (category, title, alias, value, flags), so the config can be corrected.

### Samples

`samples/` holds DXFs generated from synthetic plates. Use them to try the format in your laser
CAM software. `sheet_irregular.pdf` (A3) and `sheet_small.pdf` (A4) are example instruction
sheets.

## Step 1: probe (diagnostic journal)

`dist/LaserDxfProbe.cs` is kept as a diagnostic tool. Run it the same way. It writes a full
dump of edges, loops, hole features and attributes to `<outputFolder>\_debug\`.

## Config

All settings live in `LaserDxf.config.json`, and every setting is explained inside the file.
Settings for later steps (labels, etch, batch) are already there. An unknown or misspelt
setting is reported in the listing window rather than silently ignored.

## Development

```sh
sudo apt install mono-mcs mono-runtime     # C# compiler (used here with -langversion:5)
pip install ezdxf                          # DXF validation (from step 2)
nx-laser-dxf/tests/run_tests.sh            # builds dist/, compile-checks journals, runs tests
```

Edit the files under `src/`, never `dist/`: `tools/build_journal.py` regenerates `dist/`.
