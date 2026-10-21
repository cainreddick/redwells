Attribute VB_Name = "modSelfTest"
'==============================================================================
' modSelfTest  (test-time only - imported into the SAMPLE workbook by
' Build-WorkInstruction.ps1 and removed again afterwards)
' Fills in a realistic sample work instruction, drives every macro path without
' dialogs, checks the results, exports the sample PDF and writes a report.
'==============================================================================
Option Explicit

Private mReport As String
Private mFails As Long
Private mStep As String

Public Function RunSelfTest(ByVal assetDir As String, ByVal outDir As String) As String
    Dim ws As Worksheet
    Dim problems As Collection
    Dim pdfPath As String
    Dim i As Long
    Dim v As Variant
    Dim saved As String
    Dim src As Shape
    On Error GoTo Fail

    gSilent = True
    gLog = ""
    mFails = 0
    mReport = "Work Instruction self-test   " & Format$(Now, "yyyy-mm-dd hh:nn:ss") & vbCrLf & _
              "Excel " & Application.Version & " (build " & Application.Build & ")" & vbCrLf & _
              String(78, "=") & vbCrLf
    Application.EnableEvents = False
    Application.ScreenUpdating = False

    '--------------------------------------------------------------------------
    StepIs "structure of the clean template"
    Check SheetExists(SH_FRONT) And SheetExists(SH_BOM) And SheetExists(SH_TEMPLATE) And SheetExists(SH_LISTS), _
          "sheets Front Page, BOM, Op Template, Lists exist"
    Check TemplateSheet.Visible = xlSheetHidden, "Op Template is hidden"
    Check ListsSheet.Visible = xlSheetHidden, "Lists is hidden"
    Check ThisWorkbook.ProtectStructure, "workbook structure is protected"
    Check FrontSheet.ProtectContents, "Front Page is protected"
    Check GetOpSheets().Count = 0, "template starts with no op sheets"
    Check FrontSheet.Shapes("PPE_Icons").TopLeftCell.Column >= 6, _
          "PPE icons moved beside section 1 (top-left " & FrontSheet.Shapes("PPE_Icons").TopLeftCell.Address(False, False) & ")"
    Check PpeClearOfTables(), "PPE icons do not overlap any table"

    '--------------------------------------------------------------------------
    StepIs "front page data"
    UnprotectAll
    SetName "WI_Title", "Gearbox Housing Sub-Assembly - Bearing and Seal Fit"
    SetName "WI_DocNo", "WI-0421"
    SetName "WI_Rev", "B"
    SetName "WI_Author", "J. Smith"
    NameRange("WI_ReleaseDate").Value = DateSerial(2026, 10, 21)
    FillTable "HSE_Rows", Array( _
        Array("Lifting housing (18 kg)", "Use the jib hoist - no manual lifting"), _
        Array("Pressing bearings", "Press guard closed, two-hand control only"), _
        Array("Applying sealant", "Nitrile gloves and safety glasses"))
    FillTable "RevCtl_Rows", Array( _
        Array("A", "J. Smith", "Initial release", DateSerial(2026, 3, 2)), _
        Array("B", "J. Smith", "Seal fitting torque updated (Op 20)", DateSerial(2026, 10, 21)))
    LinkDocumentFile NameRange("AssocDoc_Rows").Row, assetDir & "\DRG-1001.pdf"
    LinkDocumentFile NameRange("AssocDoc_Rows").Row + 1, assetDir & "\SPEC-220.pdf"
    UnprotectSheet FrontSheet
    FillTable "AssocDoc_Rows", Array( _
        Array(Empty, "C", "Gearbox housing assembly drawing", "Drawing"), _
        Array(Empty, "A", "Bearing fitting specification", "Specification"), _
        Array("QP-014", "2", "Torque control procedure", "Procedure"))
    FillTable "Tooling_Rows", Array( _
        Array("T-055", "A", "Bearing press fixture"), _
        Array("T-102", "B", "Seal installation drift"))
    FillTable "Equip_Rows", Array( _
        Array("PR-03", "5 tonne hydraulic press"), _
        Array("TW-11", "Torque wrench 10-60 Nm (calibrated)"))
    RefreshRefDocList
    Check FrontSheet.Hyperlinks.Count = 2, "section 3 has 2 hyperlinks"
    For i = 1 To FrontSheet.Hyperlinks.Count
        Info "hyperlink " & i & ": " & FrontSheet.Hyperlinks(i).TextToDisplay & " -> " & FrontSheet.Hyperlinks(i).Address
    Next i
    Check SafeText(FrontSheet.Cells(NameRange("AssocDoc_Rows").Row, 1).Value) = "DRG-1001", _
          "Link Document fills Document Number from the file name"
    Check RefDocListText() = "|DRG-1001 Rev C|SPEC-220 Rev A|QP-014 Rev 2|", _
          "Reference Doc list = " & RefDocListText()

    '--------------------------------------------------------------------------
    StepIs "creating operations"
    Set ws = CreateOpSheet(NextOpNumber())
    FillOp ws, "Assembly", "DRG-1001 Rev C", Array( _
            Array("HSG-100", "Gearbox housing", "Aluminium LM25", 1), _
            Array("BRG-6204", "Bearing 6204-2RS", "Steel", 2), _
            Array("SCR-M6-20", "Cap screw M6x20", "A2 stainless", 4)), _
        "1. Check housing HSG-100 for burrs and damage. Clean both bores with a lint-free cloth." & vbLf & _
        "2. Place the housing on press fixture T-055, front bore facing up." & vbLf & _
        "3. Press bearing BRG-6204 into the front bore until fully seated (press PR-03)." & vbLf & _
        "4. Turn the housing over and repeat for the rear bore." & vbLf & _
        "5. Fit the cover with 4 off cap screws SCR-M6-20 and torque to 10 Nm (TW-11).", _
        "Press on the outer race only. Scrap any bearing that has been dropped."
    Set ws = CreateOpSheet(NextOpNumber())
    FillOp ws, "Assembly", "SPEC-220 Rev A", Array( _
            Array("SEAL-40", "Lip seal 40x62x8", "NBR", 1), _
            Array("SCR-M6-20", "Cap screw M6x20", "A2 stainless", 4)), _
        "1. Lightly oil the seal lip." & vbLf & _
        "2. Drive seal SEAL-40 into the housing with drift T-102 until flush with the face." & vbLf & _
        "3. Fit the seal retainer with 4 off cap screws SCR-M6-20 and torque to 10 Nm (TW-11)." & vbLf & _
        "4. Apply a witness mark to each screw head.", _
        "The seal lip faces the bearing."
    Set ws = CreateOpSheet(NextOpNumber())
    FillOp ws, "Inspection", "QP-014 Rev 2", Array( _
            Array("LBL-01", "Serial number label", "Polyester", 1)), _
        "1. Rotate the shaft by hand - it must turn freely with no tight spots." & vbLf & _
        "2. Check every screw carries a witness mark." & vbLf & _
        "3. Fit serial label LBL-01 to the top face." & vbLf & _
        "4. Record the serial number on the route card.", _
        "Quarantine any unit that fails and tell the team leader."
    Check OpListText() = "Op 10, Op 20, Op 30", "Add Operation x3 -> " & OpListText()
    For Each v In GetOpSheets()
        Set ws = v
        Check SafeText(ws.Range("A2").Value) = FrontValue("WI_Title"), ws.Name & ": title linked from Front Page ('" & SafeText(ws.Range("A2").Value) & "')"
        Check SafeText(ws.Range("H2").Value) = "WI-0421", ws.Name & ": Doc No. linked from Front Page ('" & SafeText(ws.Range("H2").Value) & "')"
        Check SafeText(ws.Range("J2").Value) = "B", ws.Name & ": Rev. linked from Front Page ('" & SafeText(ws.Range("J2").Value) & "')"
        Check OpNumberOf(ws) = Val(Mid$(ws.Name, 4)), ws.Name & ": Op No. cell = " & OpNumberOf(ws)
    Next v

    '--------------------------------------------------------------------------
    StepIs "insert / renumber / delete operations"
    Set ws = InsertOpAfterSheet(ThisWorkbook.Worksheets("Op 10"))
    Check ws.Name = "Op 15", "Insert Operation After Op 10 -> " & ws.Name
    Check OpListText() = "Op 10, Op 15, Op 20, Op 30", "tab order after insert: " & OpListText()
    Check ThisWorkbook.Worksheets("Op 15").Index = ThisWorkbook.Worksheets("Op 10").Index + 1, "Op 15 tab sits after Op 10"
    RenumberOpsCore
    Check OpListText() = "Op 10, Op 20, Op 30, Op 40", "Renumber -> " & OpListText()
    Check FirstPart("Op 30") = "SEAL-40" And FirstPart("Op 40") = "LBL-01", "contents moved with their sheets when renumbered"
    DeleteOpSheet ThisWorkbook.Worksheets("Op 20")
    RenumberOpsCore
    Check OpListText() = "Op 10, Op 20, Op 30", "Delete Op 20 + Renumber -> " & OpListText()
    Check FirstPart("Op 10") = "HSG-100" And FirstPart("Op 20") = "SEAL-40" And FirstPart("Op 30") = "LBL-01", _
          "op contents in the right order after delete + renumber"
    Check SafeText(ThisWorkbook.Worksheets("Op 20").Range("OpNo").Value) = "20", "Op No. cell updated by renumber"
    Check NextOpNumber() = 40, "next op number would be 40"

    '--------------------------------------------------------------------------
    StepIs "images"
    InsertImageFile ThisWorkbook.Worksheets("Op 10"), assetDir & "\op30_square.png"
    InsertImageFile ThisWorkbook.Worksheets("Op 10"), assetDir & "\op10_wide.png"
    Check PictureCount(ThisWorkbook.Worksheets("Op 10")) = 1, "Insert Image replaces the existing image"
    InsertImageFile ThisWorkbook.Worksheets("Op 20"), assetDir & "\op20_tall.jpg"
    ' Op 30 goes through the clipboard (Paste Image path)
    Set ws = ThisWorkbook.Worksheets("Op 30")
    UnprotectSheet ws
    ' Source picture sits outside the image box and is removed after pasting,
    ' as when copying from another application
    Set src = ws.Shapes.AddPicture(assetDir & "\op30_square.png", msoFalse, msoTrue, 0, 0, -1, -1)
    src.Copy
    Check PasteImageFromClipboard(ws), "Paste Image from the clipboard"
    UnprotectSheet ws
    src.Delete
    For Each v In GetOpSheets()
        CheckImageFit v
        Check ButtonCount(v) = 7, v.Name & ": all 7 buttons still present (" & ButtonCount(v) & ")"
    Next v

    '--------------------------------------------------------------------------
    StepIs "text overflow detection"
    Set ws = ThisWorkbook.Worksheets("Op 30")
    UnprotectSheet ws
    saved = SafeText(ws.Range("OpNotes").Value)
    ws.Range("OpNotes").Value = Application.WorksheetFunction.Rept("This sentence is repeated until it no longer fits the notes box. ", 12)
    Check TextOverflows(ws.Range("OpNotes")), "long Notes text is detected as overflowing"
    UpdateOverflowWarning ws
    Check SafeText(ws.Range("OpWarnNotes").Value) <> "", "overflow warning shown beside the sheet"
    ws.Range("OpNotes").Value = saved
    Check Not TextOverflows(ws.Range("OpNotes")), "short Notes text is not flagged"
    UpdateOverflowWarning ws
    Check SafeText(ws.Range("OpWarnNotes").Value) = "", "overflow warning cleared"
    For Each v In GetOpSheets()
        Check Not TextOverflows(v.Range("OpDetails")), v.Name & ": Operation Details fit the box"
    Next v

    '--------------------------------------------------------------------------
    StepIs "BOM"
    Set problems = BuildBOM()
    Check problems.Count = 0, "BOM refresh reports no problems (" & problems.Count & ")"
    CheckBomLine "HSG-100", "Gearbox housing", "1", "10"
    CheckBomLine "BRG-6204", "Bearing 6204-2RS", "2", "10"
    CheckBomLine "SCR-M6-20", "Cap screw M6x20", "8", "10, 20"
    CheckBomLine "SEAL-40", "Lip seal 40x62x8", "1", "20"
    CheckBomLine "LBL-01", "Serial number label", "1", "30"
    Check BomRowCount() = 5, "BOM has 5 lines (duplicate SCR-M6-20 merged), found " & BomRowCount()
    ' conflicting description for the same part number
    Set ws = ThisWorkbook.Worksheets("Op 20")
    ws.Range("OpParts").Cells(2, 2).Value = "Cap screw M6x25"
    Set problems = BuildBOM()
    Check problems.Count = 1, "conflicting description is flagged (" & problems.Count & " problem)"
    If problems.Count > 0 Then Info CStr(problems(1))
    Check SafeText(BomSheet.Cells(BomRowOf("SCR-M6-20"), 7).Value) <> "", "conflict note written beside the BOM line"
    ws.Range("OpParts").Cells(2, 2).Value = "Cap screw M6x20"
    BuildBOM

    '--------------------------------------------------------------------------
    StepIs "pre-export checks catch problems"
    ProbeCheck "WI_Title", "", "Title is missing"
    Set ws = ThisWorkbook.Worksheets("Op 30")
    ws.Range("OpRefDoc").Value = "XYZ-999 Rev A"
    Check ProblemsContain("not in section 3"), "Reference Doc not in section 3 is reported"
    ws.Range("OpRefDoc").Value = "QP-014 Rev 2"
    ws.Range("OpParts").Cells(2, 1).Value = "TEST-1"
    Check ProblemsContain("TEST-1: no Qty"), "part with no qty is reported"
    ws.Range("OpParts").Cells(2, 1).Value = ""
    SetName "WI_Rev", "C"
    Check ProblemsContain("last Revision Control entry"), "Revision not matching Revision Control is reported"
    SetName "WI_Rev", "B"

    StepIs "pre-export checks on the finished sample"
    Set problems = RunChecks()
    Check problems.Count = 0, "no problems on the finished sample (" & problems.Count & ")"
    For i = 1 To problems.Count
        Info "  " & problems(i)
    Next i

    '--------------------------------------------------------------------------
    StepIs "page setup"
    ProtectAll
    For Each v In Array(SH_FRONT, SH_BOM, "Op 10", "Op 20", "Op 30")
        Set ws = ThisWorkbook.Worksheets(v)
        Check ws.PageSetup.Pages.Count = 1, ws.Name & ": prints on exactly 1 page"
        saved = ControlsClash(ws)
        Check saved = "", ws.Name & ": buttons/notes sit right of the print area, no overlaps" & IIf(saved = "", "", " - " & saved)
        Check ws.PageSetup.Orientation = IIf(IsOpSheet(ws), xlLandscape, xlPortrait), _
              ws.Name & ": " & IIf(ws.PageSetup.Orientation = xlLandscape, "landscape", "portrait")
        Info ws.Name & ": print area " & ws.PageSetup.PrintArea & ", estimated scale " & Format(EstimatedFitScale(ws), "0%")
    Next v

    '--------------------------------------------------------------------------
    StepIs "saving the sample workbook"
    FrontSheet.Activate
    Application.DisplayAlerts = False
    ThisWorkbook.SaveAs Filename:=outDir & "\WI-0421_RevB.xlsm", FileFormat:=xlOpenXMLWorkbookMacroEnabled
    Application.DisplayAlerts = True

    StepIs "exporting the PDF"
    pdfPath = outDir & "\" & PdfFileName()
    If Dir(pdfPath) <> "" Then Kill pdfPath
    ExportToPdf pdfPath, False
    Check Dir(pdfPath) <> "", "PDF written: " & pdfPath
    If Dir(pdfPath) <> "" Then Info "PDF size " & Format(FileLen(pdfPath) / 1024, "0") & " KB"
    Check PdfFileName() = "WI-0421_RevB.pdf", "PDF named <DocNo>_Rev<Rev>.pdf"
    Check InStr(ThisWorkbook.Worksheets("Op 20").PageSetup.LeftFooter, "Op 2 of 3") > 0, "Op 20 footer says Op 2 of 3"
    Check InStr(ThisWorkbook.Worksheets("Op 20").PageSetup.CenterFooter, "Page 4 of 5") > 0, "Op 20 footer says Page 4 of 5"
    Check InStr(FrontSheet.PageSetup.CenterFooter, "Page 1 of 5") > 0, "Front Page footer says Page 1 of 5"
    Check ActiveWindow.SelectedSheets.Count = 1, "sheets ungrouped after export"
    Check ThisWorkbook.Names(NAME_SHADE).RefersTo = "=TRUE", "input shading back on after export"
    Check FrontSheet.ProtectContents And BomSheet.ProtectContents, "protection restored after export"

    FrontSheet.Activate
    NameRange("WI_Title").Select
    GoTo Finish
