Attribute VB_Name = "modBuild"
'==============================================================================
' modBuild  (build-time only - removed again by Build-WorkInstruction.ps1)
' Converts the original two-sheet Work_Instruction_Template.xlsx into the
' macro-driven template. Every change is made through Excel so the header
' logo, PPE icons, fonts and borders of the original survive untouched.
'
' Original layout this expects (checked before anything is changed):
'   Sheet1 - front page; BOM in J1:R47; PPE icon group over F18:I21;
'            section headers on rows 18, 24, 30, 36, 42 with 3 data rows each
'   Sheet2 - op page; header row 1-3, Parts A6:D12, Details A15:D32,
'            image E4:N28, Notes G29:N32
'==============================================================================
Option Explicit

Private mStep As String

Public Function BuildTemplate() As String
    Dim wb As Workbook
    Dim wsF As Worksheet, wsO As Worksheet, wsB As Worksheet, wsL As Worksheet
    Dim headerFill As Long
    On Error GoTo Fail
    gSilent = True
    Application.ScreenUpdating = False
    Application.EnableEvents = False
    Application.DisplayAlerts = False
    Set wb = ThisWorkbook

    StepIs "checking the original layout"
    If wb.Worksheets.Count <> 2 Then Err.Raise vbObjectError + 600, , "expected 2 sheets, found " & wb.Worksheets.Count
    Set wsF = wb.Worksheets(1)
    Set wsO = wb.Worksheets(2)
    ExpectText wsF, "C1", "WORK INSTRUCTION"
    ExpectText wsF, "L1", "ASSEMBLY BOM"
    ExpectText wsO, "C1", "Op No."
    ExpectText wsO, "L1", "IF IN DOUBT - ASK"
    If wsF.Shapes.Count <> 1 Then Err.Raise vbObjectError + 601, , "expected 1 shape (PPE icons) on Sheet1, found " & wsF.Shapes.Count
    headerFill = wsF.Range("A18").Interior.Color

    ' The original opens in Page Layout view, where Range.Left/Top are measured
    ' differently - shapes placed in that view land in the wrong cells.
    StepIs "switching to Normal view"
    SetView wsF, "A1", True
    SetView wsO, "A1", True

    StepIs "renaming sheets"
    wsF.Name = SH_FRONT
    wsO.Name = SH_TEMPLATE

    StepIs "removing the BOM from the front page"
    wsF.Range("J1:R60").UnMerge
    wsF.Columns("J:R").Delete

    ' A copy of the front page keeps the header logo and QMF 12 / Issue 03 text
    StepIs "creating the BOM sheet"
    wsF.Copy After:=wsF
    Set wsB = wb.Worksheets(wsF.Index + 1)
    wsB.Name = SH_BOM
    StripSheet wsB

    StepIs "creating the Lists sheet"
    Set wsL = wb.Worksheets.Add(After:=wb.Worksheets(wb.Worksheets.Count))
    wsL.Name = SH_LISTS
    BuildListsSheet wsL, wsF, headerFill

    StepIs "defining names"
    DefineNames

    BuildFrontPage wsF, headerFill
    BuildOpTemplate wsO, headerFill
    BuildBomSheet wsB, headerFill

    StepIs "ordering sheets"
    wsB.Move After:=wsF
    wsO.Move After:=wsB
    wsL.Move After:=wsO

    StepIs "setting views"
    SetView wsL, "A2", True
    SetView wsO, "E2", False
    SetView wsB, "A1", False
    SetView wsF, "A3", False

    StepIs "hiding and protecting"
    UnlockStructure
    wsO.Visible = xlSheetHidden
    wsL.Visible = xlSheetHidden
    RefreshRefDocList
    ProtectAll
    LockStructure
    wsF.Activate
    wb.BuiltinDocumentProperties("Title").Value = "Work Instruction Template"

    Application.DisplayAlerts = True
    Application.EnableEvents = True
    Application.ScreenUpdating = True
    BuildTemplate = "OK"
    Exit Function
Fail:
    BuildTemplate = "ERROR while " & mStep & ": " & Err.Description & " (" & Err.Number & ")"
    Application.DisplayAlerts = True
    Application.EnableEvents = True
    Application.ScreenUpdating = True
End Function

