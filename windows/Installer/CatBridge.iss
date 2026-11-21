; Cat Bridge installer (Inno Setup 6). Built by build.ps1 / the Windows workflow:
;   iscc /DAppVersion=2.1.0 /DSourceDir=..\dist\DynamicIsland Installer\CatBridge.iss
; Installs per user (no admin prompt), so updates can install themselves quietly.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\DynamicIsland"
#endif

[Setup]
AppId={{6E0B5D2A-4C1F-4C8E-9C3B-CA7B21D6E0A1}
AppName=Cat Bridge
AppVersion={#AppVersion}
AppVerName=Cat Bridge {#AppVersion}
AppPublisher=hamzaibnzakir
AppPublisherURL=https://github.com/hamzaibnzakir/cat-bridge
AppSupportURL=https://github.com/hamzaibnzakir/cat-bridge/issues
AppUpdatesURL=https://github.com/hamzaibnzakir/cat-bridge/releases
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Cat Bridge installer
DefaultDirName={localappdata}\Programs\Cat Bridge
DefaultGroupName=Cat Bridge
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=DynamicIsland-Setup
SetupIconFile=app.ico
UninstallDisplayIcon={app}\DynamicIsland.exe
UninstallDisplayName=Cat Bridge
WizardStyle=modern
WizardImageFile=wizard-100.bmp,wizard-200.bmp
WizardSmallImageFile=small-100.bmp,small-200.bmp
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupMutex=CatBridgeSetup
LicenseFile=..\..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This installs Cat Bridge {#AppVersion}: the Dynamic Island, the island taskbar, the desktop cat, Cat Face sign-in and Cat Center.%n%nYour settings are kept when you update.

[Tasks]
Name: "startup"; Description: "Start Cat Bridge when I sign in to Windows"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Put a Cat Center shortcut on the desktop"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Cat Bridge"; Filename: "{app}\DynamicIsland.exe"; Comment: "Dynamic Island, taskbar and desktop cat"
Name: "{autoprograms}\Cat Center"; Filename: "{app}\DynamicIsland.exe"; Parameters: "--center"; Comment: "Cat Bridge settings"
Name: "{autodesktop}\Cat Center"; Filename: "{app}\DynamicIsland.exe"; Parameters: "--center"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DynamicIsland"; ValueData: """{app}\DynamicIsland.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
; normal install: offer to start; quiet update: start again by itself
Filename: "{app}\DynamicIsland.exe"; Parameters: "--center"; Description: "Open Cat Bridge now"; Flags: nowait postinstall skipifsilent
Filename: "{app}\DynamicIsland.exe"; Parameters: "--updated"; Flags: nowait; Check: IsQuietUpdate

[UninstallRun]
Filename: "{app}\DynamicIsland.exe"; Parameters: "--quit"; Flags: runhidden waituntilterminated; RunOnceId: "QuitApp"

[Code]
const
  CatFaceKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{7a4a5c8b-1a84-498f-9bf5-d0fcffc94a1e}';
  RuntimeUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';

var
  DownloadPage: TDownloadWizardPage;

function IsQuietUpdate: Boolean;
begin
  Result := WizardSilent;
end;

function CatFaceInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, CatFaceKey);
end;

{ .NET 8 Desktop Runtime present? }
function HasDesktopRuntime8: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec);
  if Result then FindClose(FindRec);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Getting .NET 8', 'Cat Bridge needs the free Microsoft .NET 8 Desktop Runtime. Downloading it now…', nil);
end;

{ Close a running Cat Bridge gracefully so it can put the taskbar back and release its files. }
procedure QuitRunningApp;
var
  Code: Integer;
begin
  if FileExists(ExpandConstant('{app}\DynamicIsland.exe')) then
  begin
    Exec(ExpandConstant('{app}\DynamicIsland.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(1500);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  QuitRunningApp;
  if not HasDesktopRuntime8 then
  begin
    if WizardSilent then
    begin
      Result := 'Cat Bridge needs the .NET 8 Desktop Runtime. Run the installer normally once to set it up.';
      exit;
    end;
    DownloadPage.Clear;
    DownloadPage.Add(RuntimeUrl, 'windowsdesktop-runtime-8-x64.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        Result := 'Couldn''t download .NET 8 (' + GetExceptionMessage + '). Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and run this again.';
        exit;
      end;
    finally
      DownloadPage.Hide;
    end;
    { the runtime installs machine-wide, so it shows one admin prompt }
    if not ShellExec('runas', ExpandConstant('{tmp}\windowsdesktop-runtime-8-x64.exe'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code)
       or ((Code <> 0) and (Code <> 3010)) then
      Result := '.NET 8 didn''t install (code ' + IntToStr(Code) + '). Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and run this again.';
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  { Cat Face sign-in lives in Program Files and needs one admin prompt to remove }
  if (CurUninstallStep = usUninstall) and CatFaceInstalled then
    ShellExec('runas', ExpandConstant('{app}\FaceHelper.exe'), '--uninstall', '', SW_SHOW, ewWaitUntilTerminated, Code);
  if CurUninstallStep = usPostUninstall then
    if MsgBox('Also delete your Cat Bridge settings, saved face and notification history?' + #13#10 + '(Choose No if you might install it again.)', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\DynamicIsland'), True, True, True);
end;