Fail:
    mFails = mFails + 1
    mReport = mReport & "ERROR in step '" & mStep & "': " & Err.Description & " (" & Err.Number & ")" & vbCrLf
    Resume Finish
Finish:
    On Error Resume Next
    Application.DisplayAlerts = True
    Application.ScreenUpdating = True
    Application.EnableEvents = True
    If gLog <> "" Then mReport = mReport & String(78, "-") & vbCrLf & "Messages:" & vbCrLf & gLog
    mReport = mReport & String(78, "=") & vbCrLf & IIf(mFails = 0, "ALL CHECKS PASSED", mFails & " CHECK(S) FAILED") & vbCrLf
    WriteReport outDir & "\SelfTest_Report.txt"
    RunSelfTest = IIf(mFails = 0, "PASS", "FAIL (" & mFails & ")")
End Function

'------------------------------------------------------------------------------
' Filling in
'------------------------------------------------------------------------------
Private Sub SetName(ByVal nm As String, ByVal v As Variant)
    NameRange(nm).Value = v
End Sub

' rowsData(i)(j) = value for the j-th merged column group of data row i
Private Sub FillTable(ByVal tbl As String, ByVal rowsData As Variant)
    Dim data As Range
    Dim spec As Variant
    Dim parts() As String
    Dim i As Long, j As Long
    Set data = NameRange(tbl)
    spec = TableSpec(tbl)
    For i = 0 To UBound(rowsData)
        For j = 0 To UBound(rowsData(i))
            If Not IsEmpty(rowsData(i)(j)) Then
                parts = Split(spec(j), ":")
                data.Worksheet.Range(parts(0) & (data.Row + i)).Value = rowsData(i)(j)
            End If
        Next j
    Next i
