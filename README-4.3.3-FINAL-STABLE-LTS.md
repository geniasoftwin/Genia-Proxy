# GeniaProxy 4.3.3 Final Stable LTS

Stable Windows release promoted from the validated 4.3.3 RC2 line.

## Release scope

- sing-box pinned to official stable **1.14.0** (`windows-amd64`).
- Expected sing-box SHA-256: `AAD0EDE010EAFA7B277E520464F3A66FDE820103D737EFF739F40F3CC9451DCC`.
- Xray remains on the validated 26.3.27 baseline for this maintenance release.
- Windows TUN/DNS/System Proxy/crash-recovery logic remains based on the validated 4.3.2 Final Stable LTS baseline.
- Browser Integration uses the read-only NativeHost Bridge v2 RC5 and **Genia Proxy Switcher 5.5.6 Stable**.
- Switcher supports Local SOCKS, WebRTC Shield, fail-closed Hard Kill Switch, Trusted Exit verification, TUN Monitor and adaptive Local→TUN warm-up verification.
- Manager verified egress is exported separately from the remote endpoint and is only populated by a successful GeniaProxy channel test.

## Stable Browser Integration layout

The unpacked browser extension is shipped directly at:

`<GeniaProxy>\browser-integration\Genia-Proxy-Switcher\`

The GeniaProxy UI has one **Open Switcher folder** action. It opens the `browser-integration` directory next to the currently running `GeniaProxy.exe`; there is no hard-coded version subfolder and no `%LOCALAPPDATA%\...\BrowserExtension\<version>` dependency.

For a future Switcher update, replace the files inside the same `Genia-Proxy-Switcher` folder and reload the unpacked extension in the browser. The runtime accepts compatible Switcher manifest versions 5.5.6 or newer.

NativeHost installation/configuration remains under `%LOCALAPPDATA%\GeniaProxy\NativeHost`, because Chrome Native Messaging is registered per user.

## Acceptance completed

The RC line passed repeated Local Proxy and TUN testing, including:

- Local SOCKS 20/20 and stable exit `198.51.100.10` on the validated profile.
- Xray TUN 20/20 on XHTTP/REALITY and REALITY/Vision/RAW profiles.
- IPv6 leak probe PASS for the IPv4-only TUN design.
- route/DNS restoration across repeated TUN start/stop/profile switches.
- Bridge stale detection and automatic reconnect.
- fail-closed Local→TUN transition: transient untrusted exits were rejected while the guard remained closed.
- automatic TUN verification and automatic TUN→Local SOCKS restoration.

## Build

Windows x64 is required. Run `build-release.ps1` or the supplied Final Stable LTS outer builder. The builder verifies frozen network files, Browser Integration files, pinned cores, tests, publish output and release-state cleanliness before producing the portable ZIP.
