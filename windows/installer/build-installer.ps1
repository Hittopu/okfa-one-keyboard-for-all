$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'okfa.windows\okfa.windows.csproj'
$publishDir = Join-Path $root 'okfa.windows\publish\win-x64'
$payloadDir = Join-Path $PSScriptRoot 'payload'
$workDir = Join-Path $PSScriptRoot 'work'
$distDir = Join-Path $PSScriptRoot 'dist'
$setupPath = Join-Path $distDir 'okfa-windows-v0.1.2-setup.exe'
$sedPath = Join-Path $workDir 'okfa-windows-v0.1.2.sed'

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
New-Item -ItemType Directory -Path $workDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir

if (Test-Path -LiteralPath $payloadDir) {
    Remove-Item -LiteralPath $payloadDir -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $publishDir '*') -Destination $payloadDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.cmd') -Destination $payloadDir -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.cmd') -Destination $payloadDir -Force

$source0 = $payloadDir
$source1 = Join-Path $payloadDir 'Assets'

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$setupPath
FriendlyName=okfa Windows Installer v0.1.2
AppLaunched=cmd.exe /d /s /c ""install.cmd""
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
Strings=Strings

[Strings]
FILE0=install.cmd
FILE1=uninstall.cmd
FILE2=okfa.exe
FILE3=okfa_logo.png

[SourceFiles]
SourceFiles0=$source0
SourceFiles1=$source1

[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=

[SourceFiles1]
%FILE3%=
"@

Set-Content -LiteralPath $sedPath -Value $sed -Encoding ASCII

$iexpress = Join-Path $env:WINDIR 'System32\iexpress.exe'
& $iexpress /N /Q $sedPath

Write-Host "Installer built at $setupPath"
