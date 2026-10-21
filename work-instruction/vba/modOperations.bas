Attribute VB_Name = "modOperations"
'==============================================================================
' modOperations
' Add / insert / delete / renumber operation sheets. Each op sheet is a copy of
' the hidden "Op Template" sheet, named "Op 10", "Op 20", ... and kept in
' op-number order between the BOM and the hidden sheets.
'==============================================================================
Option Explicit

'------------------------------------------------------------------------------
' Buttons
'------------------------------------------------------------------------------
' Front Page: append the next operation (last op + 10)
Public Sub AddOperation()
    Dim ws As Worksheet
    On Error GoTo Fail
    BeginWork
    Set ws = CreateOpSheet(NextOpNumber())
    EndWork
    ws.Activate
    ws.Range("OpDept").Select
    Exit Sub
Fail:
    RecoverAfterError
    EndWork
    Notify "Could not add the operation: " & Err.Description, vbExclamation
End Sub

' Op sheet: insert a new operation straight after this one (e.g. Op 15)
Public Sub InsertOperationAfter()
    Dim ws As Worksheet
    On Error GoTo Fail
    If Not IsOpSheet(ActiveSheet) Then
        Notify "Go to the operation you want to insert after, then press this button."
        Exit Sub
    End If
    BeginWork
    Set ws = InsertOpAfterSheet(ActiveSheet)
    EndWork
    ws.Activate
    ws.Range("OpDept").Select
    Notify "Added " & ws.Name & ". Use Renumber Operations (Front Page) to re-sequence to 10, 20, 30... when you are ready."
    Exit Sub
Fail:
    RecoverAfterError
    EndWork
    Notify "Could not insert the operation: " & Err.Description, vbExclamation
End Sub

' Deletes the active op sheet, or asks which one when run from the Front Page
Public Sub DeleteOperation()
    Dim ws As Worksheet
    Dim s As String
    On Error GoTo Fail
    If IsOpSheet(ActiveSheet) Then
        Set ws = ActiveSheet
    Else
        If GetOpSheets().Count = 0 Then
            Notify "There are no operations to delete."
            Exit Sub
        End If
        s = InputBox("Which operation number do you want to delete? (e.g. 20)", "Delete Operation")
        If Trim$(s) = "" Then Exit Sub
        If Not SheetExists(OP_PREFIX & CLng(Val(s))) Then
            Notify "There is no sheet called '" & OP_PREFIX & CLng(Val(s)) & "'.", vbExclamation
            Exit Sub
        End If
        Set ws = ThisWorkbook.Worksheets(OP_PREFIX & CLng(Val(s)))
    End If
    If Not Confirm("Delete " & ws.Name & " and everything on it?" & vbCrLf & "This cannot be undone.", False, vbExclamation) Then Exit Sub

    BeginWork
    DeleteOpSheet ws
    If GetOpSheets().Count > 0 Then
        Application.ScreenUpdating = True
        If Confirm("Renumber the remaining operations to 10, 20, 30...?", False) Then
            Application.ScreenUpdating = False
            RenumberOpsCore
        End If
    End If
    EndWork
    FrontSheet.Activate
    Exit Sub
Fail:
    RecoverAfterError
    EndWork
    Notify "Could not delete the operation: " & Err.Description, vbExclamation
End Sub

Public Sub RenumberOperations()
    On Error GoTo Fail
    If GetOpSheets().Count = 0 Then
        Notify "There are no operations to renumber."
        Exit Sub
    End If
    BeginWork
    RenumberOpsCore
    EndWork
    Notify "Operations renumbered: " & OpListText()
    Exit Sub
Fail:
    RecoverAfterError
    EndWork
    Notify "Could not renumber the operations: " & Err.Description, vbExclamation
End Sub

