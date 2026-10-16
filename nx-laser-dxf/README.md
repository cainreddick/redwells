# NX Laser DXF

NX Open journal for NX 2412 (Teamcenter / NX Manager) that exports a laser-cut plate as an
annotated DXF:
- cut profile and holes on the `CUT` layer
- hole labels (`R6`, `M8`, `CS`, …) and the part number on the `ETCH` layer

The design is described in [ARCHITECTURE.md](ARCHITECTURE.md).

## Status

| Step | Journal | State |
|---|---|---|
| 1. Select a face, dump edge data | `dist/LaserDxfProbe.cs` | **ready to test** |
| 2. Plain profile DXF | | next |
| 3. Hole classification | | |
| 4. Hole labels | | |
| 5. Part number etch | | |

## Step 1: running the probe

The probe is read-only: it doesn't modify or save the part, so no Teamcenter check-out is
needed.

1. Copy `dist/LaserDxfProbe.cs` to your PC, for example `C:\Laser DXF\journals\`.
2. Open a plate part in NX (on its own, or an assembly containing it).
3. **Tools > Journal > Play** (Alt+F8), browse to `LaserDxfProbe.cs`, and click **Run**.
4. Select the plate face to view from (the countersink side if there are countersinks). You can
   also select the body, and the probe will pick the face itself.
5. The listing window shows a summary. Two files are written to `C:\Laser DXF\_debug\`:
   - `<part>_<time>_probe.txt`: the full dump (every edge, hole feature properties, all
     attributes with categories)
   - `<part>_<time>_body.json`: the body model, which I replay in tests

### What to check

- **Thickness** is 6.
- **Plate size** looks right. X runs along the longest straight edge, with the plate above it.
- Each hole's **through** column is `yes`, except blind holes and pockets.
- Countersinks show `Conical+Cylindrical CS-this-side`, with the bore as the narrowest
  diameter.
- **Bore diameters** list the diameters you expect (5.5, 5.8, …).

### What to send back

- The listing window text (or the `_probe.txt` file).
- The `_body.json` file, if you can share it.
- If it fails to compile, the error lines NX shows (they include line numbers in
  `LaserDxfProbe.cs`).

Try it on two parts if you can: one plate with Hole-command holes (ideally threaded or
countersunk ones), and one with sketch/extrude holes. Also try selecting the body instead of
the face once.

## Development

```sh
sudo apt install mono-mcs mono-runtime     # C# compiler (used here with -langversion:5)
pip install ezdxf                          # DXF validation (from step 2)
nx-laser-dxf/tests/run_tests.sh            # builds dist/, compile-checks journals, runs tests
```

Edit the files under `src/`, never `dist/`: `tools/build_journal.py` regenerates `dist/`.
