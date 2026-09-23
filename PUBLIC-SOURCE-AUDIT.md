# GeniaProxy 4.4.0 public-source audit

This is the publication gate for the GeniaProxy 4.4.0 Final Stable Direct Bridge source tree.

## Source identity

The file `PUBLIC-SOURCE-PACKAGE-MANIFEST.sha256` is a provenance manifest for the sanitized package *before* repository import. It is intentionally not a manifest of the current Git tree, because repository-owned files were preserved and minor whitespace normalization occurred during import.


- Input archive SHA-256: `3f28f700baa84a798ef1280bf94f9682af74785bcf7e0a0de99d5593aaa96e13`
- Project version: `4.4.0`
- Target framework: `net10.0-windows`
- .NET SDK pin: `10.0.400`, prerelease SDKs disabled
- Direct NuGet dependency: `QRCoder 1.8.0`

## Publication sanitization

The original source package contained project-specific network endpoints used by the validated installation. Those values are not appropriate for a public repository.

Before publication:

- browser Trusted Exit defaults were cleared;
- historical endpoint examples in documentation/tests were replaced with RFC 5737 TEST-NET addresses;
- no production UUID, password, API token, private key, certificate private material, or deployable proxy URI is intentionally retained;
- bundled engine executables/DLLs were removed from source control;
- engine acquisition was changed to verified upstream downloads with pinned SHA-256 checksums.

## Engine provenance

The public source does not commit `sing-box.exe`, `xray.exe`, or `wintun.dll`.

The 4.4.0 baseline expects:

- sing-box 1.14.0;
- Xray-core 26.3.27;
- Wintun 0.14.1 amd64.

`Prepare-Engines.ps1` verifies both release archives and extracted binaries before installing them into the local `engine` directory. See `engine/README.md` for the pinned digests.

The input archive also contained a historical sing-box 1.13.18 executable while the 4.4.0 build scripts already targeted sing-box 1.14.0. The historical executable is deliberately not published; a clean build obtains and verifies the intended 1.14.0 binary instead.

## Xray security compatibility note

Xray-core 26.3.27 predates the upstream fix for GHSA-5wf9-h793-w73c involving `pinnedPeerCertSha256`. GeniaProxy 4.4.0 rejects that configuration property fail-closed before engine launch, and the test suite contains a regression test for this guard.

Upstream advisory: <https://github.com/XTLS/Xray-core/security/advisories/GHSA-5wf9-h793-w73c>

This historical engine pin is kept only to reproduce the validated 4.4.0 baseline. New development should use a separately validated patched Xray version rather than weakening or removing the guard.

## Static audit result

The sanitized tree was checked for:

- private-key PEM markers and key/certificate container files;
- UUID-shaped access identifiers;
- common GitHub/AWS token forms;
- password/token/private-key assignments with embedded values;
- hard-coded deployable proxy URIs;
- public infrastructure hostnames and IP addresses;
- unexpected download/execute primitives;
- committed `.exe`/`.dll` engine files;
- JSON and browser-extension JavaScript syntax.

Remaining IP literals are loopback/private/link-local/multicast/routing constants, well-known public resolvers used in diagnostics, or RFC 5737 TEST-NET documentation/test addresses.

No high-risk credential or production-endpoint finding remained after sanitization.

## Limitations

This audit substantially reduces publication risk but is not a formal proof of security. The current audit environment is not Windows and does not contain the required .NET 10 SDK or Windows PowerShell, so a clean Windows build plus the 4.4.0 regression suite remains required before tagging a reproducible public `v4.4.0` release.