'------------------------------------------------------------------------------
' Front Page
'------------------------------------------------------------------------------
Private Sub BuildFrontPage(ByVal ws As Worksheet, ByVal headerFill As Long)
    Dim shp As Shape
    Dim hse As Range
    Dim i As Long, topPt As Double, leftPt As Double
    Dim t As Variant
    Dim macros As Variant, captions As Variant, helpText As Variant

    StepIs "front page: checking section headers"
    ExpectText ws, "A18", "Activity"
    ExpectText ws, "A24", "Revision"
    ExpectText ws, "A30", "Document Number"
    ExpectText ws, "A36", "Part/Assembly Number"
    ExpectText ws, "A42", "Asset Number"

    ' Stop the icon group stretching while rows are inserted under it
    Set shp = ws.Shapes(1)
    shp.Name = "PPE_Icons"
    shp.LockAspectRatio = msoTrue
    shp.Placement = xlMove

    StepIs "front page: adding a Rev. column to section 3"
    ws.Range("C30:G30").UnMerge
    ws.Range("C30").Value = "Rev."
    ws.Range("D30:G30").Merge
    ws.Range("D30").Value = "Description"
    With ws.Range("C30:G30")
        .HorizontalAlignment = xlCenter
        .Font.Bold = True
        .Interior.Color = headerFill
    End With

    ' Bottom-up so the row numbers above stay valid
    StepIs "front page: sizing the tables"
    BuildTable ws, "Equip_Rows", 42, 3, 4
    BuildTable ws, "Tooling_Rows", 36, 3, 5
    BuildTable ws, "AssocDoc_Rows", 30, 3, 5
    BuildTable ws, "RevCtl_Rows", 24, 3, 5
    BuildTable ws, "HSE_Rows", 18, 3, 5

    StepIs "front page: key fields"
    ws.Range("A3").MergeArea.WrapText = True
    MarkAsInput ws.Range("A3").MergeArea          ' title
    MarkAsInput ws.Range("A8").MergeArea          ' author
    MarkAsInput ws.Range("D8").MergeArea          ' document number
    MarkAsInput ws.Range("F8").MergeArea          ' revision
    MarkAsInput ws.Range("H8").MergeArea          ' release date
    ws.Range("D8").MergeArea.NumberFormat = "@"
    ws.Range("F8").MergeArea.NumberFormat = "@"
    SetDateValidation ws.Range("H8").MergeArea
    ' Label row: was 15pt with wrapping, so "Document Number" lost its second line
    ' and the bold labels were clipped top and bottom
    ws.Range("A7:I7").WrapText = False
    ws.Range("A7:I7").ShrinkToFit = True
    ws.Rows(7).RowHeight = 18

    StepIs "front page: approvers"
    For i = 0 To 2
        ws.Cells(13 + i, 3).Formula = "=IF(" & SH_LISTS & "!$F$" & (2 + i) & "="""",""""," & SH_LISTS & "!$F$" & (2 + i) & ")"
        MarkAsInput ws.Range("E" & (13 + i)).MergeArea
        MarkAsInput ws.Range("H" & (13 + i)).MergeArea
        SetDateValidation ws.Range("H" & (13 + i)).MergeArea
    Next i

    StepIs "front page: moving the PPE icons beside section 1"
    Set hse = NameRange("HSE_Rows")
    FitShapeInBox shp, ws.Range("F" & (hse.Row - 1) & ":I" & (hse.Row + hse.Rows.Count - 1)), 4

    StepIs "front page: tightening spacer rows"
    For Each t In Array(6, 10, 16)
        ShrinkSpacer ws, CLng(t)
    Next t
    For Each t In Array("HSE_Rows", "RevCtl_Rows", "AssocDoc_Rows", "Tooling_Rows")
        ShrinkSpacer ws, NameRange(CStr(t)).Row + NameRange(CStr(t)).Rows.Count
    Next t

    StepIs "front page: buttons and help"
    ws.Columns("J").ColumnWidth = 3
    ws.Columns("K").ColumnWidth = 30
    macros = Array("AddOperation", "DeleteOperation", "RenumberOperations", "LinkDocument", _
                   "AddRow", "DeleteRow", "RefreshBOM", "ExportPDF", "EditLists")
    captions = Array("Add Operation", "Delete Operation...", "Renumber Operations", "Link Document (section 3)", _
                     "Add Row (in the selected table)", "Delete Row (selected)", "Refresh BOM", "Export PDF", _
                     "Edit Lists (departments etc.)")
    leftPt = ws.Range("K2").Left
    topPt = ws.Range("K2").Top
    For i = 0 To UBound(macros)
        AddButton ws, CStr(macros(i)), CStr(captions(i)), leftPt, topPt + i * 28, 190, 24
    Next i
    ' How-to notes in a text box (not cells) so row heights and Add Row cannot clip them
    helpText = Array( _
        "HOW TO USE (not printed)", _
        "1. Type in the shaded cells - Tab jumps to the next one.", _
        "2. Title, Doc No., Revision, Author and Release Date flow to every op page and footer automatically.", _
        "3. Section 3: click a row, press Link Document and pick the file. Doc No. is filled in and hyperlinked.", _
        "4. Need more rows? Click in the table, press Add Row.", _
        "5. Add Operation creates the next op sheet (10, 20, 30...).", _
        "6. On an op sheet: Insert Image (file) or Paste Image (take a screenshot with Win+Shift+S first). It fits itself.", _
        "7. Parts required on each op build the BOM sheet.", _
        "8. Export PDF checks everything, then saves <DocNo>_Rev<Rev>.pdf next to this file.", _
        "Sheets are protected without a password to prevent accidents (Review > Unprotect Sheet if really needed).")
    AddHelpBox ws, "HowToUse", Join(helpText, vbLf), leftPt, topPt + (UBound(macros) + 1) * 28 + 10, 260

    StepIs "front page: print setup"
    SetupPage ws, "$A$1:$I$" & (NameRange("Equip_Rows").Row + NameRange("Equip_Rows").Rows.Count - 1), xlPortrait
