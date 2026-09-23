# Security note — GeniaProxy 4.3.3 RC1

## Network baseline

The release candidate intentionally keeps the proven GeniaProxy 4.3.2 Windows network orchestration: TUN route/DNS transaction handling, System Proxy rollback, crash recovery and Xray-specific Windows TUN management are not redesigned in this maintenance step.

## sing-box

The build pins official sing-box 1.14.0 windows-amd64 by exact SHA-256. The build must fail if the downloaded or user-supplied binary differs from the approved hash.

## Xray

Bundled Xray remains 26.3.27 in this RC to avoid combining two core upgrades in the same maintenance change. The existing fail-closed rejection of `pinnedPeerCertSha256` remains in place for the known affected path.

## Browser Integration

NativeHost Bridge v2 is optional and read-only.

- Native Messaging only; no localhost HTTP/WebSocket listener.
- exact Chrome extension origin / extension ID binding;
- exact GeniaProxy executable ownership and owned-core correlation;
- cross-engine mismatch is fail-closed;
- foreign `sing-box.exe` / `xray.exe` processes are rejected;
- Bridge cannot start/stop VPN, change profiles or execute arbitrary commands;
- Switcher independently verifies the browser-visible exit IP before reporting a trusted route;
- bundled NativeHost and Switcher packages are SHA-256 checked before installation/extraction;
- ZIP extraction rejects path traversal;
- registry scope is HKCU only.

## RC limitations before Final

- NativeHost RC4 binary is the audited integration candidate and should be rebuilt with the current supported Go toolchain before Final promotion.
- Authenticode signing of the NativeHost is recommended for Final distribution.
- End-to-end Chrome restart/reconnect/stale-state tests remain part of the RC acceptance gate.
