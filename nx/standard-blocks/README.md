# Standard Blocks for NX

An NX Open journal (C#, NX 2414, Teamcenter) that does what SolidWorks Smart
Components / Smart Features did for our jig standard blocks: drop a block into
an assembly, run the command, and the plates it bolts to get associative holes
that follow the block when it moves.

It works the same way as the manual method: make the plate the work part, WAVE
link the block's tool bodies (not fixed at timestamp), then subtract them.

## Files

| File | What it is |
|---|---|
| `StandardBlocks.cs` | The journal. All the code is in this one file. |
| `StandardBlocks.config.json` | Settings: hole table, tolerances, layer, reference sets, attribute names, max plate thickness, log folder. |
| `SB_Inspect.cs` | Launcher for the "inspect" command (later: `SB_AddHoles.cs`, `SB_Sync.cs`). Small journals like this are what ribbon buttons point at. |
| `tests/` | Unit tests for the pure logic, and a compile check for the whole journal. Not needed in NX. |

Put `StandardBlocks.cs`, `StandardBlocks.config.json` and the `SB_*.cs`
launchers **in the same folder** (your `nx_settings` folder). The journal finds
its config next to itself. To keep the config somewhere else, set the
environment variable `SB_CONFIG` to its full path. Log files go to `logs\`
next to the config unless `log.folder` says otherwise.

No NX Open Author licence is needed. Everything is a plain, uncompiled journal.

## Standard block setup (per block part)

- Part attribute `SB_BLOCK` = `true` (String).
- One solid cylinder per hole, the "tool body":
  - modelled at the final **plate cut** diameter, with its axis normal to the plate face it serves;
  - running from 5 mm inside the block to 10 mm past the contact face (15 mm long);
  - a plain cylinder: one cylindrical face and two flat ends, no chamfers;
  - String body attribute `SB_HOLE_TYPE` = a type from the hole table;
  - on layer 250, and in reference sets `SB_TOOLS` and `MODEL`.

Hole table (`holeTypes` in the config). The DXF export macro labels holes by
these diameters, so keep the two in step:

| SB_HOLE_TYPE | Plate cut | Purpose |
|---|---|---|
| R6 | dia 5.8 | 6 mm reamed dowel hole, reamed after |
| M8 | dia 5.5 | M8 tapped hole: laser pilot, then drilled and tapped |
| CLR8.2 | dia 8.2 | 8.2 mm clearance hole |

## Build steps

| Step | Command | Changes the model? | Status |
|---|---|---|---|
| 1 | Select blocks and list their tool bodies, hole types and axes | No | **Ready to test** |
| 2 | Auto-detect target plates and print the per-plate summary | No | Pure logic written and tested; NX side next |
| 3 | Teamcenter writable / checked-out pre-check | No | |
| 4 | WAVE link and subtract on one plate; moving the block updates the holes | Yes | |
| 5 | Multiple plates and blocks, undo as one step | Yes | |
| 6 | Sync command (with dry run, callable from the DXF macro) | Yes | |

### Testing step 1 in NX

1. Open a jig assembly that contains 001-4032-CAD2072. For a useful position
   check, move **and rotate** at least one block away from the assembly origin.
2. **Tools > Journal > Play**, choose `StandardBlocks.cs`, **Run**.
3. Select one or more block components and press OK. Include one component
   that isn't a block, such as a plate, to see it rejected.
4. Copy the whole Listing window and paste it back, along with any compile error.
5. Then try **Tools > Journal > Play** on `SB_Inspect.cs`. It should give the
   same result. This tests whether one journal can call another through
   `Session.Execute`; the ribbon buttons and the DXF macro will rely on that.

For each tool body, step 1 prints the diameter check, length, layer and
reference set membership, plus the axis in block and in assembly coordinates.
The "assembly position check" line tells us whether NX reports the assembly
data the way the later steps assume.

## Running the tests (optional, needs the .NET 8 SDK)

```sh
cd nx/standard-blocks
dotnet run --project tests/StandardBlocks.Tests      # unit tests for the pure logic
dotnet build tests/NxCompileCheck                    # compiles the whole journal against NX stubs
```

The tests compile `StandardBlocks.cs` itself with `SB_TEST` defined, which
leaves out the NX half. Both projects enforce **C# 5 syntax**, because NX may
compile journals with an older compiler: no `$"..."`, `?.`, `=>` members,
`nameof` or `out var` in the journal. `tests/NxCompileCheck/NxOpenStubs.cs`
holds empty stand-ins for the NX Open calls the journal uses. When NX disagrees
with a stub, fix the journal and the stub together.
