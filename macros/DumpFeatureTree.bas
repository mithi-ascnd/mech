' TrueMirror M0 - feature tree dump, VBA edition
'
' Why this exists: myDesktop (RMIT's Citrix VDI) will run SOLIDWORKS but will not let you
' register a COM add-in - regasm needs HKLM write access you do not have, and a
' non-persistent VDI image wipes anything you install at logout. VBA macros need no
' installation and no admin rights, so M0's research can be done there anyway.
'
' Classification is kept deliberately identical to src/TrueMirror.Core/FeatureSupport.cs.
' The point of M0 is the UNCLASSIFIED list at the bottom of the report: every type name
' that lands there is one the C# map does not know about yet.
'
' To run:  Tools > Macro > New...  (save the .swp to your H: drive), paste this in,
'          open a part, press F5.
' Output:  <partname>-featuretree.txt, written next to the part file.

Option Explicit

Private Const MAX_DEPTH As Long = 8
Private Const swDocPART As Long = 1

Private swApp As Object
Private swModel As Object

Private gTotal As Long
Private gCounted As Long
Private gByLevel(0 To 5) As Long
Private gUnknown As Object
Private gOut As String

Sub main()

    Set swApp = Application.SldWorks
    Set swModel = swApp.ActiveDoc

    If swModel Is Nothing Then
        MsgBox "Open a part first.", vbExclamation, "TrueMirror M0"
        Exit Sub
    End If

    If swModel.GetType <> swDocPART Then
        MsgBox "Active document is not a part.", vbExclamation, "TrueMirror M0"
        Exit Sub
    End If

    Set gUnknown = CreateObject("Scripting.Dictionary")
    gUnknown.CompareMode = 1   ' vbTextCompare

    Dim i As Long
    gTotal = 0
    gCounted = 0
    For i = 0 To 5
        gByLevel(i) = 0
    Next i
    gOut = ""

    AppendLine "TrueMirror M0 - feature tree dump"
    AppendLine "Part       : " & swModel.GetTitle
    AppendLine "Path       : " & swModel.GetPathName
    AppendLine "SOLIDWORKS : " & swApp.RevisionNumber
    AppendLine "Generated  : " & Format(Now, "yyyy-mm-dd hh:nn:ss")
    AppendLine String(78, "-")
    AppendLine ""
    AppendLine PadRight("FEATURE", 34) & PadRight("TYPENAME", 24) & "PHASE"
    AppendLine String(78, "-")

    WalkChain swModel.FirstFeature, 0, False

    WriteSummary
    WriteReport

End Sub

' Top-level features chain with GetNextFeature; sub-features with GetNextSubFeature.
' Same walk otherwise, so one routine with a flag rather than two near-copies.
Private Sub WalkChain(ByVal swFeat As Object, ByVal depth As Long, ByVal isSub As Boolean)

    Dim typeName As String
    Dim featName As String
    Dim lvl As Long
    Dim marker As String
    Dim swSub As Object
    Dim swNext As Object

    Do While Not swFeat Is Nothing

        typeName = swFeat.GetTypeName2
        featName = swFeat.Name
        lvl = ClassifyLevel(typeName)

        gTotal = gTotal + 1
        gByLevel(lvl) = gByLevel(lvl) + 1
        If lvl <> 4 Then gCounted = gCounted + 1

        If lvl = 5 Then
            If gUnknown.Exists(typeName) Then
                gUnknown(typeName) = gUnknown(typeName) + 1
            Else
                gUnknown.Add typeName, 1
            End If
        End If

        marker = ""
        On Error Resume Next
        If swFeat.IsSuppressed Then marker = "   [SUPPRESSED]"
        On Error GoTo 0

        AppendLine Space(depth * 2) & _
                   PadRight(featName, 34 - depth * 2) & _
                   PadRight(typeName, 24) & _
                   LevelName(lvl) & marker

        If depth < MAX_DEPTH Then
            Set swSub = Nothing
            On Error Resume Next
            Set swSub = swFeat.GetFirstSubFeature
            On Error GoTo 0
            If Not swSub Is Nothing Then WalkChain swSub, depth + 1, True
        End If

        Set swNext = Nothing
        On Error Resume Next
        If isSub Then
            Set swNext = swFeat.GetNextSubFeature
        Else
            Set swNext = swFeat.GetNextFeature
        End If
        On Error GoTo 0
        Set swFeat = swNext

    Loop

End Sub

