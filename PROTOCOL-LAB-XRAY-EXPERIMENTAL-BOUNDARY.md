# GeniaProxy 4.5.0 Alpha 2 — Xray experimental boundary

This document defines the Alpha 2 boundary for the
`xray-experimental` capability.

The capability is intentionally **design-only** at this stage.
It is not selectable and cannot silently replace or mutate the validated
stable Xray path.

## Current boundary

The experimental Xray lane must remain fail-closed:

- no automatic activation;
- no replacement of the stable pinned Xray engine;
- no reuse of the stable profile path without separate validation;
- no relaxation of stable Xray profile hardening;
- a separate experimental Xray engine pin is required;
- the experimental engine artifact must be hash-verified;
- isolated config validation and runtime evidence are required before
  promotion;
- explicit Protocol Lab opt-in is required.

The stable bundled Xray path therefore remains frozen while experimental
engine work is evaluated independently.

`ProtocolLabXrayExperimentalBoundary.ValidateBoundary()` enforces these
properties against the Protocol Lab capability catalog.

`ThrowIfRuntimeActivationRequested()` intentionally rejects runtime
activation until the isolated experimental-engine acceptance gates have
been completed.

## Alpha 2 decision

For the current Alpha 2 milestone:

- boundary and safety model: implemented;
- separate experimental Xray engine pin: deferred;
- experimental config generator/runtime runner: deferred;
- stable Xray engine replacement: rejected for Alpha 2 without separate
  acceptance evidence;
- stable Xray profile-hardening relaxation: rejected;
- capability state: `DesignOnly`;
- selectable in Protocol Lab: no.

This preserves the validated stable Xray path and prevents experimental
engine churn from changing stable behavior by accident.
