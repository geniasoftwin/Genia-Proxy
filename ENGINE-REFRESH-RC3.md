# GeniaProxy 4.5.0 Alpha 1 — Engine Refresh RC3

RC3 closes the engine refresh with the Windows-validated stable baseline:

- sing-box 1.14.1
- Xray 26.3.27 (`d2758a0`, `go1.26.1`, windows/amd64)
- Xray executable SHA-256: `15C2D007954AC53BA69B80EC91242786B3C0B71D52649165B4CA1D5CC96EF8F1`
- Xray executable size: `35,613,696` bytes
- Wintun unchanged
- EXP1 FIX4 Windows TUN/DNS/routes and control-plane baseline unchanged
- Browser Direct Bridge / Switcher 5.6.0 Stable unchanged

## Why RC3 restores Xray 26.3.27

RC2 isolated Xray 26.9.8 and found an intermittent Windows TUN issue on the XHTTP/XMUX UDP path. The same GeniaProxy build and profiles repeatedly passed with Xray 26.3.27, including XHTTP, XHTTP+REALITY, REALITY Vision RAW, DNS/connectivity probes, Control Plane VERIFIED, 20/20 channel checks, IPv6 leak guard, and routes/DNS rollback.

RC3 therefore does not add timing workarounds or retries to the stable GeniaProxy TUN logic. Xray 26.9.x is retained as Experimental/HOLD for later Protocol Lab testing or a future upstream-fixed release.

## Security note

Xray 26.3.27 predates the upstream fix for the `pinnedPeerCertSha256` advisory path. GeniaProxy retains its existing fail-closed profile validation that rejects that property before Xray starts. RC3 does not claim the core itself contains that upstream patch; do not bypass the GeniaProxy validation gate or use the bundled Xray independently with that option.

## RC3 release gate

1. Builder verifies 12/12 frozen network/TUN/DNS/orchestration hashes.
2. Builder verifies exact Xray 26.3.27 executable identity before smoke tests or publish.
3. Builder verifies sing-box 1.14.1 official archive/version/provenance.
4. XHTTP/REALITY/XMUX and REALITY/Vision/RAW schema smoke tests pass on Xray 26.3.27.
5. 41/41 EXP1 tests pass with 0 errors.
6. Published Xray/sing-box/Wintun identities match the prepared baseline.
7. Runtime acceptance: XHTTP TUN, XHTTP REALITY TUN, RAW REALITY TUN, sing-box TUN, VERIFIED, 20/20, IPv6 leak guard, routes/DNS rollback.
