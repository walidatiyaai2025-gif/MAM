param(
  [Parameter(Mandatory=$true)][string]$InstallRoot,
  [Parameter(Mandatory=$true)][ValidateSet('Production','UAT')][string]$EnvironmentName,
  [Parameter(Mandatory=$true)][string]$PublicHost,
  [Parameter(Mandatory=$true)][int]$WebPort,
  [Parameter(Mandatory=$true)][string]$StopSignalPath,
  [Parameter(Mandatory=$true)][string]$BrandImagePath,
  [string]$TlsPfxPath = '',
  [string]$TlsSecretPath = '',
  [string]$LogPath = ''
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
# ProtectedData lives in System.Security on Windows PowerShell 5.1. The TLS and
# X509 types used below are already available through the framework's System
# assembly; attempting Add-Type with namespace names such as System.Net.Security
# is not portable to Windows PowerShell 5.1 and can terminate the maintenance
# process before the TCP listener starts.
Add-Type -AssemblyName System.Security -ErrorAction Stop

if([string]::IsNullOrWhiteSpace($LogPath)){
  $LogPath=Join-Path $env:ProgramData 'Diwan Al Amiri\MAM\logs\maintenance-host.log'
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null
Remove-Item -LiteralPath $StopSignalPath -Force -ErrorAction SilentlyContinue

function Log([string]$Message){
  try{
    ('{0:o} {1}' -f [DateTimeOffset]::Now,$Message) | Add-Content -LiteralPath $LogPath -Encoding UTF8
  }catch{}
}

function Unprotect-Secret([string]$Path){
  if(-not(Test-Path -LiteralPath $Path -PathType Leaf)){throw "Protected secret not found: $Path"}
  $protected=[IO.File]::ReadAllBytes($Path)
  $plain=[Security.Cryptography.ProtectedData]::Unprotect($protected,$null,[Security.Cryptography.DataProtectionScope]::LocalMachine)
  try{return [Text.Encoding]::UTF8.GetString($plain)}
  finally{if($plain){[Array]::Clear($plain,0,$plain.Length)}}
}

function Read-RequestHeaders([IO.Stream]$Stream){
  $reader=New-Object IO.StreamReader($Stream,[Text.Encoding]::ASCII,$false,4096,$true)
  try{
    $null=$reader.ReadLine()
    while($true){
      $line=$reader.ReadLine()
      if($null -eq $line -or $line -eq ''){break}
    }
  }finally{$reader.Dispose()}
}

function Build-MaintenanceHtml {
  $logo=''
  try{
    if(Test-Path -LiteralPath $BrandImagePath -PathType Leaf){
      $bytes=[IO.File]::ReadAllBytes($BrandImagePath)
      $logo='<img class="crest" src="data:image/png;base64,'+[Convert]::ToBase64String($bytes)+'" alt="شعار الديوان الأميري">'
    }
  }catch{}

  return @"
<!doctype html>
<html lang="ar" dir="rtl">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <meta http-equiv="refresh" content="30">
  <title>الموقع تحت الصيانة · Diwan Al Amiri MAM</title>
  <style>
    :root{--navy:#062747;--deep:#031d36;--cyan:#2f9fe8;--gold:#d6a63b;--ink:#15324b}
    *{box-sizing:border-box}
    html,body{height:100%;margin:0}
    body{font-family:"Segoe UI",Tahoma,Arial,sans-serif;background:linear-gradient(135deg,#f4f8fc 0%,#eaf3fb 100%);color:var(--ink);display:grid;place-items:center;padding:24px}
    .card{width:min(720px,100%);background:#fff;border:1px solid #d8e6f3;border-radius:24px;box-shadow:0 24px 70px rgba(4,35,67,.16);overflow:hidden}
    .brand{background:linear-gradient(135deg,var(--deep),var(--navy));color:#fff;padding:28px 34px;display:flex;align-items:center;gap:18px;border-bottom:4px solid var(--gold)}
    .crest{width:72px;height:72px;object-fit:contain;background:#fff;border-radius:14px;padding:5px;flex:0 0 auto}
    .brand-copy strong{display:block;font-size:24px;line-height:1.3}
    .brand-copy span{display:block;color:#cfe4f6;margin-top:5px;font-size:14px}
    .content{text-align:center;padding:46px 34px 40px}
    .spinner{width:54px;height:54px;border:5px solid #d8e9f7;border-top-color:var(--cyan);border-radius:50%;margin:0 auto 24px;animation:spin 1s linear infinite}
    h1{margin:0;color:var(--navy);font-size:31px}
    p{margin:14px auto 0;max-width:520px;font-size:18px;line-height:1.9;color:#526d82}
    .en{margin-top:8px;font-size:14px;color:#7990a2;direction:ltr}
    .status{display:inline-flex;align-items:center;gap:8px;margin-top:28px;padding:9px 15px;border-radius:999px;background:#eef8ff;color:#17689d;font-size:13px;font-weight:700}
    .dot{width:8px;height:8px;border-radius:50%;background:#2aa56b;box-shadow:0 0 0 5px rgba(42,165,107,.12)}
    footer{text-align:center;padding:17px 24px;background:#f8fbfe;border-top:1px solid #e6eff7;color:#7890a5;font-size:12px}
    @keyframes spin{to{transform:rotate(360deg)}}
  </style>
</head>
<body>
  <main class="card">
    <section class="brand">
      $logo
      <div class="brand-copy">
        <strong>الديوان الأميري</strong>
        <span>نظام إدارة الأصول الإعلامية · Media Asset Management</span>
      </div>
    </section>
    <section class="content">
      <div class="spinner" aria-hidden="true"></div>
      <h1>الموقع تحت الصيانة</h1>
      <p>يجري الآن تحديث نظام إدارة الأصول الإعلامية. يرجى المحاولة خلال دقائق، وسيعود الموقع للعمل تلقائياً بعد اكتمال التحديث.</p>
      <div class="en">The MAM service is being updated. Please try again in a few minutes.</div>
      <div class="status"><span class="dot"></span><span>تحديث آمن قيد التنفيذ</span></div>
    </section>
    <footer>Diwan Al Amiri · Media Asset Management</footer>
  </main>
</body>
</html>
"@
}

$certificate=$null
$tlsPassword=$null
$listener=$null
$html=Build-MaintenanceHtml
$htmlBytes=[Text.Encoding]::UTF8.GetBytes($html)

try{
  if($EnvironmentName -eq 'Production'){
    if([string]::IsNullOrWhiteSpace($TlsPfxPath) -or -not(Test-Path -LiteralPath $TlsPfxPath -PathType Leaf)){
      throw 'Production maintenance mode requires the existing MAM PFX certificate.'
    }
    $tlsPassword=Unprotect-Secret $TlsSecretPath
    $certificate=New-Object Security.Cryptography.X509Certificates.X509Certificate2(
      $TlsPfxPath,
      $tlsPassword,
      [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet
    )
  }

  $listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Any,$WebPort)
  $listener.Start()
  Log "Maintenance host listening on 0.0.0.0:$WebPort environment=$EnvironmentName host=$PublicHost"

  while(-not(Test-Path -LiteralPath $StopSignalPath)){
    $accept=$listener.AcceptTcpClientAsync()
    while(-not $accept.Wait(500)){
      if(Test-Path -LiteralPath $StopSignalPath){break}
    }
    if(Test-Path -LiteralPath $StopSignalPath){break}
    if(-not $accept.IsCompleted){continue}

    $client=$null
    $network=$null
    $io=$null
    $ssl=$null
    try{
      $client=$accept.Result
      $network=$client.GetStream()
      $io=$network

      if($EnvironmentName -eq 'Production'){
        $ssl=New-Object Net.Security.SslStream($network,$false)
        $ssl.AuthenticateAsServer(
          $certificate,
          $false,
          [Security.Authentication.SslProtocols]::Tls12,
          $false
        )
        $io=$ssl
      }

      Read-RequestHeaders -Stream $io
      $crlf=[string][char]13 + [string][char]10
      $headers=@(
        'HTTP/1.1 503 Service Unavailable',
        'Content-Type: text/html; charset=utf-8',
        ('Content-Length: '+$htmlBytes.Length),
        'Cache-Control: no-store, no-cache, must-revalidate',
        'Pragma: no-cache',
        'Retry-After: 120',
        'Connection: close',
        'X-MAM-Maintenance: 1',
        '',
        ''
      ) -join $crlf
      $headerBytes=[Text.Encoding]::ASCII.GetBytes($headers)
      $io.Write($headerBytes,0,$headerBytes.Length)
      $io.Write($htmlBytes,0,$htmlBytes.Length)
      $io.Flush()
    }catch{
      Log ('Request failed: '+$_.Exception.Message)
    }finally{
      if($ssl){$ssl.Dispose()}
      elseif($network){$network.Dispose()}
      if($client){$client.Close()}
    }
  }

  Log 'Maintenance host received stop signal.'
}
catch{
  Log ('Maintenance host failed: '+($_ | Out-String))
  exit 1
}
finally{
  try{if($listener){$listener.Stop()}}catch{}
  try{if($certificate){$certificate.Dispose()}}catch{}
  $tlsPassword=$null
  Log 'Maintenance host stopped.'
}
