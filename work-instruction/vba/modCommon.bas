Attribute VB_Name = "modCommon"
'==============================================================================
' modCommon
' Shared constants and helpers for the Work Instruction workbook (QMF 12):
' sheet lookups, protection, messages, named ranges, validation, page setup.
'==============================================================================
Option Explicit

' ---- Sheet names -------------------------------------------------------------
Public Const SH_FRONT As String = "Front Page"
Public Const SH_BOM As String = "BOM"
Public Const SH_TEMPLATE As String = "Op Template"
Public Const SH_LISTS As String = "Lists"
Public Const OP_PREFIX As String = "Op "
Public Const OP_STEP As Long = 10

' ---- Protection --------------------------------------------------------------
' No password: protection is there to stop accidents, not to secure the file.
' (Review > Unprotect Sheet works without a password if it is ever needed.)
Public Const PROTECT_PWD As String = ""

' ---- Names -------------------------------------------------------------------
' Workbook name that switches the input-cell shading on/off (off while exporting)
Public Const NAME_SHADE As String = "ShadeInputs"
' Lists sheet: column holding the Reference Doc dropdown (rebuilt by macro)
Public Const REFDOC_COL As String = "H"
' Scratch cell (on each op sheet, outside the print area) used to measure whether text fits its box
Public Const SCRATCH_CELL As String = "Z200"

Public Const APP_TITLE As String = "Work Instruction"
Public Const BASE_FONT As String = "Segoe UI"

' ---- Run state ---------------------------------------------------------------
' gSilent is set by the build/self-test scripts: no dialogs, messages go to gLog
Public gSilent As Boolean
Public gLog As String

'------------------------------------------------------------------------------
' Messages
'------------------------------------------------------------------------------
Public Sub Notify(ByVal msg As String, Optional ByVal icon As VbMsgBoxStyle = vbInformation)
    If gSilent Then
        gLog = gLog & msg & vbCrLf
    Else
        MsgBox msg, icon, APP_TITLE
    End If
End Sub

' Yes/No question. In silent mode returns silentAnswer without asking.
Public Function Confirm(ByVal msg As String, Optional ByVal silentAnswer As Boolean = True, _
                        Optional ByVal icon As VbMsgBoxStyle = vbQuestion) As Boolean
    If gSilent Then
        gLog = gLog & msg & " -> " & IIf(silentAnswer, "Yes", "No") & vbCrLf
        Confirm = silentAnswer
    Else
        Confirm = (MsgBox(msg, vbYesNo + icon, APP_TITLE) = vbYes)
    End If
End Function

'------------------------------------------------------------------------------
' Sheets
'------------------------------------------------------------------------------
Public Function FrontSheet() As Worksheet
    Set FrontSheet = ThisWorkbook.Worksheets(SH_FRONT)
End Function

Public Function BomSheet() As Worksheet
    Set BomSheet = ThisWorkbook.Worksheets(SH_BOM)
End Function

Public Function TemplateSheet() As Worksheet
    Set TemplateSheet = ThisWorkbook.Worksheets(SH_TEMPLATE)
End Function

Public Function ListsSheet() As Worksheet
    Set ListsSheet = ThisWorkbook.Worksheets(SH_LISTS)
End Function

Public Function SheetExists(ByVal sheetName As String) As Boolean
    Dim ws As Object
    On Error Resume Next
    Set ws = ThisWorkbook.Sheets(sheetName)
    SheetExists = Not ws Is Nothing
    On Error GoTo 0
End Function

' Generated operation sheets are named "Op 10", "Op 20", ...
Public Function IsOpSheet(ByVal ws As Object) As Boolean
    If TypeName(ws) <> "Worksheet" Then Exit Function
    If ws.Name = SH_TEMPLATE Then Exit Function
    IsOpSheet = (ws.Name Like OP_PREFIX & "#*")
End Function

