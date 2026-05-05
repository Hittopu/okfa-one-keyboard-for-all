param(
    [string]$Version = "0.2.0",
    [string]$Runtime = "win-x64",
    [string]$InnoCompiler = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $parentPath = Resolve-FullPath $Parent
    $childPath = Resolve-FullPath $Child
    if (-not $childPath.StartsWith($parentPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside expected directory. Parent=$parentPath Child=$childPath"
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$windowsDir = Split-Path -Parent $scriptDir
$repoRoot = Split-Path -Parent $windowsDir
$projectPath = Join-Path $windowsDir "okfa.windows\okfa.windows.csproj"
$publishDir = Join-Path $scriptDir "publish\$Runtime"
$distDir = Join-Path $scriptDir "dist"
$issPath = Join-Path $scriptDir "okfa.iss"
$iconPath = Join-Path $windowsDir "okfa.windows\Assets\okfa.ico"
$licensePath = Join-Path $repoRoot "LICENSE"

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project not found: $projectPath"
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $candidatePaths = @(
        "C:\Users\32314\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    $InnoCompiler = ($candidatePaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw "Inno Setup compiler was not found. Install Inno Setup 6 or pass -InnoCompiler."
}

Assert-ChildPath -Parent $scriptDir -Child $publishDir
Assert-ChildPath -Parent $scriptDir -Child $distDir

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir, $distDir | Out-Null

dotnet publish $projectPath `
    -c Release `
    -r $Runtime `
    -o $publishDir `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version

& $InnoCompiler `
    "/DAppVersion=$Version" `
    "/DSourceDir=$publishDir" `
    "/DOutputDir=$distDir" `
    "/DIconFile=$iconPath" `
    "/DLicenseFile=$licensePath" `
    $issPath

$installerPath = Join-Path $distDir "okfa-win2win-v$Version-setup.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer build finished, but output was not found: $installerPath"
}

Write-Host "Installer created: $installerPath"
