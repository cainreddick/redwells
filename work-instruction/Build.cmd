@echo off
rem Double-click this file to build the Work Instruction template.
rem (Double-clicking the .ps1 only opens it in Notepad - that is a Windows default.)
cd /d "%~dp0"
if not exist "%~dp0Build-WorkInstruction.ps1" goto notextracted
if not exist "%~dp0vba\modCommon.bas" goto notextracted
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Recurse | Unblock-File; & '%~dp0Build-WorkInstruction.ps1'"
echo.
pause
exit /b

:notextracted
echo.
echo  Build.cmd is running from:
echo    %~dp0
echo  but the rest of the work-instruction folder is not there.
echo.
echo  This happens when Build.cmd is opened from INSIDE the zip file.
echo  Windows then copies only Build.cmd to a temporary folder.
echo.
echo  Fix:
echo    1. Close this window.
echo    2. Right-click work-instruction.zip and choose "Extract All...", then click Extract.
echo    3. In the extracted work-instruction folder (not the zip), double-click Build.cmd.
echo.
pause
