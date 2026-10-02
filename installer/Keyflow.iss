; Inno Setup script (https://jrsoftware.org/isinfo.php, version 6.3 or newer) that wraps a published
; self-contained build into a classic Windows installer, in English and Vietnamese (the wizard text
; lives in Languages\Vietnamese.isl — see the [Languages] section below).
;
;   1. .\publish.ps1                       (creates publish\win-x64\PianoPath.exe + Assets\)
;   2. iscc installer\Keyflow.iss          (or open this file in the Inno Setup Compiler and press F9)
;   → installer\Output\Keyflow-Setup-<version>.exe
;
; Pass /DAppVersion=1.0.0 to override the version, /DSourceDir=..\publish\win-arm64 for another build.
; Keep the fallback below in sync with <Version> in PianoPath.csproj when bumping the release version.
; Every relative path in this file is relative to the folder this script lives in.

#ifndef AppVersion
  #define AppVersion "1.0.0"
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
AppPublisherURL=https://github.com/lxmtuu/PianoPath-Releases
AppCopyright=© 2026 Yami · Neyu — Contributor: Jin
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
; The setup program's own file properties, so a downloaded Keyflow-Setup-<version>.exe says which
; release it is before anybody runs it — worth having while the packages carry no signature.
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName=Keyflow

[Languages]
; Every language listed here is compiled into the one setup program; Setup then picks the language
; whose LanguageID matches Windows (ShowLanguageDialog defaults to yes, so the user can override the
; choice on the Select Language page). Vietnamese is a *partial* translation — it overrides the
; messages this installer actually shows and lets the rest fall back to English — so it lists
; compiler:Default.isl first and Languages\Vietnamese.isl last: the files are read in order and the
; last one wins per message. Paths without the "compiler:" prefix are relative to this script.
; tools/check_sources.py checks the names in that file against installer\Languages\messages.txt.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "vietnamese"; MessagesFile: "compiler:Default.isl,Languages\Vietnamese.isl"

[LangOptions]
; LanguageName/LanguageID/LanguageCodePage normally live in the .isl file, but Vietnamese.isl only
; overrides messages, so Default.isl has already filled these three in. The "vietnamese." prefix is
; the documented way to give one language different values — and it is required: Inno Setup stops
; with `Language option "LanguageID" cannot be applied to all languages` (and the same for
; LanguageName and LanguageCodePage) when a directive without a prefix would hit more than one language.
; $041e is Vietnamese (0x041e); 1258 is its code page, which the compiler uses only if the .isl is
; stored as ANSI — ours is UTF-8 with a BOM, which Inno Setup detects on its own.
vietnamese.LanguageName=Tiếng Việt
vietnamese.LanguageID=$041e
vietnamese.LanguageCodePage=1258

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