'------------------------------------------------------------------------------
' Core routines (also used by the self-test)
'------------------------------------------------------------------------------
Public Function NextOpNumber() As Long
    Dim ops As Collection
    Dim n As Long
    Set ops = GetOpSheets()
    If ops.Count = 0 Then
        NextOpNumber = OP_STEP
    Else
        n = OpNumberOf(ops(ops.Count)) + OP_STEP
        NextOpNumber = n - (n Mod OP_STEP)       ' next multiple of 10 above the last op
    End If
End Function

' Copies the hidden template to a new sheet "Op <opNo>" in the right tab position
Public Function CreateOpSheet(ByVal opNo As Long) As Worksheet
    Dim wsT As Worksheet
    Dim ws As Worksheet
    Dim afterSheet As Worksheet
    Dim ops As Collection
    Dim i As Long
    If SheetExists(OP_PREFIX & opNo) Then Err.Raise vbObjectError + 513, , OP_PREFIX & opNo & " already exists."
    Set wsT = TemplateSheet()
    Set ops = GetOpSheets()
    Set afterSheet = BomSheet()
    For i = 1 To ops.Count
        If OpNumberOf(ops(i)) < opNo Then Set afterSheet = ops(i)
    Next i

    UnlockStructure
    wsT.Visible = xlSheetVisible
    wsT.Copy After:=afterSheet
    Set ws = ThisWorkbook.Sheets(afterSheet.Index + 1)
    wsT.Visible = xlSheetHidden
    ws.Name = OP_PREFIX & opNo
    UnprotectSheet ws
    ws.Range("OpNo").Value = opNo
    ProtectSheet ws
    LockStructure
    Set CreateOpSheet = ws
End Function

' New op half-way between this op and the next one (renumbers first if no gap)
Public Function InsertOpAfterSheet(ByVal ws As Worksheet) As Worksheet
    Dim ops As Collection
    Dim i As Long
    Dim cur As Long, nxt As Long, n As Long
    cur = OpNumberOf(ws)
    Set ops = GetOpSheets()
    nxt = 0
    For i = 1 To ops.Count
        If OpNumberOf(ops(i)) > cur Then
            nxt = OpNumberOf(ops(i))
            Exit For
        End If
    Next i
    If nxt = 0 Then
        n = cur + OP_STEP - ((cur + OP_STEP) Mod OP_STEP)
    Else
        n = cur + (nxt - cur) \ 2
        If n = cur Then
            RenumberOpsCore
            Set InsertOpAfterSheet = InsertOpAfterSheet(ws)
            Exit Function
        End If
    End If
    Set InsertOpAfterSheet = CreateOpSheet(n)
End Function

Public Sub DeleteOpSheet(ByVal ws As Worksheet)
    UnlockStructure
    Application.DisplayAlerts = False
    ws.Delete
    Application.DisplayAlerts = True
    LockStructure
End Sub

' Re-sequence to 10, 20, 30... keeping the current order
Public Sub RenumberOpsCore()
    Dim ops As Collection
    Dim ws As Worksheet
    Dim i As Long
    Set ops = GetOpSheets()
    UnlockStructure
    ' Pass 1: temporary names so the new names never clash with existing ones
    For i = 1 To ops.Count
        Set ws = ops(i)
        ws.Name = "~renumber" & i
    Next i
    ' Pass 2: final names, op numbers and tab order
    For i = 1 To ops.Count
        Set ws = ops(i)
        ws.Name = OP_PREFIX & (i * OP_STEP)
        UnprotectSheet ws
        ws.Range("OpNo").Value = i * OP_STEP
        ProtectSheet ws
        If i = 1 Then
            ws.Move After:=BomSheet()
        Else
            ws.Move After:=ops(i - 1)
        End If
    Next i
    LockStructure
End Sub

Public Function OpListText() As String
    Dim ops As Collection
    Dim i As Long
    Dim s As String
    Set ops = GetOpSheets()
    For i = 1 To ops.Count
        If i > 1 Then s = s & ", "
        s = s & ops(i).Name
    Next i
    OpListText = s
End Function
