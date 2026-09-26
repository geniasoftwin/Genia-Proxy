# GeniaProxy 4.5.0 Alpha 1 — RC3 TIMING-AB

Purpose: isolate a timing-sensitive Xray TUN readiness race without changing routes, DNS, probe timeout, retry policy, Xray, sing-box, Wintun or Control Plane acceptance logic.

- Mode A: original RC3 timing, 0 ms barrier.
- Mode B: one 150 ms cancellation-aware barrier after Windows system DNS readiness succeeds and immediately before the primary direct UDP DNS probe to 1.1.1.1:53.
- The primary UDP DNS probe still has the original 5 second timeout.
- There is no added retry.
- A failure still rolls back exactly as RC3 does.

Use the launchers in the portable folder. Fully exit GeniaProxy before switching mode.