' Op number from the sheet's OpNo cell, falling back to the sheet name
Public Function OpNumberOf(ByVal ws As Worksheet) As Long
    Dim v As Variant
    v = ws.Range("OpNo").Value
    If Not IsEmpty(v) And Not IsError(v) And IsNumeric(v) Then
        OpNumberOf = CLng(v)
    Else
        OpNumberOf = CLng(Val(Mid$(ws.Name, Len(OP_PREFIX) + 1)))
    End If
End Function

' All op sheets, sorted by op number
Public Function GetOpSheets() As Collection
    Dim result As New Collection
    Dim ws As Worksheet
    Dim i As Long
    Dim placed As Boolean
    For Each ws In ThisWorkbook.Worksheets
        If IsOpSheet(ws) Then
            placed = False
            For i = 1 To result.Count
                If OpNumberOf(ws) < OpNumberOf(result(i)) Then
                    result.Add ws, Before:=i
                    placed = True
                    Exit For
                End If
            Next i
            If Not placed Then result.Add ws
        End If
    Next ws
    Set GetOpSheets = result
End Function

' Keeps tabs in print order: Front Page, BOM, ops by number, then hidden sheets
Public Sub EnforceSheetOrder()
    Dim ops As Collection
    Dim prev As Worksheet
    Dim i As Long
    Set ops = GetOpSheets()
    UnlockStructure
    If FrontSheet.Index <> 1 Then FrontSheet.Move Before:=ThisWorkbook.Sheets(1)
    If BomSheet.Index <> 2 Then BomSheet.Move After:=FrontSheet
    Set prev = BomSheet
    For i = 1 To ops.Count
        If ops(i).Index <> prev.Index + 1 Then ops(i).Move After:=prev
        Set prev = ops(i)
    Next i
    LockStructure
End Sub

'------------------------------------------------------------------------------
' Protection
'------------------------------------------------------------------------------
Public Sub UnprotectSheet(ByVal ws As Worksheet)
    ws.Unprotect Password:=PROTECT_PWD
End Sub

Public Sub ProtectSheet(ByVal ws As Worksheet)
    Select Case ws.Name
    Case SH_LISTS
        ' Lists stays editable so the team can maintain the dropdowns
        ws.Unprotect Password:=PROTECT_PWD
    Case SH_BOM
        ws.Protect Password:=PROTECT_PWD, DrawingObjects:=True, Contents:=True, _
                   Scenarios:=True, UserInterfaceOnly:=True
        ws.EnableSelection = xlNoRestrictions
    Case SH_FRONT
        ws.Protect Password:=PROTECT_PWD, DrawingObjects:=True, Contents:=True, _
                   Scenarios:=True, UserInterfaceOnly:=True
        ' Only the shaded input cells can be selected, so Tab walks through them in order
        ws.EnableSelection = xlUnlockedCells
    Case Else
        ' Op sheets (and the template): drawing objects stay editable so engineers
        ' can add arrows / callouts over the operation image.
        ws.Protect Password:=PROTECT_PWD, DrawingObjects:=False, Contents:=True, _
                   Scenarios:=True, UserInterfaceOnly:=True
        ws.EnableSelection = xlUnlockedCells
    End Select
End Sub

Public Sub ProtectAll()
    Dim ws As Worksheet
    For Each ws In ThisWorkbook.Worksheets
        ProtectSheet ws
    Next ws
End Sub

Public Sub UnprotectAll()
    Dim ws As Worksheet
    For Each ws In ThisWorkbook.Worksheets
        UnprotectSheet ws
    Next ws
End Sub

' Workbook structure protection stops sheets being renamed, unhidden or deleted by hand
Public Sub UnlockStructure()
    If ThisWorkbook.ProtectStructure Then ThisWorkbook.Unprotect Password:=PROTECT_PWD
End Sub

Public Sub LockStructure()
    If Not ThisWorkbook.ProtectStructure Then
        ThisWorkbook.Protect Password:=PROTECT_PWD, Structure:=True, Windows:=False
    End If
