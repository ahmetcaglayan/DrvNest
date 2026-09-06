; =============================================================================
;  DrvNest installer
;
;  Build it with:
;      iscc /DMyAppVersion=1.2.3 build\installer\DrvNest.iss
;
;  It packages the already-published dist\DrvNest.exe, so publish first:
;      pwsh build\publish.ps1 -Runtime win-x64
;
;  The installer is a convenience, not the primary distribution channel. DrvNest
;  is a single self-contained executable and is meant to be usable straight off a
;  USB stick on a machine that was formatted five minutes ago.
; =============================================================================

; Overridable from the command line; the default keeps a bare `iscc` working.
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif

#define MyAppName "DrvNest"
#define MyAppPublisher "DrvNest contributors"
#define MyAppURL "https://github.com/ahmetcaglayan/DrvNest"
#define MyAppExeName "DrvNest.exe"

[Setup]
; Never change AppId: it is what lets an upgrade replace the previous install
; instead of stacking a second copy next to it.
AppId={{6D1F8A34-9C27-4E5B-BA80-3F71C6D2E945}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
UninstallDisplayIcon={app}\{#MyAppExeName}

; DrvNest installs drivers. It cannot do anything useful without elevation, so
; asking for it up front is honest rather than surprising the user later.
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible

; Matches SupportedOSPlatformVersion in DrvNest.App.csproj (Windows 10 1607).
MinVersion=10.0.14393

OutputDir=..\..\dist
OutputBaseFilename=DrvNest-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
; Unchecked on purpose: a desktop icon should be a choice, not a default.
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\..\dist\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The self-updater renames the running executable aside before dropping the new
; one in place. If the process was killed at the wrong moment that leftover is
; still here, and it is inert, so cleaning it up is safe.
Type: files; Name: "{app}\{#MyAppExeName}.old"
Type: dirifempty; Name: "{app}"

; -----------------------------------------------------------------------------
;  Deliberately NOT removed on uninstall: %ProgramData%\DrvNest
;
;  That folder holds the user's driver backups, the install history, the settings
;  and the logs. The backups in particular are the user's only route back to a
;  working driver after a bad update -- deleting them because someone uninstalled
;  the tool would destroy exactly the safety net the tool exists to provide.
;  Anyone who wants it gone can delete the folder by hand.
; -----------------------------------------------------------------------------
