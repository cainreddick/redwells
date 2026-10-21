#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the macro-enabled Work Instruction template with Excel itself, then
    runs a self-test that produces a sample work instruction and its PDF.

.DESCRIPTION
    1. Opens source\Work_Instruction_Template.xlsx in Excel (COM, read-only).
       Nothing is round-tripped through other libraries, so the header logo,
       PPE icons, fonts and borders survive.
    2. Imports the VBA modules from .\vba and runs modBuild.BuildTemplate, which
       makes every layout change through Excel.
    3. Saves dist\Work_Instruction_Template.xlsm and dist\Work_Instruction_Template.xltm
       (the build-only module is removed first).
    4. Unless -SkipTest: copies the .xlsm, imports modSelfTest, fills in a sample
       WI with 3 operations, checks everything, and writes
       dist\sample\WI-0421_RevB.xlsm, dist\sample\WI-0421_RevB.pdf and
       dist\sample\SelfTest_Report.txt.

    Excel's "Trust access to the VBA project object model" setting is switched on
    for the duration of the build (HKCU registry) and put back afterwards.

.PARAMETER Source
    The original template (default: .\source\Work_Instruction_Template.xlsx).
.PARAMETER OutDir
    Output folder (default: .\dist).
.PARAMETER SkipTest
    Build the template only.
.PARAMETER Hidden
    Keep the Excel window hidden while building (default: visible, so any
    unexpected Excel dialog can be seen and closed).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Build-WorkInstruction.ps1
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot 'source\Work_Instruction_Template.xlsx'),
    [string]$OutDir = (Join-Path $PSScriptRoot 'dist'),
    [switch]$SkipTest,
    [switch]$Hidden
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$VbaDir = Join-Path $PSScriptRoot 'vba'
$RuntimeModules = @('modCommon', 'modFrontPage', 'modOperations', 'modImages', 'modBOM', 'modExport', 'modDev')

$xlOpenXMLWorkbookMacroEnabled = 52
$xlOpenXMLTemplateMacroEnabled = 53
$msoAutomationSecurityLow = 1

$script:VbomBackup = @{}

function Write-Step([string]$Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

#------------------------------------------------------------------------------
# "Trust access to the VBA project object model" (needed to import modules)
#------------------------------------------------------------------------------
function Enable-VbomAccess {
    $officeKey = 'HKCU:\Software\Microsoft\Office'
    $keys = @(Get-ChildItem $officeKey -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -match '^\d+\.\d+$' -and (Test-Path (Join-Path $_.PSPath 'Excel')) } |
        ForEach-Object { Join-Path $_.PSPath 'Excel\Security' })
    if ($keys.Count -eq 0) { $keys = @("$officeKey\16.0\Excel\Security") }
    foreach ($k in $keys) {
        if (-not (Test-Path $k)) { New-Item -Path $k -Force | Out-Null }
        $old = Get-ItemProperty -Path $k -Name AccessVBOM -ErrorAction SilentlyContinue
        if ($old) { $script:VbomBackup[$k] = $old.AccessVBOM } else { $script:VbomBackup[$k] = $null }
        Set-ItemProperty -Path $k -Name AccessVBOM -Value 1 -Type DWord
    }
    $policy = Get-ChildItem 'HKCU:\Software\Policies\Microsoft\Office' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ItemProperty -Path (Join-Path $_.PSPath 'Excel\Security') -Name AccessVBOM -ErrorAction SilentlyContinue } |
        Where-Object { $_ -and $_.AccessVBOM -eq 0 }
    if ($policy) {
        Write-Warning 'A group policy blocks access to the VBA project object model - the module import will probably fail. Ask IT, or run the build on a PC without that policy.'
    }
}

function Restore-VbomAccess {
    foreach ($k in @($script:VbomBackup.Keys)) {
        $v = $script:VbomBackup[$k]
        if ($null -eq $v) {
            Remove-ItemProperty -Path $k -Name AccessVBOM -ErrorAction SilentlyContinue
        } else {
            Set-ItemProperty -Path $k -Name AccessVBOM -Value $v -Type DWord
        }
    }
}

#------------------------------------------------------------------------------
# VBA import helpers (files are normalised to CRLF / ASCII, as the VBE expects)
#------------------------------------------------------------------------------
function Import-VbaModule($Workbook, [string]$Path) {
    if (-not (Test-Path $Path)) { throw "VBA module not found: $Path" }
    $text = [IO.File]::ReadAllText($Path)
    $text = ($text -replace "`r`n", "`n") -replace "`n", "`r`n"
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetFileName($Path))
    [IO.File]::WriteAllText($tmp, $text, [Text.Encoding]::ASCII)
    $null = $Workbook.VBProject.VBComponents.Import($tmp)
    Remove-Item $tmp -Force
}

function Set-ThisWorkbookCode($Workbook, [string]$Path) {
    $lines = [IO.File]::ReadAllLines($Path)
    $start = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^Attribute VB_') { $start = $i + 1 }
    }
    $body = ($lines[$start..($lines.Count - 1)]) -join "`r`n"
    $codeName = $Workbook.CodeName
    if (-not $codeName) { $codeName = 'ThisWorkbook' }
    $module = $Workbook.VBProject.VBComponents.Item($codeName).CodeModule
    if ($module.CountOfLines -gt 0) { $module.DeleteLines(1, $module.CountOfLines) }
    $module.AddFromString($body)
}

