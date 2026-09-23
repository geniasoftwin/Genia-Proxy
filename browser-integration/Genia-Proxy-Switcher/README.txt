GENIA PROXY SWITCHER DIRECT 5.6.0 STABLE
========================================

Stable Direct Bridge browser integration for GeniaProxy 4.4.0 Final Stable.
Manifest build: 5.6.0.6.

Validated behavior
------------------
- Local SOCKS verification and Trusted Exit checks.
- Direct Bridge polling outside operationQueue with single in-flight refresh.
- Adaptive polling during Local -> TUN transitions.
- Coalesced proxy-error handling.
- Independent TUN Monitor lane.
- Kill-switch-safe access to the loopback Direct Bridge while general web traffic remains fail-closed.
- TUN VERIFIED session fencing: Manager/TUN loss invalidates stale verification immediately.
- Automatic browser SOCKS release in TUN and restoration on return to Local.
- Daily Audit 10/10 support in Local and TUN modes.

Intentional policy
------------------
If GeniaProxy closes or disappears, the browser remains fail-closed. Press Disable proxy manually when you intentionally want to return Chrome to ordinary system/direct networking.

Compatibility
-------------
The internal transport identifier remains direct-1-exp2 for protocol compatibility.