End Sub

'------------------------------------------------------------------------------
' Screen / events
'------------------------------------------------------------------------------
Public Sub BeginWork()
    Application.ScreenUpdating = False
    Application.EnableEvents = False
End Sub

Public Sub EndWork()
    Application.EnableEvents = True
    Application.ScreenUpdating = True
    Application.CutCopyMode = False
End Sub

' Puts protection back after a macro failed part-way through
Public Sub RecoverAfterError()
    On Error Resume Next
    Application.DisplayAlerts = True
    ThisWorkbook.Names(NAME_SHADE).RefersTo = "=TRUE"
    ProtectAll
    LockStructure
End Sub

'------------------------------------------------------------------------------
' Names and values
'------------------------------------------------------------------------------
Public Function NameRange(ByVal nm As String) As Range
    Set NameRange = ThisWorkbook.Names(nm).RefersToRange
End Function

' Text of a cell value; errors and empties become ""
Public Function SafeText(ByVal v As Variant) As String
    If IsError(v) Or IsEmpty(v) Or IsNull(v) Then
        SafeText = ""
    Else
        SafeText = Trim$(CStr(v))
    End If
End Function

' Value of a single-cell workbook name on the Front Page, as text
Public Function FrontValue(ByVal nm As String) As String
    FrontValue = SafeText(NameRange(nm).Cells(1, 1).Value)
End Function

