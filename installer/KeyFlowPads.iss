#define AppTitle "KeyFlow Pads"
#define AppVersion "1.3.0"
#ifndef PublishDir
  #define PublishDir AddBackslash(SourcePath) + "..\artifacts\publish"
#endif
#ifndef ReleaseDir
  #define ReleaseDir "..\release\Installer"
#endif
#define ExpectedVersion AppVersion + ".0"
#if GetVersionNumbersString(AddBackslash(PublishDir) + "KeyFlowPads.exe") != ExpectedVersion
  #error Published KeyFlowPads.exe version does not match the installer. Run scripts/Build-Release.ps1 to republish.
#endif
#if GetVersionNumbersString(AddBackslash(PublishDir) + "KeyFlowPads.dll") != ExpectedVersion
  #error Published KeyFlowPads.dll version does not match the installer.
#endif

[Setup]
AppId={{9F19B930-9B23-4E52-8682-431918E07A26}
AppName={#AppTitle}
AppVersion={#AppVersion}
AppPublisher=KeyFlow Pads
DefaultDirName={localappdata}\Programs\KeyFlow Pads
DefaultGroupName=KeyFlow Pads
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\assets\branding\KeyFlowPads-Icon.ico
UninstallDisplayIcon={app}\KeyFlowPads.exe
UninstallDisplayName={#AppTitle}
OutputDir={#ReleaseDir}
OutputBaseFilename=KeyFlowPads-{#AppVersion}-Setup
Compression=lzma2/fast
SolidCompression=yes
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no
VersionInfoDescription=KeyFlow Pads Windows Installer
VersionInfoProductName=KeyFlow Pads
VersionInfoVersion={#ExpectedVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\KeyFlow Pads"; Filename: "{app}\KeyFlowPads.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\KeyFlow Pads"; Filename: "{app}\KeyFlowPads.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\KeyFlowPads.exe"; Description: "Open KeyFlow Pads"; Flags: nowait postinstall skipifsilent
