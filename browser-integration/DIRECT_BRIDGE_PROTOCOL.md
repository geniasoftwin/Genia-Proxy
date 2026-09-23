# Genia Proxy Switcher Direct 5.6.0 Stable — Direct Bridge protocol

Transport: read-only HTTP over IPv4 loopback.

Endpoint:

`GET http://127.0.0.1:47831/v1/status`

The server is hosted inside `GeniaProxy.exe`. There is no Native Messaging manifest and no `GeniaProxy.NativeHost.exe` process.

## Security model

- Listener binds only to `127.0.0.1`.
- Read-only: only `GET` and `OPTIONS` are accepted.
- No start/stop/profile-switch/command endpoint exists.
- Browser exit is still verified independently by Switcher against Trusted Exit IPs.
- TUN transition remains fail-closed: browser SOCKS is released only under the DNR guard and the guard is released only after an accepted fresh browser exit.
- Web origins are rejected; Chrome extension origins are allowed. Requests without an Origin are accepted because the endpoint is loopback-only and read-only.

## Status payload

The JSON status shape intentionally remains compatible with Bridge Protocol 1 so the proven 5.5.6 TUN transition logic can be reused:

- `type: "status"`
- `protocol: 1`
- `connected`
- `version`
- `bridgeVersion`
- `engine`
- `profile`
- `mode` (`local`, `system`, `tun`)
- `localProxy`
- `endpoint`
- `verifiedExitIp`
- `verifiedExitAt`
- `verifiedExitSucceeded`
- `uptimeSeconds`

Multiple Chrome profiles may query the same endpoint independently. No Extension ID registration is required.

Compatibility transport identifier: `direct-1-exp2`.
