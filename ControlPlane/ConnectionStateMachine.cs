namespace GeniaProxy.ControlPlane;

public sealed class ConnectionStateMachine : IDisposable
{
    private static readonly Dictionary<ConnectionState, HashSet<ConnectionState>> Allowed =
        new()
        {
            [ConnectionState.Idle] = [ConnectionState.Connecting],
            [ConnectionState.Connecting] = [
                ConnectionState.LocalReady,
                ConnectionState.TunWarmup,
                ConnectionState.Disconnecting,
                ConnectionState.Recovering
            ],
            [ConnectionState.LocalReady] = [
                ConnectionState.Verified,
                ConnectionState.Switching,
                ConnectionState.Disconnecting,
                ConnectionState.Recovering
            ],
            [ConnectionState.TunWarmup] = [
                ConnectionState.Verified,
                ConnectionState.Switching,
                ConnectionState.Disconnecting,
                ConnectionState.Recovering
            ],
            [ConnectionState.Verified] = [
                ConnectionState.LocalReady,
                ConnectionState.TunWarmup,
                ConnectionState.Switching,
                ConnectionState.Disconnecting,
                ConnectionState.Recovering
            ],
            [ConnectionState.Switching] = [
                ConnectionState.Connecting,
                ConnectionState.LocalReady,
                ConnectionState.TunWarmup,
                ConnectionState.Verified,
                ConnectionState.Disconnecting,
                ConnectionState.Recovering
            ],
            [ConnectionState.Disconnecting] = [
                ConnectionState.Idle,
                ConnectionState.Recovering
            ],
            [ConnectionState.Recovering] = [
                ConnectionState.Connecting,
                ConnectionState.LocalReady,
                ConnectionState.TunWarmup,
                ConnectionState.Verified,
                ConnectionState.Disconnecting,
                ConnectionState.Idle
            ]
        };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ISessionJournal journal;
    private long sequence;
    private bool disposed;

