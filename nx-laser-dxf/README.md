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
| 2. Plain profile DXF + config | `dist/LaserDxf.cs` + `dist/LaserDxf.config.json` | **ready to test** |
| 3. Hole classification | | next |
| 4. Hole labels | | |
| 5. Part number etch, options dialog | | |

## Step 2: plain profile DXF

1. Copy **both** `dist/LaserDxf.cs` and `dist/LaserDxf.config.json` into the same folder,
   for example `C:\Laser DXF\journals\`. The journal looks for the config next to itself.
   You can also set the environment variable `LASER_DXF_CONFIG` to point at the config file.
2. Open a plate part and play `LaserDxf.cs` (Tools > Journal > Play).
3. Select the plate face (or the body).
4. The DXF is written to `C:\Laser DXF\{ID}-{REV}.dxf`, for example
   `S180-06068-47020-A.dxf`. If the file already exists, you're asked before it's overwritten.
   - All geometry is on the `CUT` layer (colour 7), in mm, with the origin at the bottom-left
     corner. R2000 format.
   - Countersunk and counterbored holes are cut at the bore. Holes that don't go through are
     left out and listed as warnings.
5. The listing window shows the part number, plate size, hole counts and diameters, any
   warnings, and the output path. A detailed log is appended to
   `C:\Laser DXF\_logs\LaserDxf_<date>.log`, and a copy of the body model goes to
   `C:\Laser DXF\_debug\`.

### What to check

- The **part number** and **revision letter** are right (this checks the attribute names
  against Teamcenter).
- The DXF opens in your laser CAM software at the right size, the right way round (as seen
  from the face you picked), with closed contours.
- If your CAM has trouble with it, set `"version": "R12"` in the config and try again.
- The ready-made samples in `samples/` let you try the format in your CAM software before
  running NX.

### What to send back

The listing window text, and the DXF if anything looks wrong. A screenshot from the CAM
software is helpful too.

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