End Sub

Private Sub FillOp(ByVal ws As Worksheet, ByVal dept As String, ByVal refDoc As String, _
                   ByVal partRows As Variant, ByVal details As String, ByVal notes As String)
    Dim i As Long, j As Long
    UnprotectSheet ws
    ws.Range("OpDept").Value = dept
    ws.Range("OpRefDoc").Value = refDoc
    For i = 0 To UBound(partRows)
        For j = 0 To 3
            ws.Range("OpParts").Cells(i + 1, j + 1).Value = partRows(i)(j)
        Next j
    Next i
    ws.Range("OpDetails").Value = details
    ws.Range("OpNotes").Value = notes
End Sub

'------------------------------------------------------------------------------
' Checks
'------------------------------------------------------------------------------
Private Sub Check(ByVal ok As Boolean, ByVal what As String)
    If ok Then
        mReport = mReport & "PASS  " & what & vbCrLf
    Else
        mReport = mReport & "FAIL  " & what & "   [step: " & mStep & "]" & vbCrLf
        mFails = mFails + 1
    End If
End Sub

Private Sub Info(ByVal s As String)
    mReport = mReport & "      " & s & vbCrLf
End Sub

Private Sub StepIs(ByVal s As String)
    mStep = s
    mReport = mReport & "-- " & s & vbCrLf
