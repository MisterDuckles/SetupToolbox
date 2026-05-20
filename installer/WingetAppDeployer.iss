; ============================================================================
;  WingetAppDeployer — Inno Setup installer (per-user, unpackaged WinUI 3)
; ============================================================================
;  Pakt de self-contained Release-publish-map in tot één setup.exe.
;  Per-user install (PrivilegesRequired=lowest → GEEN UAC) naar
;  %LocalAppData%\Programs\WingetAppDeployer.
;
;  Ondersteunt /SILENT /VERYSILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS —
;  dit is wat de self-update straks aanroept: download nieuwe setup.exe →
;  /SILENT → Restart Manager sluit de draaiende app → bestanden vervangen →
;  app herstart. Geen zelfgebouwde folder-swapper nodig.
;
;  Compileer via scripts\build-installer.ps1 (dat zet PublishDir + AppVersion
;  via /D). De #ifndef-fallbacks laten 'm ook standalone compileren.
; ============================================================================

#define MyAppName "WingetAppDeployer"
#define MyAppPublisher "MisterDuckles"
#define MyAppURL "https://github.com/MisterDuckles/WinGetAppDeployer"
#define MyAppExeName "WingetAppDeployer.WinUI.exe"

#ifndef PublishDir
  #define PublishDir AddBackslash(SourcePath) + "..\src\WingetAppDeployer.WinUI\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish"
#endif
#ifndef AppVersion
  #define AppVersion GetFileVersion(AddBackslash(PublishDir) + MyAppExeName)
#endif

[Setup]
; Vast AppId zodat een update dezelfde install vervangt (niet naast de oude).
AppId={{893E42B3-254D-4CFC-A90B-F29708D5882E}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
; Per-user install → geen UAC bij install én bij self-update.
PrivilegesRequired=lowest
OutputDir={#AddBackslash(SourcePath)}Output
OutputBaseFilename=WingetAppDeployer-Setup-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Restart Manager: sluit de draaiende app tijdens een update en herstart 'm.
CloseApplications=yes
RestartApplications=yes
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AddBackslash(PublishDir)}*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
