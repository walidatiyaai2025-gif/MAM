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
AppId={{6E8CB5EA-974E-4CE9-A7B4-457C6F66E17D}
AppName=Diwan Al Amiri MAM Demo
AppVerName=Diwan Al Amiri MAM Demo {#MyVersion}
AppVersion={#MyVersion}
AppPublisher=Diwan Al Amiri
DefaultDirName={autopf}\Diwan Al Amiri\MAM Demo
DefaultGroupName=Diwan Al Amiri MAM Demo
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=DiwanMAM-Demo-Setup-{#MyVersion}-x64
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
MinVersion=10.0.22000
CloseApplications=yes
RestartApplications=no
VersionInfoCompany=Diwan Al Amiri
VersionInfoDescription=Diwan Al Amiri Media Asset Management Offline Demo Setup
VersionInfoProductName=Diwan Al Amiri MAM Demo
VersionInfoVersion={#NumericVersion}
UninstallDisplayName=Diwan Al Amiri MAM Demo

[Files]
Source: "{#SourceRoot}\demo\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\Diwan Al Amiri\MAM Demo"

[Icons]
Name: "{group}\Open MAM Demo"; Filename: "http://demomam.da.gov.kw/"
Name: "{commondesktop}\MAM Demo"; Filename: "http://demomam.da.gov.kw/"

[Run]
Filename: "http://demomam.da.gov.kw/"; Description: "Open MAM Demo"; Flags: shellexec postinstall skipifsilent nowait

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\setup\Uninstall-MamDemo.ps1"""; Flags: runhidden waituntilterminated

[Code]
function ParamOrDefault(Name, DefaultValue: String): String;
var V: String;
begin
  V := ExpandConstant('{param:' + Name + '|}');
  if V = '' then Result := DefaultValue else Result := V;
end;

function IsIntegerInRange(Value: String; MinValue, MaxValue: Integer): Boolean;
var N, I: Integer; S: String;
begin
  S := Trim(Value);
  if (S = '') or (Length(S) > 5) then begin Result := False; Exit; end;
  for I := 1 to Length(S) do begin
    if Pos(Copy(S, I, 1), '0123456789') = 0 then begin Result := False; Exit; end;
  end;
  N := StrToInt(S);
  Result := (N >= MinValue) and (N <= MaxValue);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsWin64 then
  begin
    MsgBox('MAM Demo requires 64-bit Windows 11.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure InitializeWizard;
begin
  WizardForm.Caption := 'Diwan Al Amiri · MAM Offline Demo';
  WizardForm.WelcomeLabel1.Caption := 'Diwan Al Amiri Media Asset Management · Demo';
  WizardForm.WelcomeLabel2.Caption :=
    'Offline Windows 11 demo · نسخة عرض محلية بدون إنترنت' + #13#10 + #13#10 +
    'This installer includes the API, Web portal and embedded SQLite database. It does not require SQL Server, IIS, Active Directory or Internet access.' + #13#10 +
    'After installation open: http://demomam.da.gov.kw/';
end;

procedure ConfigureDemo;
var Params, PowerShell, ApiPort, WebPort: String; ResultCode: Integer;
begin
  ApiPort := Trim(ParamOrDefault('APIPORT','5099'));
  WebPort := Trim(ParamOrDefault('WEBPORT','80'));
  if not IsIntegerInRange(ApiPort,1,65535) or not IsIntegerInRange(WebPort,1,65535) then
    RaiseException('Demo API and Web ports must be between 1 and 65535.');
  if ApiPort = WebPort then
    RaiseException('Demo API and Web ports must be different.');

  PowerShell := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Params := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\setup\Configure-MamDemo.ps1') + '"' +
    ' -InstallRoot "' + ExpandConstant('{app}') + '"' +
    ' -ApiPort ' + ApiPort + ' -WebPort ' + WebPort;
  if not Exec(PowerShell, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('Unable to launch the MAM Demo configuration engine.');
  if ResultCode <> 0 then
    RaiseException('MAM Demo configuration failed. Review configure-demo-error.log. Exit code: ' + IntToStr(ResultCode));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then ConfigureDemo;
end;