End Sub

Private Function FirstPart(ByVal sheetName As String) As String
    FirstPart = SafeText(ThisWorkbook.Worksheets(sheetName).Range("OpParts").Cells(1, 1).Value)
End Function

' "" if every button / note box is right of the print area and none overlap; else what is wrong
Private Function ControlsClash(ByVal ws As Worksheet) As String
    Dim area As Range
    Dim shp As Shape, other As Shape
    Dim s As String
    Set area = ws.Range(ws.PageSetup.PrintArea)
    For Each shp In ws.Shapes
        If shp.Name Like "btn_*" Or shp.Name = "HowToUse" Then
            If shp.Left < area.Left + area.Width - 0.5 Then s = s & shp.Name & " is inside the print area (" & shp.TopLeftCell.Address(False, False) & "); "
            For Each other In ws.Shapes
                If (other.Name Like "btn_*" Or other.Name = "HowToUse") And other.Name <> shp.Name Then
                    If shp.Left < other.Left + other.Width And other.Left < shp.Left + shp.Width And _
                       shp.Top < other.Top + other.Height And other.Top < shp.Top + shp.Height Then
                        If shp.Name < other.Name Then s = s & shp.Name & " overlaps " & other.Name & "; "
                    End If
                End If
            Next other
        End If
    Next shp
    ControlsClash = s
