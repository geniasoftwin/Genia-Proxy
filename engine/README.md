# Engine provenance — GeniaProxy 4.4.0

GeniaProxy does not commit the runtime engine binaries to source control.

Run `Prepare-Engines.ps1` from the repository root to obtain the validated
Windows x64 runtime set from the upstream release locations. The preparation
scripts verify the downloaded archive before extraction and verify the selected
binary again before installing it under `engine/`.

## sing-box 1.14.0

- upstream: SagerNet/sing-box
- release asset: `sing-box-1.14.0-windows-amd64.zip`
- archive SHA-256: `3FFB56267DA14E287BE48BD10CF7E6505260125BAD940B75101FBB4D5D58E5D6`
- extracted `sing-box.exe` SHA-256: `AAD0EDE010EAFA7B277E520464F3A66FDE820103D737EFF739F40F3CC9451DCC`

## Xray-core 26.3.27

- upstream: XTLS/Xray-core
- release asset: `Xray-windows-64.zip`
- archive SHA-256: `D004C39288CE9ADA487C6F398C7C545F7D749E44BDFDD59DBC9F865AFBA4E1AD`
- extracted `xray.exe` SHA-256: `15C2D007954AC53BA69B80EC91242786B3C0B71D52649165B4CA1D5CC96EF8F1`

This historical 4.4.0 pin predates the upstream fix for GHSA-5wf9-h793-w73c.
GeniaProxy 4.4.0 rejects `pinnedPeerCertSha256` fail-closed before launching
Xray. New development should move to a separately validated patched Xray
release rather than weakening that guard.

## Wintun 0.14.1 amd64

- upstream: wintun.net
- release asset: `wintun-0.14.1.zip`
- archive SHA-256: `07C256185D6EE3652E09FA55C0B673E2624B565E02C4B9091C79CA7D2F24EF51`
- extracted `wintun.dll` SHA-256: `E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE`

## Repository policy

The executable/DLL files produced by engine preparation are local build inputs
and are ignored by Git. Only licenses, preparation scripts, and expected
checksums belong in the public source repository.
