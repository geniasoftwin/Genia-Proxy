# GeniaProxy 4.3.2 LTS RC2

RC2 is the long-term freeze candidate after the manual forced-crash gate exposed a stale-DNS recovery issue in RC1.

## RC2-only change

- When a stale validated TUN recovery snapshot exists in USER mode, GeniaProxy offers a one-time UAC recovery helper.
- The helper runs the existing hardened WindowsTunNetworkService rollback in an elevated short-lived process and exits.
- The main UI remains in USER mode.
- New connections remain fail-closed until the recovery snapshot is removed successfully.
- Closing the USER application while recovery is pending no longer attempts privileged DNS changes and no longer produces a misleading shutdown failure.

Network protocols, XHTTP/XMUX/REALITY generation, TUN route/DNS algorithms, engines and Wintun are otherwise unchanged from RC1.
