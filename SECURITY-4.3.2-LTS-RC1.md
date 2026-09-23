# Security status — GeniaProxy 4.3.2 LTS RC1

Retained HF4 controls:
- Xray pinnedPeerCertSha256 rejected fail-closed;
- Xray/sing-box engine SHA-256 checked before launch and after publish;
- TUN IPv6 fail-closed policy;
- strict TUN recovery snapshot validation;
- hardened elevated rollback and exact route ownership;
- core maintenance blocked in elevated ADMIN mode;
- cancellation-aware endpoint DNS resolution;
- portable release excludes settings, recovery snapshots and user profiles.

LTS policy: no network/security code changes after approval except a critical advisory or protocol break, and any such change starts a new validation cycle.