' Mirrors FeatureSupport.Classify in src/TrueMirror.Core/FeatureSupport.cs.
' Keep the two in step - if you add an entry here, add it there.
Private Function ClassifyLevel(ByVal t As String) As Long

    Select Case LCase$(t)

        ' v0.1 - sketch-based, no topological references
        Case "profilefeature", "extrusion", "cut", "revolution"
            ClassifyLevel = 0

        ' v0.2 - needs the geometric entity resolver
        Case "fillet", "chamfer"
            ClassifyLevel = 1

        ' v0.3 - breadth
        Case "lpattern", "cirpattern", "mirrorpattern", "shell", "draft", "holewzd"
            ClassifyLevel = 2

        ' explicitly out of scope for v1
        Case "sweep", "loft", "sweepcut", "loftcut", "surfacecut"
            ClassifyLevel = 3

        ' present in every tree, nothing to transcribe
        Case "refplane", "originprofilefeature", "coordsys", "refaxis", _
             "materialfolder", "historyfolder", "sensorfolder", "detailcabinet", _
             "solidbodyfolder", "surfacebodyfolder", "envfolder", "lightsfolder", _
             "favoritefolder", "eqnfolder", "annotations", "comments"
            ClassifyLevel = 4

        Case Else
            ClassifyLevel = 5

    End Select

End Function

Private Sub WriteSummary()

    AppendLine ""
    AppendLine String(78, "-")
    AppendLine "COVERAGE  (trivial nodes excluded from the denominator)"
    AppendLine ""
    AppendLine "  nodes walked : " & gTotal
    AppendLine "  counted      : " & gCounted
    AppendLine ""
    AppendLine "  v0.1         : " & PadRight(CStr(gByLevel(0)), 6) & Pct(gByLevel(0), gCounted)
    AppendLine "  v0.2         : " & PadRight(CStr(gByLevel(1)), 6) & Pct(gByLevel(1), gCounted)
    AppendLine "  v0.3         : " & PadRight(CStr(gByLevel(2)), 6) & Pct(gByLevel(2), gCounted)
    AppendLine "  out-of-scope : " & PadRight(CStr(gByLevel(3)), 6) & Pct(gByLevel(3), gCounted)
    AppendLine "  UNKNOWN      : " & PadRight(CStr(gByLevel(5)), 6) & Pct(gByLevel(5), gCounted)
    AppendLine "  (trivial)    : " & gByLevel(4)

    If gUnknown.Count > 0 Then
        AppendLine ""
        AppendLine String(78, "-")
        AppendLine "UNCLASSIFIED TYPE NAMES - this is the M0 deliverable."
        AppendLine "Add each of these to FeatureSupport.cs with a deliberate phase."
        AppendLine ""
        Dim k
        For Each k In gUnknown.Keys
            AppendLine "  " & PadRight(CStr(k), 32) & "x" & gUnknown(k)
        Next k
    End If

End Sub

Private Sub WriteReport()

    Dim outPath As String
    Dim f As Integer

    outPath = ReportPath(swModel)

    On Error GoTo WriteFailed
    f = FreeFile
    Open outPath For Output As #f
    Print #f, gOut
    Close #f
    On Error GoTo 0

    MsgBox "Wrote " & gTotal & " nodes (" & gUnknown.Count & " unclassified types)." _
           & vbCrLf & vbCrLf & outPath, vbInformation, "TrueMirror M0"
    Exit Sub

WriteFailed:
    ' On myDesktop the part may sit on a read-only share. Fall back to the H: drive.
    MsgBox "Could not write to:" & vbCrLf & outPath & vbCrLf & vbCrLf & _
           "Report follows in the Immediate window (Ctrl+G) instead.", _
           vbExclamation, "TrueMirror M0"
    Debug.Print gOut

End Sub

Private Function ReportPath(ByVal m As Object) As String

    Dim p As String
    Dim dotPos As Long

    p = m.GetPathName

    If Len(p) = 0 Then
        ReportPath = Environ$("USERPROFILE") & "\TrueMirror-featuretree.txt"
        Exit Function
    End If

    dotPos = InStrRev(p, ".")
    If dotPos > 0 Then p = Left$(p, dotPos - 1)
    ReportPath = p & "-featuretree.txt"

End Function

Private Function LevelName(ByVal lvl As Long) As String
    Select Case lvl
        Case 0: LevelName = "v0.1"
        Case 1: LevelName = "v0.2"
        Case 2: LevelName = "v0.3"
        Case 3: LevelName = "out-of-scope"
        Case 4: LevelName = "trivial"
        Case Else: LevelName = "UNKNOWN"
    End Select
End Function

Private Function Pct(ByVal n As Long, ByVal d As Long) As String
    If d = 0 Then
        Pct = ""
    Else
        Pct = "(" & Format(n / d * 100, "0.0") & "%)"
    End If
End Function

Private Function PadRight(ByVal s As String, ByVal n As Long) As String
    If Len(s) >= n Then
        PadRight = s & " "
    Else
        PadRight = s & Space(n - Len(s))
    End If
End Function

Private Sub AppendLine(ByVal s As String)
    gOut = gOut & s & vbCrLf
End Sub