End Sub

' Resizes a table to wantRows data rows, formats it and names its data rows
Private Sub BuildTable(ByVal ws As Worksheet, ByVal tbl As String, ByVal hdrRow As Long, _
                       ByVal haveRows As Long, ByVal wantRows As Long)
    Dim spec As Variant
    Dim firstRow As Long, lastRow As Long, i As Long, r As Long
    Dim h As Double
    spec = TableSpec(tbl)
    firstRow = hdrRow + 1
    lastRow = hdrRow + haveRows
    h = ws.Rows(firstRow).RowHeight
    If h < 16.8 Then h = 16.8
    For i = haveRows + 1 To wantRows
        ws.Rows(lastRow).Insert Shift:=xlDown, CopyOrigin:=xlFormatFromLeftOrAbove
        lastRow = lastRow + 1
    Next i
    ThisWorkbook.Names.Add Name:=tbl, _
        RefersTo:="='" & ws.Name & "'!$A$" & firstRow & ":$" & SpecLastCol(spec) & "$" & lastRow
    For r = firstRow To lastRow
        FormatTableRow ws, r, spec, h
    Next r
    ApplyTableBorders tbl
    ApplyTableInputs tbl
End Sub

Private Sub ShrinkSpacer(ByVal ws As Worksheet, ByVal r As Long)
    If Application.WorksheetFunction.CountA(ws.Range("A" & r & ":I" & r)) = 0 Then ws.Rows(r).RowHeight = 8
End Sub

