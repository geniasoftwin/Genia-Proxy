# Security notes — GeniaProxy 4.4.0 Final Stable Direct Bridge

The Direct Bridge is deliberately read-only.

- Binds only to IPv4 loopback `127.0.0.1`.
- Fixed endpoint: `/v1/status`.
- Accepts only `GET` and `OPTIONS`; unsupported methods return 405.
- Does not expose start/stop, profile switching, configuration writes, or command execution.
- Rejects ordinary non-extension web Origins.
- Uses `Cache-Control: no-store` and `X-Content-Type-Options: nosniff`.
- Switcher independently verifies browser egress and Trusted Exit before releasing its Hard Kill Switch.
- During Hard Kill Switch operation, a narrowly scoped extension-origin rule keeps only the loopback Direct Bridge bootstrap reachable; general web traffic stays blocked.
- Loss of an already VERIFIED TUN invalidates that verification and closes the guard until a fresh protected route is verified.
- Closing or losing GeniaProxy does not automatically fail open: the browser remains blocked until the user explicitly disables proxy protection.

The internal transport string `direct-1-exp2` is retained for compatibility and does not indicate an experimental release.
