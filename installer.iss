; ── Winnow Inno Setup Script ─────────────────────────────────────────────────
; Prerequisites:
;   1. Run: dotnet publish /p:PublishProfile=win-x64-singlefile
;      This produces: publish\win-x64\Winnow.exe
;   2. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php
;   3. Open this file in Inno Setup and click Build > Compile
; ─────────────────────────────────────────────────────────────────────────────

#define AppName    "Windows PC File Cleaner"
#define AppVersion "1.0.0"
#define AppPublisher "Your Name"
#define AppExeName "Winnow.exe"
#define SourceDir  "publish\win-x64"

[Setup]
AppId={{B3C1A2D4-7E5F-4A8B-9C3D-1F2E6A4B7D9E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisherURL=https://github.com/
AppSupportURL=https://github.com/
AppUpdatesURL=https://github.com/
DefaultDirName={autopf}\Windows PC File Cleaner
DefaultGroupName=Windows PC File Cleaner
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
Name: "startupicon"; Description: "Launch FileTinder at Windows startup"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Add to Windows startup (optional task)
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; Tasks: startupicon; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
