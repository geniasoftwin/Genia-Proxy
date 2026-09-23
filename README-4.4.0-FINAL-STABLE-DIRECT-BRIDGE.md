# GeniaProxy 4.4.0 Final Stable Direct Bridge

GeniaProxy 4.4.0 promotes the validated Direct Bridge branch over the frozen GeniaProxy 4.3.3 network/TUN/Xray baseline.

## Architecture

- `GeniaProxy.exe` hosts a read-only loopback endpoint at `http://127.0.0.1:47831/v1/status`.
- Genia Proxy Switcher Direct 5.6.0 Stable reads Manager state through ordinary extension `fetch()`.
- No `GeniaProxy.NativeHost.exe`, Native Messaging registration, or Extension-ID binding is used.
- Multiple Chrome profiles can query the same read-only Manager independently.
- The internal transport identifier remains `direct-1-exp2` for compatibility; it is a protocol identifier, not a release-status flag.

## Stable fixes included

- Direct Bridge polling is outside the shared browser `operationQueue`.
- Bridge refresh is single-in-flight and overlapping refreshes are coalesced.
- Adaptive polling covers inactive/disconnected and Local -> TUN transition states.
- Chrome proxy-error bursts are coalesced instead of flooding Manager state work.
- TUN verification runs in its own single-in-flight lane.
- Hard Kill Switch permits only the extension-origin loopback Bridge bootstrap while ordinary web traffic remains fail-closed.
- TUN VERIFIED is invalidated immediately when the Manager/TUN disappears; a new TUN requires a fresh browser verification.
- Local SOCKS is restored automatically when returning from TUN.
- Normal WPF window shutdown no longer re-enters `Close()` from `Window_Closing`; the final close is deferred to a later Dispatcher turn.

## Intentional shutdown policy

When GeniaProxy closes or disappears, Switcher remains fail-closed. Ordinary browser internet is restored only after the user explicitly presses **Disable proxy** in Switcher. This is intentional. A forced Task Manager `End Task` is not a graceful shutdown and Windows may record `AppHangB1`; GeniaProxy recovery and Switcher fail-closed behavior remain active.

## Frozen baseline

The validated 4.3.3 TUN/DNS/Xray/recovery files and bundled Xray/Wintun baseline remain byte-for-byte hash-gated by the portable builder.
