# GeniaProxy 4.5.0 Alpha 1 — Engine Refresh RC1

Candidate scope: sing-box 1.14.1 only.

Baseline: EXP1 FIX4 Startup Recovery + Protocol Lab Boundary.

Frozen in this candidate:
- Xray 26.3.27
- TUN/DNS/routes algorithms
- Control Plane and session journal
- Direct Bridge / Browser Switcher 5.6.0 Stable
- Protocol Lab boundary

Updated:
- sing-box 1.14.0 -> 1.14.1 official stable.

Integrity gate:
- official archive: sing-box-1.14.1-windows-amd64.zip
- archive SHA-256: 5197F16D492D93202DC623622149A6ED040F8ECA263128F91D603F2B901BAA89
- extracted sing-box.exe hash is recorded in engine/sing-box.sha256 and engine/sing-box.provenance.txt at build time.

Promotion rule: do not update Xray in RC1. Pass Local/TUN regression first, then create a separate Xray refresh candidate.