'------------------------------------------------------------------------------
' Op Template
'------------------------------------------------------------------------------
Private Sub BuildOpTemplate(ByVal ws As Worksheet, ByVal headerFill As Long)
    Dim i As Long
    Dim leftPt As Double, topPt As Double
    Dim macros As Variant, captions As Variant

    StepIs "op template: column widths"
    ' Left panel about 20% wider so the 4-column parts table is readable
    ws.Columns("A").ColumnWidth = 11
    ws.Columns("B").ColumnWidth = 14
    ws.Columns("C").ColumnWidth = 10
    ws.Columns("D").ColumnWidth = 6
    ws.Columns("K").ColumnWidth = 16              ' Reference Doc

    StepIs "op template: header row"
    ' WI title box in the blank A1:B3, styled like the Op No. box
    ws.Range("C1:D1").Copy ws.Range("A1")
    ws.Range("A1").Value = "Work Instruction"
    ws.Range("C2:D3").Copy ws.Range("A2")
    ws.Range("A2").MergeArea.NumberFormat = "General"   ' C2 is Text-formatted in the original
    ws.Range("A2").Formula = "=IF(WI_Title="""","""",WI_Title)"
    With ws.Range("A2").MergeArea
        .Font.Size = 9
        .Font.Bold = True
        .WrapText = True
        .HorizontalAlignment = xlCenter
        .VerticalAlignment = xlCenter
    End With
    ' Reference Doc replaces the unlabelled box, styled like the Rev. box
    ws.Range("K1:K3").UnMerge
    ws.Range("J1").Copy ws.Range("K1")
    ws.Range("K1").Value = "Ref. Doc"
    ws.Range("J2:J3").Copy ws.Range("K2")
    With ws.Range("K2").MergeArea
        .Font.Size = 9
        .Font.Color = vbBlack
        .WrapText = True
        .HorizontalAlignment = xlCenter
        .VerticalAlignment = xlCenter
    End With
    ws.Range("K2").Value = ""
    ' Linked from the Front Page - never retyped
    ws.Range("C2").MergeArea.NumberFormat = "General"
    ws.Range("C2").Value = ""
    ws.Range("H2").Formula = "=IF(WI_DocNo="""","""",WI_DocNo)"
    ws.Range("J2").Formula = "=IF(WI_Rev="""","""",WI_Rev)"
    ws.Range("H2").MergeArea.ShrinkToFit = True
    ws.Range("J2").MergeArea.ShrinkToFit = True

    StepIs "op template: names"
    AddLocalName ws, "OpNo", "$C$2"
    AddLocalName ws, "OpDept", "$E$2"
    AddLocalName ws, "OpRefDoc", "$K$2"
    AddLocalName ws, "OpParts", "$A$7:$D$12"
    AddLocalName ws, "OpDetails", "$A$15"
    AddLocalName ws, "OpNotes", "$G$29"
    AddLocalName ws, "OpImageBox", "$E$4:$N$28"
    AddLocalName ws, "OpWarnDetails", "$P$20"
    AddLocalName ws, "OpWarnNotes", "$P$21"

    StepIs "op template: header inputs"
    MarkAsInput ws.Range("E2").MergeArea
    SetListValidation ws.Range("E2").MergeArea, "DeptList", True
    MarkAsInput ws.Range("K2").MergeArea
    SetListValidation ws.Range("K2").MergeArea, "RefDocList", True, _
        "Pick the drawing/spec this operation uses (from Front Page section 3)."

    StepIs "op template: parts table"
    ws.Range("A6:D12").UnMerge
    ws.Range("A6:D12").Clear
    ws.Range("A6:D6").Value = Array("Part No.", "Description", "Material", "Qty.")
    With ws.Range("A6:D12")
        .Font.Name = BASE_FONT
        .Font.Size = 9
        .VerticalAlignment = xlCenter
        .ShrinkToFit = True
    End With
    With ws.Range("A6:D6")
        .Font.Bold = True
        .Interior.Color = headerFill
        .HorizontalAlignment = xlCenter
    End With
    ws.Range("A7:C12").HorizontalAlignment = xlLeft
    ws.Range("A7:C12").NumberFormat = "@"
    ws.Range("D7:D12").HorizontalAlignment = xlCenter
    ws.Rows("6:12").RowHeight = 15
    MarkAsInput ws.Range("A7:D12")
    SetPositiveNumberValidation ws.Range("D7:D12")
    SetEdge ws.Range("A6:D12"), xlInsideHorizontal, xlThin
    SetEdge ws.Range("A6:D12"), xlInsideVertical, xlThin
    SetOutline ws.Range("A6:D12")

    StepIs "op template: operation details"
    ws.Range("A15:D16").UnMerge
    ws.Range("A17:D32").UnMerge
    ws.Range("A15:D32").Clear
    ws.Range("A15:D32").Merge
    FormatTextBox ws.Range("A15:D32")
    SetOutline ws.Range("A15:D32")
    MarkAsInput ws.Range("A15:D32")
    AddOverflowFormat ws.Range("A15:D32"), "$P$20"

    StepIs "op template: notes"
    FormatTextBox ws.Range("G29").MergeArea
    MarkAsInput ws.Range("G29").MergeArea
    AddOverflowFormat ws.Range("G29").MergeArea, "$P$21"

    StepIs "op template: image box"
    ' On-screen hint only: blank while exporting (ShadeInputs = FALSE)
    ws.Range("E4").Formula = "=IF(" & NAME_SHADE & ",""Image area - use Insert Image or Paste Image (right)"","""")"
    With ws.Range("E4").MergeArea
        .Font.Name = BASE_FONT
        .Font.Size = 12
        .Font.Italic = True
        .Font.Color = RGB(150, 150, 150)
        .HorizontalAlignment = xlCenter
        .VerticalAlignment = xlCenter
    End With

    StepIs "op template: buttons"
    ws.Columns("O").ColumnWidth = 3
    ws.Columns("P").ColumnWidth = 40
    macros = Array("InsertImage", "PasteImage", "DeleteImage", "InsertOperationAfter", _
                   "DeleteOperation", "GoToFrontPage", "ExportPDF")
    captions = Array("Insert Image (from file)", "Paste Image (from clipboard)", "Remove Image", _
                     "Insert Operation After This One", "Delete This Operation", "Go to Front Page", "Export PDF")
    leftPt = ws.Range("P4").Left
    topPt = ws.Range("P4").Top
    For i = 0 To UBound(macros)
        AddButton ws, CStr(macros(i)), CStr(captions(i)), leftPt, topPt + i * 28, 200, 24
    Next i
    With ws.Range("P20:P21")
        .Font.Name = BASE_FONT
        .Font.Size = 9
        .Font.Bold = True
        .Font.Color = vbRed
    End With
    ws.Range("P23").Value = "Type in the shaded cells - Tab jumps to the next one."
    ws.Range("P24").Value = "Arrows/callouts can be drawn over the image (Insert > Shapes)."
    With ws.Range("P23:P24")
        .Font.Name = BASE_FONT
        .Font.Size = 9
        .Font.Color = RGB(64, 64, 64)
    End With

    StepIs "op template: print setup"
    SetupPage ws, "$A$1:$N$32", xlLandscape
End Sub

' Non-printing note beside the print area; first line bold
Private Sub AddHelpBox(ByVal ws As Worksheet, ByVal boxName As String, ByVal txt As String, _
                       ByVal leftPt As Double, ByVal topPt As Double, ByVal w As Double)
    Dim tb As Shape
    Set tb = ws.Shapes.AddTextbox(msoTextOrientationHorizontal, leftPt, topPt, w, 100)
    tb.Name = boxName
    With tb.TextFrame2
        .WordWrap = msoTrue
        .MarginLeft = 6
        .MarginRight = 6
        .MarginTop = 4
        .MarginBottom = 4
        .TextRange.Text = txt
        .TextRange.Font.Name = BASE_FONT
        .TextRange.Font.Size = 9
        .TextRange.Font.Fill.ForeColor.RGB = RGB(64, 64, 64)
        .TextRange.ParagraphFormat.SpaceAfter = 3
        .TextRange.Paragraphs(1).Font.Bold = msoTrue
        .AutoSize = msoAutoSizeShapeToFitText
    End With
    tb.Fill.ForeColor.RGB = RGB(248, 248, 248)
    tb.Line.ForeColor.RGB = RGB(200, 200, 200)
    tb.Placement = xlFreeFloating
    tb.PrintObject = False
    tb.Locked = True
End Sub

Private Sub FormatTextBox(ByVal rng As Range)
    With rng
        .Font.Name = BASE_FONT
        .Font.Size = 10
        .Font.Bold = False
        .WrapText = True
        .ShrinkToFit = False
        .HorizontalAlignment = xlLeft
        .VerticalAlignment = xlTop
    End With
End Sub

Private Sub SetOutline(ByVal rng As Range)
    SetEdge rng, xlEdgeLeft, xlMedium
    SetEdge rng, xlEdgeRight, xlMedium
    SetEdge rng, xlEdgeTop, xlMedium
    SetEdge rng, xlEdgeBottom, xlMedium
End Sub

' Light red box (screen only) when the warning cell beside the sheet is filled in
Private Sub AddOverflowFormat(ByVal rng As Range, ByVal warnAddr As String)
    With rng.FormatConditions.Add(Type:=xlExpression, Formula1:="=" & NAME_SHADE & "*(" & warnAddr & "<>"""")")
        .Interior.Color = RGB(255, 199, 206)
        .SetFirstPriority
    End With
