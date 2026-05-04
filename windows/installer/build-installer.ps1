param(
    [string]$Version = '0.1.2'
)

$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'okfa.windows\okfa.windows.csproj'
$publishDir = Join-Path $root 'okfa.windows\publish\win-x64'
$distDir = Join-Path $PSScriptRoot 'dist'
$issPath = Join-Path $PSScriptRoot 'okfa.iss'
$outputBaseName = "okfa-windows-v$Version-setup"

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir

$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc)) {
    $iscc = Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
}
if (-not (Test-Path -LiteralPath $iscc)) {
    $iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
}

if (-not (Test-Path -LiteralPath $iscc)) {
    throw 'Inno Setup compiler (ISCC.exe) was not found.'
}

& $iscc `
    "/DMyAppVersion=$Version" `
    "/DPublishDir=$publishDir" `
    "/DOutputDir=$distDir" `
    "/DOutputBaseFilename=$outputBaseName" `
    "/DSetupIconFile=$(Join-Path $root 'okfa.windows\Assets\okfa.ico')" `
    $issPath

$setupPath = Join-Path $distDir "$outputBaseName.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Installer was not created: $setupPath"
}

Write-Host "Installer built at $setupPath"
