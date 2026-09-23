# GeniaProxy Browser Integration v2

Architecture:

```text
GeniaProxy 4.3.3 Final Stable LTS
  -> read-only Native Messaging status
GeniaProxy.NativeHost Bridge v2 RC5
  -> exact allowed Chrome extension origin
Genia Proxy Switcher 5.5.6 Stable
  -> independent browser-visible route verification
```

The Bridge is deliberately not a remote-control API. It exposes status only.

GeniaProxy installs the NativeHost to `%LOCALAPPDATA%\GeniaProxy\NativeHost`, writes UTF-8 JSON without BOM, and registers `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.geniapixia.geniaproxy`.

## Stable Switcher folder

The unpacked extension is shipped directly at:

`<GeniaProxy>\browser-integration\Genia-Proxy-Switcher\`

The Browser Integration window opens the parent `browser-integration` folder. There is no version-specific extraction path. Future compatible Switcher releases can replace the files inside the same stable folder and then be reloaded from `chrome://extensions`.

When the portable GeniaProxy directory changes, GeniaProxy refreshes `managerRoot` on startup while preserving the configured extension ID where possible.

## Local mode

Switcher owns the browser SOCKS5 setting (`127.0.0.1:2080`), keeps Strict Local routing, WebRTC Shield and the fail-closed browser guard active, and independently verifies the browser-visible exit.

## TUN mode

When Manager reports `mode=tun`, Switcher releases its own Chrome SOCKS setting while keeping the browser guard closed. It performs fresh system-route probes and releases the guard only after an accepted exit is observed. Slow first TUN starts are handled by an adaptive warm-up loop rather than waiting for the normal 30-second health cadence.

The TUN Monitor never starts/stops GeniaProxy, never changes profiles and never enables a DIRECT fallback.
