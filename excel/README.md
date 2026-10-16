# Excel macro: export selection to PDF

`PdfExport.bas` saves whatever cells you have selected as a PDF.

When you run it, it:

1. Checks you've selected cells (not a chart or shape).
2. Opens a Save As box named after the file you're in (e.g. `S180-06068-37002-1.csv`
   becomes `S180-06068-37002-1.pdf`), in the same folder as that file.
3. Asks before overwriting an existing file.
4. Scales the selection to fit one page across, exports it, then puts the sheet's page
   settings back the way they were.
5. Opens the finished PDF.

Two settings at the top of the module change this:

| Setting                | Default | Effect                                                    |
| ---------------------- | ------- | --------------------------------------------------------- |
| `FIT_TO_ONE_PAGE_WIDE` | `True`  | `False` keeps the sheet's own scaling                     |
| `OPEN_AFTER_EXPORT`    | `True`  | `False` shows a "Saved to…" message instead of opening it |

If you select several separate blocks with Ctrl+click, each block goes on its own page.

## Install so it works in every workbook (recommended)

The Personal Macro Workbook (`PERSONAL.XLSB`) opens hidden every time Excel starts, so the
macro is available everywhere.

1. If you've never recorded a macro, create the Personal Macro Workbook first:
   **View → Macros → Record Macro**, set *Store macro in* to **Personal Macro Workbook**,
   click **OK**, then straight away **View → Macros → Stop Recording**.
2. Press **Alt+F11** to open the VBA editor.
3. In the Project pane on the left, click **VBAProject (PERSONAL.XLSB)**.
4. **File → Import File…** and choose `PdfExport.bas`.
5. Press **Ctrl+S** to save, then close the VBA editor.
   When you close Excel, click **Save** if it asks about the Personal Macro Workbook.

## Add the button

**Quick Access Toolbar** (one click, in every workbook):

1. **File → Options → Quick Access Toolbar**.
2. Set *Choose commands from* to **Macros**.
3. Pick `PERSONAL.XLSB!ExportSelectionToPDF`, click **Add >>**.
4. Click **Modify…** to choose an icon and set the display name to e.g. *Export selection to PDF*.
5. Click **OK**.

**Button on a sheet** (only in that workbook):

1. **Insert → Shapes**, draw a shape and type a label on it.
2. Right-click the shape → **Assign Macro…** → choose `ExportSelectionToPDF` → **OK**.
3. Save the workbook as **Excel Macro-Enabled Workbook (*.xlsm)**.

**Keyboard shortcut** (optional): **View → Macros → View Macros**, select the macro,
click **Options…** and set a key, e.g. Ctrl+Shift+P.

## Notes

- Needs Excel 2010 or later on Windows. On a Mac the Save As box ignores the PDF filter,
  but the export itself works.
- If macros are blocked, go to **File → Options → Trust Center → Trust Center Settings →
  Macro Settings**, or right-click the downloaded `.bas` file → **Properties → Unblock**.

## Troubleshooting

**"Cannot run the macro 'something.csv'!…" or "'something.xlsx'!…"**
The macro was imported into (or the button was linked to) that file, not into
`PERSONAL.XLSB`. CSV and .xlsx files can't keep macros, so once that file is closed the
button points at nothing. To fix it:

1. Remove the old button: **File → Options → Quick Access Toolbar**, select it on the
   right, click **Remove**.
2. Import `PdfExport.bas` into **VBAProject (PERSONAL.XLSB)** as described above and save.
3. Add the button again, making sure you pick the entry that starts with
   `PERSONAL.XLSB!`.
