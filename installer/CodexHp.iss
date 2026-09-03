#ifndef AppVersion
  #define AppVersion "0.3.1"
#endif

#ifndef SourceExe
  #define SourceExe "..\out\win-x64\CodexHp.exe"
#endif

#ifndef OutputDirectory
  #define OutputDirectory "..\out\installer"
#endif

[Setup]
AppId={{07145274-E70C-4F8C-AA28-51418D59824A}
AppName=AI Usage Overlay
AppVersion={#AppVersion}
AppVerName=AI Usage Overlay {#AppVersion}
AppPublisher=AI Usage Overlay
DefaultDirName={localappdata}\Programs\AIUsageOverlay
DefaultGroupName=AI Usage Overlay
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDirectory}
OutputBaseFilename=AIUsageOverlay-Setup-{#AppVersion}-x64
SetupIconFile=..\src\CodexHp.App\Assets\CodexHp.ico
UninstallDisplayIcon={app}\CodexHp.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=CodexHp.exe
RestartApplications=no
AppMutex=Local\AIUsageOverlay.SingleInstance
LicenseFile=..\LICENSE
VersionInfoVersion={#AppVersion}
VersionInfoDescription=AI Usage Overlay Setup
VersionInfoProductName=AI Usage Overlay
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "autostart"; Description: "Start AI Usage Overlay when I sign in to Windows"; Flags: checkedonce

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "CodexHp.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\AI Usage Overlay"; Filename: "{app}\CodexHp.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\AIUsageOverlay"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AIUsageOverlay"; ValueData: """{app}\CodexHp.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\CodexHp.exe"; Description: "Launch AI Usage Overlay"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
