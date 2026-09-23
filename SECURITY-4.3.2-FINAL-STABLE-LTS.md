# Security & Leak Review — GeniaProxy 4.3.2 Final Stable LTS

Review date: 2026-08-28

## Result

No unmitigated critical or high-severity issue was identified in the GeniaProxy application path during the final source review and manual leak/recovery gate. The release remains a portable user-mode application with documented residual risks below.

## Confirmed protections

1. **Loopback-only local listeners.** Xray SOCKS/HTTP inbounds are generated on `127.0.0.1`; they are not intentionally exposed to LAN/WAN.
2. **TUN IPv6 fail-closed.** An available system IPv6 default route causes TUN startup to fail rather than allowing native IPv6 to bypass the IPv4-only tunnel.
3. **Leak-safe TUN DNS and rollback.** Xray TUN temporarily applies `1.1.1.1/1.0.0.1`; validated rollback restores the original physical-adapter DNS. The final forced-crash gate restored `192.168.0.1` on the tested host.
4. **Owned-route recovery.** TUN crash snapshots are bounded in size and validated for exact route shape, session id, IPv4 values and expected applied DNS before rollback.
5. **Constrained elevation.** The normal UI remains USER mode. Stale TUN recovery uses a fixed `--recover-tun-only` UAC helper that invokes the existing validated rollback and exits.
6. **System-proxy rollback is conservative.** GeniaProxy restores its snapshot only while the current settings still match the values GeniaProxy applied; external changes are preserved.
7. **Xray pinning advisory is fail-closed in GeniaProxy.** Bundled Xray 26.3.27 is in the upstream affected range of GHSA-5wf9-h793-w73c for the `pinnedPeerCertSha256` scenario. GeniaProxy recursively rejects any Xray profile containing that property before core start, and has a regression test for the block.
8. **No cleartext remote channel probes.** Built-in channel/TUN probes use HTTPS.
9. **Remote core APIs disabled from imported Xray JSON.** Runtime normalization removes `api`, `stats`, `metrics` and `reverse` root sections and replaces logging with warning level.
10. **Core launch is hash-checked.** Xray, sing-box and Wintun are SHA-256 verified before launch, and the release builder pins the frozen binaries byte-for-byte.
11. **Runtime-state hygiene.** The release builder rejects bundled user profiles/settings/TUN/system-proxy backup state. Runtime JSON files are deleted after use and stale runtime files are cleaned on startup.
12. **Security-patched .NET baseline.** Final source pins .NET SDK 10.0.400, which carries the .NET 10.0.11 August 2026 security patch. The final builder refuses a runtime baseline older than 10.0.11.
13. **Manual final leak/recovery gate passed.** REALITY TUN returned the expected VPS IPv4, 20/20 HTTPS, IPv6 leak probe PASS, clean DNS rollback, and successful reconnect after forced crash recovery.

## Upstream security status considered

- .NET 10.0.11 (2026-08-11) is the current security patch and fixes multiple August 2026 CVEs, including RCE/EoP/information-disclosure issues affecting earlier 10.0.x patches. Final Stable is built only with this patched baseline or newer.

- Xray-core GHSA-5wf9-h793-w73c (published 2026-07-10) affects versions >=26.1.13 and is patched >=26.7.11. This LTS intentionally retains the already regression-tested Xray 26.3.27 but blocks the affected `pinnedPeerCertSha256` configuration path fail-closed. Do not run the bundled `xray.exe` independently with that option.
- sing-box CVE-2023-43644 / GHSA-r5hm-mp3j-285g affected old versions before 1.4.5. Bundled sing-box 1.13.18 is well beyond the patched range.

## Residual / operational risks

- **Portable profile secrets are stored as JSON at rest.** VLESS UUIDs, REALITY credentials and Hysteria2 passwords are not DPAPI-encrypted because the Windows build preserves portable profile files and QR/backup interoperability. Keep the portable directory in a private NTFS user folder and protect exported backups/QR codes as credentials.
- **SHA-256 sidecar files are integrity checks, not Authenticode signatures.** The builder pins release hashes, but malware already running as the same user can potentially alter a portable directory. Verify the distributed ZIP SHA-256 and use trusted storage.
- **The portable EXE is not a substitute for OS code signing.** If distributing broadly, sign the final EXE/ZIP pipeline with a trusted Authenticode certificate.
- **Local/System Proxy are not full-tunnel privacy modes.** Applications that ignore the Windows proxy, WebRTC/ICE and other direct transports may bypass them. Use TUN when full-route privacy is required.
- **TUN is deliberately IPv4-only and fail-closed for native IPv6.** This prevents IPv6 leakage but means an IPv6-enabled environment can require disabling IPv6 for that adapter or using proxy mode.
- **Known upstream non-security regressions are not automatically absorbed.** The LTS core binaries remain frozen until a separate regression cycle proves an update.

## LTS update policy

The 4.3.2 network baseline is frozen. Ship a security maintenance update only for an unmitigated upstream vulnerability, Windows compatibility break, or a reproducible GeniaProxy security defect. Core upgrades require the full automated and manual LTS regression gate again.
