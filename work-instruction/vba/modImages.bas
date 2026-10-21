Attribute VB_Name = "modImages"
'==============================================================================
' modImages
' Operation image: insert from file or paste from the clipboard, scale to fit
' the image box keeping the aspect ratio, centre it, replace any old image.
' Large pictures are downscaled first so the workbook and PDF stay small.
'==============================================================================
Option Explicit

Private Const IMAGE_NAME As String = "OpImage"
Private Const MAX_IMAGE_PX As Long = 1600     ' longest side after downscaling

' WIA (Windows Image Acquisition) format IDs
Private Const WIA_FORMAT_JPEG As String = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}"
Private Const WIA_FORMAT_PNG As String = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}"

'------------------------------------------------------------------------------
' Buttons
'------------------------------------------------------------------------------
Public Sub InsertImage()
    Dim ws As Worksheet
    Dim f As Variant
    On Error GoTo Fail
    If Not IsOpSheet(ActiveSheet) Then
        Notify "Go to an operation sheet first."
        Exit Sub
    End If
    Set ws = ActiveSheet
    f = Application.GetOpenFilename( _
            "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff),*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", _
            , "Choose the image for " & ws.Name)
    If VarType(f) = vbBoolean Then Exit Sub
    BeginWork
    InsertImageFile ws, CStr(f)
    EndWork
    Exit Sub
Fail:
    RecoverAfterError
    EndWork
    Notify "Could not insert the image: " & Err.Description, vbExclamation
End Sub

Public Sub PasteImage()
    On Error GoTo Fail
    If Not IsOpSheet(ActiveSheet) Then
        Notify "Go to an operation sheet first."
        Exit Sub
    End If
    If Not ClipboardHasPicture() Then
        Notify "There is no picture on the clipboard." & vbCrLf & vbCrLf & _
               "Take a screenshot with Win+Shift+S (or copy an image), then press Paste Image again.", vbExclamation
        Exit Sub
    End If
    Application.EnableEvents = False
    If Not PasteImageFromClipboard(ActiveSheet) Then
        Notify "The clipboard content could not be pasted as a picture.", vbExclamation
    End If
    Application.EnableEvents = True
    Exit Sub
Fail:
    RecoverAfterError
    EndWork
    Notify "Could not paste the image: " & Err.Description, vbExclamation
End Sub

Public Sub DeleteImage()
    On Error GoTo Fail
    If Not IsOpSheet(ActiveSheet) Then Exit Sub
    If Not HasOpImage(ActiveSheet) Then Exit Sub
    If Not Confirm("Remove the image from " & ActiveSheet.Name & "?", False) Then Exit Sub
    UnprotectSheet ActiveSheet
    RemoveOpImages ActiveSheet, ""
    ProtectSheet ActiveSheet
    Exit Sub
Fail:
    RecoverAfterError
    Notify "Could not remove the image: " & Err.Description, vbExclamation
End Sub

'------------------------------------------------------------------------------
' Core routines (also used by the self-test)
'------------------------------------------------------------------------------
Public Sub InsertImageFile(ByVal ws As Worksheet, ByVal filePath As String)
    Dim src As String
    Dim shp As Shape
    Dim box As Range
    src = DownscaledCopy(filePath)
    Set box = ws.Range("OpImageBox")
    UnprotectSheet ws
    RemoveOpImages ws, ""
    Set shp = ws.Shapes.AddPicture(Filename:=src, LinkToFile:=msoFalse, SaveWithDocument:=msoTrue, _
                                   Left:=box.Left, Top:=box.Top, Width:=-1, Height:=-1)
    PlaceOpImage shp, box
    ProtectSheet ws
    If src <> filePath Then
        On Error Resume Next
        Kill src
        On Error GoTo 0
    End If
End Sub

Public Function PasteImageFromClipboard(ByVal ws As Worksheet) As Boolean
    Dim before As Long
    Dim shp As Shape
    Dim box As Range
    Set box = ws.Range("OpImageBox")
    UnprotectSheet ws
    ws.Activate
    box.Cells(1, 1).Select
    before = ws.Shapes.Count
    ' JPEG keeps screenshots/photos small; fall back to PNG, then a plain paste
    On Error Resume Next
    ws.PasteSpecial Format:="Picture (JPEG)", Link:=False, DisplayAsIcon:=False
    If ws.Shapes.Count = before Then ws.PasteSpecial Format:="Picture (PNG)", Link:=False, DisplayAsIcon:=False
    If ws.Shapes.Count = before Then ws.Paste
    On Error GoTo 0
    If ws.Shapes.Count > before Then
        Set shp = ws.Shapes(ws.Shapes.Count)
        If IsPictureShape(shp) Then
            shp.Name = IMAGE_NAME & "_new"
            RemoveOpImages ws, shp.Name
            PlaceOpImage shp, box
            PasteImageFromClipboard = True
        Else
            shp.Delete                   ' pasted something that is not a picture
        End If
    End If
    ws.Range("OpDetails").Select
    ProtectSheet ws
