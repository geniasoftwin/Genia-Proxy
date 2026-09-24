# GeniaProxy 4.5.0 Alpha 1 EXP1

## State Machine & Structured Session Journal

This experiment starts the 4.5 control-plane on top of the frozen GeniaProxy 4.4.0 Final Stable Direct Bridge network baseline.

### EXP1 FIX3

- Makes successful verification refresh idempotent while the current session is already `Verified`.
- Adds `verificationRefreshed` journal events without synthesizing `Verified -> Verified` state transitions.
- Keeps refresh session-bound: a stale session ID cannot refresh the current session evidence.
- Browser Switcher TUN WebRTC self-test now treats extension-origin local ICE as informational when the system TUN is VERIFIED and no public ICE candidate is exposed.
- Clears transient Manager route metadata (`transition`, endpoint and current verification evidence) when Direct Bridge/Manager is lost, while retaining fail-closed Hard Kill Switch behavior.
- Does not modify frozen 4.4.0 TUN/DNS/Xray/sing-box algorithms.

### EXP1 FIX2

- Automatic TUN control-plane verification starts after the frozen 4.4.0 TUN path reports network ready.
- Verification callbacks are bound to the originating `sessionId`; late results from a disconnected/superseded session are ignored.
- Direct Bridge `verifiedExitIp` / `verifiedExitAt` / `verifiedExitSucceeded` are derived only from the active Control Plane session.
- Legacy per-profile `LastExitIp` / `LastTestUtc` remain historical settings and are not treated as current-route proof.
- The automatic probe is diagnostic-only and is cancelled on disconnect, reconnect, recovery, or application shutdown.

### EXP1 FIX1

- JSONL journal uses short-lived append handles with `FileShare.ReadWrite | FileShare.Delete`; diagnostics can read/copy the journal during an active session.
- State-machine synchronization objects are disposed explicitly.

### Included

- Explicit control-plane states: `idle -> connecting -> local_ready / tun_warmup -> verified -> switching -> disconnecting -> recovering`.
- Per-connection `sessionId`; verification is never inherited by a new session.
- `verifiedExit`, `verifiedAt`, verification source/status and 60-second freshness policy.
- Append-only JSONL session journal at `data/diagnostics/session-journal.jsonl`.
- Diagnostics snapshot showing control-plane state without changing networking.
- Manager channel-test integration: a successful test with an exit IP promotes the current session to `verified`.
- Failed/repeated verification invalidates the verified state.

### Frozen by design

EXP1 does not modify the 4.4.0 network transaction implementation. In particular the following remain on the Final Stable baseline:

- TUN/DNS/routes and recovery algorithms.
- Core process lifecycle implementation.
- Xray/sing-box runtime configuration algorithms.
- System proxy implementation.
- Connection test implementation.
- Browser Switcher Direct 5.6.0 Stable behavior and Direct Bridge protocol v1.

### Deferred

Authenticated Direct Bridge, Manager Route Coherence v2 and browser/manager coherence enforcement are intentionally deferred to the next Alpha 1 experiments after this lifecycle/journal foundation passes regression testing.

## EXP1 FIX4 — Startup Recovery Hardening + Protocol Lab Boundary

FIX4 hardens the process/startup layer discovered by the crash/restart test. It does not change the frozen TUN/DNS/core algorithms.

- Early startup journal: `data\diagnostics\startup-journal.jsonl`.
- Second-instance activation requires an ACK from the primary instance after the UI dispatcher handles activation.
- If ACK fails, the secondary waits briefly for safe mutex takeover. If the old process remains alive, GeniaProxy shows a visible warning instead of exiting silently.
- Elevated USER → ADMIN handoff has an explicit primary-exit gate.
- Pending TUN recovery is detected before MainWindow; an ADMIN startup attempts the existing rollback before normal UI initialization.
- Protocol Watch features are separated behind the Protocol Lab boundary and remain disabled in Alpha 1.
