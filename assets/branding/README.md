# Diwan Al Amiri Branding Assets

## Approved primary crest

Status: **OWNER SUPPLIED / APPROVED FOR PROJECT USE**

Source received: 2026-09-10

Original uploaded filename: `Screenshot 2025-04-07 075807(1).png`

Source dimensions: `234 x 223 px`

SHA-256 of the exact owner-supplied PNG bytes:

`bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb`

Canonical application asset path once committed as binary:

`assets/branding/diwan-al-amiri-logo.png`

Required derived assets:

- `assets/branding/diwan-al-amiri-app-icon.ico`
- `assets/branding/diwan-al-amiri-favicon.svg`
- optional resolution-specific PNGs for Windows/Web packaging

## Non-negotiable asset rules

- Use the exact supplied crest as the source of truth.
- Preserve its original internal colors.
- Do not redraw, recolor, distort, crop or fabricate a substitute crest.
- Derived icons may crop/recompose only when explicitly reviewed and approved for the target icon surface; the primary logo itself remains untouched.
- Preserve aspect ratio and sufficient clear space.
- The product UI identity surrounding the crest is Navy + Gold as defined in `docs/BRANDING_UI_UX.md`.

## Repository binary status

The connector used to initialize this repository writes UTF-8 text files but does not upload arbitrary local binary content. Therefore the binary PNG itself is not silently substituted or reconstructed here. The SHA-256 above locks the exact source so a later binary commit can be verified byte-for-byte.

P01 implementation must fail branding acceptance if the canonical logo asset is missing or if its SHA-256 does not match the approved source, unless the owner explicitly approves a replacement source asset.
