; Inno Setup script for PermaDel. Built by build.ps1 from the self-contained publish output.

#ifndef AppVersion
  #define AppVersion "1.1.1"
#endif

#define AppName "PermaDel"
#define AppExeName "PermaDel.exe"
#define ExtensionDll "PermaDel.ShellExtension.dll"
#define AppPublisher "Luka Stojiljkovic"
#define AppUrl "https://github.com/lukastojiljkovic/PermaDel"
#define PublishDir "..\artifacts\publish\win-x64"

[Setup]
AppId={{8C5E2B8A-3F4D-4E6B-9B1A-6D2F7C9E4A15}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DisableDirPage=auto
DisableProgramGroupPage=yes
; Users must accept the Terms of Use, which also cover the redistributed Microsoft components.
LicenseFile=..\TERMS.md
OutputDir=..\artifacts\installer
OutputBaseFilename={#AppName}-{#AppVersion}-Setup
SetupIconFile=..\src\PermaDel\Assets\PermaDel.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Registering the unsigned File Explorer extension package requires administrator rights.
PrivilegesRequired=admin
; The per-user settings below belong to the account that approves the elevation, which is the signed-in user in the
; usual case of an administrator account.
UsedUserAreasWarning=no
; File Explorer keeps the extension DLL loaded, so only PermaDel itself is closed; the DLL is replaced on restart if needed.
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "contextmenu"; Description: "Add ""Shred with PermaDel"" to the File Explorer context menu"; GroupDescription: "File Explorer:"; MinVersion: 10.0.22000
Name: "verification"; Description: "Ask for Windows Hello or the account password before shredding"; GroupDescription: "Security:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,{#ExtensionDll}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\{#ExtensionDll}"; DestDir: "{app}"; Flags: ignoreversion restartreplace uninsrestartdelete
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\TERMS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\{#AppName}"; ValueType: dword; ValueName: "RequireVerification"; ValueData: 1; Tasks: verification; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\{#AppName}"; ValueType: dword; ValueName: "RequireVerification"; ValueData: 0; Tasks: not verification; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExeName}"; Parameters: "--register-shell"; StatusMsg: "Adding PermaDel to the File Explorer context menu..."; Flags: runhidden waituntilterminated; Tasks: contextmenu
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\{#AppExeName}"; Parameters: "--unregister-shell"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterShellExtension"

[UninstallDelete]
; An update installer that was downloaded but never run (PRIVACY.md), for the account that runs the uninstaller.
Type: filesandordirs; Name: "{localappdata}\{#AppName}\Updates"
Type: dirifempty; Name: "{localappdata}\{#AppName}"
