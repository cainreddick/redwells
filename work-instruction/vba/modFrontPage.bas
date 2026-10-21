Attribute VB_Name = "modFrontPage"
'==============================================================================
' modFrontPage
' Front Page tables (sections 1-5): Add Row / Delete Row, Link Document,
' the Reference Doc dropdown source, and showing/hiding the Lists sheet.
'==============================================================================
Option Explicit

' Workbook names of the data rows of each Front Page table
Public Function TableNames() As Variant
    TableNames = Array("HSE_Rows", "RevCtl_Rows", "AssocDoc_Rows", "Tooling_Rows", "Equip_Rows")
End Function

' Merged column groups of each table's data rows
Public Function TableSpec(ByVal tableName As String) As Variant
    Select Case tableName
    Case "HSE_Rows":      TableSpec = Array("A:B", "C:E")               ' Activity | Precaution
    Case "RevCtl_Rows":   TableSpec = Array("A:A", "B:B", "C:G", "H:I") ' Revision | Author | Description | Date
    Case "AssocDoc_Rows": TableSpec = Array("A:B", "C:C", "D:G", "H:I") ' Document No. | Rev. | Description | Type
    Case "Tooling_Rows":  TableSpec = Array("A:C", "D:D", "E:I")        ' Part/Assembly No. | Rev. | Description
    Case "Equip_Rows":    TableSpec = Array("A:C", "D:G")               ' Asset No. | Description
    End Select
End Function

Public Function SpecLastCol(ByVal spec As Variant) As String
    Dim parts() As String
    parts = Split(spec(UBound(spec)), ":")
    SpecLastCol = parts(UBound(parts))
End Function

' Which table (if any) the given cell's row belongs to
Public Function TableAt(ByVal cell As Range) As String
    Dim t As Variant
    If cell.Worksheet.Name <> SH_FRONT Then Exit Function
    For Each t In TableNames()
        If Not Intersect(cell.EntireRow, NameRange(CStr(t))) Is Nothing Then
            TableAt = CStr(t)
            Exit Function
        End If
    Next t
End Function

'------------------------------------------------------------------------------
' Formatting of table rows (used by the build and by Add Row)
'------------------------------------------------------------------------------
Public Sub FormatTableRow(ByVal ws As Worksheet, ByVal r As Long, ByVal spec As Variant, ByVal rowHeight As Double)
    Dim seg As Variant
    Dim parts() As String
    Dim rng As Range
    Set rng = ws.Range("A" & r & ":" & SpecLastCol(spec) & r)
    rng.UnMerge
    With rng
        .Font.Name = BASE_FONT
        .Font.Size = 11
        .Font.Bold = False
        .Font.Underline = xlUnderlineStyleNone
        .Font.Color = vbBlack
        .HorizontalAlignment = xlCenter
        .VerticalAlignment = xlCenter
        .WrapText = False
        .ShrinkToFit = True
    End With
    For Each seg In spec
        parts = Split(seg, ":")
        If parts(0) <> parts(1) Then ws.Range(parts(0) & r & ":" & parts(1) & r).Merge
    Next seg
    ws.Rows(r).RowHeight = rowHeight
End Sub

' Borders for header row + data rows of a table
Public Sub ApplyTableBorders(ByVal tableName As String)
    Dim data As Range
    Set data = NameRange(tableName)
    ApplyGrid data.Worksheet.Range(data.Cells(1, 1).Offset(-1, 0), _
                                   data.Cells(data.Rows.Count, data.Columns.Count)), 1
End Sub

' Input shading and validation for a table's data rows
Public Sub ApplyTableInputs(ByVal tableName As String)
    Dim data As Range
    Set data = NameRange(tableName)
    MarkAsInput data
    Select Case tableName
    Case "RevCtl_Rows"
        SetDateValidation ColRange(data, "H:I")
    Case "AssocDoc_Rows"
        SetListValidation ColRange(data, "H:I"), "DocTypeList", False
        ColRange(data, "A:C").NumberFormat = "@"     ' doc numbers / revs stay as typed
    Case "Tooling_Rows"
        ColRange(data, "A:D").NumberFormat = "@"
    End Select
End Sub

