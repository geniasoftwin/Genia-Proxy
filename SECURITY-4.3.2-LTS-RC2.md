# Security note — 4.3.2 LTS RC2

The forced-crash LTS gate found that RC1 left the recovery snapshot and leak-safe DNS behind after taskkill, while a non-elevated restart could not restore DNS. RC2 keeps fail-closed behavior but adds a constrained UAC helper mode (`--recover-tun-only`) that performs only validated TUN rollback and exits. The normal application remains non-elevated.

The helper uses the same strict snapshot validation and exact route/DNS ownership checks as the frozen HF4 recovery service. No arbitrary command, path or snapshot data is accepted beyond that existing validation.
