# GeniaProxy 4.2.1 — Mobile QR hotfix

This hotfix changes profile sharing only. Networking, TUN orchestration, DNS,
rollback, Xray/sing-box binaries and connection state logic are unchanged.

## Changed

- Profile QR no longer embeds the entire desktop JSON configuration.
- Hysteria2 profiles are shared as compact `hysteria2://` URIs.
- Xray VLESS + XHTTP + TLS profiles are shared as compact `vless://` URIs.
- QR error correction is raised from L to M because payloads are now much smaller.
- Share window displays the mobile link format and payload size.
- Added **Copy link** alongside Copy QR / Save PNG.
- Profiles that cannot be represented safely by one supported mobile URI fail
  explicitly instead of silently creating a huge JSON QR.
- Added round-trip regression coverage for Hysteria2 and VLESS/XHTTP share URIs.

## Security

The QR/share URI still contains connection credentials. It must only be shared
with a trusted recipient.


## Build hygiene
- CA1865 analyzer warning removed by using `StartsWith(char)` for the IPv6 bracket check; behavior is unchanged.
