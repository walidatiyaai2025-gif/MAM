# P11 UAT — Repository-Executable Acceptance

This matrix is the engineering UAT that can be executed in CI. Final target-site device/browser/operator UAT remains `DEFERRED_TO_P12 / OWNER_LAST`.

## Windows Desktop
- Launch/install from the generated Desktop package on Windows.
- Verify Diwan Al Amiri branding, Navy/Gold identity and official logo rendering.
- Exercise Arabic RTL and English LTR layouts at normal and constrained window sizes; responsive navigation must remain usable.
- Confirm keyboard focus/accessibility automation metadata on representative controls.
- Validate upload, search, metadata/curation, administration and reports/operations entry points.
- Confirm loading, empty, validation error and degraded dependency states are explicit rather than blank/silent.
- Confirm tape capture is Windows-only and does not grant Desktop direct permanent-storage or SQL ownership.

## Responsive Web
- Render representative desktop and narrow/mobile viewports.
- Exercise Arabic RTL and English LTR direction switching.
- Validate upload, search/preview, metadata/curation, administration and reports/operations through the Central API proxy only.
- Validate loading, empty, error and degraded states.
- Confirm no tape-capture control or direct SQL/Primary/Backup credential path is exposed in Web.

## Deployment/UAT failure cases
- Modified release artifact or SHA-256 mismatch must fail validation.
- Materialized production configuration containing unresolved placeholders must fail validation.
- InstallRoot overlapping Primary or Backup Storage must fail closed.
- Primary and Backup roots overlapping each other must fail closed.
- SQL migration failure blocks deployment.
- Missing production signing certificate must not produce a signed claim.
- Dependency outage must surface a degraded/error state rather than false Ready/Protected status.

## CI evidence mapping
- `p11-acceptance` Ubuntu job: generated release artifacts, checksum verification, packaged API/Web/Worker boot, SQL migration, upgrade database/catalog/media-reference preservation, external Primary media preservation, validator positive/negative cases and boundary checks.
- `p11-acceptance` Windows job: generated Desktop clean install, upgrade, uninstall preservation, fail-closed signing path and rendered Arabic/English Desktop/Web evidence.
- full `ci`: retained P00–P08 functional/security regressions.
- `p09-acceptance` and `p10-acceptance`: retained resilience/DR and security/performance/compatibility regressions on the same commit.

Site-specific browser versions, physical capture hardware, real production identity/network/storage and authorized operator sign-off are not represented as PASS by this document.
