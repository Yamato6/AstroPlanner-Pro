; AstroPlanner Pro installer (Inno Setup 6).
;
; Built by CI on every v* tag:
;   iscc /DMyAppVersion=2.0.0 /DPublishDir=..\publish /DOutputDir=..\dist installer\AstroPlanner.iss
; (Inno Setup 6 or 7.)
;
; The 3D sky needs the Microsoft Edge WebView2 Runtime (part of Windows 11 and of most Windows 10
; installs). If it is missing, the installer explains it and offers to open Microsoft's download
; page in the browser; it never downloads or runs anything itself (spec §12). Without it the app
; uses its 2D sky charts.
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
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Astrophotography planner"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

; Nothing in [UninstallDelete]: the user data folder is kept on purpose.

[CustomMessages]
english.WebView2Missing=The 3D sky of AstroPlanner Pro uses the Microsoft Edge WebView2 Runtime, which is not installed on this computer.%n%nEverything else works without it, and the sky falls back to 2D charts.%n%nOpen Microsoft's WebView2 download page in your browser now? (Choose the "Evergreen Bootstrapper" or "Evergreen Standalone Installer".)

[Code]
const
  WebView2ClientKey = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2DownloadUrl = 'https://developer.microsoft.com/microsoft-edge/webview2/';

{ A WebView2 Runtime registration: its "pv" value is a version other than empty or 0.0.0.0. }
function HasRuntimeVersion(RootKey: Integer; SubKey: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, SubKey, 'pv', Version)
    and (Version <> '') and (Version <> '0.0.0.0');
end;

{ The Evergreen runtime, per machine (32- or 64-bit registry view) or per user. }
function IsWebView2Installed(): Boolean;
begin
  Result := HasRuntimeVersion(HKLM32, 'SOFTWARE\' + WebView2ClientKey)
    or HasRuntimeVersion(HKLM64, 'SOFTWARE\' + WebView2ClientKey)
    or HasRuntimeVersion(HKCU, 'Software\' + WebView2ClientKey);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if (CurStep = ssPostInstall) and not WizardSilent() and not IsWebView2Installed() then
  begin
    if MsgBox(CustomMessage('WebView2Missing'), mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', WebView2DownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;
