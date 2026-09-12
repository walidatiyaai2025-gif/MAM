param([int]$RemoveConfiguration = 0)
$ErrorActionPreference='SilentlyContinue'
foreach($name in @('Diwan MAM API','Diwan MAM Web','Diwan MAM Worker')) {
  Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
  Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
  Remove-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue
}
if ($RemoveConfiguration -eq 1) {
  Remove-Item -LiteralPath (Join-Path $env:ProgramData 'Diwan Al Amiri\MAM') -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
