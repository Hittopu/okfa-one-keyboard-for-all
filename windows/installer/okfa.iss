#define MyAppName "okfa"
#define MyAppPublisher "okfa"
#define MyAppURL "https://github.com/Hittopu/okfa-one-keyboard-for-all"
#define MyAppExeName "okfa.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.1.2"
#endif

#ifndef PublishDir
  #define PublishDir "..\\okfa.windows\\publish\\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir ".\\dist"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "okfa-windows-v0.1.2-setup"
#endif

#ifndef SetupIconFile
  #define SetupIconFile "..\\okfa.windows\\Assets\\okfa.ico"
#endif

[Setup]
AppId={{A65C6A31-5D6B-4CE0-9FB3-B7AB9448C43D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\okfa
DefaultGroupName=okfa
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile={#SetupIconFile}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\okfa"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\okfa"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch okfa"; Flags: nowait postinstall skipifsilent
