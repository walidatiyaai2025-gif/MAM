#ifndef MyVersion
  #error MyVersion is required
#endif
#ifndef NumericVersion
  #error NumericVersion is required
#endif
#ifndef SourceRoot
  #error SourceRoot is required
#endif
#ifndef BrandRoot
  #error BrandRoot is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif

[Setup]
AppId={{8B3C114E-5D62-4F36-AE1D-D23C8CE04A91}
AppName=Diwan Al Amiri MAM Desktop
AppVerName=Diwan Al Amiri MAM Desktop {#MyVersion}
AppVersion={#MyVersion}
AppPublisher=Diwan Al Amiri
DefaultDirName={autopf}\Diwan Al Amiri\MAM Desktop
DefaultGroupName=Diwan Al Amiri MAM
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=DiwanMAM-Desktop-Setup-{#MyVersion}-x64
SetupIconFile={#BrandRoot}\diwan-setup.ico
WizardImageFile={#BrandRoot}\wizard-large.bmp
WizardSmallImageFile={#BrandRoot}\wizard-small.bmp
WizardStyle=modern
WizardSizePercent=120
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\MAM.Desktop.exe
VersionInfoCompany=Diwan Al Amiri
VersionInfoDescription=Diwan Al Amiri Media Asset Management Desktop Setup
VersionInfoProductName=Diwan Al Amiri MAM Desktop
VersionInfoVersion={#NumericVersion}

[Files]
Source: "{#SourceRoot}\desktop\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\Diwan Al Amiri\MAM"

[Icons]
Name: "{autoprograms}\Diwan Al Amiri MAM"; Filename: "{app}\MAM.Desktop.exe"; WorkingDir: "{app}"; IconFilename: "{app}\MAM.Desktop.exe"
Name: "{autodesktop}\Diwan Al Amiri MAM"; Filename: "{app}\MAM.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut / إنشاء اختصار على سطح المكتب"; GroupDescription: "Shortcuts / الاختصارات:"; Flags: unchecked

[Run]
Filename: "{app}\MAM.Desktop.exe"; Description: "Launch Diwan Al Amiri MAM / تشغيل نظام الديوان الأميري"; Flags: nowait postinstall skipifsilent

[Code]
var
  ApiPage: TInputQueryWizardPage;
  CapturePage: TInputQueryWizardPage;
  PreservePage: TInputOptionWizardPage;
  ExistingConfig: Boolean;

function JsonEscape(Value: String): String;
begin
  Value := StringChangeEx(Value, '\', '\\', True);
  Value := StringChangeEx(Value, '"', '\"', True);
  Result := Value;
end;

function ParamOrDefault(Name, DefaultValue: String): String;
var V: String;
begin
  V := ExpandConstant('{param:' + Name + '|}');
  if V = '' then Result := DefaultValue else Result := V;
end;

procedure InitializeWizard;
begin
  WizardForm.Caption := 'Diwan Al Amiri · Media Asset Management';
  WizardForm.WelcomeLabel1.Caption := 'Diwan Al Amiri Media Asset Management';
  WizardForm.WelcomeLabel2.Caption := 'Premium Desktop installation · تثبيت تطبيق الديوان الأميري' + #13#10 + #13#10 +
    'All client configuration is completed inside this setup. No manual file or environment-variable editing is required.';

  ApiPage := CreateInputQueryPage(wpSelectDir,
    'Central API / الخدمة المركزية',
    'Desktop connection settings',
    'Enter the Central API base URL. This value is stored by Setup and loaded automatically by the Desktop application.');
  ApiPage.Add('Central API URL:', False);
  ApiPage.Values[0] := ParamOrDefault('APIURL', 'https://mam-api.diwan.local');

  CapturePage := CreateInputQueryPage(ApiPage.ID,
    'Capture workspace / مساحة التسجيل',
    'Local workstation settings',
    'The cache path is managed by Setup. Leave the provider empty until a certified real-hardware provider is approved for this workstation.');
  CapturePage.Add('Capture cache path:', False);
  CapturePage.Add('Certified capture provider (optional):', False);
  CapturePage.Values[0] := ParamOrDefault('CAPTURECACHE', '%LOCALAPPDATA%\Diwan Al Amiri\MAM\CaptureCache');
  CapturePage.Values[1] := ParamOrDefault('CAPTUREPROVIDER', '');

  ExistingConfig := FileExists(ExpandConstant('{commonappdata}\Diwan Al Amiri\MAM\desktop.setup.json'));
  PreservePage := CreateInputOptionPage(CapturePage.ID,
    'Upgrade behavior / سلوك الترقية',
    'Configuration preservation',
    'Choose whether an existing Desktop configuration should be preserved.', True, False);
  PreservePage.Add('Preserve existing configuration (recommended) / الاحتفاظ بالإعدادات الحالية');
  PreservePage.Add('Replace configuration with the values entered in this Setup / استبدال الإعدادات');
  if ExistingConfig then PreservePage.SelectedValueIndex := 0 else PreservePage.SelectedValueIndex := 1;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var Url: String;
begin
  Result := True;
  if CurPageID = ApiPage.ID then begin
    Url := Trim(ApiPage.Values[0]);
    if (Url = '') or ((Pos('https://', Lowercase(Url)) <> 1) and (Pos('http://', Lowercase(Url)) <> 1)) then begin
      MsgBox('Enter an absolute HTTP/HTTPS Central API URL.', mbError, MB_OK);
      Result := False;
    end;
  end;
  if CurPageID = CapturePage.ID then begin
    if Trim(CapturePage.Values[0]) = '' then begin
      MsgBox('Capture cache path is required.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure WriteDesktopConfiguration;
var Path, Json: String;
begin
  Path := ExpandConstant('{commonappdata}\Diwan Al Amiri\MAM\desktop.setup.json');
  if ExistingConfig and (PreservePage.SelectedValueIndex = 0) then Exit;
  Json := '{' + #13#10 +
    '  "apiBaseUrl": "' + JsonEscape(Trim(ApiPage.Values[0])) + '",' + #13#10 +
    '  "captureCacheRoot": "' + JsonEscape(Trim(CapturePage.Values[0])) + '",' + #13#10 +
    '  "captureProvider": "' + JsonEscape(Trim(CapturePage.Values[1])) + '"' + #13#10 +
    '}' + #13#10;
  if not SaveStringToFile(Path, Json, False) then
    RaiseException('Unable to write Desktop configuration.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then WriteDesktopConfiguration;
end;
