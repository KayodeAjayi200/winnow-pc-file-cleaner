; ── Winnow PC File Cleaner — Inno Setup Script ────────────────────────────────
;
; To build locally:
;   1. dotnet publish FileTinder.csproj /p:PublishProfile=win-x64-singlefile /p:Version=1.0.0
;   2. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php
;   3. ISCC.exe /DAppVersion=1.0.0 installer.iss
;      (or open in Inno Setup IDE and click Build → Compile)
;
; In CI the version is injected automatically via /DAppVersion=X.Y.Z
; ─────────────────────────────────────────────────────────────────────────────

; AppVersion can be overridden from the command line: /DAppVersion=1.2.3
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName      "Winnow PC File Cleaner"
#define AppPublisher "Kayode Ajayi"
#define AppExeName   "Winnow.exe"
#define SourceDir    "publish\win-x64"
#define RepoURL      "https://github.com/KayodeAjayi200/winnow-pc-file-cleaner"

[Setup]
AppId={{B3C1A2D4-7E5F-4A8B-9C3D-1F2E6A4B7D9E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#RepoURL}
AppSupportURL={#RepoURL}/issues
AppUpdatesURL={#RepoURL}/releases
DefaultDirName={autopf}\Winnow PC File Cleaner
DefaultGroupName=Winnow PC File Cleaner
AllowNoIcons=yes
OutputDir=installer
OutputBaseFilename=WinnowPCFileCleanerSetup-{#AppVersion}
SetupIconFile=app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Launch Winnow at Windows startup"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";     Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Optional Windows startup entry
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; Tasks: startupicon; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
