#!/usr/bin/env bash
set -euo pipefail

fail() { echo "P07 boundary acceptance FAILED: $*" >&2; exit 1; }

if grep -RInE 'ICaptureProvider|MAM\.Application\.Capture|SimulatedCaptureProvider|CaptureRecoveryManifestStore' src/MAM.Web src/MAM.Api src/MAM.Worker --exclude-dir=obj --exclude-dir=bin; then
  fail "Professional capture runtime leaked into Web/API/Worker source."
fi

if grep -RInE 'SqlConnection|Microsoft\.Data\.SqlClient|FileSystemStorageObjectStore|BackupStorage|PrimaryStorage' src/MAM.Desktop --exclude-dir=obj --exclude-dir=bin; then
  fail "Desktop gained a direct SQL/storage implementation path."
fi

[[ -f src/MAM.Application/Capture/ICaptureProvider.cs ]] || fail "Capture provider boundary missing."
[[ -f src/MAM.Infrastructure/Capture/SimulatedCaptureProvider.cs ]] || fail "CI simulator missing."

grep -q 'CI/development-only' src/MAM.Infrastructure/Capture/SimulatedCaptureProvider.cs || fail "Simulator must be explicitly non-production evidence."
grep -q 'simulator evidence does not satisfy approved real-hardware certification' tests/MAM.P07.CaptureAcceptance.Checks/Program.cs || fail "Real-hardware gate disclaimer missing."

echo "P07 Windows-only capture/security boundary acceptance: PASS"
