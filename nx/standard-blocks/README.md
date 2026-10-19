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
| `deploy.bat` | Copies the journal into your NX folder (default `C:\nx_settings`), once as `StandardBlocks.cs` and once per command as `SB_<command>.cs`. |
| `tests/` | Unit tests for the pure logic, and a compile check for the whole journal. Not needed in NX. |

Run `deploy.bat` (or `deploy.bat D:\other\folder`) to install. It puts the
journal and config in one folder; the journal finds its config next to itself.
The config is only copied when the folder doesn't have one, so your edits
survive updates.

**One file, several commands.** NX can't run one source journal from another:
`Session.Execute` only takes compiled DLLs, which need an Author licence. So
the journal reads its command from its own file name. `SB_Inspect.cs` runs
"inspect", and later `SB_AddHoles.cs` and `SB_Sync.cs` will run theirs. These
are identical copies of `StandardBlocks.cs`; `deploy.bat` makes them so they
never drift apart. Point each ribbon button at its `SB_` copy. Playing
`StandardBlocks.cs` itself runs "inspect". To keep the config somewhere else, set the
environment variable `SB_CONFIG` to its full path. Log files go to `logs\`
next to the config unless `log.folder` says otherwise.

No NX Open Author licence is needed. Everything is a plain, uncompiled journal.

## Standard block setup (per block part)

- Part attribute `SB_BLOCK` = `true` (String).
- One solid cylinder per hole, the "tool body":
  - modelled at the final **plate cut** diameter, with its axis normal to the plate face it serves;
  - running from 5 mm inside the block to 10 mm past the contact face (15 mm long);
  - a plain cylinder: one cylindrical face and two flat ends, no chamfers;
  - String attribute `SB_Hole_Type` = a type from the hole table, on the body.
    The value can also be the cut diameter as a number (e.g. Real 5.8), which is
    matched to the hole table by diameter, with a note. If the body has none,
    the journal also accepts one on the feature that made it. All bodies from that feature then share one type, and the diameter
    check catches any that don't match;
  - on layer 250, and in reference sets `SB_TOOLS` and `MODEL`.

Hole table (`holeTypes` in the config). The DXF export macro labels holes by
these diameters, so keep the two in step:

| SB_Hole_Type | Plate cut | Purpose |
|---|---|---|
| R6 | dia 5.8 | 6 mm reamed dowel hole, reamed after |
| M8 | dia 5.5 | M8 tapped hole: laser pilot, then drilled and tapped |
| CLR8.2 | dia 8.2 | 8.2 mm clearance hole |

## Agreed behaviour

- **Only the first plate is cut.** If a tool body passes through stacked plates, only the plate touching the block gets the hole.
- **Only the block and the plates it bolts to matter.** Any other solid a tool body touches is ignored without comment: bolts, dowels, clamps, other blocks, anything that isn't a flat plate. A flat plate over 6 mm thick is reported as skipped.
- **Existing hole on the same axis: cut anyway and warn.** This happens with reused plates.
- **Plate part used more than once** (twice in one jig, or reused in the mirrored jig): warn, because its holes apply to every copy.
- **Blocks are replaced by deleting the old one and adding a new one.** Sync removes the holes and links of deleted blocks.
- **Final sanity check:** Sync warns about any block that has no holes yet, or any of whose tool bodies has no hole. Run it once the jig is thought complete. The DXF macro should make the same check before exporting; how it does that is decided at step 6, because one source journal cannot call another.
- The top assembly is normally checked out while working, so block components can carry an instance-ID attribute that Sync uses to find them.
- Parts are in millimetres and loaded fully.
- Feature names: `SB_<blockID>_<holeType>_LINK` for each WAVE link and `SB_<blockID>_<holeType>` for each subtract, with `_2`, `_3`… when two copies of one block cut the same plate.

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
5. Then play `SB_Inspect.cs`, the copy made by `deploy.bat`. It should give the
   same result, which shows the file-name choice of command works.

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
