# Diwan Al Amiri Branding Assets

## Approved primary crest

Status: **OWNER SUPPLIED / APPROVED FOR PROJECT USE / BYTE-FOR-BYTE VERIFIED**

Source received: 2026-09-10  
Original uploaded filename: `Screenshot 2025-04-07 075807(1).png`  
Source dimensions: `234 x 223 px`  
Source byte length: `69136`

SHA-256 of the exact owner-supplied PNG bytes:

`bb26a4358aa74c8c34fd1100ff816ce25ef8074da0f2cf99e356f4d379d8e3fb`

Because the repository connector cannot commit arbitrary binary file content safely, P01 stores the exact PNG bytes as base64 source chunks in `src/MAM.Application/Branding/DiwanCrestData.*.cs`. `DiwanCrestData.Bytes` reconstructs those bytes at runtime and validates the SHA-256 before Desktop or Web renders the crest. This is not a redraw, recolor or substitute asset.

Web serves the validated bytes at `/assets/branding/diwan-al-amiri-crest.png`. Windows creates its image source directly from the same validated byte array. Both platforms therefore consume one exact source.

Required release-derived assets remain later packaging work and must be generated from this source only:

- approved Windows `.ico` derivative;
- approved favicon/PWA derivative;
- optional resolution-specific PNG derivatives.

## Non-negotiable asset rules

- Use the exact supplied crest as the source of truth.
- Preserve its original internal colors.
- Do not redraw, recolor, distort, crop or fabricate a substitute crest.
- Derived icons may crop/recompose only when explicitly reviewed and approved for the target icon surface; the primary crest itself remains untouched.
- Preserve aspect ratio and sufficient clear space.
- The product UI identity surrounding the crest is Navy + Gold as defined in `docs/BRANDING_UI_UX.md`.

P01 branding acceptance fails if the reconstructed bytes do not have the approved length and SHA-256 unless the owner explicitly approves a replacement source asset.
