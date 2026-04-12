#define AppName      "VELO"
#define AppVersion   "1.0.0"
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
DefaultDirName={autopf}\{#AppName}
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
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "Create Start Menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checked

[Files]
; Main executable
Source: "..\publish\VELO\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Resources (blocklists, scripts)
Source: "..\publish\VELO\resources\*"; DestDir: "{app}\resources"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\VELO\Cache"
Type: filesandordirs; Name: "{userappdata}\VELO\WebView2"

[Code]
const
  WEBVIEW2_KEY = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WEBVIEW2_URL = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  CRLF = #13#10;

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