End Sub

Private Sub AddLocalName(ByVal ws As Worksheet, ByVal nm As String, ByVal addr As String)
    ws.Names.Add Name:=nm, RefersTo:="='" & ws.Name & "'!" & addr
End Sub

'------------------------------------------------------------------------------
' BOM sheet
'------------------------------------------------------------------------------
Private Sub BuildBomSheet(ByVal ws As Worksheet, ByVal headerFill As Long)
    StepIs "BOM sheet: layout"
    ws.Cells.Font.Name = BASE_FONT
    ws.Columns("A").ColumnWidth = 18
    ws.Columns("B").ColumnWidth = 36
    ws.Columns("C").ColumnWidth = 18
    ws.Columns("D").ColumnWidth = 7
    ws.Columns("E").ColumnWidth = 14
    ws.Columns("F").ColumnWidth = 3
    ws.Columns("G").ColumnWidth = 70
    ws.Range("A1:E1").Merge
    ws.Range("A1").Value = "ASSEMBLY BOM"
    With ws.Range("A1")
        .Font.Size = 16
        .Font.Bold = True
        .HorizontalAlignment = xlCenter
    End With
    ws.Rows(1).RowHeight = 24
    ws.Range("A2:E2").Merge
    ws.Range("A2").Formula = "=IF(WI_DocNo="""","""",WI_DocNo&""  Rev ""&WI_Rev&""   -   ""&WI_Title)"
    ws.Range("A2").Font.Size = 10
    ws.Range("A2").HorizontalAlignment = xlCenter
    ws.Range("A4:E4").Value = Array("Part No.", "Description", "Material", "Qty.", "Used in Op(s)")
    With ws.Range("A4:E4")
        .Font.Size = 11
        .Font.Bold = True
        .Interior.Color = headerFill
        .HorizontalAlignment = xlCenter
        .VerticalAlignment = xlCenter
    End With
    ws.Rows(4).RowHeight = 18
    ws.Range("G4").Value = "Checks (not printed)"
    ws.Range("G4").Font.Bold = True
    ws.Range("G4").Font.Size = 9
    AddButton ws, "RefreshBOM", "Refresh BOM", ws.Range("G1").Left, ws.Range("G1").Top + 2, 120, 22
    AddButton ws, "GoToFrontPage", "Go to Front Page", ws.Range("G1").Left + 130, ws.Range("G1").Top + 2, 120, 22
    SetupPage ws, "$A$1:$E$5", xlPortrait

    StepIs "BOM sheet: first refresh"
    BuildBOM
