#!/usr/bin/env bash
set -euo pipefail

fail() { echo "P07 boundary acceptance FAILED: $*" >&2; exit 1; }

if grep -RInE 'ICaptureProvider|MAM\.Application\.Capture|SimulatedCaptureProvider|CaptureRecoveryManifestStore|MAM\.Capture\.Windows' src/MAM.Web src/MAM.Api src/MAM.Worker --exclude-dir=obj --exclude-dir=bin; then
  fail "Professional capture runtime leaked into Web/API/Worker source."
fi

if grep -RInE 'SqlConnection|Microsoft\.Data\.SqlClient|FileSystemStorageObjectStore|BackupStorage|PrimaryStorage' src/MAM.Desktop --exclude-dir=obj --exclude-dir=bin; then
  fail "Desktop gained a direct SQL/storage implementation path."
fi

[[ -f src/MAM.Application/Capture/ICaptureProvider.cs ]] || fail "Capture provider boundary missing."
[[ -f src/MAM.Capture.Windows/MAM.Capture.Windows.csproj ]] || fail "Isolated Windows capture runtime project missing."
[[ -f src/MAM.Capture.Windows/SimulatedCaptureProvider.cs ]] || fail "CI simulator missing from isolated capture layer."
[[ -f src/MAM.Capture.Windows/CaptureRecoveryManifestStore.cs ]] || fail "Durable capture recovery store missing from isolated capture layer."
[[ -f src/MAM.Desktop/MainWindow.P07.cs ]] || fail "Connected Windows capture workspace missing."
[[ -f docs/phase-evidence/P07_REAL_HARDWARE_ACCEPTANCE.md ]] || fail "Owner-last real-hardware acceptance package missing."
[[ -f eng/p07-real-hardware-evidence.ps1 ]] || fail "Owner-last evidence validator missing."

if grep -q 'MAM.Infrastructure/MAM.Infrastructure.csproj' src/MAM.Desktop/MAM.Desktop.csproj; then
  fail "Desktop must not reference the general Infrastructure project."
fi
if grep -q 'MAM.Infrastructure/MAM.Infrastructure.csproj' src/MAM.Capture.Windows/MAM.Capture.Windows.csproj; then
  fail "Windows capture runtime must not reference the general Infrastructure project."
fi
grep -q 'MAM.Capture.Windows/MAM.Capture.Windows.csproj' src/MAM.Desktop/MAM.Desktop.csproj || fail "Desktop does not reference isolated capture runtime."
grep -q 'MamUploadApiClient' src/MAM.Desktop/MainWindow.P07.cs || fail "Capture workspace does not hand finalized media through Central API durable upload."
grep -q 'MamProtectionApiClient\|_protectionClient' src/MAM.Desktop/MainWindow.P07.cs || fail "Capture workspace does not preserve P06 protection handoff visibility."
grep -q 'BackupProtectionState.Protected' src/MAM.Desktop/MainWindow.P07.cs || fail "Capture cache cleanup is not gated on verified Backup protection."
grep -q 'CI/development-only' src/MAM.Capture.Windows/SimulatedCaptureProvider.cs || fail "Simulator must be explicitly non-production evidence."
grep -q 'simulator evidence does not satisfy approved real-hardware certification' tests/MAM.P07.CaptureAcceptance.Checks/Program.cs || fail "Real-hardware gate disclaimer missing."

if grep -q '\$IsWindows' eng/p07-real-hardware-evidence.ps1; then
  fail "Owner-last validator must remain compatible with Windows PowerShell 5.1."
fi
pwsh -NoProfile -Command '$tokens=$null; $parseErrors=$null; [void][System.Management.Automation.Language.Parser]::ParseFile("eng/p07-real-hardware-evidence.ps1", [ref]$tokens, [ref]$parseErrors); if ($parseErrors.Count -gt 0) { $parseErrors | ForEach-Object { Write-Error $_.Message }; exit 1 }' || fail "Owner-last PowerShell evidence validator has syntax errors."

echo "P07 Windows-only capture/security boundary acceptance: PASS"
