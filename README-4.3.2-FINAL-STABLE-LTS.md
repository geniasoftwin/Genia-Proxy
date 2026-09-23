# GeniaProxy 4.3.2 Final Stable LTS

Long-term frozen Windows release promoted from the tested 4.3.2 LTS RC2.

## Validated protocols and modes

- Hysteria2 + TLS via sing-box.
- VLESS + XHTTP + TLS via Xray.
- VLESS + XHTTP `stream-one` + XMUX + REALITY via Xray.
- Local SOCKS5, Windows System Proxy and full TUN modes.

## Final manual LTS gate (2026-08-28)

- REALITY Local: expected VPS exit IPv4, HTTP 200, 20/20 parallel HTTPS.
- XHTTP/TLS Local: expected VPS exit IPv4, HTTP 200, 20/20 parallel HTTPS.
- Hysteria2 Local: expected VPS exit IPv4, HTTP 200, 20/20 parallel HTTPS.
- REALITY TUN: expected VPS exit IPv4, 20/20 parallel HTTPS, IPv6 leak probe PASS.
- TUN DNS rollback: physical adapter changed from leak-safe `1.1.1.1/1.0.0.1` back to original `192.168.0.1`.
- Forced crash: GeniaProxy and Xray exited, stale adapter was absent, RC2 recovery prompt appeared, original DNS was restored, Hysteria2 Local worked 20/20 after recovery.
- First-run Windows Firewall delay can trip the bounded readiness timeout; after firewall permission is recorded, a normal retry succeeds.

## Frozen baseline

Network/TUN/DNS/rollback/XHTTP/XMUX/REALITY algorithms and bundled engines are frozen from RC2. Final Stable changes only release metadata and documentation.

Future modifications belong in a maintenance/security branch unless a critical compatibility or security issue requires an LTS update.

## Build security baseline

Final Stable requires .NET SDK 10.0.400 / .NET 10.0.11 security runtime baseline or newer compatible patch.
