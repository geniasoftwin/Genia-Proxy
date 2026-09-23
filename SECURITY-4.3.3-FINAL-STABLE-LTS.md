# Security note — GeniaProxy 4.3.3 Final Stable LTS

## Frozen network baseline

The release intentionally keeps the proven Windows route/DNS transaction handling, System Proxy rollback, crash recovery and Xray TUN orchestration from the validated 4.3.2 LTS line. Finalization does not redesign those components.

## sing-box

The build pins sing-box 1.14.0 windows-amd64 by exact SHA-256 and fails if the core differs from the approved binary.

## Xray

Bundled Xray remains 26.3.27 in 4.3.3 so the sing-box maintenance upgrade is not combined with a second core upgrade. Existing Xray profile validation and fail-closed protections are retained.

## Browser Integration

NativeHost Bridge v2 is optional and read-only.

- Native Messaging only; no localhost HTTP/WebSocket listener.
- exact extension-origin binding through the configured Chrome Extension ID.
- Bridge cannot start/stop the VPN, change profiles or execute arbitrary commands.
- owned GeniaProxy/core correlation is fail-closed for foreign or incoherent processes.
- browser-visible exit IP is independently verified by Switcher.
- Local SOCKS failures engage the browser Hard Kill Switch.
- during Local→TUN transition, the browser guard stays closed until a fresh browser-visible exit is accepted.
- an accepted TUN exit must match a fresh Manager verified exit when available, otherwise a configured Trusted Exit.
- transition probes continue through the normal TUN warm-up window instead of falling back to a long health cadence.
- WebRTC Shield remains active in both Local and TUN monitoring modes.

The shipped Switcher folder is intentionally replaceable for future compatible updates. NativeHost remains separately hash-pinned and bound to the user-approved extension ID.

## Signing

The bundled NativeHost RC5 binary is the same audited/tested candidate used during acceptance. Authenticode signing remains recommended when a release certificate is available.