End Function

Private Function ButtonCount(ByVal ws As Worksheet) As Long
    Dim shp As Shape
    For Each shp In ws.Shapes
        If shp.Name Like "btn_*" Then ButtonCount = ButtonCount + 1
    Next shp
End Function

Private Function PictureCount(ByVal ws As Worksheet) As Long
    Dim shp As Shape
    For Each shp In ws.Shapes
        If IsPictureShape(shp) Then
            If Not Intersect(shp.TopLeftCell, ws.Range("OpImageBox")) Is Nothing Then PictureCount = PictureCount + 1
        End If
    Next shp
End Function

Private Sub CheckImageFit(ByVal ws As Worksheet)
    Dim shp As Shape
    Dim pic As Shape
    Dim box As Range
    Dim inside As Boolean, centred As Boolean, fills As Boolean
    Set box = ws.Range("OpImageBox")
    For Each shp In ws.Shapes
        If IsPictureShape(shp) Then Set pic = shp
    Next shp
    Check PictureCount(ws) = 1, ws.Name & ": exactly one image in the image box"
    If pic Is Nothing Then Exit Sub
    inside = pic.Left >= box.Left - 0.5 And pic.Top >= box.Top - 0.5 And _
             pic.Left + pic.Width <= box.Left + box.Width + 0.5 And pic.Top + pic.Height <= box.Top + box.Height + 0.5
    centred = Abs((pic.Left + pic.Width / 2) - (box.Left + box.Width / 2)) < 1 And _
              Abs((pic.Top + pic.Height / 2) - (box.Top + box.Height / 2)) < 1
    fills = Abs(pic.Width - (box.Width - 8)) < 1 Or Abs(pic.Height - (box.Height - 8)) < 1
    Check inside And centred And fills, ws.Name & ": image inside the box, centred and scaled to fit (" & _
          Format(pic.Width, "0") & " x " & Format(pic.Height, "0") & " pt in a " & _
          Format(box.Width, "0") & " x " & Format(box.Height, "0") & " pt box)"
