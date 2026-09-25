# GeniaProxy 4.5.0 Alpha 2 — Protocol Lab status

This file records the Alpha 2 disposition of every Protocol Lab candidate.

## Runtime-verified and selectable only inside Protocol Lab

### AnyTLS

- engine family: sing-box;
- support state: RuntimeVerified;
- selectable in Protocol Lab: yes;
- enabled by default: no;
- generated config accepted by pinned sing-box 1.14.1;
- isolated loopback runtime path verified;
- not promoted to stable.

### TUIC

- engine family: sing-box;
- support state: RuntimeVerified;
- selectable in Protocol Lab: yes;
- enabled by default: no;
- generated config accepted by pinned sing-box 1.14.1;
- isolated loopback runtime path verified;
- TLS verification remained enabled;
- 0-RTT remained disabled;
- not promoted to stable.

### Snell v6

- engine family: sing-box;
- support state: RuntimeVerified;
- selectable in Protocol Lab: yes;
- enabled by default: no;
- generated config accepted by pinned sing-box 1.14.1;
- isolated loopback runtime path verified;
- initial path uses Snell v6;
- not promoted to stable.

## Design-only / deferred

### Whitelist-oriented mode

- support state: DesignOnly;
- selectable in Protocol Lab: no;
- runtime transport: deferred;
- route/DNS integration: deferred;
- stable-mode integration: rejected for Alpha 2;
- third-party service impersonation or hidden transport is outside the
  allowed Alpha 2 boundary.

See `PROTOCOL-LAB-WHITELIST-BOUNDARY.md`.

### Xray experimental

- support state: DesignOnly;
- selectable in Protocol Lab: no;
- separate experimental engine pin: deferred;
- runtime activation: deferred;
- silent replacement of the stable Xray pin: rejected;
- relaxation of stable Xray profile hardening: rejected.

See `PROTOCOL-LAB-XRAY-EXPERIMENTAL-BOUNDARY.md`.

## Frozen stable baseline

Protocol Lab does not change the stable connection/TUN path by default.
The stable Xray and sing-box engine pins, TUN ownership/recovery behavior,
startup recovery and existing profile import path remain separate from the
runtime-verified Protocol Lab candidates.
