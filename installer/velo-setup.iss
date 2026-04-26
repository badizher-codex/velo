#define AppName      "VELO"
#define AppVersion   "2.0.5.3"
#define AppPublisher "VELO Browser Contributors"
#define AppURL       "https://github.com/badizher-codex/velo"
#define AppExeName   "VELO.exe"
#define AppId        "{{B4D1E2F3-8A5C-4E6D-9F7B-2C3D4E5F6A7B}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={code:GetInstallDir|{autopf}\{#AppName}}
DefaultGroupName={#AppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\publish\installer
OutputBaseFilename=VELO-v{#AppVersion}-Setup
SetupIconFile=..\src\VELO.App\velo.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=10.0.17763
UninstallDisplayName={#AppName} Browser
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Privacy Browser Setup
PrivilegesRequired=admin
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "Create Start Menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; All publish output (self-contained — includes runtime DLLs, native libs, resources)
Source: "..\publish\VELO\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
; ── ProgID for HTML files ─────────────────────────────────────────────────────
Root: HKLM; Subkey: "SOFTWARE\Classes\VELOHTML"; ValueType: string; ValueData: "VELO HTML Document"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Classes\VELOHTML\DefaultIcon"; ValueType: string; ValueData: "{app}\{#AppExeName},1"
Root: HKLM; Subkey: "SOFTWARE\Classes\VELOHTML\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" ""%1"""

; ── ProgID for URLs ───────────────────────────────────────────────────────────
Root: HKLM; Subkey: "SOFTWARE\Classes\VELOURL"; ValueType: string; ValueData: "VELO URL"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Classes\VELOURL\DefaultIcon"; ValueType: string; ValueData: "{app}\{#AppExeName},1"
Root: HKLM; Subkey: "SOFTWARE\Classes\VELOURL\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" ""%1"""
Root: HKLM; Subkey: "SOFTWARE\Classes\VELOURL"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""

; ── RegisteredApplications ───────────────────────────────────────────────────
Root: HKLM; Subkey: "SOFTWARE\RegisteredApplications"; ValueType: string; ValueName: "VELO"; ValueData: "SOFTWARE\VELO\Capabilities"; Flags: uninsdeletevalue

Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "VELO"
Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Privacy-first browser for Windows"
Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\{#AppExeName},0"

; ── File associations ─────────────────────────────────────────────────────────
Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities\FileAssociations"; ValueType: string; ValueName: ".htm";   ValueData: "VELOHTML"
Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities\FileAssociations"; ValueType: string; ValueName: ".html";  ValueData: "VELOHTML"
Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities\FileAssociations"; ValueType: string; ValueName: ".shtml"; ValueData: "VELOHTML"
Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities\FileAssociations"; ValueType: string; ValueName: ".xhtml"; ValueData: "VELOHTML"
Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities\FileAssociations"; ValueType: string; ValueName: ".webp";  ValueData: "VELOHTML"
Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities\FileAssociations"; ValueType: string; ValueName: ".svg";   ValueData: "VELOHTML"

; ── URL associations ─────────────────────────────────────────────────────────
Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities\URLAssociations"; ValueType: string; ValueName: "http";  ValueData: "VELOURL"
Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities\URLAssociations"; ValueType: string; ValueName: "https"; ValueData: "VELOURL"
Root: HKLM; Subkey: "SOFTWARE\VELO\Capabilities\URLAssociations"; ValueType: string; ValueName: "ftp";   ValueData: "VELOURL"

; ── Start Menu Internet (legacy, requerido por Windows 10) ───────────────────
Root: HKLM; Subkey: "SOFTWARE\Clients\StartMenuInternet\VELO"; ValueType: string; ValueData: "VELO"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Clients\StartMenuInternet\VELO\DefaultIcon"; ValueType: string; ValueData: "{app}\{#AppExeName},0"
Root: HKLM; Subkey: "SOFTWARE\Clients\StartMenuInternet\VELO\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"""

[UninstallDelete]
; WebView2 profile (cookies, browser cache) — safe to remove on uninstall
; User data (vault, history, bookmarks) in {localappdata}\VELO\ is intentionally
; left on disk so it survives a reinstall.
Type: filesandordirs; Name: "{localappdata}\VELO\Profile"

[Code]
const
  WEBVIEW2_KEY  = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WEBVIEW2_URL  = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  UNINSTALL_KEY = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B4D1E2F3-8A5C-4E6D-9F7B-2C3D4E5F6A7B}_is1';
  CRLF = #13#10;

{ If a previous installation exists, reuse its install location. }
function GetInstallDir(Default: String): String;
var
  Path: String;
begin
  if RegQueryStringValue(HKLM, UNINSTALL_KEY, 'InstallLocation', Path) and (Path <> '') then
    Result := Path
  else
    Result := Default;
end;

function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(HKLM, WEBVIEW2_KEY, 'pv', Version)
         or RegQueryStringValue(HKCU, WEBVIEW2_KEY, 'pv', Version);
  if Result then
    Result := (Version <> '') and (Version <> '0.0.0.0');
end;

procedure InitializeWizard;
begin
  if not WebView2Installed then
    MsgBox(
      'VELO requires the Microsoft Edge WebView2 Runtime.' + CRLF +
      CRLF +
      'After the installation finishes, please download and install WebView2 from:' + CRLF +
      WEBVIEW2_URL + CRLF +
      CRLF +
      '(Windows 11 and most Windows 10 systems already have it installed.)',
      mbInformation, MB_OK
    );
end;