End Sub

' Removes everything from a copied sheet but keeps its header/footer and page setup
Private Sub StripSheet(ByVal ws As Worksheet)
    Dim i As Long
    For i = ws.Shapes.Count To 1 Step -1
        ws.Shapes(i).Delete
    Next i
    ws.Cells.UnMerge
    ws.Cells.Clear
    On Error Resume Next
    ws.Cells.Validation.Delete
    ws.Cells.FormatConditions.Delete
    On Error GoTo 0
    ws.Columns.ColumnWidth = ws.StandardWidth
    ws.Rows.RowHeight = ws.StandardHeight
End Sub

'------------------------------------------------------------------------------
' Lists sheet
'------------------------------------------------------------------------------
Private Sub BuildListsSheet(ByVal ws As Worksheet, ByVal wsF As Worksheet, ByVal headerFill As Long)
    Dim i As Long
    ws.Cells.Font.Name = BASE_FONT
    ws.Cells.Font.Size = 10
    ws.Range("A1").Value = "Departments"
    WriteColumn ws, 1, 2, Array("Assembly", "Machining", "Fabrication", "Welding", "Inspection", "Stores")
    ws.Range("C1").Value = "Document Types"
    WriteColumn ws, 3, 2, Array("Drawing", "Specification", "Procedure", "Standard", "Work Instruction", "Other")
    ws.Range("E1").Value = "Approver role"
    ws.Range("F1").Value = "Approver name"
    For i = 0 To 2
        ws.Cells(2 + i, 5).Value = Replace(SafeText(wsF.Cells(13 + i, 1).Value), ":", "")
        ws.Cells(2 + i, 6).Value = SafeText(wsF.Cells(13 + i, 3).Value)
    Next i
    ws.Range(REFDOC_COL & "1").Value = "Reference Docs (automatic)"
    ws.Columns(REFDOC_COL).NumberFormat = "@"
    With ws.Range("A1:" & REFDOC_COL & "1")
        .Font.Bold = True
    End With
    ws.Range("A1,C1,E1:F1," & REFDOC_COL & "1").Interior.Color = headerFill
    ws.Columns("A").ColumnWidth = 22
    ws.Columns("B").ColumnWidth = 3
    ws.Columns("C").ColumnWidth = 22
    ws.Columns("D").ColumnWidth = 3
    ws.Columns("E").ColumnWidth = 16
    ws.Columns("F").ColumnWidth = 22
    ws.Columns("G").ColumnWidth = 3
    ws.Columns(REFDOC_COL).ColumnWidth = 30
    ws.Columns("I").ColumnWidth = 3
    ws.Columns("J").ColumnWidth = 70
    ws.Range("J1").Value = "Edit the Departments, Document Types and Approver names here."
    ws.Range("J2").Value = "Keep each list in one block (no blank rows). Up to 199 entries per list."
    ws.Range("J3").Value = "Reference Docs is rebuilt automatically from Front Page section 3 - do not edit it."
    ws.Range("J1").Font.Bold = True
    AddButton ws, "HideLists", "Done - hide this sheet", ws.Range("J5").Left, ws.Range("J5").Top, 170, 26