    public ConnectionStateMachine(
        SessionContext session,
        ISessionJournal journal)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        State = ConnectionState.Idle;
        StateSinceUtc = session.StartedAtUtc;
    }

    public SessionContext Session { get; private set; }

    public ConnectionState State { get; private set; }

    public DateTimeOffset StateSinceUtc { get; private set; }

    public event EventHandler<StateChangedEventArgs>? StateChanged;

    public bool CanTransitionTo(ConnectionState next) =>
        next != State &&
        Allowed.TryGetValue(State, out HashSet<ConnectionState>? states) &&
        states.Contains(next);

    public async Task StartAsync(
        string? reason = "connect-requested",
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != ConnectionState.Idle)
            {
                throw new InvalidOperationException(
                    $"Session {Session.SessionId} cannot start from {State}."
                );
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            await AppendUnsafeAsync(
                ConnectionEventKind.SessionStarted,
                ConnectionState.Idle,
                previousState: null,
                reason: reason,
                data: SessionMetadata(),
                timestamp: now,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);

            await TransitionUnsafeAsync(
                ConnectionState.Connecting,
                reason,
                data: null,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void UpdateSessionMetadata(
        string? profileName = null,
        string? core = null,
        string? mode = null)
    {
        Session = Session with
        {
            ProfileName = profileName ?? Session.ProfileName,
            Core = core ?? Session.Core,
            Mode = mode ?? Session.Mode
        };
    }

    public async Task TransitionAsync(
        ConnectionState next,
        string? reason = null,
        IReadOnlyDictionary<string, string?>? data = null,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await TransitionUnsafeAsync(
                next,
                reason,
                data,
                cancellationToken
            ).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RecordEventAsync(
        ConnectionEventKind kind,
        string? reason = null,
        IReadOnlyDictionary<string, string?>? data = null,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AppendUnsafeAsync(
                kind,
                State,
                previousState: null,
                reason: reason,
                data: data,
                timestamp: DateTimeOffset.UtcNow,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkVerifiedAsync(
        string verifiedExit,
        string? expectedExit = null,
        string? verificationSource = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedExit);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string normalizedExit = verifiedExit.Trim();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            // FIX3: verification is idempotent for the current session. A fresh
            // successful probe while already VERIFIED refreshes evidence/TTL only;
            // it must not synthesize an illegal Verified -> Verified transition.
            if (State == ConnectionState.Verified)
            {
                string? previousVerifiedExit = Session.VerifiedExit;
                Session = Session with
                {
                    ExpectedExit = expectedExit ?? Session.ExpectedExit,
                    VerifiedExit = normalizedExit,
                    VerifiedAtUtc = now,
                    VerificationSucceeded = true,
                    VerificationSource = verificationSource,
                    LastReason = "exit-verification-refreshed"
                };

                await AppendUnsafeAsync(
                    ConnectionEventKind.VerificationRefreshed,
                    ConnectionState.Verified,
                    previousState: null,
                    reason: "exit-verification-refreshed",
                    data: new Dictionary<string, string?>
                    {
                        ["expectedExit"] = Session.ExpectedExit,
                        ["previousVerifiedExit"] = previousVerifiedExit,
                        ["verifiedExit"] = normalizedExit,
                        ["verificationSource"] = verificationSource
                    },
                    timestamp: now,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);
                return;
            }

            if (State is not (
                    ConnectionState.LocalReady or
                    ConnectionState.TunWarmup or
                    ConnectionState.Recovering or
                    ConnectionState.Switching))
            {
                throw new InvalidOperationException(
                    $"Verification cannot complete while state is {State}."
                );
            }

            ConnectionState previous = State;
            Session = Session with
            {
                ExpectedExit = expectedExit,
                VerifiedExit = normalizedExit,
                VerifiedAtUtc = now,
                VerificationSucceeded = true,
                VerificationSource = verificationSource,
                LastReason = "exit-verified"
            };
            State = ConnectionState.Verified;
            StateSinceUtc = now;

            var data = new Dictionary<string, string?>
            {
                ["expectedExit"] = expectedExit,
                ["verifiedExit"] = normalizedExit,
                ["verificationSource"] = verificationSource
            };

            await AppendUnsafeAsync(
                ConnectionEventKind.VerificationSucceeded,
                ConnectionState.Verified,
                previous,
                "exit-verified",
                data,
                now,
                cancellationToken
            ).ConfigureAwait(false);

            StateChanged?.Invoke(
                this,
                new StateChangedEventArgs(
                    previous,
                    ConnectionState.Verified,
                    now,
                    "exit-verified"
                )
            );
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkVerificationFailedAsync(
        string? reason = null,
        string? verificationSource = null,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Session = Session with
            {
                VerifiedExit = null,
                VerifiedAtUtc = null,
                VerificationSucceeded = false,
                VerificationSource = verificationSource,
                LastReason = reason ?? "exit-verification-failed"
            };

            await AppendUnsafeAsync(
                ConnectionEventKind.VerificationFailed,
                State,
                previousState: null,
                reason: Session.LastReason,
                data: new Dictionary<string, string?>
                {
                    ["verificationSource"] = verificationSource
                },
                timestamp: DateTimeOffset.UtcNow,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);

            if (State == ConnectionState.Verified)
            {
                ConnectionState fallback = Session.Mode == "tun"
                    ? ConnectionState.TunWarmup
                    : ConnectionState.LocalReady;

                await TransitionUnsafeAsync(
                    fallback,
                    Session.LastReason,
                    data: null,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task FinishAsync(
        string? reason = "session-complete",
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == ConnectionState.Idle)
            {
                return;
            }

            if (State != ConnectionState.Idle)
            {
                if (State is not (
                        ConnectionState.Disconnecting or
                        ConnectionState.Recovering))
                {
                    await TransitionUnsafeAsync(
                        ConnectionState.Disconnecting,
                        reason,
                        data: null,
                        cancellationToken: cancellationToken
                    ).ConfigureAwait(false);
                }

                await TransitionUnsafeAsync(
                    ConnectionState.Idle,
                    reason,
                    data: null,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);
            }

            await AppendUnsafeAsync(
                ConnectionEventKind.SessionStopped,
                ConnectionState.Idle,
                previousState: null,
                reason: reason,
                data: SessionMetadata(),
                timestamp: DateTimeOffset.UtcNow,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public bool IsVerificationFresh(
        TimeSpan ttl,
        DateTimeOffset? nowUtc = null)
    {
        if (State != ConnectionState.Verified ||
            Session.VerificationSucceeded != true ||
            Session.VerifiedAtUtc is null ||
            string.IsNullOrWhiteSpace(Session.VerifiedExit))
        {
            return false;
        }

        DateTimeOffset now = nowUtc ?? DateTimeOffset.UtcNow;
        TimeSpan age = now - Session.VerifiedAtUtc.Value;
        return age >= TimeSpan.Zero && age <= ttl;
    }

    public async Task InvalidateVerificationAsync(
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Session = Session with
            {
                VerifiedExit = null,
                VerifiedAtUtc = null,
                VerificationSucceeded = null,
                VerificationSource = null,
                LastReason = reason ?? "verification-invalidated"
            };

            await AppendUnsafeAsync(
                ConnectionEventKind.Warning,
                State,
                previousState: null,
                reason: Session.LastReason,
                data: null,
                timestamp: DateTimeOffset.UtcNow,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task TransitionUnsafeAsync(
        ConnectionState next,
        string? reason,
        IReadOnlyDictionary<string, string?>? data,
        CancellationToken cancellationToken)
    {
        ConnectionState previous = State;
        if (!CanTransitionTo(next))
        {
            throw new InvalidOperationException(
                $"Illegal connection state transition: {previous} -> {next}."
            );
        }

        State = next;
        StateSinceUtc = DateTimeOffset.UtcNow;
        Session = Session with { LastReason = reason };

        await AppendUnsafeAsync(
            ConnectionEventKind.StateTransition,
            next,
            previous,
            reason,
            data,
            StateSinceUtc,
            cancellationToken
        ).ConfigureAwait(false);

        StateChanged?.Invoke(
            this,
            new StateChangedEventArgs(
                previous,
                next,
                StateSinceUtc,
                reason
            )
        );
    }

    private ValueTask AppendUnsafeAsync(
        ConnectionEventKind kind,
        ConnectionState state,
        ConnectionState? previousState,
        string? reason,
        IReadOnlyDictionary<string, string?>? data,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken) =>
        journal.AppendAsync(
            new JournalEvent(
                timestamp,
                Session.SessionId,
                ++sequence,
                kind,
                state,
                previousState,
                reason,
                data
            ),
            cancellationToken
        );

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private Dictionary<string, string?> SessionMetadata() =>
        new()
        {
            ["profile"] = Session.ProfileName,
            ["core"] = Session.Core,
            ["mode"] = Session.Mode
        };
}

public sealed class StateChangedEventArgs : EventArgs
{
    public StateChangedEventArgs(
        ConnectionState previous,
        ConnectionState current,
        DateTimeOffset changedAtUtc,
        string? reason)
    {
        Previous = previous;
        Current = current;
        ChangedAtUtc = changedAtUtc;
        Reason = reason;
    }

    public ConnectionState Previous { get; }
    public ConnectionState Current { get; }
    public DateTimeOffset ChangedAtUtc { get; }
    public string? Reason { get; }
}
