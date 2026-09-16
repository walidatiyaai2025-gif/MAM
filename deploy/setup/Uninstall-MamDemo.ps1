param([switch]$RemoveDemoData)
$ErrorActionPreference='Stop'
foreach($task in @('Diwan MAM Demo API','Diwan MAM Demo Web')){
  Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
  Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue
}
$hosts=Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$tag='# Diwan MAM Demo'
if(Test-Path -LiteralPath $hosts){
  $lines=Get-Content -LiteralPath $hosts | Where-Object {$_ -notmatch [regex]::Escape($tag)}
  Set-Content -LiteralPath $hosts -Value $lines -Encoding ASCII -Force
  ipconfig /flushdns | Out-Null
}
if($RemoveDemoData){
  $dataRoot=Join-Path $env:ProgramData 'Diwan Al Amiri\MAM Demo'
  Remove-Item -LiteralPath $dataRoot -Recurse -Force -ErrorAction SilentlyContinue
}