Public Function SafeFileName(ByVal s As String) As String
    Dim ch As Variant
    For Each ch In Array("\", "/", ":", "*", "?", """", "<", ">", "|")
        s = Replace(s, CStr(ch), "-")
    Next ch
    SafeFileName = Trim$(s)
End Function

' "&" has a special meaning in headers/footers
Public Function EscapeHeaderText(ByVal s As String) As String
    EscapeHeaderText = Replace(s, "&", "&&")
End Function

' Columns of a range given as letters, e.g. ColRange(rng, "H:I")
Public Function ColRange(ByVal rng As Range, ByVal cols As String) As Range
    Dim parts() As String
    parts = Split(cols, ":")
    Set ColRange = rng.Worksheet.Range(parts(0) & rng.Row & ":" & parts(UBound(parts)) & _
                                       (rng.Row + rng.Rows.Count - 1))
End Function

'------------------------------------------------------------------------------
' Input cells: unlocked, shaded on screen (shading is switched off for the PDF)
'------------------------------------------------------------------------------
Public Function InputFillColor() As Long
    InputFillColor = RGB(255, 242, 204)
End Function

Public Sub MarkAsInput(ByVal rng As Range)
    rng.Locked = False
    rng.FormatConditions.Delete
    With rng.FormatConditions.Add(Type:=xlExpression, Formula1:="=" & NAME_SHADE)
        .Interior.Color = InputFillColor()
        .StopIfTrue = False
    End With
End Sub

'------------------------------------------------------------------------------
' Data validation
'------------------------------------------------------------------------------
Public Sub SetListValidation(ByVal rng As Range, ByVal listName As String, _
                             Optional ByVal strict As Boolean = True, Optional ByVal prompt As String = "")
    With rng.Validation
        .Delete
        .Add Type:=xlValidateList, AlertStyle:=IIf(strict, xlValidAlertStop, xlValidAlertInformation), _
             Operator:=xlBetween, Formula1:="=" & listName
        .IgnoreBlank = True
        .InCellDropdown = True
        .ShowError = True
        If strict Then
            .ErrorTitle = APP_TITLE
            .ErrorMessage = "Please pick a value from the list."
        End If
        If prompt <> "" Then
            .InputTitle = ""
            .InputMessage = prompt
            .ShowInput = True
        End If
    End With
End Sub

Public Sub SetDateValidation(ByVal rng As Range)
    With rng.Validation
        .Delete
        .Add Type:=xlValidateDate, AlertStyle:=xlValidAlertStop, Operator:=xlBetween, _
             Formula1:=CStr(CLng(DateSerial(2000, 1, 1))), Formula2:=CStr(CLng(DateSerial(2099, 12, 31)))
        .IgnoreBlank = True
        .ErrorTitle = "Date"
        .ErrorMessage = "Please enter a date, e.g. 21/10/2026."
        .ShowError = True
    End With
    rng.NumberFormat = "dd/mm/yyyy"
End Sub

Public Sub SetPositiveNumberValidation(ByVal rng As Range)
    With rng.Validation
        .Delete
        .Add Type:=xlValidateDecimal, AlertStyle:=xlValidAlertStop, Operator:=xlGreater, Formula1:="0"
        .IgnoreBlank = True
        .ErrorTitle = "Quantity"
        .ErrorMessage = "Quantity must be a number greater than zero."
        .ShowError = True
    End With
End Sub

'------------------------------------------------------------------------------
' Borders
'------------------------------------------------------------------------------
Public Sub SetEdge(ByVal rng As Range, ByVal edge As XlBordersIndex, ByVal weight As XlBorderWeight)
    With rng.Borders(edge)
        .LineStyle = xlContinuous
        .Weight = weight
        .Color = vbBlack
    End With
End Sub

' Grid in the style of the original template: thin lines between rows,
' medium lines between columns and around the outside.
Public Sub ApplyGrid(ByVal rng As Range, Optional ByVal headerRows As Long = 1)
    If rng.Rows.Count > 1 Then SetEdge rng, xlInsideHorizontal, xlThin
    If rng.Columns.Count > 1 Then SetEdge rng, xlInsideVertical, xlMedium
    SetEdge rng, xlEdgeLeft, xlMedium
    SetEdge rng, xlEdgeRight, xlMedium
    SetEdge rng, xlEdgeTop, xlMedium
    SetEdge rng, xlEdgeBottom, xlMedium
    If headerRows > 0 And rng.Rows.Count > headerRows Then
        SetEdge rng.Rows(headerRows), xlEdgeBottom, xlMedium
    End If
End Sub

'------------------------------------------------------------------------------
' Shapes and buttons
'------------------------------------------------------------------------------
' Scales a shape to fit inside a box (keeping its aspect ratio) and centres it
Public Sub FitShapeInBox(ByVal shp As Shape, ByVal box As Range, Optional ByVal pad As Double = 4)
    Dim r As Double
    shp.LockAspectRatio = msoTrue
    r = (box.Width - 2 * pad) / shp.Width
    If shp.Height * r > box.Height - 2 * pad Then r = (box.Height - 2 * pad) / shp.Height
    shp.Width = shp.Width * r
    shp.Left = box.Left + (box.Width - shp.Width) / 2
    shp.Top = box.Top + (box.Height - shp.Height) / 2
End Sub

' Form-control button. Named "btn_<Macro>" so FixButtonActions can re-point it.
Public Function AddButton(ByVal ws As Worksheet, ByVal macroName As String, ByVal caption As String, _
                          ByVal leftPt As Double, ByVal topPt As Double, _
                          Optional ByVal w As Double = 150, Optional ByVal h As Double = 24) As Object
    Dim b As Object
    Set b = ws.Buttons.Add(leftPt, topPt, w, h)
    b.Caption = caption
    b.OnAction = macroName
    b.Name = "btn_" & macroName
    b.Placement = xlFreeFloating
    b.PrintObject = False
    b.Font.Name = BASE_FONT
    b.Font.Size = 10
    Set AddButton = b
End Function

' Re-points every button at this workbook's macro (safe after File > New / Save As)
Public Sub FixButtonActions()
    Dim ws As Worksheet
    Dim shp As Shape
    For Each ws In ThisWorkbook.Worksheets
        For Each shp In ws.Shapes
            If shp.Name Like "btn_*" Then shp.OnAction = Mid$(shp.Name, 5)
        Next shp
    Next ws
End Sub

'------------------------------------------------------------------------------
' Page setup: every printed sheet is exactly one A4 page
'------------------------------------------------------------------------------
Public Sub SetupPage(ByVal ws As Worksheet, ByVal area As String, ByVal orient As XlPageOrientation)
    With ws.PageSetup
        .PrintArea = area
        On Error Resume Next            ' PaperSize fails if the default printer has no A4
        .PaperSize = xlPaperA4
        On Error GoTo 0
        .Orientation = orient
        .Zoom = False
        .FitToPagesWide = 1
        .FitToPagesTall = 1
        .LeftMargin = Application.InchesToPoints(0.5)
        .RightMargin = Application.InchesToPoints(0.5)
        .TopMargin = Application.InchesToPoints(0.95)   ' clears the header logo
        .BottomMargin = Application.InchesToPoints(0.6)
        .HeaderMargin = Application.InchesToPoints(0.3)
        .FooterMargin = Application.InchesToPoints(0.25)
        .CenterHorizontally = True
        .CenterVertically = False
        .PrintGridlines = False
        .PrintHeadings = False
        .BlackAndWhite = False
        .ScaleWithDocHeaderFooter = False                 ' logo stays the same size on every page
        .AlignMarginsHeaderFooter = True
        .LeftFooter = ""
        .CenterFooter = ""
        .RightFooter = ""
    End With
End Sub

' Rough print scale Excel will use for fit-to-one-page (1 = 100%)
Public Function EstimatedFitScale(ByVal ws As Worksheet) As Double
    Dim area As Range
    Dim pw As Double, ph As Double
    Set area = ws.Range(ws.PageSetup.PrintArea)
    If ws.PageSetup.Orientation = xlPortrait Then
        pw = 595.3: ph = 841.9
    Else
        pw = 841.9: ph = 595.3
    End If
    pw = pw - ws.PageSetup.LeftMargin - ws.PageSetup.RightMargin
    ph = ph - ws.PageSetup.TopMargin - ws.PageSetup.BottomMargin
    EstimatedFitScale = Application.WorksheetFunction.Min(1, pw / area.Width, ph / area.Height)
End Function

'------------------------------------------------------------------------------
' Saving
'------------------------------------------------------------------------------
' Save As dialog limited to .xlsm, suggesting <DocNo>_Rev<Rev>.xlsm
Public Function SaveWorkbookWithDialog() As Boolean
    Dim f As Variant
    Dim suggested As String
    On Error GoTo Fail
    suggested = SafeFileName(FrontValue("WI_DocNo") & "_Rev" & FrontValue("WI_Rev"))
    If suggested = "_Rev" Then suggested = "Work Instruction"
    f = Application.GetSaveAsFilename(InitialFileName:=suggested & ".xlsm", _
            FileFilter:="Excel Macro-Enabled Workbook (*.xlsm),*.xlsm", _
            Title:="Save work instruction (macro-enabled)")
    If VarType(f) = vbBoolean Then Exit Function
    If LCase$(Right$(CStr(f), 5)) <> ".xlsm" Then f = CStr(f) & ".xlsm"
    If Dir(CStr(f)) <> "" Then
        If Not Confirm(CStr(f) & vbCrLf & "already exists. Replace it?", False, vbExclamation) Then Exit Function
    End If
    Application.EnableEvents = False
    Application.DisplayAlerts = False
    ThisWorkbook.SaveAs Filename:=CStr(f), FileFormat:=xlOpenXMLWorkbookMacroEnabled
    Application.DisplayAlerts = True
    Application.EnableEvents = True
    SaveWorkbookWithDialog = True
    Exit Function
Fail:
    Application.DisplayAlerts = True
    Application.EnableEvents = True
    Notify "Could not save the workbook: " & Err.Description, vbExclamation
End Function

Public Function IsMasterTemplateFile() As Boolean
    IsMasterTemplateFile = (LCase$(Right$(ThisWorkbook.Name, 5)) = ".xltm")
End Function
