# GeniaProxy 4.5.0 Alpha 2 — Whitelist-oriented Protocol Lab boundary

This document defines the Alpha 2 boundary for the experimental
`whitelist-mode` capability.

The capability is intentionally **design-only** in Alpha 2 at this stage.
It is not selectable and cannot modify stable connection behavior.

## Current boundary

The implementation must remain fail-closed:

- no automatic activation;
- no stable connection-path mutation;
- no Windows route changes;
- no Windows DNS changes;
- no third-party service impersonation or hidden transport through an
  unrelated allowed service;
- explicit Protocol Lab opt-in is required before any future runtime work.

`ProtocolLabWhitelistModeBoundary.ValidateBoundary()` enforces these
properties against the Protocol Lab capability catalog.

`ThrowIfRuntimeActivationRequested()` intentionally rejects runtime
activation until a separate transport design, generated-config validation,
rollback model, regression suite and isolated runtime evidence exist.

## Alpha 2 decision

For the current Alpha 2 milestone:

- boundary and safety model: implemented;
- runtime transport: deferred;
- routing/DNS integration: deferred;
- stable-mode integration: rejected for Alpha 2;
- capability state: `DesignOnly`;
- selectable in Protocol Lab: no.

This keeps the validated Alpha 1 TUN ownership/recovery behavior frozen
while still leaving a documented extension point for later experiments.
