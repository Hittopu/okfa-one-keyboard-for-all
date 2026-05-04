@echo off
setlocal

set "APP_NAME=okfa"
set "INSTALL_DIR=%LOCALAPPDATA%\Programs\okfa"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$shell = New-Object -ComObject WScript.Shell; " ^
  "foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) { " ^
  "  $link = Join-Path $folder 'okfa.lnk'; " ^
  "  if (Test-Path $link) { Remove-Item $link -Force } " ^
  "}"

if exist "%INSTALL_DIR%" (
  rmdir /s /q "%INSTALL_DIR%"
)

echo Uninstalled %APP_NAME%.
exit /b 0