'------------------------------------------------------------------------------
' Add Row / Delete Row (buttons)
'------------------------------------------------------------------------------
Public Sub AddRow()
    Dim tbl As String
    Dim scale As Double
    On Error GoTo Fail
    tbl = ""
    If ActiveSheet.Name = SH_FRONT Then tbl = TableAt(ActiveCell)
    If tbl = "" Then
        Notify "Click in one of the Front Page tables (sections 1 to 5) first, then press Add Row."
        Exit Sub
    End If
    BeginWork
    AddTableRow tbl
    scale = EstimatedFitScale(FrontSheet)
    EndWork
    If scale < 0.75 Then
        Notify "Row added. The Front Page now prints at about " & Format(scale, "0%") & _
               " to fit on one A4 page - consider removing unused rows elsewhere.", vbExclamation
    End If
    Exit Sub
Fail:
    RecoverAfterError
    EndWork
    Notify "Could not add a row: " & Err.Description, vbExclamation
End Sub

' Inserts one blank row just above the table's last row (so the name grows with it)
Public Sub AddTableRow(ByVal tbl As String)
    Dim ws As Worksheet
    Dim data As Range
    Dim r As Long
    Dim h As Double
    Set ws = FrontSheet
    Set data = NameRange(tbl)
    r = data.Row + data.Rows.Count - 1
    h = ws.Rows(data.Row).RowHeight
    UnprotectSheet ws
    ws.Rows(r).Insert Shift:=xlDown, CopyOrigin:=xlFormatFromLeftOrAbove
    FormatTableRow ws, r, TableSpec(tbl), h
    ApplyTableBorders tbl
    ApplyTableInputs tbl
    ProtectSheet ws
End Sub

Public Sub DeleteRow()
    Dim ws As Worksheet
    Dim tbl As String
    Dim data As Range
    Dim r As Long
    On Error GoTo Fail
    Set ws = FrontSheet
    tbl = ""
    If ActiveSheet.Name = SH_FRONT Then tbl = TableAt(ActiveCell)
    If tbl = "" Then
        Notify "Click in the row you want to remove (sections 1 to 5), then press Delete Row."
        Exit Sub
    End If
    Set data = NameRange(tbl)
    If data.Rows.Count <= 2 Then
        Notify "Each table keeps at least two rows.", vbExclamation
        Exit Sub
    End If
    r = ActiveCell.Row
    If Application.WorksheetFunction.CountA(ws.Range("A" & r & ":" & SpecLastCol(TableSpec(tbl)) & r)) > 0 Then
        If Not Confirm("Delete this row and what is typed in it?", False) Then Exit Sub
    End If
    BeginWork
    UnprotectSheet ws
    ws.Rows(r).Delete
    ApplyTableBorders tbl
    If tbl = "AssocDoc_Rows" Then RefreshRefDocList
    ProtectSheet ws
    EndWork
    Exit Sub
Fail:
    RecoverAfterError
    EndWork
    Notify "Could not delete the row: " & Err.Description, vbExclamation
End Sub

'------------------------------------------------------------------------------
' Link Document (section 3)
'------------------------------------------------------------------------------
Public Sub LinkDocument()
    Dim ws As Worksheet
    Dim data As Range
    Dim r As Long
    Dim f As String
    On Error GoTo Fail
    Set ws = FrontSheet
    Set data = NameRange("AssocDoc_Rows")
    r = 0
    If ActiveSheet.Name = SH_FRONT Then
        If Not Intersect(ActiveCell.EntireRow, data) Is Nothing Then r = ActiveCell.Row
    End If
    If r = 0 Then r = FirstEmptyTableRow("AssocDoc_Rows")
    If r = 0 Then
        If Not Confirm("Section 3 is full. Add a row for the new document?", True) Then Exit Sub
        AddTableRow "AssocDoc_Rows"
        r = FirstEmptyTableRow("AssocDoc_Rows")
    ElseIf SafeText(ws.Cells(r, 1).Value) <> "" Then
        If Not Confirm("Replace '" & SafeText(ws.Cells(r, 1).Value) & "' with a new linked document?", False) Then Exit Sub
    End If

    With Application.FileDialog(msoFileDialogFilePicker)
        .Title = "Choose the associated document (drawing, spec, procedure...)"
        .AllowMultiSelect = False
        .Filters.Clear
        .Filters.Add "All files", "*.*"
        If ThisWorkbook.Path <> "" Then .InitialFileName = ThisWorkbook.Path & "\"
        If .Show <> -1 Then Exit Sub
        f = .SelectedItems(1)
    End With

    BeginWork
    LinkDocumentFile r, f
    EndWork
    ws.Activate
    ws.Cells(r, 3).Select          ' Rev. is next to fill in
    Exit Sub
Fail:
    RecoverAfterError
    EndWork
    Notify "Could not link the document: " & Err.Description, vbExclamation
End Sub

