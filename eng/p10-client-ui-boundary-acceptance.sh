#!/usr/bin/env bash
set -euo pipefail

web_html='src/MAM.Web/wwwroot/index.html'
web_js='src/MAM.Web/wwwroot/app.js'
web_css='src/MAM.Web/wwwroot/styles.css'
desktop_xaml='src/MAM.Desktop/MainWindow.xaml'
desktop_cs='src/MAM.Desktop/MainWindow.xaml.cs'
worker='src/MAM.Worker/Program.cs'
config='config/appsettings.Development.template.json'

need() {
  local pattern="$1" file="$2" label="$3"
  grep -Fq -- "$pattern" "$file" || { echo "FAIL: $label ($pattern) missing from $file" >&2; exit 1; }
}

# Web accessibility and language-direction contract.
need '<html lang="en" dir="ltr">' "$web_html" 'Web default language/direction'
need 'name="viewport"' "$web_html" 'responsive viewport'
need 'aria-label="Primary navigation"' "$web_html" 'navigation accessible name'
need 'alt="Diwan Al Amiri crest"' "$web_html" 'brand image alternative text'
need 'aria-live="polite"' "$web_html" 'live status region'
need "document.documentElement.dir=arabic?'rtl':'ltr'" "$web_js" 'runtime RTL/LTR switch'
need "document.documentElement.lang=arabic?'ar':'en'" "$web_js" 'runtime language switch'
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
need 'AutomationProperties.Name="Diwan Al Amiri crest"' "$desktop_xaml" 'Desktop crest accessible name'
need 'AutomationProperties.Name="User name"' "$desktop_xaml" 'Desktop user field accessible name'
need 'AutomationProperties.Name="Password"' "$desktop_xaml" 'Desktop password field accessible name'
need 'AutomationProperties.Name="Switch language"' "$desktop_xaml" 'Desktop language switch accessible name'
need '<Trigger Property="IsKeyboardFocused" Value="True">' "$desktop_xaml" 'Desktop visible keyboard focus'
need 'SizeChanged="Window_SizeChanged"' "$desktop_xaml" 'Desktop responsive resize hook'
need 'RootGrid.FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;' "$desktop_cs" 'Desktop RTL/LTR runtime switch'
need 'SidebarColumn.Width' "$desktop_cs" 'Desktop responsive sidebar behavior'

# Central boundary / Windows-only capture regression markers.
if grep -R -nE 'SqlConnection|Microsoft\.Data\.SqlClient|Storage\.Primary\.Root|Storage\.Backup\.Root' src/MAM.Web src/MAM.Desktop --include='*.cs' --include='*.js' --include='*.xaml' | grep -vE '(^|/)obj/|(^|/)bin/'; then
  echo 'FAIL: client source contains direct SQL or permanent-storage implementation reference.' >&2
  exit 1
fi
need 'Windows-only capability.' "$web_js" 'Web capture boundary message'
need 'Windows Tape Capture Workspace' "$desktop_cs" 'Windows capture workspace'
need 'MaxConcurrentMediaJobsPerWorker' "$config" 'bounded media-worker setting'
need 'MaxConcurrentBackupJobsPerWorker' "$config" 'bounded backup-worker setting'
need 'LeaseNextAsync(workerId)' "$worker" 'durable worker leasing'
need 'if (!didWork) await Task.Delay(1000);' "$worker" 'idle backpressure instead of busy spin'

echo 'P10 client/platform/accessibility/RTL-LTR boundary acceptance passed.'