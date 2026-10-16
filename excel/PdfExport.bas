Attribute VB_Name = "PdfExport"
Option Explicit

' Exports whatever cells are selected to a PDF.
' Assign ExportSelectionToPDF to a button (Quick Access Toolbar, ribbon, or a shape on a sheet).

Private Const FIT_TO_ONE_PAGE_WIDE As Boolean = True   ' scale the selection to fit one page across
Private Const OPEN_AFTER_EXPORT As Boolean = True       ' open the PDF once it's saved

Public Sub ExportSelectionToPDF()
    Dim rng As Range
    Dim ws As Worksheet
    Dim defaultName As String
    Dim savePath As Variant
    Dim oldZoom As Variant
    Dim oldWide As Variant
    Dim oldTall As Variant
    Dim pageSetupChanged As Boolean

    If TypeName(Selection) <> "Range" Then
        MsgBox "Select the cells you want to export first.", vbExclamation, "Export to PDF"
        Exit Sub
    End If

    Set rng = Selection
    Set ws = rng.Worksheet

    ' Suggest "<Sheet name> <date time>.pdf" next to the workbook (or in the default folder if unsaved)
    defaultName = CleanFileName(ws.Name & " " & Format(Now, "yyyy-mm-dd hhmm")) & ".pdf"
    If Len(ws.Parent.Path) > 0 Then defaultName = ws.Parent.Path & Application.PathSeparator & defaultName

    savePath = Application.GetSaveAsFilename( _
        InitialFileName:=defaultName, _
        FileFilter:="PDF Files (*.pdf), *.pdf", _
        Title:="Save selection as PDF")
    If VarType(savePath) = vbBoolean Then Exit Sub   ' user pressed Cancel

    If LCase$(Right$(savePath, 4)) <> ".pdf" Then savePath = savePath & ".pdf"

    If Len(Dir(savePath)) > 0 Then
        If MsgBox("""" & savePath & """ already exists." & vbNewLine & "Replace it?", _
                  vbQuestion + vbYesNo, "Export to PDF") <> vbYes Then Exit Sub
    End If

    On Error GoTo Failed
    Application.ScreenUpdating = False

    If FIT_TO_ONE_PAGE_WIDE Then
        With ws.PageSetup
            oldZoom = .Zoom
            oldWide = .FitToPagesWide
            oldTall = .FitToPagesTall
            pageSetupChanged = True
            .Zoom = False
            .FitToPagesWide = 1
            .FitToPagesTall = False
        End With
    End If

    rng.ExportAsFixedFormat _
        Type:=xlTypePDF, _
        Filename:=savePath, _
        Quality:=xlQualityStandard, _
        IncludeDocProperties:=True, _
        IgnorePrintAreas:=False, _
        OpenAfterPublish:=OPEN_AFTER_EXPORT

    RestorePageSetup ws, pageSetupChanged, oldZoom, oldWide, oldTall
    Application.ScreenUpdating = True

    If Not OPEN_AFTER_EXPORT Then
        MsgBox "Saved to:" & vbNewLine & savePath, vbInformation, "Export to PDF"
    End If
    Exit Sub

Failed:
    Dim errMsg As String
    errMsg = Err.Description
    On Error Resume Next
    RestorePageSetup ws, pageSetupChanged, oldZoom, oldWide, oldTall
    Application.ScreenUpdating = True
    MsgBox "Couldn't export the PDF." & vbNewLine & vbNewLine & errMsg & vbNewLine & vbNewLine & _
           "If a file with that name is open in a PDF viewer, close it and try again.", _
           vbCritical, "Export to PDF"
End Sub

Private Sub RestorePageSetup(ByVal ws As Worksheet, ByVal changed As Boolean, _
                             ByVal oldZoom As Variant, ByVal oldWide As Variant, ByVal oldTall As Variant)
    If Not changed Then Exit Sub
    On Error Resume Next
    With ws.PageSetup
        If VarType(oldZoom) = vbBoolean Then
            .Zoom = False
            .FitToPagesWide = oldWide
            .FitToPagesTall = oldTall
        Else
            .Zoom = oldZoom
        End If
    End With
End Sub

Private Function CleanFileName(ByVal s As String) As String
    Dim ch As Variant
    For Each ch In Array("\", "/", ":", "*", "?", """", "<", ">", "|")
        s = Replace(s, ch, "_")
    Next ch
    CleanFileName = Trim$(s)
End Function
