Attribute VB_Name = "modExport"
'==============================================================================
' modExport
' Export PDF: refresh BOM -> pre-export checks -> one A4 PDF named
' <DocNo>_Rev<Rev>.pdf next to the workbook (Front Page, BOM, ops in order).
' Also the "does this text fit its box?" measurement used for overflow warnings.
'==============================================================================
Option Explicit

Private Const FOOTER_FONT As String = "&""Segoe UI,Regular""&9 "   ' trailing space: a doc no. starting with a digit must not extend the size code

'------------------------------------------------------------------------------
' Button
'------------------------------------------------------------------------------
Public Sub ExportPDF()
    Dim problems As Collection
    Dim pdfPath As String
    On Error GoTo Fail

    If ThisWorkbook.Path = "" Then
        Notify "Save this work instruction first - the PDF is saved in the same folder."
        If Not SaveWorkbookWithDialog() Then Exit Sub
    End If

    BeginWork
    RefreshRefDocList
    Set problems = RunChecks()
    EndWork
    If problems.Count > 0 Then
        If Not Confirm("Please check the following before issuing:" & vbCrLf & vbCrLf & _
                       JoinLines(problems, 25) & vbCrLf & "Export the PDF anyway?", True, vbExclamation) Then Exit Sub
    End If

    pdfPath = ThisWorkbook.Path & "\" & PdfFileName()
    If Dir(pdfPath) <> "" Then
        If Not Confirm(PdfFileName() & " already exists in this folder." & vbCrLf & "Overwrite it?", True, vbExclamation) Then Exit Sub
    End If

    BeginWork
    ExportToPdf pdfPath, Not gSilent
    EndWork
    If gSilent Then Notify "Exported " & pdfPath
    Exit Sub
Fail:
    RecoverAfterError
    EndWork
    Notify "The PDF was not exported: " & Err.Description & vbCrLf & vbCrLf & _
           "If the PDF is open in a viewer, close it and try again.", vbExclamation
End Sub

Public Function PdfFileName() As String
    Dim docNo As String
    docNo = FrontValue("WI_DocNo")
    If docNo = "" Then docNo = "Work Instruction"
    PdfFileName = SafeFileName(docNo & "_Rev" & FrontValue("WI_Rev")) & ".pdf"
End Function

'------------------------------------------------------------------------------
' Export (no checks, no questions)
'------------------------------------------------------------------------------
Public Sub ExportToPdf(ByVal pdfPath As String, ByVal openAfter As Boolean)
    Dim ops As Collection
    Dim sheetList() As Variant
    Dim total As Long, pg As Long
    Dim ws As Worksheet
    Dim prevSheet As Object
    Dim docRef As String
    Dim exportErr As Long, exportMsg As String

    EnforceSheetOrder
    Set ops = GetOpSheets()
    total = 2 + ops.Count
    ReDim sheetList(0 To total - 1)
    sheetList(0) = SH_FRONT
    sheetList(1) = SH_BOM
    For pg = 1 To ops.Count
        sheetList(pg + 1) = ops(pg).Name
    Next pg

    docRef = FrontValue("WI_DocNo")
    If FrontValue("WI_Rev") <> "" Then docRef = docRef & " Rev " & FrontValue("WI_Rev")

    ' Footers: "Op X of Y" (op pages), "Page N of M", document reference.
    ' Written as text because each sheet is exactly one page.
    UnprotectAll
    For pg = 1 To total
        Set ws = ThisWorkbook.Worksheets(sheetList(pg - 1))
        With ws.PageSetup
            If pg > 2 Then
                .LeftFooter = FOOTER_FONT & "Op " & (pg - 2) & " of " & ops.Count
            Else
                .LeftFooter = ""
            End If
            .CenterFooter = FOOTER_FONT & "Page " & pg & " of " & total
            .RightFooter = FOOTER_FONT & EscapeHeaderText(docRef)
        End With
    Next pg

    With ThisWorkbook.BuiltinDocumentProperties
        .Item("Title").Value = FrontValue("WI_Title")
        .Item("Subject").Value = docRef
        .Item("Author").Value = FrontValue("WI_Author")
    End With

    ' Input shading and on-screen hints are for Excel only
    ThisWorkbook.Names(NAME_SHADE).RefersTo = "=FALSE"
    Application.CalculateFull

    Set prevSheet = ActiveSheet
    ThisWorkbook.Activate
    ThisWorkbook.Worksheets(sheetList).Select
    On Error Resume Next
    ActiveSheet.ExportAsFixedFormat Type:=xlTypePDF, Filename:=pdfPath, Quality:=xlQualityStandard, _
        IncludeDocProperties:=True, IgnorePrintAreas:=False, OpenAfterPublish:=openAfter
    exportErr = Err.Number
    exportMsg = Err.Description
    On Error GoTo 0

    ' Always ungroup the sheets and restore shading/protection, even if the export failed
    FrontSheet.Select
    prevSheet.Activate
    ThisWorkbook.Names(NAME_SHADE).RefersTo = "=TRUE"
    Application.CalculateFull
    ProtectAll
    If exportErr <> 0 Then Err.Raise exportErr, , exportMsg
End Sub

