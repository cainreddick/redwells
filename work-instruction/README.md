# Work Instruction template (QMF 12, Issue 03)

The work instruction template as an Excel macro workbook. Engineers fill it in and press
**Export PDF** to get one A4 PDF for the operator: Front Page and BOM in portrait, then one
landscape page per operation.

Everything is built **by Excel itself** from the original
`source/Work_Instruction_Template.xlsx`. No other library touches the file, so the SST
logo in the header, the QMF 12 / Issue 03 text, the PPE icons, fonts and borders all
come through unchanged.

```
work-instruction/
  Build-WorkInstruction.ps1   builds the template and runs the self-test (Excel via COM)
  source/                     the original template, unchanged
  vba/                        the VBA, one file per module (this is what goes in version control)
    modCommon.bas             constants, protection, messages, validation, page setup
    modFrontPage.bas          section tables, Add/Delete Row, Link Document, Lists sheet
    modOperations.bas         Add / Insert / Delete / Renumber operations
    modImages.bas             Insert Image, Paste Image, fit-to-box, downscaling
    modBOM.bas                Refresh BOM
    modExport.bas             Export PDF, pre-export checks, text-overflow check
    modDev.bas                ExportAllModules (re-export the VBA after editing in Excel)
    ThisWorkbook.cls          workbook events
    build/modBuild.bas        one-off conversion of the original layout (not shipped)
    test/modSelfTest.bas      sample WI + automated checks (not shipped)
  dist/                       build output (git-ignored)
```

## Building it (Windows, Excel 2016 / Microsoft 365)

1. Close Excel.
2. Double-click **`Build.cmd`** in this folder. A console window opens and stays open
   until you press a key, so you can read the result.
   (Double-clicking `Build-WorkInstruction.ps1` itself only opens it in Notepad - that is a
   Windows default. `Build.cmd` runs it for you.) To run it from a PowerShell prompt
   instead: `powershell -ExecutionPolicy Bypass -File .\Build-WorkInstruction.ps1`
3. An Excel window opens and runs the build, then a self-test. This takes about a minute. Leave it alone until the script finishes.

Output:

| File | What it is |
|---|---|
| `dist/Work_Instruction_Template.xltm` | **The master.** Every new WI starts from this (see below). |
| `dist/Work_Instruction_Template.xlsm` | The same, as a normal workbook. Use it to edit or inspect the master. |
| `dist/sample/WI-0421_RevB.xlsm` | Sample WI: front page, 3 operations, images, BOM |
| `dist/sample/WI-0421_RevB.pdf` | The sample's exported PDF |
| `dist/sample/SelfTest_Report.txt` | PASS/FAIL list of everything the self-test checked |

While it runs, the script switches on Excel's *Trust access to the VBA project object model*
so it can import the modules. It switches the setting back afterwards. If a group policy
blocks that setting, the script says so.

**If the build or self-test fails**, the console and `SelfTest_Report.txt` name the step
that failed. Send that text back, together with the PDF if there is one.

## Making new work instructions from the master

1. Copy `Work_Instruction_Template.xltm` to the folder Excel uses for personal templates
   (File > Options > Save > *Default personal templates location*, e.g.
   `Documents\Custom Office Templates`) or to a shared template folder.
2. Start each new WI with **File > New > Personal > Work_Instruction_Template**, or
   double-click the `.xltm` in Explorer. You get an unsaved copy, so the master can't be
   overwritten by accident. If someone opens the master itself, they get a warning, and
   saving it asks for confirmation.
3. The first save always offers **.xlsm** and suggests `<DocNo>_Rev<Rev>.xlsm`. Saving
   as .xlsx would remove the macros, so the template doesn't offer it.

### Trusted Location (so macros run without the yellow security bar)

Do this once on each engineer's PC:

1. Excel > **File > Options > Trust Center > Trust Center Settings... > Trusted Locations**.
2. **Add new location...** and pick the folder that holds the master `.xltm` and the
   folder(s) where WIs are saved. Tick *Subfolders of this location are also trusted*.
3. If the folder is on a network share, first tick **Allow Trusted Locations on my
   network** on the same page. Use the `\\server\share\...` path, not a mapped drive letter.
4. A file that came by email or download is marked "from the internet", and Excel blocks
   its macros even in a trusted location. Right-click the file > **Properties** > tick
   **Unblock** > OK.

IT can push the same setting to everyone by Group Policy:
*Microsoft Excel > Excel Options > Security > Trust Center > Trusted Locations*.

## How engineers use it

The same notes are on the Front Page, to the right of the print area.