function Remove-VbaModule($Workbook, [string]$Name) {
    $components = $Workbook.VBProject.VBComponents
    $components.Remove($components.Item($Name))
}

function Invoke-Macro($Excel, $Workbook, [string]$Macro, [object[]]$Arguments = @()) {
    $full = "'" + $Workbook.Name.Replace("'", "''") + "'!" + $Macro
    switch ($Arguments.Count) {
        0 { return $Excel.Run($full) }
        2 { return $Excel.Run($full, $Arguments[0], $Arguments[1]) }
        default { throw "Invoke-Macro: unsupported argument count" }
    }
}

#------------------------------------------------------------------------------
# Test assets: placeholder images and two small linked PDF documents
#------------------------------------------------------------------------------
function New-PlaceholderImage([string]$Path, [int]$Width, [int]$Height, [string]$Label, [int[]]$Rgb) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb($Rgb[0], $Rgb[1], $Rgb[2]))
    $penWidth = [float][Math]::Max(4, [Math]::Min($Width, $Height) / 120)
    $pen = New-Object System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::FromArgb(60, 60, 60)), $penWidth
    $inset = [int]$penWidth
    $g.DrawRectangle($pen, $inset, $inset, $Width - 2 * $inset, $Height - 2 * $inset)
    $g.DrawLine($pen, 0, 0, $Width, $Height)
    $g.DrawLine($pen, $Width, 0, 0, $Height)
    $d = [Math]::Min($Width, $Height) * 0.5
    $g.DrawEllipse($pen, [float](($Width - $d) / 2), [float](($Height - $d) / 2), [float]$d, [float]$d)
    $size = [float][Math]::Max(12, [Math]::Min($Width, $Height) / 14)
    $font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', $size, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $text = "$Label`n$Width x $Height px"
    $box = $g.MeasureString($text, $font)
    $rect = New-Object System.Drawing.RectangleF -ArgumentList ([float](($Width - $box.Width) / 2 - 20)), ([float](($Height - $box.Height) / 2 - 10)), ([float]($box.Width + 40)), ([float]($box.Height + 20))
    $g.FillRectangle([System.Drawing.Brushes]::White, $rect)
    $g.DrawString($text, $font, [System.Drawing.Brushes]::Black, $rect, $format)
    $g.DrawString('TOP LEFT', $font, [System.Drawing.Brushes]::DarkRed, [float]($inset * 3), [float]($inset * 3))
    if ($Path.EndsWith('.jpg')) { $imgFormat = [System.Drawing.Imaging.ImageFormat]::Jpeg } else { $imgFormat = [System.Drawing.Imaging.ImageFormat]::Png }
    $bmp.Save($Path, $imgFormat)
    $font.Dispose(); $pen.Dispose(); $g.Dispose(); $bmp.Dispose()
}

function New-DummyPdf([string]$Path, [string]$Title) {
    $content = "BT /F1 28 Tf 72 760 Td ($Title) Tj ET BT /F1 12 Tf 72 730 Td (Placeholder document for the work instruction self-test) Tj ET"
    $objects = @(
        '<< /Type /Catalog /Pages 2 0 R >>',
        '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
        '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
        "<< /Length $($content.Length) >>`nstream`n$content`nendstream",
        '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'
    )
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("%PDF-1.4`n")
    $offsets = @()
    for ($i = 0; $i -lt $objects.Count; $i++) {
        $offsets += $sb.Length
        [void]$sb.Append("$($i + 1) 0 obj`n$($objects[$i])`nendobj`n")
    }
    $xref = $sb.Length
    [void]$sb.Append("xref`n0 $($objects.Count + 1)`n0000000000 65535 f `n")
    foreach ($o in $offsets) { [void]$sb.Append(('{0:D10} 00000 n ' -f $o) + "`n") }
    [void]$sb.Append("trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n")
    [IO.File]::WriteAllText($Path, $sb.ToString(), [Text.Encoding]::ASCII)
}

function New-TestAssets([string]$Dir) {
    New-Item -ItemType Directory -Force -Path $Dir | Out-Null
    New-PlaceholderImage (Join-Path $Dir 'op10_wide.png') 1600 900 'OP 10 - PLACEHOLDER (wide)' @(214, 230, 245)
    New-PlaceholderImage (Join-Path $Dir 'op20_tall.jpg') 2400 3200 'OP 20 - PLACEHOLDER (tall, large JPEG)' @(226, 239, 218)
    New-PlaceholderImage (Join-Path $Dir 'op30_square.png') 1000 1000 'OP 30 - PLACEHOLDER (square, pasted)' @(252, 228, 214)
    New-DummyPdf (Join-Path $Dir 'DRG-1001.pdf') 'DRG-1001 Rev C - Gearbox housing assembly'
    New-DummyPdf (Join-Path $Dir 'SPEC-220.pdf') 'SPEC-220 Rev A - Bearing fitting specification'
}

