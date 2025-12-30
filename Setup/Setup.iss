; Inno docs: https://jrsoftware.org/ishelp/
; Preprocessor docs: https://jrsoftware.org/ispphelp/
#pragma verboselevel 9

#define public Dependency_Path_NetCoreCheck "InnoDependencyInstaller\dependencies\"
#include "InnoDependencyInstaller\CodeDependencies.iss"

; Extract version from exe
#define Exe "..\publish\AutoAudioSwitcher.exe"
#ifnexist Exe
  #pragma error Exe + " does not exist"
#endif
#define FileVersion GetStringFileInfo(Exe, "FileVersion")
#define ProductVersion GetStringFileInfo(Exe, "ProductVersion")
#define Version Copy(ProductVersion, 1, Pos("+", ProductVersion) - 1)
#pragma message "Version is " + Version

[Setup]
AppCopyright=Copyright (c) Max Kagamine
AppId={{F09F929B-E98F-A1E9-9FB3-E383AAE383B3}
AppName=Auto Audio Switcher
AppPublisher=Max Kagamine
AppPublisherURL=https://github.com/maxkagamine/AutoAudioSwitcher
AppSupportURL=https://github.com/maxkagamine/AutoAudioSwitcher/issues
AppUpdatesURL=https://github.com/maxkagamine/AutoAudioSwitcher/releases
AppVerName=Auto Audio Switcher {#Version}
AppVersion={#Version}
ArchitecturesInstallIn64BitMode=x64compatible
DefaultDirName={userpf}\Auto Audio Switcher
DisableDirPage=yes
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE.txt
OutputBaseFilename=AutoAudioSwitcher-Setup
PrivilegesRequired=lowest
RestartApplications=no
ShowLanguageDialog=auto
SolidCompression=yes
VersionInfoProductTextVersion={#ProductVersion}
VersionInfoVersion={#FileVersion}
WizardStyle=classic dynamic

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userstartup}\Auto Audio Switcher"; Filename: "{app}\AutoAudioSwitcher.exe"

[Run]
Filename: "{app}\AutoAudioSwitcher.exe"; Description: "{cm:LaunchProgram,Auto Audio Switcher}"; Flags: nowait postinstall

[Code]
const
  APP_MUTEX = 'f09f929b-e98f-a1e9-9fb3-e383aae383b3';
  WINDOW_NAME = 'Auto Audio Switcher';
  APP_EXE = 'AutoAudioSwitcher.exe';
  WM_CLOSE = 16;

function InitializeSetup: Boolean;
begin
  Dependency_AddDotNet100Desktop;
  Result := True;
end;

procedure CloseApplication;
var
  Hwnd: HWND;
  Timeout: Integer;
  ErrorCode: Integer;
begin
  if not CheckForMutexes(APP_MUTEX) then
    exit;
  Hwnd := FindWindowByWindowName(WINDOW_NAME);
  if Hwnd <> 0 then
  begin
    SendMessage(Hwnd, WM_CLOSE, 0, 0);
    Timeout := 100; // 10s
    while (Timeout > 0) and CheckForMutexes(APP_MUTEX) do
    begin
      Sleep(100);
      Timeout := Timeout - 1;
    end;
  end;
  if CheckForMutexes(APP_MUTEX) then
    Exec('taskkill.exe', '/f /im ' + APP_EXE, '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  OriginalCaption: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    OriginalCaption := UninstallProgressForm.StatusLabel.Caption;
    UninstallProgressForm.StatusLabel.Caption := SetupMessage(msgStatusClosingApplications);
    CloseApplication;
    UninstallProgressForm.StatusLabel.Caption := OriginalCaption;
  end;
end;
