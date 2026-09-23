# GeniaProxy 4.5.0 Roadmap

## Purpose

GeniaProxy 4.5.0 is the active development line built on the validated 4.4.x baseline. The goal is to improve network lifecycle reliability while evaluating new engine capabilities without destabilizing the stable branch.

## Release principles

- preserve the proven 4.4.x behavior unless a change is intentional and tested;
- separate production-ready work from protocol and engine experiments;
- prefer reversible changes;
- document every engine upgrade and configuration migration;
- require regression coverage for TUN, DNS, routes, recovery, and profile switching.

## Workstreams

### 1. Startup and recovery

- harden startup failure rollback;
- verify stale process, route, adapter, and DNS cleanup;
- improve restart after elevation or privilege transitions;
- keep recovery logic idempotent.

### 2. TUN and network lifecycle

- regression-test route ownership and cleanup;
- validate DNS restoration under failure paths;
- test IPv4/IPv6 combinations;
- test sleep/wake, network switching, reconnect, and abrupt engine exit;
- benchmark experimental sing-box TUN/network-stack changes in isolated branches.

### 3. Protocol and transport lab

Candidate features should be evaluated only when upstream marks them stable enough to justify integration work.

Current areas of interest include:

- MASQUE / CONNECT-IP experiments;
- HTTP/2 and HTTP/3 proxy transport behavior;
- CONNECT-UDP compatibility;
- Tailcat / WireGuard-derived networking experiments;
- new Xray transports or security modes when they appear upstream.

No experimental protocol is considered part of stable 4.5.0 until explicitly promoted after testing.

### 4. Security

- evaluate certificate pinning as an opt-in feature;
- keep secrets outside the repository;
- improve configuration validation before engine launch;
- document certificate/key rotation and rollback procedures;
- verify that logs and diagnostics do not expose credentials.

### 5. Engine management

For every sing-box or Xray-core update:

1. classify the release as stable, beta/RC, alpha, or other pre-release;
2. review upstream release notes and migration documentation;
3. run a side-by-side compatibility test against the pinned stable version;
4. test representative GeniaProxy profiles;
5. record regressions before changing the default engine.

## Initial release gates

Before a 4.5.0 stable release:

- no known route or DNS leak on normal shutdown;
- no stale TUN state after failed startup;
- repeated connect/disconnect torture test passes;
- profile switching passes;
- sleep/wake and network-change recovery passes;
- all bundled engine versions are documented and reproducible;
- experimental features remain disabled unless explicitly promoted;
- security-sensitive defaults are reviewed.

## Versioning

Development milestones should use explicit pre-release identifiers such as:

- `v4.5.0-alpha.1`
- `v4.5.0-alpha.2`
- `v4.5.0-beta.1`
- `v4.5.0-rc.1`
- `v4.5.0`

The stable tag should be created only after the release gates are satisfied.
