@echo off
rem Double-click this file to build the Work Instruction template.
rem (Double-clicking the .ps1 only opens it in Notepad - that is a Windows default.)
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -Recurse | Unblock-File; & .\Build-WorkInstruction.ps1"
echo.
pause
