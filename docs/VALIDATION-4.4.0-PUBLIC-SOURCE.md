# GeniaProxy 4.4.0 Public Source Validation

Validation date: 2026-09-23

This record covers the sanitized public-source candidate derived from GeniaProxy 4.4.0 Final Stable Direct Bridge.

## Build environment

- Windows x64
- .NET SDK 10.0.401
- project baseline: .NET 10.0
- global.json requested 10.0.400 with compatible roll-forward

## Engine preparation

Verified through Prepare-Engines.ps1:

- sing-box 1.14.0
  - SHA-256: AAD0EDE010EAFA7B277E520464F3A66FDE820103D737EFF739F40F3CC9451DCC
- Xray-core 26.3.27
  - SHA-256: 15C2D007954AC53BA69B80EC91242786B3C0B71D52649165B4CA1D5CC96EF8F1
- Wintun 0.14.1 amd64
  - SHA-256: E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE

No engine EXE/DLL is intended to be committed to the public repository.

## Automated tests

GeniaProxy test suite:

- checks: 32
- failures: 0
- result: PASS

The suite includes profile import/validation, TUN configuration validation, Direct Bridge metadata, operation coordination, JSON profile protection, endpoint safety, Xray pinnedPeerCertSha256 blocking, TUN snapshot validation, topology rejection, privacy-probe parsing, and related regression coverage.

## Release build

build-release.ps1:

- engine preparation: PASS
- dependency restore: PASS
- win-x64 publish: PASS
- portable ZIP creation: PASS
- GeniaProxy.exe present: PASS
- file version 4.4.0.0: PASS
- Direct Bridge marker validation: PASS
- Switcher Direct 5.6.0 Stable marker validation: PASS
- third-party notices: PASS

Three .NET analyzer warnings were emitted (CA1513 and CA1822). They are non-fatal code-quality warnings and did not fail compilation or tests.

## Runtime TUN validation

### Xray

Test-TunIntegration.ps1:

- result: PASS

### sing-box

Test-TunIntegration.ps1:

- GeniaProxy DNS on TUN: PASS
- interface metric 5: PASS
- Windows DNS resolution through TUN: PASS
- expected external exit path: PASS
- parallel HTTPS requests: 20/20 HTTP 200
- result: PASS

No production server address is recorded in this public validation document.

## Cleanup validation

After Stop:

- lingering sing-box process: none
- lingering xray process: none
- lingering GeniaProxy/geniaproxy-tun adapter: none

Result: PASS

## Publication gate

The sanitized 4.4.0 source is approved for import into the review branch after preserving repository-level secret exclusions and excluding build outputs, downloaded binaries, runtime data, profiles, and local machine state.

This validation does not by itself authorize a v4.4.0 tag on main; the GitHub import diff must still be reviewed before promotion.
