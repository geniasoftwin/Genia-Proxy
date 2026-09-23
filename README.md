# GeniaProxy

**GeniaProxy** is a Windows network client and orchestration layer built around modern proxy engines such as **sing-box** and **Xray-core**.

The project focuses on reliable profile management, TUN networking, DNS/routing integration, engine lifecycle control, recovery behavior, and careful evaluation of new protocols and transports before they are promoted to stable builds.

> **Project status**
>
> - **4.4.x** — stable baseline
> - **4.5.x** — active development / protocol and network lab
> - Experimental engine, protocol, transport, and TUN features are evaluated separately before promotion to stable builds.

## Project goals

- Reliable Windows TUN and routing lifecycle
- Safe start/stop, restart, recovery, and rollback behavior
- Support for sing-box and Xray-core
- Reproducible portable builds
- Explicit separation of stable and experimental functionality
- Compatibility-focused testing for DNS, IPv4/IPv6, reconnect, sleep/wake, and profile switching
- Conservative adoption of new engine releases and network features

## Branch model

- `main` — stable, reviewed state
- `develop` — integration branch for the next release
- `feature/*` — focused feature work
- `fix/*` — targeted fixes
- `experiment/*` — protocol/engine experiments not considered production-ready

Release tags use the form `vX.Y.Z`. Pre-release tags should be explicit, for example `v4.5.0-alpha.1`.

## GeniaProxy 4.5

The 4.5 development line is intended for controlled evaluation of new sing-box/Xray capabilities, TUN/network behavior, transport changes, security modes, and recovery improvements.

See [docs/ROADMAP-4.5.0.md](docs/ROADMAP-4.5.0.md) once the development documentation lands.

## Security

Never commit real server credentials, UUIDs, private keys, certificates, API tokens, passwords, production endpoints, or private configuration files.

Security-sensitive reports should be handled privately rather than published with exploitable details. See [SECURITY.md](SECURITY.md).

## Contributing

Development conventions, branch naming, testing expectations, and pull-request guidance are documented in [CONTRIBUTING.md](CONTRIBUTING.md).

## License

No open-source license has been selected yet. Until a license is explicitly added, no permission is granted to copy, modify, or redistribute the source beyond what applicable law allows.

---

New sing-box/Xray releases and experimental protocol features are intentionally tested in isolated branches before adoption into the stable GeniaProxy line.