'------------------------------------------------------------------------------
' Pre-export checks
'------------------------------------------------------------------------------
Public Function RunChecks() As Collection
    Dim problems As New Collection
    Dim fields As Variant, labels As Variant
    Dim ops As Collection
    Dim ws As Worksheet
    Dim i As Long
    Dim v As Variant
    Dim refList As String, refDoc As String, lastRev As String

    ' Front page fields
    fields = Array("WI_Title", "WI_DocNo", "WI_Rev", "WI_Author", "WI_ReleaseDate")
    labels = Array("Title", "Document Number", "Revision", "Author", "Release Date")
    For i = 0 To UBound(fields)
        If FrontValue(CStr(fields(i))) = "" Then problems.Add "Front Page: " & labels(i) & " is missing."
    Next i

    ' Latest Revision Control entry should match the document revision
    lastRev = ""
    For Each v In NameRange("RevCtl_Rows").Columns(1).Cells
        If SafeText(v.Value) <> "" Then lastRev = SafeText(v.Value)
    Next v
    If lastRev = "" Then
        problems.Add "Front Page: section 2 (Revision Control) is empty."
    ElseIf FrontValue("WI_Rev") <> "" And StrComp(lastRev, FrontValue("WI_Rev"), vbTextCompare) <> 0 Then
        problems.Add "Front Page: the last Revision Control entry is '" & lastRev & _
                     "' but the document Revision is '" & FrontValue("WI_Rev") & "'."
    End If

    ' Op sheets
    Set ops = GetOpSheets()
    If ops.Count = 0 Then problems.Add "There are no operation sheets - use Add Operation on the Front Page."
    refList = RefDocListText()
    For i = 1 To ops.Count
        Set ws = ops(i)
        If SafeText(ws.Range("OpDept").Value) = "" Then problems.Add ws.Name & ": Department is empty."
        If Not HasOpImage(ws) Then problems.Add ws.Name & ": no image."
        If SafeText(ws.Range("OpDetails").Value) = "" Then problems.Add ws.Name & ": Operation Details are empty."
        If TextOverflows(ws.Range("OpDetails")) Then problems.Add ws.Name & ": Operation Details text does not fit the box (it will be cut off)."
        If TextOverflows(ws.Range("OpNotes")) Then problems.Add ws.Name & ": Notes text does not fit the box (it will be cut off)."
        refDoc = SafeText(ws.Range("OpRefDoc").Value)
        If refDoc <> "" Then
            If InStr(1, refList, "|" & refDoc & "|", vbTextCompare) = 0 Then
                problems.Add ws.Name & ": Reference Doc '" & refDoc & "' is not in section 3 (Associated Documents)."
            End If
        End If
        UpdateOverflowWarning ws
    Next i

    ' BOM (refreshed here) - missing quantities, missing part numbers, conflicts
    For Each v In BuildBOM()
        problems.Add v
    Next v

    Set RunChecks = problems
End Function

'------------------------------------------------------------------------------
' Text overflow
'------------------------------------------------------------------------------
' True if the (wrapped) text of a merged box needs more height than the box has.
' Measured by auto-fitting a scratch cell of the same width and font, placed on
' the same (visible) sheet far outside the print area.
Public Function TextOverflows(ByVal cell As Range) As Boolean
    Dim area As Range
    Dim sc As Range
    Dim txt As String
    Dim wasProtected As Boolean
    Set area = cell.MergeArea
    txt = SafeText(area.Cells(1, 1).Value)
    If txt = "" Then Exit Function
    Set sc = area.Worksheet.Range(SCRATCH_CELL)
    wasProtected = area.Worksheet.ProtectContents
    If wasProtected Then UnprotectSheet area.Worksheet
    SetColumnWidthPoints sc.EntireColumn, area.Width - 5
    With sc
        .NumberFormat = "@"
        .Font.Name = area.Cells(1, 1).Font.Name
        .Font.Size = area.Cells(1, 1).Font.Size
        .Font.Bold = area.Cells(1, 1).Font.Bold
        .WrapText = True
        .Value = txt
        .EntireRow.AutoFit
        TextOverflows = (.RowHeight > area.Height - 2)
        .Clear
        .EntireRow.RowHeight = .Worksheet.StandardHeight
    End With
    If wasProtected Then ProtectSheet area.Worksheet
End Function

Private Sub SetColumnWidthPoints(ByVal col As Range, ByVal pts As Double)
    Dim i As Long
    If pts > 1500 Then pts = 1500
    col.ColumnWidth = pts / 5.3
    For i = 1 To 3
        If col.Width > 0 Then col.ColumnWidth = Application.WorksheetFunction.Min(255, col.ColumnWidth * pts / col.Width)
    Next i
End Sub

' Red warning beside the op sheet (not printed) + red box via conditional format
Public Sub UpdateOverflowWarning(ByVal ws As Worksheet)
    Dim wasProtected As Boolean
    wasProtected = ws.ProtectContents
    If wasProtected Then UnprotectSheet ws
    If TextOverflows(ws.Range("OpDetails")) Then
        ws.Range("OpWarnDetails").Value = "Operation Details text is too long for the box - it will be cut off in the PDF."
    Else
        ws.Range("OpWarnDetails").Value = ""
    End If
    If TextOverflows(ws.Range("OpNotes")) Then
        ws.Range("OpWarnNotes").Value = "Notes text is too long for the box - it will be cut off in the PDF."
    Else
        ws.Range("OpWarnNotes").Value = ""
    End If
    If wasProtected Then ProtectSheet ws
End Sub
