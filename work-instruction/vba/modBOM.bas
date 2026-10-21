Attribute VB_Name = "modBOM"
'==============================================================================
' modBOM
' Builds the BOM sheet from the "Parts required" table of every op sheet.
' Duplicate part numbers are merged (quantities summed); a different
' description or material for the same part number is flagged.
'==============================================================================
Option Explicit

Private Const BOM_HEADER_ROW As Long = 4
Private Const BOM_LAST_COL As String = "E"        ' printed columns A:E
Private Const BOM_NOTE_COL As Long = 7            ' column G: checks (not printed)

' Fields of each merged BOM line (stored as a Variant array)
Private Const P_PARTNO As Long = 0
Private Const P_DESC As Long = 1
Private Const P_MAT As Long = 2
Private Const P_QTY As Long = 3
Private Const P_OPS As Long = 4
Private Const P_QTYMISSING As Long = 5
Private Const P_NOTE As Long = 6

Public Sub RefreshBOM()
    Dim problems As Collection
    On Error GoTo Fail
    BeginWork
    Set problems = BuildBOM()
    EndWork
    BomSheet.Activate
    If problems.Count > 0 Then
        Notify "BOM refreshed. Please check:" & vbCrLf & vbCrLf & JoinLines(problems, 20), vbExclamation
    End If
    Exit Sub
Fail:
    RecoverAfterError
    EndWork
    Notify "Could not refresh the BOM: " & Err.Description, vbExclamation
End Sub

