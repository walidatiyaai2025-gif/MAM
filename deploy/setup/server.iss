#ifndef MyVersion
  #error MyVersion is required
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
AppId={{A97CA8AB-17A1-4A88-9C39-B06A2A4D49F7}
AppName=Diwan Al Amiri MAM Server
AppVerName=Diwan Al Amiri MAM Server {#MyVersion}
AppVersion={#MyVersion}
AppPublisher=Diwan Al Amiri
DefaultDirName={autopf}\Diwan Al Amiri\MAM Server
DefaultGroupName=Diwan Al Amiri MAM
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=DiwanMAM-Server-Setup-{#MyVersion}-x64
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
VersionInfoCompany=Diwan Al Amiri
VersionInfoDescription=Diwan Al Amiri Media Asset Management Server Setup
VersionInfoProductName=Diwan Al Amiri MAM Server
VersionInfoVersion={#MyVersion}

[Files]
Source: "{#SourceRoot}\server\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\Diwan Al Amiri\MAM"

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\setup\Uninstall-MamServer.ps1"""; Flags: runhidden waituntilterminated

[Code]
var
  EnvironmentPage: TInputOptionWizardPage;
  NetworkPage, SqlPage, StoragePage, PolicyPage, IdentityPage, TlsPasswordPage: TInputQueryWizardPage;
  ServiceModePage, OptionsPage: TInputOptionWizardPage;
  TlsFilePage: TInputFileWizardPage;
  SqlTemp, ServiceTemp, TlsTemp: String;

function ParamOrDefault(Name, DefaultValue: String): String;
var V: String;
begin
  V := ExpandConstant('{param:' + Name + '|}');
  if V = '' then Result := DefaultValue else Result := V;
end;

function ParamIsOne(Name: String; DefaultValue: Boolean): Boolean;
var V: String;
begin
  V := Lowercase(ExpandConstant('{param:' + Name + '|}'));
  if V = '' then Result := DefaultValue else Result := (V = '1') or (V = 'true') or (V = 'yes');
end;

function SelectedEnvironment: String;
begin
  if EnvironmentPage.SelectedValueIndex = 0 then Result := 'Production' else Result := 'UAT';
end;

function SelectedAuthMode: String;
begin
  Result := PolicyPage.Values[1];
end;

function SelectedServiceMode: String;
begin
  if ServiceModePage.SelectedValueIndex = 0 then Result := 'System' else Result := 'Custom';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = TlsFilePage.ID) or (PageID = TlsPasswordPage.ID) then
    Result := SelectedEnvironment <> 'Production';
  if PageID = IdentityPage.ID then
    Result := SelectedServiceMode <> 'Custom';
end;

procedure InitializeWizard;
var EnvDefault, ModeDefault: String;
begin
  WizardForm.Caption := 'Diwan Al Amiri · Media Asset Management Server';
  WizardForm.WelcomeLabel1.Caption := 'Diwan Al Amiri Media Asset Management';
  WizardForm.WelcomeLabel2.Caption := 'Premium Server/Web installation · تثبيت خادم وبوابة الديوان الأميري' + #13#10 + #13#10 +
    'This wizard configures API, Web, Worker, SQL migrations, storage, secure secrets, startup tasks and firewall rules. No manual configuration-file editing is required.';

  EnvironmentPage := CreateInputOptionPage(wpSelectDir,
    'Deployment environment / بيئة النشر', 'Choose the target environment',
    'Production is fail-closed and requires HTTPS certificate material. UAT can use HTTP for acceptance/testing.', True, False);
  EnvironmentPage.Add('Production · إنتاج (HTTPS required)');
  EnvironmentPage.Add('UAT · اختبار قبول');
  EnvDefault := Lowercase(ParamOrDefault('ENVIRONMENT','Production'));
  if EnvDefault = 'uat' then EnvironmentPage.SelectedValueIndex := 1 else EnvironmentPage.SelectedValueIndex := 0;

  NetworkPage := CreateInputQueryPage(EnvironmentPage.ID,
    'Network / الشبكة', 'Public endpoints',
    'Enter the DNS host and the dedicated API/Web ports. Setup creates the runtime bindings and optional firewall rules.');
  NetworkPage.Add('Public DNS host / اسم الخادم:', False);
  NetworkPage.Add('API port:', False);
  NetworkPage.Add('Web portal port:', False);
  NetworkPage.Values[0] := ParamOrDefault('PUBLICHOST','mam.diwan.local');
  NetworkPage.Values[1] := ParamOrDefault('APIPORT','5080');
  NetworkPage.Values[2] := ParamOrDefault('WEBPORT','5481');

  SqlPage := CreateInputQueryPage(NetworkPage.ID,
    'SQL Server / قاعدة البيانات', 'Authoritative catalog database',
    'Enter a SQL Server connection string including Initial Catalog/Database. Setup can create the database and apply all migrations automatically. The value is DPAPI-encrypted and is never written to appsettings.');
  SqlPage.Add('SQL connection string:', True);

  StoragePage := CreateInputQueryPage(SqlPage.ID,
    'Storage / التخزين', 'Primary and Backup targets',
    'Primary and Backup must be distinct. Local paths are created automatically; UNC/SMB targets are validated at runtime under the selected service identity.');
  StoragePage.Add('Primary storage root:', False);
  StoragePage.Add('Backup storage root:', False);
  StoragePage.Values[0] := ParamOrDefault('PRIMARYROOT','D:\DiwanMAM\Primary');
  StoragePage.Values[1] := ParamOrDefault('BACKUPROOT','E:\DiwanMAM\Backup');

  PolicyPage := CreateInputQueryPage(StoragePage.ID,
    'Policies / السياسات', 'Deployment values managed by Setup',
    'These values are written to the generated production configuration. Site approval remains a separate P12 acceptance requirement.');
  PolicyPage.Add('Database backup policy ID:', False);
  PolicyPage.Add('Authentication mode (ActiveDirectory / OIDC / Local):', False);
  PolicyPage.Add('Recycle retention days:', False);
  PolicyPage.Add('Audit retention days:', False);
  PolicyPage.Add('Audit read policy label:', False);
  PolicyPage.Values[0] := ParamOrDefault('BACKUPPOLICY','OWNER_LAST_PENDING');
  PolicyPage.Values[1] := ParamOrDefault('AUTHMODE','ActiveDirectory');
  PolicyPage.Values[2] := ParamOrDefault('RETENTIONDAYS','30');
  PolicyPage.Values[3] := ParamOrDefault('AUDITRETENTIONDAYS','365');
  PolicyPage.Values[4] := ParamOrDefault('AUDITREADPOLICY','MetadataAndDownloads');

  ServiceModePage := CreateInputOptionPage(PolicyPage.ID,
    'Service identity / هوية التشغيل', 'Choose the Windows runtime identity',
    'Local System is suitable for local storage. Use a managed/domain account when your storage or SQL topology requires network identity.', True, False);
  ServiceModePage.Add('Local System');
  ServiceModePage.Add('Custom Windows/domain service account');
  ModeDefault := Lowercase(ParamOrDefault('SERVICEMODE','System'));
  if ModeDefault = 'custom' then ServiceModePage.SelectedValueIndex := 1 else ServiceModePage.SelectedValueIndex := 0;

  IdentityPage := CreateInputQueryPage(ServiceModePage.ID,
    'Service credentials / بيانات حساب التشغيل', 'Custom Windows/domain identity',
    'The password is used only to register Windows startup tasks and is not written to MAM configuration files.');
  IdentityPage.Add('Account (DOMAIN\user):', False);
  IdentityPage.Add('Password:', True);
  IdentityPage.Values[0] := ParamOrDefault('SERVICEUSER','');

  TlsFilePage := CreateInputFilePage(IdentityPage.ID,
    'Production TLS / شهادة HTTPS', 'Select the production PFX certificate',
    'The PFX is copied into the protected MAM secret store. Production mode will not install without certificate material.');
  TlsFilePage.Add('PFX certificate:', 'PFX certificate files|*.pfx|All files|*.*', '.pfx');
  TlsFilePage.Values[0] := ParamOrDefault('TLSPFX','');

  TlsPasswordPage := CreateInputQueryPage(TlsFilePage.ID,
    'TLS certificate password / كلمة مرور الشهادة', 'Protected certificate credential',
    'The PFX password is stored with Windows DPAPI LocalMachine protection.');
  TlsPasswordPage.Add('PFX password:', True);

  OptionsPage := CreateInputOptionPage(TlsPasswordPage.ID,
    'Automation / الأتمتة', 'Setup-managed deployment actions',
    'Recommended options remove post-install manual steps.', False, False);
  OptionsPage.Add('Create database if needed and apply migrations');
  OptionsPage.Add('Create Domain/Private Windows Firewall rules');
  OptionsPage.Add('Start API, Web and Worker after Setup');
  OptionsPage.Values[0] := ParamIsOne('APPLYMIGRATIONS', True);
  OptionsPage.Values[1] := ParamIsOne('OPENFIREWALL', True);
  OptionsPage.Values[2] := ParamIsOne('STARTSERVICES', True);
end;

function IsIntegerInRange(Value: String; MinValue, MaxValue: Integer): Boolean;
var N: Integer;
begin
  Result := TryStrToInt(Trim(Value), N) and (N >= MinValue) and (N <= MaxValue);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var ApiPort, WebPort: Integer; Auth: String;
begin
  Result := True;
  if CurPageID = NetworkPage.ID then begin
    if Trim(NetworkPage.Values[0]) = '' then begin MsgBox('Public DNS host is required.', mbError, MB_OK); Result := False; Exit; end;
    if not IsIntegerInRange(NetworkPage.Values[1],1,65535) or not IsIntegerInRange(NetworkPage.Values[2],1,65535) then begin MsgBox('Ports must be between 1 and 65535.', mbError, MB_OK); Result := False; Exit; end;
    ApiPort := StrToInt(Trim(NetworkPage.Values[1])); WebPort := StrToInt(Trim(NetworkPage.Values[2]));
    if ApiPort = WebPort then begin MsgBox('API and Web ports must be different.', mbError, MB_OK); Result := False; Exit; end;
  end;
  if CurPageID = SqlPage.ID then begin
    if (Trim(SqlPage.Values[0]) = '') and (ExpandConstant('{param:SQLSECRETFILE|}') = '') then begin MsgBox('SQL connection string is required.', mbError, MB_OK); Result := False; Exit; end;
  end;
  if CurPageID = StoragePage.ID then begin
    if (Trim(StoragePage.Values[0]) = '') or (Trim(StoragePage.Values[1]) = '') or (CompareText(Trim(StoragePage.Values[0]),Trim(StoragePage.Values[1])) = 0) then begin MsgBox('Primary and Backup storage roots must be non-empty and distinct.', mbError, MB_OK); Result := False; Exit; end;
  end;
  if CurPageID = PolicyPage.ID then begin
    Auth := Trim(PolicyPage.Values[1]);
    if (CompareText(Auth,'ActiveDirectory') <> 0) and (CompareText(Auth,'OIDC') <> 0) and (CompareText(Auth,'Local') <> 0) then begin MsgBox('Authentication mode must be ActiveDirectory, OIDC or Local.', mbError, MB_OK); Result := False; Exit; end;
    if Trim(PolicyPage.Values[0]) = '' then begin MsgBox('Database backup policy ID is required.', mbError, MB_OK); Result := False; Exit; end;
    if not IsIntegerInRange(PolicyPage.Values[2],1,36500) or not IsIntegerInRange(PolicyPage.Values[3],1,36500) then begin MsgBox('Retention values must be positive day counts.', mbError, MB_OK); Result := False; Exit; end;
  end;
  if CurPageID = IdentityPage.ID then begin
    if (Trim(IdentityPage.Values[0]) = '') or (IdentityPage.Values[1] = '') then begin MsgBox('Custom service account and password are required.', mbError, MB_OK); Result := False; Exit; end;
  end;
  if CurPageID = TlsFilePage.ID then begin
    if (Trim(TlsFilePage.Values[0]) = '') or not FileExists(TlsFilePage.Values[0]) then begin MsgBox('Production requires an existing PFX certificate.', mbError, MB_OK); Result := False; Exit; end;
  end;
  if CurPageID = TlsPasswordPage.ID then begin
    if TlsPasswordPage.Values[0] = '' then begin MsgBox('PFX password is required for Production.', mbError, MB_OK); Result := False; Exit; end;
  end;
end;

procedure CopySecretToTemp(SourcePath, DestinationPath: String);
var Content: AnsiString;
begin
  if not LoadStringFromFile(SourcePath, Content) then RaiseException('Unable to read secret input file.');
  if not SaveStringToFile(DestinationPath, String(Content), False) then RaiseException('Unable to prepare protected secret input.');
end;

procedure PrepareSecrets;
var ExternalSql: String;
begin
  SqlTemp := ExpandConstant('{tmp}\mam-sql.secret');
  ServiceTemp := ExpandConstant('{tmp}\mam-service.secret');
  TlsTemp := ExpandConstant('{tmp}\mam-tls.secret');
  ExternalSql := ExpandConstant('{param:SQLSECRETFILE|}');
  if ExternalSql <> '' then CopySecretToTemp(ExternalSql, SqlTemp)
  else if not SaveStringToFile(SqlTemp, SqlPage.Values[0], False) then RaiseException('Unable to prepare SQL secret.');
  if SelectedServiceMode = 'Custom' then
    if not SaveStringToFile(ServiceTemp, IdentityPage.Values[1], False) then RaiseException('Unable to prepare service-account secret.');
  if SelectedEnvironment = 'Production' then
    if not SaveStringToFile(TlsTemp, TlsPasswordPage.Values[0], False) then RaiseException('Unable to prepare TLS secret.');
end;

function BoolInt(Value: Boolean): String;
begin if Value then Result := '1' else Result := '0'; end;

procedure ConfigureServer;
var Params, PfxPath, ServiceUser, PowerShell: String; ResultCode: Integer;
begin
  PrepareSecrets;
  PowerShell := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  PfxPath := ''; if SelectedEnvironment = 'Production' then PfxPath := TlsFilePage.Values[0];
  ServiceUser := ''; if SelectedServiceMode = 'Custom' then ServiceUser := IdentityPage.Values[0];
  Params := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\setup\Configure-MamServer.ps1') + '"' +
    ' -InstallRoot "' + ExpandConstant('{app}') + '"' +
    ' -EnvironmentName "' + SelectedEnvironment + '"' +
    ' -PublicHost "' + Trim(NetworkPage.Values[0]) + '"' +
    ' -ApiPort ' + Trim(NetworkPage.Values[1]) + ' -WebPort ' + Trim(NetworkPage.Values[2]) +
    ' -SqlSecretInputPath "' + SqlTemp + '"' +
    ' -PrimaryRoot "' + Trim(StoragePage.Values[0]) + '" -BackupRoot "' + Trim(StoragePage.Values[1]) + '"' +
    ' -BackupPolicyId "' + Trim(PolicyPage.Values[0]) + '" -AuthMode "' + Trim(PolicyPage.Values[1]) + '"' +
    ' -RetentionDays ' + Trim(PolicyPage.Values[2]) + ' -AuditRetentionDays ' + Trim(PolicyPage.Values[3]) +
    ' -AuditReadPolicy "' + Trim(PolicyPage.Values[4]) + '"' +
    ' -ServiceMode "' + SelectedServiceMode + '" -ServiceUser "' + ServiceUser + '" -ServicePasswordInputPath "' + ServiceTemp + '"' +
    ' -TlsPfxPath "' + PfxPath + '" -TlsPasswordInputPath "' + TlsTemp + '"' +
    ' -ApplyMigrations ' + BoolInt(OptionsPage.Values[0]) + ' -OpenFirewall ' + BoolInt(OptionsPage.Values[1]) + ' -StartServices ' + BoolInt(OptionsPage.Values[2]);
  if not Exec(PowerShell, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then RaiseException('Unable to launch the server configuration engine.');
  if ResultCode <> 0 then RaiseException('Server configuration failed. Review the Setup log. Exit code: ' + IntToStr(ResultCode));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then ConfigureServer;
end;
