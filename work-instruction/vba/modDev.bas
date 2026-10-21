Attribute VB_Name = "modDev"
'==============================================================================
' modDev
' Maintenance helper: export all VBA modules as text files for version control.
' Needs File > Options > Trust Center > Macro Settings >
' "Trust access to the VBA project object model".
'==============================================================================
Option Explicit

Public Sub ExportAllModules()
    Dim comp As Object
    Dim folder As String
    Dim ext As String
    Dim n As Long
    On Error GoTo Fail
    If ThisWorkbook.Path = "" Then
        Notify "Save the workbook first."
        Exit Sub
    End If
    folder = ThisWorkbook.Path & "\vba"
    If Dir(folder, vbDirectory) = "" Then MkDir folder
    For Each comp In ThisWorkbook.VBProject.VBComponents
        ext = ""
        Select Case comp.Type
        Case 1: ext = ".bas"                    ' standard module
        Case 2: ext = ".cls"                    ' class module
        Case 3: ext = ".frm"                    ' user form
        Case 100                                ' sheet / workbook module: only if it has code
            If comp.CodeModule.CountOfLines > 0 Then ext = ".cls"
        End Select
        If ext <> "" Then
            comp.Export folder & "\" & comp.Name & ext
            n = n + 1
        End If
    Next comp
    Notify n & " modules exported to " & folder
    Exit Sub
Fail:
    Notify "Export failed: " & Err.Description & vbCrLf & vbCrLf & _
           "Enable 'Trust access to the VBA project object model' in the Trust Center.", vbExclamation
End Sub