End Function

Private Sub PlaceOpImage(ByVal shp As Shape, ByVal box As Range)
    FitShapeInBox shp, box, 4
    shp.Placement = xlMoveAndSize
    shp.Name = IMAGE_NAME
    shp.AlternativeText = "Operation image"
End Sub

' Deletes pictures sitting in the image box (arrows / callouts are kept)
Public Sub RemoveOpImages(ByVal ws As Worksheet, ByVal keepName As String)
    Dim i As Long
    Dim shp As Shape
    Dim box As Range
    Set box = ws.Range("OpImageBox")
    For i = ws.Shapes.Count To 1 Step -1
        Set shp = ws.Shapes(i)
        If IsPictureShape(shp) And shp.Name <> keepName Then
            If Not Intersect(shp.TopLeftCell, box) Is Nothing Then shp.Delete
        End If
    Next i
End Sub

Public Function HasOpImage(ByVal ws As Worksheet) As Boolean
    Dim shp As Shape
    Dim box As Range
    Set box = ws.Range("OpImageBox")
    For Each shp In ws.Shapes
        If IsPictureShape(shp) Then
            If Not Intersect(shp.TopLeftCell, box) Is Nothing Then
                HasOpImage = True
                Exit Function
            End If
        End If
    Next shp
End Function

Public Function IsPictureShape(ByVal shp As Shape) As Boolean
    IsPictureShape = (shp.Type = msoPicture Or shp.Type = msoLinkedPicture)
End Function

Private Function ClipboardHasPicture() As Boolean
    Dim f As Variant
    On Error Resume Next
    For Each f In Application.ClipboardFormats
        If f = xlClipboardFormatBitmap Or f = xlClipboardFormatPICT Or f = xlClipboardFormatScreenPICT Then
            ClipboardHasPicture = True
        End If
    Next f
End Function

' Returns a downscaled temporary copy of a large image, or the original path.
' Uses Windows Image Acquisition (built into Windows); falls back to the original.
Private Function DownscaledCopy(ByVal filePath As String) As String
    Dim img As Object
    Dim ip As Object
    Dim ext As String
    Dim outPath As String
    Dim needScale As Boolean, needConvert As Boolean, keepPng As Boolean
    DownscaledCopy = filePath
    On Error GoTo Fallback
    ext = LCase$(Mid$(filePath, InStrRev(filePath, ".") + 1))
    keepPng = (ext = "png" Or ext = "gif")          ' keep transparency / sharp screenshots
    Set img = CreateObject("WIA.ImageFile")
    img.LoadFile filePath
    needScale = (img.Width > MAX_IMAGE_PX Or img.Height > MAX_IMAGE_PX)
    needConvert = (ext = "bmp" Or ext = "tif" Or ext = "tiff")
    If Not needScale And Not needConvert Then Exit Function

    Set ip = CreateObject("WIA.ImageProcess")
    If needScale Then
        ip.Filters.Add ip.FilterInfos("Scale").FilterID
        ip.Filters(ip.Filters.Count).Properties("MaximumWidth").Value = MAX_IMAGE_PX
        ip.Filters(ip.Filters.Count).Properties("MaximumHeight").Value = MAX_IMAGE_PX
        ip.Filters(ip.Filters.Count).Properties("PreserveAspectRatio").Value = True
    End If
    ip.Filters.Add ip.FilterInfos("Convert").FilterID
    If keepPng Then
        ip.Filters(ip.Filters.Count).Properties("FormatID").Value = WIA_FORMAT_PNG
        ext = "png"
    Else
        ip.Filters(ip.Filters.Count).Properties("FormatID").Value = WIA_FORMAT_JPEG
        ip.Filters(ip.Filters.Count).Properties("Quality").Value = 85
        ext = "jpg"
    End If
    Set img = ip.Apply(img)
    outPath = Environ$("TEMP") & "\wi_image_" & Format$(Now, "yyyymmdd_hhnnss") & "." & ext
    If Dir(outPath) <> "" Then Kill outPath
    img.SaveFile outPath
    DownscaledCopy = outPath
    Exit Function
Fallback:
    DownscaledCopy = filePath
End Function
