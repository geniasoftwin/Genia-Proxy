# Engine Update Policy

GeniaProxy uses third-party networking engines whose behavior can change independently of the application. Engine updates are therefore treated as compatibility changes, not simple dependency bumps.

## Release classes

### Stable

May be considered for the default GeniaProxy engine after regression testing.

### Release candidate / beta

May be evaluated in a development branch when it contains a feature or fix relevant to GeniaProxy. It should not become the default engine without a specific reason and full validation.

### Alpha / experimental / pre-release

Use only in isolated experiment branches. Do not ship as the normal stable engine.

## Required review

Before adopting a new engine version:

- read the official release notes;
- read migration/deprecation documentation;
- identify protocol, transport, TUN, DNS, routing, TLS, and configuration changes;
- compare defaults against the currently pinned version;
- check for Windows-specific regressions;
- test representative profiles;
- record checksums for shipped binaries.

## Compatibility risks to track

- removed or renamed configuration fields;
- changed defaults;
- TUN stack or routing behavior;
- DNS behavior;
- TLS verification and certificate handling;
- UDP behavior;
- IPv6 behavior;
- transport negotiation and fallback;
- platform-specific privileges;
- performance or memory regressions;
- engine startup/shutdown semantics.

## Promotion

An experimental engine version may be promoted only when:

- the upstream release maturity is appropriate;
- GeniaProxy regression testing passes;
- migration requirements are documented;
- rollback is available;
- no known critical compatibility regression remains unresolved.