End Sub

Private Sub WriteColumn(ByVal ws As Worksheet, ByVal col As Long, ByVal firstRow As Long, ByVal values As Variant)
    Dim i As Long
    For i = 0 To UBound(values)
        ws.Cells(firstRow + i, col).Value = values(i)
    Next i
End Sub

'------------------------------------------------------------------------------
' Workbook names
'------------------------------------------------------------------------------
Private Sub DefineNames()
    Dim fp As String
    fp = "='" & SH_FRONT & "'!"
    ThisWorkbook.Names.Add Name:=NAME_SHADE, RefersTo:="=TRUE"
    ThisWorkbook.Names.Add Name:="WI_Title", RefersTo:=fp & "$A$3"
    ThisWorkbook.Names.Add Name:="WI_Author", RefersTo:=fp & "$A$8"
    ThisWorkbook.Names.Add Name:="WI_DocNo", RefersTo:=fp & "$D$8"
    ThisWorkbook.Names.Add Name:="WI_Rev", RefersTo:=fp & "$F$8"
    ThisWorkbook.Names.Add Name:="WI_ReleaseDate", RefersTo:=fp & "$H$8"
    ThisWorkbook.Names.Add Name:="DeptList", _
        RefersTo:="=OFFSET(" & SH_LISTS & "!$A$2,0,0,MAX(1,COUNTA(" & SH_LISTS & "!$A$2:$A$200)),1)"
    ThisWorkbook.Names.Add Name:="DocTypeList", _
        RefersTo:="=OFFSET(" & SH_LISTS & "!$C$2,0,0,MAX(1,COUNTA(" & SH_LISTS & "!$C$2:$C$200)),1)"
    ThisWorkbook.Names.Add Name:="RefDocList", _
        RefersTo:="=OFFSET(" & SH_LISTS & "!$" & REFDOC_COL & "$2,0,0,MAX(1,COUNTA(" & SH_LISTS & "!$" & _
                  REFDOC_COL & "$2:$" & REFDOC_COL & "$500)),1)"
End Sub

'------------------------------------------------------------------------------
' Helpers
'------------------------------------------------------------------------------
Private Sub StepIs(ByVal s As String)
    mStep = s
End Sub

Private Sub ExpectText(ByVal ws As Worksheet, ByVal addr As String, ByVal expected As String)
    If StrComp(SafeText(ws.Range(addr).Value), expected, vbTextCompare) <> 0 Then
        Err.Raise vbObjectError + 602, , "the original layout has changed: " & ws.Name & "!" & addr & _
                  " should say '" & expected & "' but says '" & SafeText(ws.Range(addr).Value) & "'"
    End If
End Sub

Private Sub SetView(ByVal ws As Worksheet, ByVal firstCell As String, ByVal showGrid As Boolean)
    ws.Activate
    ActiveWindow.View = xlNormalView
    ActiveWindow.DisplayGridlines = showGrid
    ActiveWindow.Zoom = 100
    ActiveWindow.ScrollRow = 1
    ActiveWindow.ScrollColumn = 1
    ws.Range(firstCell).Select
End Sub
