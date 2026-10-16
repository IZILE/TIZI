#ifndef PublishDir
  #error Supply /DPublishDir with the verified payload directory.
#endif
#ifndef DeliveryDir
  #error Supply /DDeliveryDir.
#endif
#ifndef AppVersion
  #define AppVersion "0.1.42"
#endif
#define BrandIcon "TIZI-" + AppVersion + ".ico"
[Setup]
#ifdef TestInstall
AppId={{B13BFC14-092B-47B8-9A42-4B38B0DA72B9}
#else
AppId={{B13BFC14-092B-47B8-9A42-4B38B0DA72B8}
#endif
AppName=TIZI
AppVersion={#AppVersion}
AppPublisher=TIZI
DefaultDirName={localappdata}\Programs\TIZI
DefaultGroupName=TIZI
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
UninstallDisplayName=TIZI
UninstallDisplayIcon={app}\{#BrandIcon}
OutputDir={#DeliveryDir}
OutputBaseFilename=TIZI-Setup
SetupIconFile={#PublishDir}\TIZI.ico
LicenseFile={#PublishDir}\LICENSE
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
CloseApplications=no
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=TIZI
[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
#ifndef TestInstall
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
#endif
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "{#PublishDir}\TIZI.ico"; DestDir: "{app}"; DestName: "{#BrandIcon}"; Flags: ignoreversion
[Icons]
#ifndef TestInstall
Name: "{autoprograms}\TIZI\TIZI"; Filename: "{app}\TIZI.exe"; WorkingDir: "{app}"; IconFilename: "{app}\{#BrandIcon}"
Name: "{autoprograms}\TIZI\Uninstall TIZI"; Filename: "{uninstallexe}"; WorkingDir: "{app}"; IconFilename: "{app}\{#BrandIcon}"
Name: "{autodesktop}\TIZI"; Filename: "{app}\TIZI.exe"; WorkingDir: "{app}"; IconFilename: "{app}\{#BrandIcon}"; Tasks: desktopicon
#endif
[Run]
#ifndef TestInstall
Filename: "{app}\TIZI.exe"; Description: "{cm:LaunchProgram,TIZI}"; Flags: nowait postinstall skipifsilent
#endif
[Registry]
#ifndef TestInstall
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "TIZI"; Flags: uninsdeletevalue
#endif

; User credentials and preferences in LocalAppData\TIZI are deliberately retained on uninstall.
[Code]
function InitializeSetup(): Boolean;
begin
#ifdef TestInstall
  Result := True;
#else
  Result := not CheckForMutexes('Local\TIZI-' + GetUserNameString());
  if not Result then SuppressibleMsgBox('Please close TIZI before installing an update.', mbInformation, MB_OK, IDOK);
#endif
end;

procedure SHChangeNotify(EventId: Integer; Flags: Cardinal; const Item1: String; Item2: NativeInt);
  external 'SHChangeNotify@shell32.dll stdcall';

#ifndef TestInstall
function ImportCommand(): String;
begin
  Result := '"' + ExpandConstant('{app}\TIZI.exe') + '" "%1"';
end;

procedure RegisterImportProtocol(const Scheme: String);
var Existing: String;
begin
  { Keep another client's existing protocol association. }
  if RegQueryStringValue(HKCR, Scheme + '\shell\open\command', '', Existing) then
    if CompareText(Existing, ImportCommand()) <> 0 then Exit;
  RegWriteStringValue(HKCU, 'Software\Classes\' + Scheme, '', 'URL:TIZI subscription import');
  RegWriteStringValue(HKCU, 'Software\Classes\' + Scheme, 'URL Protocol', '');
  RegWriteStringValue(HKCU, 'Software\Classes\' + Scheme + '\shell\open\command', '', ImportCommand());
end;

procedure RemoveOwnImportProtocol(const Scheme: String);
var Existing: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Classes\' + Scheme + '\shell\open\command', '', Existing) then
    if CompareText(Existing, ImportCommand()) = 0 then
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\' + Scheme);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    RemoveOwnImportProtocol('tizi'); RemoveOwnImportProtocol('clash'); RemoveOwnImportProtocol('shadowrocket'); RemoveOwnImportProtocol('sub');
  end;
end;
#endif

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    SHChangeNotify($00002000, $0005, ExpandConstant('{app}\{#BrandIcon}'), 0);
#ifndef TestInstall
    RegisterImportProtocol('tizi'); RegisterImportProtocol('clash'); RegisterImportProtocol('shadowrocket'); RegisterImportProtocol('sub');
    SHChangeNotify($00002000, $0005, ExpandConstant('{autodesktop}\TIZI.lnk'), 0);
    SHChangeNotify($00002000, $0005, ExpandConstant('{autoprograms}\TIZI\TIZI.lnk'), 0);
    SHChangeNotify($00002000, $0005, ExpandConstant('{autoprograms}\TIZI\Uninstall TIZI.lnk'), 0);
#endif
  end;
end;