End Sub

Private Function BomRowOf(ByVal partNo As String) As Long
    Dim r As Long
    For r = 5 To 200
        If SafeText(BomSheet.Cells(r, 1).Value) = partNo Then
            BomRowOf = r
            Exit Function
        End If
    Next r
End Function

Private Function BomRowCount() As Long
    Dim r As Long
    r = 5
    Do While SafeText(BomSheet.Cells(r, 1).Value) <> ""
        r = r + 1
    Loop
    BomRowCount = r - 5
End Function

Private Sub CheckBomLine(ByVal partNo As String, ByVal desc As String, ByVal qty As String, ByVal ops As String)
    Dim r As Long
    r = BomRowOf(partNo)
    If r = 0 Then
        Check False, "BOM line for " & partNo
        Exit Sub
    End If
    Check SafeText(BomSheet.Cells(r, 2).Value) = desc And SafeText(BomSheet.Cells(r, 4).Value) = qty And _
          SafeText(BomSheet.Cells(r, 5).Value) = ops, _
          "BOM " & partNo & ": qty " & SafeText(BomSheet.Cells(r, 4).Value) & ", used in Op " & SafeText(BomSheet.Cells(r, 5).Value)
End Sub

Private Function ProblemsContain(ByVal fragment As String) As Boolean
    Dim p As Variant
    For Each p In RunChecks()
        If InStr(1, p, fragment, vbTextCompare) > 0 Then ProblemsContain = True
    Next p
End Function

Private Sub ProbeCheck(ByVal nm As String, ByVal tempValue As String, ByVal expected As String)
    Dim keep As Variant
    keep = NameRange(nm).Value
    NameRange(nm).Value = tempValue
    Check ProblemsContain(expected), "missing field is reported: " & expected
    NameRange(nm).Value = keep
End Sub

' True if the PPE icon group does not overlap any Front Page table
Private Function PpeClearOfTables() As Boolean
    Dim shp As Shape
    Dim t As Variant
    Dim tbl As Range
    Set shp = FrontSheet.Shapes("PPE_Icons")
    PpeClearOfTables = True
    For Each t In TableNames()
        Set tbl = NameRange(CStr(t))
        Set tbl = tbl.Offset(-1, 0).Resize(tbl.Rows.Count + 1)       ' include the header row
        If shp.Left < tbl.Left + tbl.Width And shp.Left + shp.Width > tbl.Left And _
           shp.Top < tbl.Top + tbl.Height And shp.Top + shp.Height > tbl.Top Then PpeClearOfTables = False
    Next t
End Function

Private Sub WriteReport(ByVal path As String)
    Dim f As Integer
    f = FreeFile
    Open path For Output As #f
    Print #f, mReport
    Close #f
End Sub