' Fills Document Number from the file name and hyperlinks it to the file
Public Sub LinkDocumentFile(ByVal r As Long, ByVal filePath As String)
    Dim ws As Worksheet
    Dim cell As Range
    Dim docNo As String
    Dim addr As String
    Set ws = FrontSheet
    Set cell = ws.Cells(r, 1)
    addr = ToUncPath(filePath)
    docNo = Mid$(filePath, InStrRev(filePath, "\") + 1)
    If InStrRev(docNo, ".") > 1 Then docNo = Left$(docNo, InStrRev(docNo, ".") - 1)

    UnprotectSheet ws
    ws.Hyperlinks.Add Anchor:=cell, Address:=addr, TextToDisplay:=docNo, ScreenTip:=addr
    ' The Hyperlink style changes the font; put the template font back (blue, underlined)
    With cell.MergeArea.Font
        .Name = BASE_FONT
        .Size = 11
        .Color = RGB(5, 99, 193)
        .Underline = xlUnderlineStyleSingle
    End With
    ProtectSheet ws
    RefreshRefDocList
End Sub

Private Function FirstEmptyTableRow(ByVal tbl As String) As Long
    Dim data As Range
    Dim i As Long
    Set data = NameRange(tbl)
    For i = 1 To data.Rows.Count
        If Application.WorksheetFunction.CountA(data.Rows(i)) = 0 Then
            FirstEmptyTableRow = data.Row + i - 1
            Exit Function
        End If
    Next i
End Function

' Mapped drive letters differ between PCs: link to \\server\share\... instead
Public Function ToUncPath(ByVal p As String) As String
    Dim fso As Object
    Dim drv As Object
    ToUncPath = p
    If Len(p) < 3 Or Mid$(p, 2, 1) <> ":" Then Exit Function
    On Error GoTo Done
    Set fso = CreateObject("Scripting.FileSystemObject")
    Set drv = fso.GetDrive(Left$(p, 2))
    If drv.DriveType = 3 Then                     ' 3 = network drive
        If Len(drv.ShareName) > 0 Then ToUncPath = drv.ShareName & Mid$(p, 3)
    End If
Done:
End Function

'------------------------------------------------------------------------------
' Reference Doc dropdown (op sheets) = section 3 "DocNo Rev X"
'------------------------------------------------------------------------------
Public Function RefDocLabel(ByVal docNo As String, ByVal rev As String) As String
    If rev = "" Then
        RefDocLabel = docNo
    Else
        RefDocLabel = docNo & " Rev " & rev
    End If
End Function

Public Sub RefreshRefDocList()
    Dim data As Range
    Dim lst As Worksheet
    Dim r As Long, n As Long
    Dim docNo As String
    Set data = NameRange("AssocDoc_Rows")
    Set lst = ListsSheet
    lst.Range(REFDOC_COL & "2:" & REFDOC_COL & "500").ClearContents
    n = 1
    For r = 1 To data.Rows.Count
        docNo = SafeText(data.Cells(r, 1).Value)
        If docNo <> "" Then
            n = n + 1
            lst.Range(REFDOC_COL & n).Value = RefDocLabel(docNo, SafeText(data.Cells(r, 3).Value))
        End If
    Next r
End Sub

' "|label1|label2|" for quick membership tests
Public Function RefDocListText() As String
    Dim lst As Worksheet
    Dim r As Long
    Dim s As String
    Set lst = ListsSheet
    s = "|"
    For r = 2 To 500
        If SafeText(lst.Range(REFDOC_COL & r).Value) <> "" Then s = s & SafeText(lst.Range(REFDOC_COL & r).Value) & "|"
    Next r
    RefDocListText = s
End Function

'------------------------------------------------------------------------------
' Lists sheet (departments, document types, approvers)
'------------------------------------------------------------------------------
Public Sub EditLists()
    On Error GoTo Fail
    UnlockStructure
    ListsSheet.Visible = xlSheetVisible
    LockStructure
    ListsSheet.Activate
    ListsSheet.Range("A2").Select
    Exit Sub
Fail:
    RecoverAfterError
    Notify "Could not open the Lists sheet: " & Err.Description, vbExclamation
End Sub

Public Sub HideLists()
    On Error GoTo Fail
    UnlockStructure
    FrontSheet.Activate
    ListsSheet.Visible = xlSheetHidden
    LockStructure
    Exit Sub
Fail:
    RecoverAfterError
    Notify "Could not hide the Lists sheet: " & Err.Description, vbExclamation
End Sub

Public Sub GoToFrontPage()
    FrontSheet.Activate
End Sub
