@echo off
rem Copies the Standard Blocks journal into an NX folder (default C:\nx_settings).
rem Each SB_<command>.cs is an identical copy of StandardBlocks.cs: the journal
rem picks its command from its own file name, so ribbon buttons point at these.
rem The config is only copied if the folder has none, so your edits are kept.

setlocal
set "DEST=%~1"
if "%DEST%"=="" set "DEST=C:\nx_settings"
if not exist "%DEST%" mkdir "%DEST%" || goto :fail

copy /Y "%~dp0StandardBlocks.cs" "%DEST%\StandardBlocks.cs" >nul || goto :fail
for %%C in (Inspect Detect) do (
    copy /Y "%~dp0StandardBlocks.cs" "%DEST%\SB_%%C.cs" >nul || goto :fail
)
if not exist "%DEST%\StandardBlocks.config.json" copy "%~dp0StandardBlocks.config.json" "%DEST%\" >nul

echo Standard Blocks journals copied to %DEST%
exit /b 0

:fail
echo Copy to %DEST% FAILED.
exit /b 1
