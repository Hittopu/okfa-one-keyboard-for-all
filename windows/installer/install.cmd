@echo off
setlocal enabledelayedexpansion

set "APP_NAME=okfa"
set "INSTALL_DIR=%LOCALAPPDATA%\Programs\okfa"
set "SOURCE_DIR=%~dp0"

if exist "%INSTALL_DIR%" (
  rmdir /s /q "%INSTALL_DIR%"
)
mkdir "%INSTALL_DIR%"

robocopy "%SOURCE_DIR%" "%INSTALL_DIR%" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$shell = New-Object -ComObject WScript.Shell; " ^
  "$desktop = [Environment]::GetFolderPath('Desktop'); " ^
  "$programs = [Environment]::GetFolderPath('Programs'); " ^
  "$target = Join-Path $env:LOCALAPPDATA 'Programs\okfa\okfa.exe'; " ^
  "$workDir = Split-Path $target; " ^
  "$desktopLink = $shell.CreateShortcut((Join-Path $desktop 'okfa.lnk')); " ^
  "$desktopLink.TargetPath = $target; $desktopLink.WorkingDirectory = $workDir; $desktopLink.IconLocation = $target; $desktopLink.Save(); " ^
  "$startLink = $shell.CreateShortcut((Join-Path $programs 'okfa.lnk')); " ^
  "$startLink.TargetPath = $target; $startLink.WorkingDirectory = $workDir; $startLink.IconLocation = $target; $startLink.Save();"

echo Installed %APP_NAME% to %INSTALL_DIR%.
echo You can launch it from the Start menu or desktop shortcut.
exit /b 0
