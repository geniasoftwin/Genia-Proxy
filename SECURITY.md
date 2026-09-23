# Security Policy

## Supported versions

Security work is currently focused on the active stable and development lines of GeniaProxy.

| Version | Status |
| --- | --- |
| 4.5.x | Development / evaluation |
| 4.4.x | Stable baseline |
| Older lines | Best effort only |

## Reporting a vulnerability

Please do **not** publish working exploits, credentials, private server details, or other sensitive security material in a public GitHub issue.

For now, contact the project owner privately through an established trusted channel. A dedicated security-reporting address or GitHub Security Advisory workflow may be added later.

When reporting an issue, include:

- affected GeniaProxy version or commit;
- affected engine and engine version, if relevant;
- operating system and build;
- concise reproduction steps;
- expected and actual behavior;
- logs with all credentials, UUIDs, tokens, certificates, IP addresses, and private endpoints removed or redacted;
- whether the issue can expose traffic, credentials, DNS, routing state, or privilege boundaries.

## Secrets and production configuration

Never commit:

- server passwords or API tokens;
- VLESS UUIDs or equivalent private identifiers;
- REALITY private keys;
- TLS private keys or production certificates;
- SSH keys;
- production endpoints that should remain private;
- unredacted diagnostic bundles containing sensitive network information.

Use sanitized examples and placeholders in documentation and tests.

## Dependency and engine updates

sing-box, Xray-core, and other networking components should be upgraded only after compatibility and regression testing. Experimental or pre-release engine versions belong in isolated development or experiment branches until validated.
