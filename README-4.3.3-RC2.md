# GeniaProxy 4.3.3 RC2

Maintenance release candidate over the validated 4.3.2 Final Stable LTS Windows baseline.

## Changes in 4.3.3 RC2

- sing-box is pinned to official stable **1.14.0** (`windows-amd64`).
- Expected sing-box SHA-256: `AAD0EDE010EAFA7B277E520464F3A66FDE820103D737EFF739F40F3CC9451DCC`.
- Optional **Browser Integration** is integrated into the GeniaProxy UI.
- Bundled read-only **GeniaProxy NativeHost Bridge v2 RC5**.
- Bundled **Genia Proxy Switcher 5.5.4 Manager Bridge v2** package with read-only TUN Monitor.
- Bridge installation, update, removal, portable-path refresh and Switcher extraction are handled by GeniaProxy.
- Browser Bridge remains read-only: no VPN start/stop, profile switching, shell execution, or arbitrary manager actions.
- Browser Trusted Exit remains independently verified by the extension.
- Bridge RC5 reports verified egress only from a successful GeniaProxy channel test; endpoint and egress are kept separate.
- Repeated Chrome proxy-error bursts are coalesced while the fail-closed guard is already engaged.
- Xray, Windows TUN/DNS orchestration, System Proxy and crash-recovery baseline remain based on validated 4.3.2 LTS logic.

## Build

Windows x64 is required.

1. Install .NET SDK 10.0.400 or newer with the current .NET 10 / Windows Desktop security runtime.
2. Run `build-release.ps1` or the outer 4.3.3 RC2 portable builder.
3. If the source still contains the historical 1.13.x binary, the build automatically prepares official sing-box 1.14.0 and verifies the exact SHA-256 before compilation.
4. Offline option: place the already verified `sing-box.exe` 1.14.0 next to this source as `sing-box-1.14.0.exe` and rerun the build.

## Browser Integration

Open **Сервис -> Интеграция с браузером...**.

- Prepare Switcher.
- Load the extracted folder from `chrome://extensions` in Developer Mode.
- Copy the extension ID.
- Paste it into GeniaProxy and install/update Bridge.
- Restart Chrome.

If the portable GeniaProxy folder is moved, the installed Bridge `managerRoot` is refreshed automatically on the next GeniaProxy startup.

## RC acceptance already established before integration

Manual testing with sing-box 1.14.0 on the 4.3.2 application baseline passed:

- Local proxy 20/20 repeatedly (100/100 aggregate in repeated checks).
- TUN 20/20 repeatedly.
- `auto_route` / `strict_route` connectivity gate.
- IPv6 leak guard.
- rapid TUN start/stop/start cycles.
- immediate route/IP restoration after disconnect.

4.3.3 RC2 is the Final Candidate. It requires the final Windows builder gate plus a short regression of Local Proxy, TUN Monitor, Xray TUN and Bridge reconnect before promotion to Final Stable LTS.
