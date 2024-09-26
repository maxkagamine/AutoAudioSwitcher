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
ShowLanguageDialog=auto
SolidCompression=yes
VersionInfoProductTextVersion={#ProductVersion}
VersionInfoVersion={#FileVersion}
WizardStyle=classic

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userstartup}\Auto Audio Switcher"; Filename: "{app}\AutoAudioSwitcher.exe"

[Run]
Filename: "{app}\AutoAudioSwitcher.exe"; Flags: nowait postinstall

[Code]
function InitializeSetup: Boolean;
begin
  Dependency_AddDotNet90Desktop;
  Result := True;
end;
