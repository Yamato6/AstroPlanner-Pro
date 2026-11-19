; AstroPlanner Pro installer (Inno Setup 6).
;
; Built by CI on every v* tag:
;   iscc /DMyAppVersion=2.0.0 /DPublishDir=..\publish /DOutputDir=..\dist installer\AstroPlanner.iss
;
; Installs for the current user, without administrator rights, in
; %LocalAppData%\Programs\AstroPlanner Pro. User data (settings, plan, logbook) lives in
; %LocalAppData%\AstroPlannerPro and is never touched on uninstall or upgrade.
; AstroPlanner v1 has its own AppId and data folder: both apps can be installed side by side.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define MyAppName "AstroPlanner Pro"
#define MyAppFileName "AstroPlannerPro"
#define MyAppPublisher "Yamato6"
#define MyAppURL "https://github.com/Yamato6/AstroPlanner-Pro"
#define MyAppExeName "AstroPlannerPro.exe"

[Setup]
; Fixed AppId: future versions install over this one instead of side by side.
; (Different from AstroPlanner v1's {8C3E5D2A-...}.)
AppId={{A0E582E8-976D-4B28-ADF2-923F8052053C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppCopyright=© 2026 {#MyAppPublisher}
VersionInfoVersion={#Copy(MyAppVersion, 1, Pos("-", MyAppVersion + "-") - 1)}
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} installer

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

LicenseFile=..\LICENSE
SetupIconFile=..\src\AstroPlanner.UI\Assets\AstroPlanner.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
WizardSizePercent=110

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

Compression=lzma2/max
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename={#MyAppFileName}_{#MyAppVersion}_Setup

; If the app is running, offer to close it so that it can be updated.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Astrophotography planner"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

; Nothing in [UninstallDelete]: the user data folder is kept on purpose.
