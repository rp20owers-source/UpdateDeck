#ifndef AppArch
  #define AppArch "x64"
#endif
#ifndef PublishDir
  #error PublishDir must be provided
#endif
#ifndef ArtifactDir
  #error ArtifactDir must be provided
#endif
#define AppVersion "2.0.1"
[Setup]
AppId={{0EF48617-880D-479C-B39E-24B886120B80}
AppName=UpdateDeck
AppVersion={#AppVersion}
AppVerName=UpdateDeck {#AppVersion}
AppPublisher=Ryan Powers
VersionInfoVersion={#AppVersion}
VersionInfoDescription=UpdateDeck software maintenance
DefaultDirName={autopf}\UpdateDeck
DefaultGroupName=UpdateDeck
DisableProgramGroupPage=yes
OutputDir={#ArtifactDir}
OutputBaseFilename=UpdateDeck-{#AppVersion}-win-{#AppArch}-Setup
SetupIconFile=..\src\UpdateDeck.App\Assets\UpdateDeck.ico
UninstallDisplayIcon={app}\UpdateDeck.exe
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
MinVersion=10.0.19041
#if AppArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
SetupArchitecture=x86
#else
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
SetupArchitecture=x64
#endif
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
AppMutex=UpdateDeck.Running
RestartApplications=no
Uninstallable=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
UsePreviousPrivileges=yes
DisableWelcomePage=no
LicenseFile=..\LICENSE
InfoBeforeFile=INSTALL-NOTES.txt
[Tasks]
Name: desktopicon; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "portable.mode,Data\*,*.pdb"
[Icons]
Name: "{autoprograms}\UpdateDeck"; Filename: "{app}\UpdateDeck.exe"; IconFilename: "{app}\Assets\UpdateDeck.ico"
Name: "{autodesktop}\UpdateDeck"; Filename: "{app}\UpdateDeck.exe"; IconFilename: "{app}\Assets\UpdateDeck.ico"; Tasks: desktopicon
[Run]
Filename: "{app}\UpdateDeck.exe"; Description: "Open UpdateDeck"; Flags: nowait postinstall skipifsilent runasoriginaluser
[Code]
function RegisteredVersionAllowed(RootKey: Integer): Boolean;
var Version: String; Packed: Int64;
begin
  Result := True;
  if RegQueryStringValue(RootKey, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{0EF48617-880D-479C-B39E-24B886120B80}_is1', 'DisplayVersion', Version) then
  begin
    if not StrToVersion(Version, Packed) then
    begin
      SuppressibleMsgBox('The installed UpdateDeck version could not be verified. Inspect its uninstall registration before replacing it.', mbError, MB_OK, IDOK);
      Result := False;
    end
    else if ComparePackedVersion(Packed, PackVersionComponents(2, 0, 1, 0)) > 0 then
    begin
      SuppressibleMsgBox('A newer UpdateDeck version is installed. This preview will not downgrade it.', mbError, MB_OK, IDOK);
      Result := False;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  { Check both registrations independently: an older per-user entry must not
    hide a newer machine installation. Unknown versions require inspection. }
  Result := RegisteredVersionAllowed(HKCU);
  if Result then
    Result := RegisteredVersionAllowed(HKLM);
end;
