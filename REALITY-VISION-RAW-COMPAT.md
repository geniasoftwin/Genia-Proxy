# GeniaProxy 4.3.2 LTS — REALITY Vision RAW/TCP compatibility update

Supported share-link matrix added by this maintenance patch:

- `VLESS + XHTTP + TLS` — unchanged.
- `VLESS + XHTTP + REALITY + stream-one` — unchanged.
- `VLESS + REALITY + Vision + RAW/TCP` — added.

RAW/TCP fail-closed requirements: explicit `type=tcp` or `type=raw`, `security=reality`, `flow=xtls-rprx-vision`, SNI/serverName, fingerprint, REALITY public key/password, valid even-length hexadecimal shortId up to 16 chars, and either no `headerType` or `headerType=none`.

This update intentionally does not alter Windows TUN routes, DNS transaction/rollback, process security, profile storage, engine hashes, sing-box, Xray binary, or Wintun.


## FIX2: Vision RAW UDP/XUDP compatibility

For VLESS + REALITY + `xtls-rprx-vision` + RAW/TCP, GeniaProxy now
adds a dedicated XUDP Mux lane:

- `mux.enabled = true`
- `mux.concurrency = -1` (TCP stays on native Vision/RAW)
- `mux.xudpConcurrency = 8`
- `mux.xudpProxyUDP443 = "reject"`

This is required because Vision does not accept native VLESS UDP requests.
UDP (including the TUN DNS transport probe) is carried as XUDP without
changing the frozen Windows TUN/DNS/rollback implementation.


## FIX3: fingerprint-preserving finalization

Device validation on Windows with bundled Xray 26.3.27 confirmed the RAW/Vision path with an explicit `fp=firefox`: Local SOCKS and TUN both passed 20/20, expected exit IPv4 was observed, IPv6 leak probe passed, and TUN DNS/routes rolled back cleanly after disconnect.

GeniaProxy therefore does **not** choose or substitute a browser fingerprint. The exact `fp` value from the share link is preserved through import, runtime normalization and QR/share round-trip. Fingerprints are validated fail-closed as compact ASCII tokens; malformed values such as whitespace/injection are rejected. The server-specific working profile may use `fp=firefox`, while other valid profiles keep their own explicit fingerprint.

No Xray downgrade or compatibility core is included. Bundled Xray remains 26.3.27. Frozen TUN/DNS/security/rollback files remain byte-for-byte unchanged.
