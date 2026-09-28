; Inno Setup script (https://jrsoftware.org/isinfo.php, version 6.3 or newer) that wraps a published
; self-contained build into a classic Windows installer.
;
;   1. .\publish.ps1                       (creates publish\win-x64\PianoPath.exe + Assets\)
;   2. iscc installer\Keyflow.iss          (or open this file in the Inno Setup Compiler and press F9)
;   → installer\Output\Keyflow-Setup-<version>.exe
;
; Pass /DAppVersion=0.3.0 to override the version, /DSourceDir=..\publish\win-arm64 for another build.
; Keep the fallback below in sync with <Version> in PianoPath.csproj when bumping the release version.

#ifndef AppVersion
  #define AppVersion "0.3.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif

[Setup]
AppId={{9B4E2A61-5C7D-4F03-8E2B-6A1D0C3F7B21}
AppName=Keyflow
AppVersion={#AppVersion}
AppVerName=Keyflow {#AppVersion}
AppPublisher=lxmtuu
AppPublisherURL=https://github.com/lxmtuu/PianoPath
DefaultDirName={autopf}\Keyflow
DefaultGroupName=Keyflow
UninstallDisplayIcon={app}\PianoPath.exe
OutputDir=Output
OutputBaseFilename=Keyflow-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Everything dotnet publish produced: PianoPath.exe plus Assets\ConcertGrand.sf2 and Assets\ATTRIBUTION.txt.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Keyflow"; Filename: "{app}\PianoPath.exe"
Name: "{group}\Uninstall Keyflow"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Keyflow"; Filename: "{app}\PianoPath.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PianoPath.exe"; Description: "{cm:LaunchProgram,Keyflow}"; Flags: nowait postinstall skipifsilent