- **Shaded cells are where you type.** Tab jumps to the next one. Everything else is
  locked. The shading is switched off in the PDF.
- **Front Page** holds the Title, Document Number, Revision, Author and Release Date. Every
  op page, the BOM and the PDF footers read them from here. Nothing is retyped.
- **Section 3 - Associated Documents:** click a row, press **Link Document**, and pick the
  file. The Document Number is filled in from the file name (you can still edit it) and
  hyperlinked to the file. Mapped drive letters are converted to `\\server\share` paths
  so the link works on other PCs. The links still work in the PDF.
- **Add Row / Delete Row:** click in a section's table first.
- **Add Operation** (Front Page) creates the next op sheet: Op 10, Op 20, Op 30...
  On an op sheet, **Insert Operation After This One** makes e.g. Op 15. **Renumber
  Operations** re-sequences everything to 10, 20, 30.... **Delete Operation** deletes the
  current op, or asks which one when run from the Front Page.
- **Op sheet:**
  - Pick the Department and the Ref. Doc from the dropdowns. The Ref. Doc list is section 3 of the Front Page, shown as "DocNo Rev X".
  - Fill in Parts required (Part No. / Description / Material / Qty.), Operation Details and Notes.
  - **Insert Image** picks a file. **Paste Image** pastes a screenshot: take it with Win+Shift+S, then press the button.
  - The image is scaled to fit the box, centred, and replaces any earlier image.
  - Large photos are reduced to 1600 px on the long side before inserting.
  - You can draw arrows and callouts over the image (Insert > Shapes). They are kept when the image is replaced.
  - If Operation Details or Notes get too long for their box, the box turns red and a warning appears to the right of the sheet.
- **Refresh BOM** collects every op's parts into the BOM sheet. The BOM also refreshes automatically on export.
  - Lines with the same part number are merged and their quantities added up.
  - The "Used in Op(s)" column lists which ops use each part.
  - A different description or material for the same part number is flagged beside the BOM (not printed) and in the export checks.
- **Export PDF** refreshes the BOM, then checks for:
  - a missing title, doc no., revision, author or release date
  - Revision Control not matching the Revision
  - ops with no department, image or details
  - parts with no qty or no part no.
  - text that doesn't fit its box
  - a Ref. Doc that isn't in section 3
  - BOM conflicts

  It lists every problem in one message, so you can cancel or continue. It then saves
  `<DocNo>_Rev<Rev>.pdf` next to the workbook, asking first if that file already exists.
  Footers show `Op X of Y`, `Page N of M` and the doc no./rev.
- **Edit Lists** (Front Page) opens the hidden Lists sheet. It holds the Departments,
  Document Types and approver names. The Departments are placeholders, so replace them
  with your own. Press **Done** to hide it again.

## Decisions taken (change them in the VBA if needed)

| Item | Choice |
|---|---|
| Front Page rows | Section 1 HSE **5**, 2 Revision Control **5**, 3 Associated Documents **5**, 4 Tooling **5**, 5 Equipment **4**. Add Row adds more. The page prints at about 87% and warns below about 75%. |
| Parts per op | **6** rows (fixed: the left panel has no room for more) |
| Op page | Left panel about 20% wider for the 4-column parts table. The WI title is in the blank box left of Op No. Ref. Doc replaces the unlabelled box. |
| Section 3 | New **Rev.** column, so the Ref. Doc dropdown can show "DocNo Rev X" |
| PPE icons | Moved beside section 1 (HSE), scaled to fit, no overlaps |
| BOM order | Order of first use (Op 10's parts first), i.e. build order |
| "Op X of Y" | X = position of the op (Op 20 of 3 ops is "Op 2 of 3") |
| Protection | Sheets and workbook structure, **no password** (Review > Unprotect Sheet if ever needed) |
| Margins | 0.5" left/right, 0.95" top (clears the logo), 0.6" bottom. Each sheet fits to exactly 1 A4 page. The logo keeps its size on every page. |
| Footer | File name removed. Now `Op X of Y` (op pages), `Page N of M`, `<DocNo> Rev <Rev>` |
| Dates | dd/mm/yyyy, validated (the original release date was US mm-dd-yy) |

## Editing the macros

Edit in Excel's VBA editor (Alt+F11), then run **modDev.ExportAllModules**. It writes
every module to a `vba` folder next to the workbook, and you copy those files over the
ones here and commit.

To rebuild from scratch after changing `vba/*.bas`, run the build script again.

The constants at the top of `modCommon.bas` control the sheet names, op step (10) and
protection password (blank).
