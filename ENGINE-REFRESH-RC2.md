# GeniaProxy 4.5.0 Alpha 1 — Engine Refresh RC2

Candidate scope: Xray 26.9.8 only.

Baseline retained:
- sing-box 1.14.1 (RC1 accepted candidate)
- EXP1 FIX4 control-plane / startup recovery
- Browser Direct Bridge + Switcher 5.6.0 Stable
- existing Windows TUN/DNS/routes orchestration
- Wintun unchanged

Changed in RC2:
- Xray 26.3.27 -> 26.9.8 official windows-64 release asset.
- Official archive SHA-256 pin: FFB3DC91680A819F021C1A94FC1A726DA6C7081801191BC5D40CC8F225503D46.
- Xray executable SHA-256 is generated from the verified archive and stored in engine/xray.sha256 plus engine/xray.provenance.txt.

Release-channel note:
GitHub currently marks v26.9.8 as a pre-release. This RC2 exists specifically to test it in isolation; do not promote it to the stable 4.5.0 baseline until the full Windows regression matrix is green.

Required RC2 regression:
1. Xray Local: repeated start/stop on known-good XHTTP/REALITY profile.
2. Xray TUN: adapter, routes, DNS, connectivity probe, Control Plane VERIFIED, rollback.
3. XHTTP REALITY + XMUX schema smoke.
4. VLESS REALITY Vision RAW schema smoke.
5. Manual 1+20 verification refresh after auto TUN VERIFIED.
6. Browser Direct Bridge: observed exit == manager verified exit; route coherence match; Daily Audit 10/10.
7. sing-box 1.14.1 one-cycle Local/TUN regression to prove the accepted RC1 engine did not change.

Promotion rule: RC2 can be combined with RC1 only after the Xray regression is green.
