#!/usr/bin/env bash
set -euo pipefail

root="src/MAM.Web/wwwroot"
version="0.12.18-p12.15"

if [[ ! -d "$root" ]]; then
  echo "Missing web root: $root" >&2
  exit 1
fi

echo "Checking for public-internet runtime dependencies under $root ..."

# Runtime web assets must not reach public CDNs, font services, or arbitrary HTTP(S) URLs.
# Relative same-origin URLs are the required deployment contract for the air-gapped portal.
mapfile -t offenders < <(
  grep -RInE --include='*.html' --include='*.css' --include='*.js' \
    '(https?:)?//(cdn\.|cdn.jsdelivr.net|cdnjs.cloudflare.com|unpkg.com|fonts.googleapis.com|fonts.gstatic.com)|https?://' \
    "$root" || true
)

if (( ${#offenders[@]} > 0 )); then
  printf '%s\n' "${offenders[@]}" >&2
  echo "FAIL: public-internet runtime dependency detected." >&2
  exit 2
fi

echo "No public-internet runtime dependency detected."

index="$root/index.html"
landing="$root/landing.html"
fonts="$root/fonts.css"

for file in "$index" "$landing" "$fonts" "$root/offline-runtime.css" "$root/offline-runtime.js"; do
  [[ -f "$file" ]] || { echo "Missing required air-gap asset: $file" >&2; exit 3; }
done

if grep -qiE '@import[[:space:]]+url\(["'\'' ]*https?://' "$fonts"; then
  echo "FAIL: fonts.css still imports a remote font." >&2
  exit 4
fi

if ! grep -Fq "/offline-runtime.css?v=$version" "$index"; then
  echo "FAIL: app shell does not load versioned offline-runtime.css." >&2
  exit 5
fi

if ! grep -Fq "/offline-runtime.js?v=$version" "$index"; then
  echo "FAIL: app shell does not load versioned offline-runtime.js." >&2
  exit 6
fi

# The final navigation owner and preferences layer must both be cache-busted to the
# release version so Chrome/Edge cannot mix old and new menu runtimes after upgrade.
if ! grep -Fq "/p132-navigation-final.js?v=$version" "$index"; then
  echo "FAIL: final navigation owner is not release-version cache-busted." >&2
  exit 7
fi

if ! grep -Fq "/p1214-navigation-management.js?v=$version" "$index"; then
  echo "FAIL: navigation-management runtime is not release-version cache-busted." >&2
  exit 8
fi

p132_line=$(grep -nF "/p132-navigation-final.js?v=$version" "$index" | head -n1 | cut -d: -f1)
p1214_line=$(grep -nF "/p1214-navigation-management.js?v=$version" "$index" | head -n1 | cut -d: -f1)
if (( p132_line >= p1214_line )); then
  echo "FAIL: p132 final navigation owner must load before p1214 preferences." >&2
  exit 9
fi

if ! grep -Fq "document.addEventListener('click'" "$root/p132-navigation-final.js"; then
  echo "FAIL: delegated navigation click owner is missing." >&2
  exit 10
fi

if ! grep -Fq "event.stopImmediatePropagation()" "$root/p132-navigation-final.js"; then
  echo "FAIL: navigation final owner no longer blocks stale competing click handlers." >&2
  exit 11
fi

if grep -qiE 'cdn\.jsdelivr\.net|fonts\.googleapis\.com|fonts\.gstatic\.com|cdnjs\.cloudflare\.com|unpkg\.com' "$landing" "$index" "$fonts"; then
  echo "FAIL: known CDN/font dependency remains in an entrypoint." >&2
  exit 12
fi

echo "P12.15 AIR-GAP + NAVIGATION CACHE ACCEPTANCE: PASS"
