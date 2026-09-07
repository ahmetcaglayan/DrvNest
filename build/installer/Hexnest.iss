; =============================================================================
;  Hexnest installer
;
;  Build it with:
;      iscc /DMyAppVersion=1.2.3 build\installer\Hexnest.iss
;
;  It packages the already-published dist\Hexnest.exe, so publish first:
;      pwsh build\publish.ps1 -Runtime win-x64
;
;  The installer is a convenience, not the primary distribution channel. Hexnest
;  is a single self-contained executable and is meant to be usable straight off a
;  USB stick on a machine that was formatted five minutes ago.
; =============================================================================

; The version comes from the command line and has no default. Both callers already
; pass it - release.yml and build/make-release.ps1 both run iscc with
; /DMyAppVersion - so nothing is lost, and a literal here would be stale the moment
; the next version shipped: it sat at 1.3.0 through the 1.4.0 release and would have
; registered the new build in Add/Remove Programs under the old number. Failing
; loudly is better than stamping a number nobody chose.
#ifndef MyAppVersion
  #error MyAppVersion is not defined. Pass /DMyAppVersion=<version>, or build the installer through build/make-release.ps1, which reads it from Directory.Build.props.
#endif

#define MyAppName "Hexnest"
#define MyAppPublisher "Hexnest contributors"
#define MyAppURL "https://github.com/ahmetcaglayan/Hexnest"
#define MyAppExeName "Hexnest.exe"

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

; Hexnest installs drivers. It cannot do anything useful without elevation, so
; asking for it up front is honest rather than surprising the user later.
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible

; Matches SupportedOSPlatformVersion in Hexnest.App.csproj (Windows 10 1607).
MinVersion=10.0.14393

OutputDir=..\..\dist
OutputBaseFilename=Hexnest-Setup
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
;  Deliberately NOT removed on uninstall: %ProgramData%\Hexnest
;
;  That folder holds the user's driver backups, the install history, the settings
;  and the logs. The backups in particular are the user's only route back to a
;  working driver after a bad update -- deleting them because someone uninstalled
;  the tool would destroy exactly the safety net the tool exists to provide.
;  Anyone who wants it gone can delete the folder by hand.
; -----------------------------------------------------------------------------
