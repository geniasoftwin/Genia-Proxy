# GeniaProxy 4.5.0 — Protocol Lab Boundary

Alpha 1 keeps the 4.4.0 networking baseline frozen while the control-plane, recovery and Direct Bridge layers evolve.

The following capabilities are explicitly classified as **Protocol Lab** and are disabled by default in Alpha 1:

- AnyTLS
- TUIC
- Snell
- Whitelist Mode
- Xray experimental features

`ProtocolLabFeatureCatalog` is the code-level guard for this boundary. Alpha 1 startup validates that no Protocol Lab capability is enabled by default.

## Branching rule

Stable/control-plane work may observe, diagnose and coordinate the existing supported network paths, but must not silently activate Protocol Lab features.

Protocol Lab work can be developed in Alpha 2 / dedicated experimental builds and promoted only after separate compatibility, security and regression testing.

## Planned order

1. Finish EXP1 startup/control-plane hardening.
2. EXP2: Authenticated Direct Bridge + Route Coherence v2.
3. Freeze Alpha 1 stable foundation.
4. Alpha 2 / Protocol Lab: evaluate AnyTLS, TUIC, Snell, Whitelist Mode and Xray experimental capabilities independently.