' Rebuilds the BOM table. Returns a list of problems found on the way.
Public Function BuildBOM() As Collection
    Dim problems As New Collection
    Dim parts As Object
    Dim partOrder As New Collection
    Dim ops As Collection
    Dim ws As Worksheet
    Dim bom As Worksheet
    Dim rng As Range
    Dim i As Long, r As Long, outRow As Long, lastRow As Long
    Dim pn As String, key As String, desc As String, mat As String
    Dim qtyV As Variant, p As Variant, k As Variant
    Dim opNo As Long

    Set parts = CreateObject("Scripting.Dictionary")
    Set ops = GetOpSheets()

    For i = 1 To ops.Count
        Set ws = ops(i)
        opNo = OpNumberOf(ws)
        Set rng = ws.Range("OpParts")
        For r = 1 To rng.Rows.Count
            pn = SafeText(rng.Cells(r, 1).Value)
            desc = SafeText(rng.Cells(r, 2).Value)
            mat = SafeText(rng.Cells(r, 3).Value)
            qtyV = rng.Cells(r, 4).Value
            If pn = "" Then
                If desc <> "" Or mat <> "" Or SafeText(qtyV) <> "" Then
                    problems.Add ws.Name & ", parts row " & r & ": no Part No. (left out of the BOM)."
                End If
            Else
                key = UCase$(pn)
                If Not parts.Exists(key) Then
                    parts.Add key, Array(pn, desc, mat, 0#, "", False, "")
                    partOrder.Add key
                End If
                p = parts(key)
                If desc <> "" Then
                    If p(P_DESC) = "" Then
                        p(P_DESC) = desc
                    ElseIf StrComp(p(P_DESC), desc, vbTextCompare) <> 0 Then
                        p(P_NOTE) = AddNote(p(P_NOTE), "description in " & ws.Name & " is '" & desc & "'")
                    End If
                End If
                If mat <> "" Then
                    If p(P_MAT) = "" Then
                        p(P_MAT) = mat
                    ElseIf StrComp(p(P_MAT), mat, vbTextCompare) <> 0 Then
                        p(P_NOTE) = AddNote(p(P_NOTE), "material in " & ws.Name & " is '" & mat & "'")
                    End If
                End If
                If SafeText(qtyV) <> "" And IsNumeric(qtyV) Then
                    p(P_QTY) = p(P_QTY) + CDbl(qtyV)
                Else
                    p(P_QTYMISSING) = True
                    problems.Add ws.Name & ", part " & pn & ": no Qty."
                End If
                If InStr(", " & p(P_OPS) & ",", ", " & opNo & ",") = 0 Then
                    If p(P_OPS) = "" Then
                        p(P_OPS) = CStr(opNo)
                    Else
                        p(P_OPS) = p(P_OPS) & ", " & opNo
                    End If
                End If
                parts(key) = p
            End If
        Next r
    Next i

    ' ---- write the table ----
    Set bom = BomSheet()
    UnprotectSheet bom
    lastRow = bom.Cells(bom.Rows.Count, 1).End(xlUp).Row
    If bom.Cells(bom.Rows.Count, BOM_NOTE_COL).End(xlUp).Row > lastRow Then
        lastRow = bom.Cells(bom.Rows.Count, BOM_NOTE_COL).End(xlUp).Row
    End If
    If lastRow > BOM_HEADER_ROW Then
        bom.Range(bom.Cells(BOM_HEADER_ROW + 1, 1), bom.Cells(lastRow + 1, BOM_NOTE_COL)).UnMerge
        bom.Range(bom.Cells(BOM_HEADER_ROW + 1, 1), bom.Cells(lastRow + 1, BOM_NOTE_COL)).Clear
    End If

    outRow = BOM_HEADER_ROW + 1
    If partOrder.Count = 0 Then
        bom.Range("A" & outRow & ":" & BOM_LAST_COL & outRow).Merge
        bom.Cells(outRow, 1).Value = "No parts listed on the operation sheets yet."
        bom.Cells(outRow, 1).HorizontalAlignment = xlCenter
        outRow = outRow + 1
    Else
        bom.Range("A" & outRow & ":" & BOM_LAST_COL & (outRow + partOrder.Count - 1)).NumberFormat = "@"
        For Each k In partOrder
            p = parts(k)
            bom.Cells(outRow, 1).Value = p(P_PARTNO)
            bom.Cells(outRow, 2).Value = p(P_DESC)
            bom.Cells(outRow, 3).Value = p(P_MAT)
            If p(P_QTYMISSING) And p(P_QTY) = 0 Then
                bom.Cells(outRow, 4).Value = "?"
            Else
                bom.Cells(outRow, 4).NumberFormat = "General"
                bom.Cells(outRow, 4).Value = p(P_QTY)
            End If
            bom.Cells(outRow, 5).Value = p(P_OPS)
            If p(P_NOTE) <> "" Then
                bom.Cells(outRow, BOM_NOTE_COL).Value = "CHECK: " & p(P_NOTE)
                problems.Add "BOM: part " & p(P_PARTNO) & " - " & p(P_NOTE) & _
                             " (BOM uses '" & p(P_DESC) & "' / '" & p(P_MAT) & "')."
            End If
            outRow = outRow + 1
        Next k
    End If

    ' ---- format ----
    With bom.Range("A" & (BOM_HEADER_ROW + 1) & ":" & BOM_LAST_COL & (outRow - 1))
        .Font.Name = BASE_FONT
        .Font.Size = 10
        .VerticalAlignment = xlCenter
        .WrapText = True
        .Locked = True
        .Rows.RowHeight = 17
    End With
    bom.Range("A" & (BOM_HEADER_ROW + 1) & ":C" & (outRow - 1)).HorizontalAlignment = xlLeft
    bom.Range("D" & (BOM_HEADER_ROW + 1) & ":E" & (outRow - 1)).HorizontalAlignment = xlCenter
    bom.Range("A" & (BOM_HEADER_ROW + 1) & ":" & BOM_LAST_COL & (outRow - 1)).Rows.AutoFit
    ApplyGrid bom.Range("A" & BOM_HEADER_ROW & ":" & BOM_LAST_COL & (outRow - 1)), 1
    With bom.Range(bom.Cells(BOM_HEADER_ROW + 1, BOM_NOTE_COL), bom.Cells(outRow - 1, BOM_NOTE_COL))
        .Font.Name = BASE_FONT
        .Font.Size = 9
        .Font.Color = vbRed
        .WrapText = False
    End With
    bom.PageSetup.PrintArea = "$A$1:$" & BOM_LAST_COL & "$" & (outRow - 1)
    ProtectSheet bom
    Set BuildBOM = problems
End Function

Private Function AddNote(ByVal existing As String, ByVal note As String) As String
    If existing = "" Then
        AddNote = note
    Else
        AddNote = existing & "; " & note
    End If
End Function

' Joins collection items into lines, showing at most maxLines
Public Function JoinLines(ByVal items As Collection, ByVal maxLines As Long) As String
    Dim i As Long
    Dim s As String
    For i = 1 To items.Count
        If i > maxLines Then
            s = s & "... and " & (items.Count - maxLines) & " more." & vbCrLf
            Exit For
        End If
        s = s & "- " & items(i) & vbCrLf
    Next i
    JoinLines = s
End Function
