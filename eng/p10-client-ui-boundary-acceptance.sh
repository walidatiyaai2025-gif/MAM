#!/usr/bin/env bash
set -euo pipefail

web_html='src/MAM.Web/wwwroot/index.html'
web_js='src/MAM.Web/wwwroot/app.js'
web_css='src/MAM.Web/wwwroot/styles.css'
desktop_xaml='src/MAM.Desktop/MainWindow.xaml'
desktop_cs='src/MAM.Desktop/MainWindow.xaml.cs'
desktop_transport='src/MAM.Desktop/DesktopProductionTransport.cs'
worker='src/MAM.Worker/Program.cs'
config='config/appsettings.Development.template.json'

need() {
  local pattern="$1" file="$2" label="$3"
  grep -Fq -- "$pattern" "$file" || { echo "FAIL: $label ($pattern) missing from $file" >&2; exit 1; }
}

# Web accessibility and Arabic-only direction contract.
need '<html lang="ar" dir="rtl">' "$web_html" 'Web default Arabic language/direction'
need 'name="viewport"' "$web_html" 'responsive viewport'
need 'aria-label="التنقل الرئيسي"' "$web_html" 'navigation accessible name'
need 'alt="شعار الديوان الأميري"' "$web_html" 'brand image alternative text'
need 'aria-live="polite"' "$web_html" 'live status region'
need 'window.mamForceArabicState = forceArabicState;' "$web_html" 'Arabic-only runtime policy'
need "localStorage.setItem('mam.language', 'ar');" "$web_html" 'Arabic-only durable language state'
need "url.searchParams.set('lang', 'ar');" "$web_html" 'Arabic-only URL normalization'
need 'id="languageButton" hidden aria-hidden="true" tabindex="-1"' "$web_html" 'hidden language switch'
need 'aria-label=' "$web_js" 'dynamic form accessible name'
need 'const esc=' "$web_js" 'dynamic HTML escaping helper'
need 'button:focus-visible' "$web_css" 'visible keyboard focus'
need '@media (max-width:820px)' "$web_css" 'tablet/mobile responsive breakpoint'
need '@media (max-width:520px)' "$web_css" 'small-mobile responsive breakpoint'
need '@media (max-width:360px)' "$web_css" 'narrow-mobile responsive breakpoint'
need '@media (prefers-reduced-motion:reduce)' "$web_css" 'reduced-motion accommodation'
need 'border-inline-end' "$web_css" 'direction-aware logical border'
need 'text-align:start' "$web_css" 'direction-aware text alignment'

# Windows accessibility, keyboard focus, bilingual direction and responsive layout contract.
# Desktop remains bilingual; the Arabic-only decision applies to the Web surface in this PR.
need 'AutomationProperties.Name="Diwan Al Amiri crest"' "$desktop_xaml" 'Desktop crest accessible name'
need 'Windows SSO · https://mam.da.gov.kw' "$desktop_xaml" 'Desktop Production Windows SSO identity surface'
need 'ProductionOrigin = "https://mam.da.gov.kw/"' "$desktop_transport" 'Desktop Production gateway lock'
need 'UseDefaultCredentials = true' "$desktop_transport" 'Desktop Windows integrated authentication'
need 'request.Headers.Remove("X-MAM-Dev-User")' "$desktop_transport" 'Desktop development identity stripping'
need 'AutomationProperties.Name="Switch language"' "$desktop_xaml" 'Desktop language switch accessible name'

if grep -Fq -- 'AutomationProperties.Name="User name"' "$desktop_xaml" || grep -Fq -- 'AutomationProperties.Name="Password"' "$desktop_xaml"; then
  echo 'FAIL: standard Production Desktop must not expose local username/password credential fields.' >&2
  exit 1
fi
if grep -Fq -- 'demo.operator' "$desktop_xaml" || grep -Fq -- 'DEVELOPMENT DEMO' "$desktop_xaml"; then
  echo 'FAIL: standard Production Desktop still exposes Demo identity/state.' >&2
  exit 1
fi
need '<Trigger Property="IsKeyboardFocused" Value="True">' "$desktop_xaml" 'Desktop visible keyboard focus'
need 'SizeChanged="Window_SizeChanged"' "$desktop_xaml" 'Desktop responsive resize hook'
need 'RootGrid.FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;' "$desktop_cs" 'Desktop RTL/LTR runtime switch'
need 'SidebarColumn.Width' "$desktop_cs" 'Desktop responsive sidebar behavior'

# Central boundary / decommissioned direct-capture regression markers.
if grep -R -nE 'SqlConnection|Microsoft\.Data\.SqlClient|Storage\.Primary\.Root|Storage\.Backup\.Root' src/MAM.Web src/MAM.Desktop --include='*.cs' --include='*.js' --include='*.xaml' | grep -vE '(^|/)obj/|(^|/)bin/'; then
  echo 'FAIL: client source contains direct SQL or permanent-storage implementation reference.' >&2
  exit 1
fi
need 'Direct tape recording is intentionally not part of MAM' "$web_js" 'Web direct-capture decommission message'
need 'Tape Inventory' "$desktop_cs" 'Desktop Tape Inventory workspace'
if grep -Fq -- 'Tag="capture"' "$desktop_xaml"; then
  echo 'FAIL: Desktop navigation must not expose direct Tape Capture.' >&2
  exit 1
fi
need 'MaxConcurrentMediaJobsPerWorker' "$config" 'bounded media-worker setting'
need 'MaxConcurrentBackupJobsPerWorker' "$config" 'bounded backup-worker setting'
need 'LeaseNextAsync(workerId)' "$worker" 'durable worker leasing'
need 'if (!didWork) await Task.Delay(1000);' "$worker" 'idle backpressure instead of busy spin'

echo 'P10 client/platform/accessibility acceptance passed: Web accessibility preserved and standard Desktop uses accessible Production Windows SSO with no local/Demo credential surface.'