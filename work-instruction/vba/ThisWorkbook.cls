VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
END
Attribute VB_Name = "ThisWorkbook"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = True
'==============================================================================
' ThisWorkbook
' Workbook events: re-apply protection on open (UserInterfaceOnly does not
' survive closing), keep the Reference Doc list and overflow warnings up to
' date, and stop the workbook being saved without its macros.
'==============================================================================
Option Explicit

Private Sub Workbook_Open()
    On Error Resume Next
    Application.ScreenUpdating = False
    UnprotectAll
    FixButtonActions
    ThisWorkbook.Names(NAME_SHADE).RefersTo = "=TRUE"   ' in case an export was interrupted
    RefreshRefDocList
    ProtectAll
    LockStructure
    Application.ScreenUpdating = True
    On Error GoTo 0
    If IsMasterTemplateFile() Then
        Notify "This is the MASTER template, opened for editing." & vbCrLf & vbCrLf & _
               "To start a new work instruction, close it and use File > New > Personal " & _
               "(or double-click the .xltm in Explorer).", vbExclamation
    End If
End Sub

Private Sub Workbook_BeforeSave(ByVal SaveAsUI As Boolean, Cancel As Boolean)
    If gSilent Then Exit Sub
    If IsMasterTemplateFile() Then
        If Not Confirm("You are about to change the MASTER template that every new work instruction starts from." & _
                       vbCrLf & vbCrLf & "Save the master?", False, vbExclamation) Then Cancel = True
        Exit Sub
    End If
    ' New (never saved) workbooks and Save As: offer .xlsm only, so the macros are kept
    If SaveAsUI Or ThisWorkbook.Path = "" Then
        Cancel = True
        SaveWorkbookWithDialog
    End If
End Sub

Private Sub Workbook_SheetChange(ByVal Sh As Object, ByVal Target As Range)
    On Error GoTo Done
    If Sh.Name = SH_FRONT Then
        If Not Intersect(Target, NameRange("AssocDoc_Rows")) Is Nothing Then RefreshRefDocList
    ElseIf IsOpSheet(Sh) Then
        If Not Intersect(Target, Union(Sh.Range("OpDetails").MergeArea, Sh.Range("OpNotes").MergeArea)) Is Nothing Then
            Application.EnableEvents = False
            UpdateOverflowWarning Sh
        End If
    End If
Done:
    Application.EnableEvents = True
End Sub