#------------------------------------------------------------------------------
# Main
#------------------------------------------------------------------------------
$excel = $null
$exitCode = 0
try {
    if (-not (Test-Path $Source)) { throw "Source workbook not found: $Source" }
    $Source = (Resolve-Path $Source).Path
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $OutDir = (Resolve-Path $OutDir).Path
    $xlsm = Join-Path $OutDir 'Work_Instruction_Template.xlsm'
    $xltm = Join-Path $OutDir 'Work_Instruction_Template.xltm'
    foreach ($f in @($xlsm, $xltm)) {
        if (Test-Path $f) {
            try { Remove-Item $f -Force } catch { throw "Cannot replace $f - is it open in Excel? Close it and run again." }
        }
    }

    Write-Step 'Allowing access to the VBA project for this build (restored afterwards)'
    Enable-VbomAccess

    Write-Step 'Starting Excel'
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = -not $Hidden
    $excel.DisplayAlerts = $false
    $excel.AutomationSecurity = $msoAutomationSecurityLow
    Write-Host "    Excel $($excel.Version) build $($excel.Build)"

    Write-Step "Opening $Source (read-only)"
    $wb = $excel.Workbooks.Open($Source, 0, $true)
    $wb.SaveAs($xlsm, $xlOpenXMLWorkbookMacroEnabled)

    Write-Step 'Importing VBA modules'
    try { $null = $wb.VBProject.VBComponents.Count }
    catch {
        throw "Excel refused access to the VBA project. In Excel: File > Options > Trust Center > Trust Center Settings > Macro Settings > tick 'Trust access to the VBA project object model', then run this script again."
    }
    foreach ($m in $RuntimeModules) { Import-VbaModule $wb (Join-Path $VbaDir "$m.bas") }
    Set-ThisWorkbookCode $wb (Join-Path $VbaDir 'ThisWorkbook.cls')
    Import-VbaModule $wb (Join-Path $VbaDir 'build\modBuild.bas')

    Write-Step 'Building the template inside Excel (modBuild.BuildTemplate)'
    $result = Invoke-Macro $excel $wb 'modBuild.BuildTemplate'
    if ($result -ne 'OK') { throw "BuildTemplate: $result" }

    Remove-VbaModule $wb 'modBuild'
    $excel.EnableEvents = $false
    $wb.Save()
    Write-Step "Saved $xlsm"
    $wb.SaveAs($xltm, $xlOpenXMLTemplateMacroEnabled)
    Write-Step "Saved $xltm"
    $wb.Close($false)
    $excel.EnableEvents = $true

    if (-not $SkipTest) {
        $sampleDir = Join-Path $OutDir 'sample'
        $assetDir = Join-Path $OutDir 'test-assets'
        if (Test-Path $sampleDir) {
            try { Remove-Item $sampleDir -Recurse -Force } catch { throw "Cannot clear $sampleDir - is the sample PDF or workbook open? Close it and run again." }
        }
        New-Item -ItemType Directory -Force -Path $sampleDir | Out-Null

        Write-Step 'Creating test images and documents'
        New-TestAssets $assetDir

        Write-Step 'Running the self-test (sample WI with 3 operations)'
        $work = Join-Path $sampleDir 'selftest_working.xlsm'
        Copy-Item $xlsm $work
        $wb2 = $excel.Workbooks.Open($work)        # Workbook_Open runs as it would for a user
        Import-VbaModule $wb2 (Join-Path $VbaDir 'test\modSelfTest.bas')
        $result = Invoke-Macro $excel $wb2 'modSelfTest.RunSelfTest' @($assetDir, $sampleDir)
        Remove-VbaModule $wb2 'modSelfTest'
        $excel.EnableEvents = $false
        $wb2.Save()
        $wb2.Close($false)
        $excel.EnableEvents = $true
        Remove-Item $work -Force -ErrorAction SilentlyContinue

        $report = Join-Path $sampleDir 'SelfTest_Report.txt'
        if (Test-Path $report) { Get-Content $report | Write-Host }
        if ($result -eq 'PASS') {
            Write-Host "Self-test: PASS" -ForegroundColor Green
        } else {
            Write-Host "Self-test: $result" -ForegroundColor Yellow
            $exitCode = 2
        }
    }

    Write-Host ''
    Write-Host 'Done. Files:' -ForegroundColor Green
    Get-ChildItem $OutDir -Recurse -File | Where-Object { $_.FullName -notlike '*test-assets*' } |
        ForEach-Object { Write-Host ("    {0}  ({1:N0} KB)" -f $_.FullName, ($_.Length / 1KB)) }
}
catch {
    Write-Host ''
    Write-Host "BUILD FAILED: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
finally {
    if ($excel) {
        try {
            $excel.DisplayAlerts = $false
            while ($excel.Workbooks.Count -gt 0) { $excel.Workbooks.Item(1).Close($false) }
        } catch { }
        $excel.Quit()
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel)
        $excel = $null
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    Restore-VbomAccess
}
exit $exitCode
