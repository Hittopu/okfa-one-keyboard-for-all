#define AppName "okfa"
#ifndef AppVersion
#define AppVersion "0.2.0"
#endif
#ifndef SourceDir
#define SourceDir "..\okfa.windows\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#endif
#ifndef OutputDir
#define OutputDir "dist"
#endif
#ifndef IconFile
#define IconFile "..\okfa.windows\Assets\okfa.ico"
#endif
#ifndef LicenseFile
#define LicenseFile "..\..\LICENSE"
#endif

[Setup]
AppId={{907C1F73-0531-47A2-B58D-A9E9D7B33E01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=okfa
AppPublisherURL=https://github.com/Hittopu/okfa-one-keyboard-for-all
AppSupportURL=https://github.com/Hittopu/okfa-one-keyboard-for-all/issues
DefaultDirName={localappdata}\Programs\okfa
DefaultGroupName=okfa
DisableProgramGroupPage=yes
LicenseFile={#LicenseFile}
OutputDir={#OutputDir}
OutputBaseFilename=okfa-win2win-v{#AppVersion}-setup
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\okfa.exe
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Tasks]
Name: "desktopicons"; Description: "Create desktop shortcuts"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\okfa Receiver"; Filename: "{app}\okfa.exe"; WorkingDir: "{app}"; IconFilename: "{app}\okfa.exe"
Name: "{group}\okfa Sender"; Filename: "{app}\okfa.exe"; Parameters: "--sender"; WorkingDir: "{app}"; IconFilename: "{app}\okfa.exe"
Name: "{autodesktop}\okfa Receiver"; Filename: "{app}\okfa.exe"; WorkingDir: "{app}"; IconFilename: "{app}\okfa.exe"; Tasks: desktopicons
Name: "{autodesktop}\okfa Sender"; Filename: "{app}\okfa.exe"; Parameters: "--sender"; WorkingDir: "{app}"; IconFilename: "{app}\okfa.exe"; Tasks: desktopicons

[Run]
Filename: "{app}\okfa.exe"; Description: "Launch okfa Receiver"; Flags: nowait postinstall skipifsilent unchecked
