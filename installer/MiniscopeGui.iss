; Miniscope GUI – Inno Setup 6 installer script
; Requires Inno Setup 6.3+: https://jrsoftware.org/isdl.php
;
; Build locally:  iscc installer\MiniscopeGui.iss
; Build in CI:    see .github/workflows/installer.yml

#define AppName      "UCLA Miniscope V4 GUI"
#define AppDirName   "MiniscopeV4Gui"
#define AppVersion   "0.1.0"
#define AppPublisher "Open Ephys"
#define AppURL       "https://open-ephys.org/miniscope-docs"

; Never change AppId after the first public release — it is the identity that
; ties together installs, updates, and uninstalls across all versions.
; Generate a fresh GUID (Tools > Generate GUID in the Inno Setup IDE) when
; forking this script for a different application.
#define AppId "{{3F8A4C91-7E2B-4D5F-A8C3-9B1E6D2F7A54}"

; ---------------------------------------------------------------------------
[Setup]
; ---------------------------------------------------------------------------
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}

DefaultDirName={localappdata}\{#AppDirName}
DisableDirPage=yes

DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

OutputDir=..\artifacts
OutputBaseFilename=MiniscopeGui-Setup-{#AppVersion}

SetupIconFile=..\OpenEphys.MiniscopeV4.Gui\Resources\icon.ico
WizardStyle=modern

Compression=lzma2/ultra64
SolidCompression=yes

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

UninstallDisplayIcon={app}\icon.ico
UninstallDisplayName={#AppName}

; ---------------------------------------------------------------------------
[Languages]
; ---------------------------------------------------------------------------
Name: "english"; MessagesFile: "compiler:Default.isl"

; ---------------------------------------------------------------------------
[Files]
; ---------------------------------------------------------------------------

Source: "..\launcher\Run.ps1"; DestDir: "{app}"; Flags: ignoreversion

Source: "..\launcher\config.yml"; DestDir: "{app}"; \
  Flags: onlyifdoesntexist uninsneveruninstall

Source: "..\OpenEphys.MiniscopeV4.Gui\Workflows\MiniscopeGui.bonsai"; \
  DestDir: "{app}"; Flags: ignoreversion

Source: "..\OpenEphys.MiniscopeV4.Gui\Workflows\LogProperty.bonsai"; \
  DestDir: "{app}"; Flags: ignoreversion

Source: "..\launcher\.bonsai\Bonsai.config"; DestDir: "{app}\.bonsai"; Flags: ignoreversion
Source: "..\launcher\.bonsai\NuGet.config";  DestDir: "{app}\.bonsai"; Flags: ignoreversion

#if DirExists(SourcePath + '\..\launcher\lib')
Source: "..\launcher\lib\*"; DestDir: "{app}\lib"; Flags: ignoreversion recursesubdirs
#endif

Source: "..\OpenEphys.MiniscopeV4.Gui\Resources\icon.ico"; \
  DestDir: "{app}"; Flags: ignoreversion

; ---------------------------------------------------------------------------
[Icons]
; ---------------------------------------------------------------------------

Name: "{autodesktop}\{#AppName}"; \
  Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Run.ps1"""; \
  WorkingDir: "{app}"; \
  IconFilename: "{app}\icon.ico"; \
  Comment: "Launch the Miniscope GUI"

Name: "{group}\{#AppName}"; \
  Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Run.ps1"""; \
  WorkingDir: "{app}"; \
  IconFilename: "{app}\icon.ico"; \
  Comment: "Launch the Miniscope GUI"

; ---------------------------------------------------------------------------
[Run]
; ---------------------------------------------------------------------------

Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\Run.ps1"" -BootstrapOnly"; \
  WorkingDir: "{app}"; \
  StatusMsg: "Downloading and installing dependencies (this might take a while)..."; \
  Flags: runhidden waituntilterminated

Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Run.ps1"""; \
  WorkingDir: "{app}"; \
  Description: "Launch {#AppName} now"; \
  Flags: nowait postinstall skipifsilent

; ---------------------------------------------------------------------------
[UninstallDelete]
; ---------------------------------------------------------------------------

Type: filesandordirs; Name: "{app}\.bonsai"
