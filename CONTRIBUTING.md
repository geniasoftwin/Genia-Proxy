# Contributing to GeniaProxy

GeniaProxy follows a conservative workflow because changes to networking, TUN, DNS, routing, privileges, and engine lifecycle can have system-wide effects.

## Branches

- `main` — stable, reviewed state;
- `develop` — integration branch for the next release;
- `feature/<name>` — focused functionality;
- `fix/<name>` — bug fixes;
- `experiment/<name>` — protocol, transport, TUN, or engine experiments.

Avoid committing experimental networking behavior directly to `main`.

## Commit guidance

Prefer focused commits with descriptive messages, for example:

- `feat: add profile validation`
- `fix: restore routes after failed TUN startup`
- `test: add DNS recovery regression`
- `docs: document engine compatibility matrix`

## Pull requests

A pull request should explain:

1. what changed;
2. why the change is needed;
3. which networking paths are affected;
4. how it was tested;
5. rollback or compatibility concerns.

## Minimum regression checks

For changes that touch networking or engine lifecycle, validate as applicable:

- startup and clean shutdown;
- repeated connect/disconnect cycles;
- TUN route creation and cleanup;
- DNS application and restoration;
- IPv4 and IPv6 behavior;
- profile switching;
- reconnect after network loss;
- sleep/wake recovery;
- failed-start rollback;
- non-admin vs administrator behavior;
- engine crash/restart handling;
- no leaked routes, DNS state, processes, or temporary configuration after exit.

## Engine changes

When changing sing-box or Xray-core versions, record:

- old and new versions;
- stable vs pre-release status;
- relevant upstream changelog items;
- configuration migration requirements;
- known protocol or platform regressions;
- results of the GeniaProxy regression suite.

## Security

Do not include credentials, private keys, production certificates, real access tokens, or private server configurations in commits, issues, logs, or test fixtures.

See [SECURITY.md](SECURITY.md).
