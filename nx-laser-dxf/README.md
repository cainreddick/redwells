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
| 3. Hole classification | `dist/LaserDxf.cs` | **ready to test** |
| 4. Hole labels | `dist/LaserDxf.cs` | **ready to test** |
| 5. Part number etch (auto placement) | `dist/LaserDxf.cs` | **ready to test**; options dialog and pick-a-point come next |

## Running it

1. Copy **both** `dist/LaserDxf.cs` and `dist/LaserDxf.config.json` into the same folder,
   for example `C:\nx_settings\`. The journal looks for the config next to itself, or you can
   set the environment variable `LASER_DXF_CONFIG`.
2. Open a plate part and play `LaserDxf.cs` (Tools > Journal > Play).
3. Select the plate face (or the body).
4. The DXF is written to `<outputFolder>\{ID}-{REV}.dxf`. If the file already exists, you're
   asked before it's overwritten.
   - **CUT** layer (colour 7): the profile and the through holes, as true lines, arcs and
     circles. Countersunk and counterbored holes are cut at the bore.
   - **ETCH** layer (colour 5): the hole labels and the part number. By default they are
     single-stroke vectors; set `"textMode": "Text"` for DXF TEXT entities.
5. The listing window shows the part number and where it came from, the hole counts, what was
   labelled and why, where the part number went, any warnings, and the output path. A
   detailed log is appended to `<outputFolder>\_logs\`.

### How holes are labelled

For each round through hole, in this order:
1. **NX Hole feature**: a threaded hole gives its thread size (`M8 x 1.25` → `M8`, format
   `labels.threadLabelFormat`), and a countersunk hole gives `CS`.
2. **Countersink geometry**: a cone in the hole wall gives `CS`, even when the hole was
   extruded.
3. **Diameter table** (`labels.table`): the modelled bore diameter ± tolerance gives the
   label. A row's `cutDiameter`, if set, replaces the hole diameter in the DXF.

Holes matching none of these get no label. Each label is tried right of the hole, then above,
left, below, then on the diagonals. The first spot clear of other holes, other labels and the
plate edge is used. If there's no clear spot, the least bad one is used and a warning is
listed.

### Part number

The text comes from `etch.format` (default `{ID}-{REV}`, with the revision as a letter). It is
placed in the clearest spot on the plate: horizontal if it fits, otherwise turned 90°,
otherwise shrunk down to `etch.minTextHeight`.

If the ID or revision attribute can't be found, the listing prints every attribute exactly
as NX Open reports it (category, title, alias, value, flags), so the config can be corrected.

### Samples

`samples/` holds DXFs generated from synthetic plates. Use them to try the format in your laser
CAM software.

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
