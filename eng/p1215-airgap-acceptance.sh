#!/usr/bin/env bash
set -euo pipefail

root="src/MAM.Web/wwwroot"
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n1)

if [[ -z "$version" ]]; then
  echo "Unable to read product version from Directory.Build.props" >&2
  exit 1
fi

if [[ ! -d "$root" ]]; then
  echo "Missing web root: $root" >&2
  exit 1
fi

echo "Checking for public-internet runtime dependencies under $root ..."

# Air-gap policy is about runtime resource/network dependencies, not plain text that
# happens to mention an https:// example (for example the rich-text link prompt).
# Fail on known public asset hosts anywhere, and on literal remote resource/network
# references in HTML/CSS/JS execution contexts.
offenders=()

while IFS= read -r line; do
  [[ -n "$line" ]] && offenders+=("$line")
done < <(
  grep -RInEi --include='*.html' --include='*.css' --include='*.js' \
    'cdn\.jsdelivr\.net|cdnjs\.cloudflare\.com|unpkg\.com|fonts\.googleapis\.com|fonts\.gstatic\.com' \
    "$root" || true
)

while IFS= read -r line; do
  [[ -n "$line" ]] && offenders+=("$line")
done < <(
  grep -RInEi --include='*.html' \
    '<(script|link|img|source|video|audio|iframe)[^>]+(src|href)[[:space:]]*=[[:space:]]*["'\'']https?://' \
    "$root" || true
)

while IFS= read -r line; do
  [[ -n "$line" ]] && offenders+=("$line")
done < <(
  grep -RInEi --include='*.css' \
    '(@import[^;]*(https?:)?//|url\([[:space:]]*["'\'']?https?://)' \
    "$root" || true
)

while IFS= read -r line; do
  [[ -n "$line" ]] && offenders+=("$line")
done < <(
  grep -RInEi --include='*.js' \
    '(fetch|import)[[:space:]]*\([[:space:]]*["'\'']https?://|new[[:space:]]+WebSocket[[:space:]]*\([[:space:]]*["'\'']wss?://|\.open[[:space:]]*\([^,]+,[[:space:]]*["'\'']https?://' \
    "$root" || true
)

if (( ${#offenders[@]} > 0 )); then
  printf '%s\n' "${offenders[@]}" | sort -u >&2
  echo "FAIL: public-internet runtime dependency detected." >&2
  exit 2
fi

echo "No public-internet runtime dependency detected."

index="$root/index.html"
landing="$root/landing.html"
fonts="$root/fonts.css"
nav_runtime="$root/p132-navigation-final.js"

for file in "$index" "$landing" "$fonts" "$root/offline-runtime.css" "$root/offline-runtime.js" "$nav_runtime"; do
  [[ -f "$file" ]] || { echo "Missing required air-gap/navigation asset: $file" >&2; exit 3; }
done

if grep -qiE '@import[^;]*(https?:)?//' "$fonts"; then
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

if ! grep -Fq "/p132-navigation-final.js?v=$version" "$index"; then
  echo "FAIL: final navigation owner is not release-version cache-busted." >&2
  exit 7
fi

if ! grep -Fq "/p1214-navigation-management.js?v=$version" "$index"; then
  echo "FAIL: navigation-management runtime is not release-version cache-busted." >&2
  exit 8
fi

# Production proved that the resilient owner must be the final external runtime so
# no later navigation/preferences layer can replace or intercept its pointer owner.
p132_line=$(grep -nF "/p132-navigation-final.js?v=$version" "$index" | head -n1 | cut -d: -f1)
p1214_line=$(grep -nF "/p1214-navigation-management.js?v=$version" "$index" | head -n1 | cut -d: -f1)
last_external_script_line=$(grep -nE '<script[[:space:]]+src=' "$index" | tail -n1 | cut -d: -f1)

if (( p132_line <= p1214_line )); then
  echo "FAIL: p132 final navigation owner must load after p1214 preferences." >&2
  exit 9
fi

if (( p132_line != last_external_script_line )); then
  echo "FAIL: p132 final navigation owner must be the last external script." >&2
  exit 10
fi

# Regression contract from the successful production server hotfix:
# - window capture, not document-only delegation;
# - pointerup ownership so an overlay cannot consume the later click;
# - coordinate fallback so event.target may be an unrelated overlay;
# - elementsFromPoint diagnostics for browser hit-test evidence;
# - stopImmediatePropagation to block stale competing handlers.
if ! grep -Fq "window.addEventListener('pointerup'" "$nav_runtime"; then
  echo "FAIL: window-capture pointerup navigation owner is missing." >&2
  exit 11
fi

if ! grep -Fq "function controlAtPoint" "$nav_runtime"; then
  echo "FAIL: coordinate-based sidebar control resolution is missing." >&2
  exit 12
fi

if ! grep -Fq "document.elementsFromPoint" "$nav_runtime"; then
  echo "FAIL: overlay hit-test diagnostics are missing." >&2
  exit 13
fi

if ! grep -Fq "event.stopImmediatePropagation()" "$nav_runtime"; then
  echo "FAIL: navigation final owner no longer blocks stale competing handlers." >&2
  exit 14
fi

if ! grep -Fq "owner: 'window-capture-coordinate-fallback'" "$nav_runtime"; then
  echo "FAIL: navigation runtime ownership marker is incorrect." >&2
  exit 15
fi

if ! grep -Fq "z-index:2147483001" "$nav_runtime"; then
  echo "FAIL: sidebar/nav stacking hardening is missing." >&2
  exit 16
fi

if grep -qiE 'cdn\.jsdelivr\.net|fonts\.googleapis\.com|fonts\.gstatic\.com|cdnjs\.cloudflare\.com|unpkg\.com' "$landing" "$index" "$fonts"; then
  echo "FAIL: known CDN/font dependency remains in an entrypoint." >&2
  exit 17
fi

echo "P12.16 AIR-GAP + OVERLAY-RESILIENT NAVIGATION ACCEPTANCE: PASS"
