namespace GeniaProxy.ControlPlane;

/// <summary>
/// Observability/control-plane layer for GeniaProxy 4.5.0 Alpha 1.
/// It does not start/stop cores, TUN, routes, DNS or system proxy.
/// The frozen 4.4.0 ConnectionSession remains authoritative for networking.
/// </summary>
public sealed class ControlPlaneCoordinator : IDisposable
{
    public static readonly TimeSpan DefaultVerificationTtl =
        TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonlSessionJournal journal;
    private readonly TimeSpan verificationTtl;
    private ConnectionStateMachine? machine;
    private bool disposed;

    public ControlPlaneCoordinator(
        string journalPath,
        TimeSpan? verificationTtl = null)
    {
        journal = new JsonlSessionJournal(journalPath);
        this.verificationTtl =
            verificationTtl ?? DefaultVerificationTtl;
    }

    public ControlPlaneSnapshot Snapshot => machine is null
        ? ControlPlaneSnapshot.Empty
        : ControlPlaneSnapshot.From(machine, verificationTtl);

    public async Task BeginSessionAsync(
        string profileName,
        string mode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (machine is not null && machine.State != ConnectionState.Idle)
            {
                await machine.FinishAsync(
                    "superseded-by-new-session",
                    cancellationToken
                ).ConfigureAwait(false);
            }

            machine?.Dispose();
            machine = new ConnectionStateMachine(
                SessionContext.Start(profileName, core: null, mode: mode),
                journal
            );

            await machine.StartAsync(
                "connect-requested",
                cancellationToken
            ).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkNetworkReadyAsync(
        string core,
        string mode,
        bool tunMode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (machine is null)
            {
                return;
            }

            machine.UpdateSessionMetadata(core: core, mode: mode);

            ConnectionState readyState = tunMode
                ? ConnectionState.TunWarmup
                : ConnectionState.LocalReady;

            if (machine.State == ConnectionState.Connecting)
            {
                string reason = tunMode
                    ? "tun-network-ready"
                    : "local-listener-ready";

                await machine.TransitionAsync(
                    readyState,
                    reason,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);

                await machine.RecordEventAsync(
                    tunMode
                        ? ConnectionEventKind.TunWarmupStarted
                        : ConnectionEventKind.LocalReady,
                    reason,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkVerificationStartedAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        Guid sessionId = Snapshot.SessionId;
        if (sessionId == Guid.Empty)
        {
            return;
        }

        _ = await MarkVerificationStartedAsync(
            sessionId,
            source,
            cancellationToken
        ).ConfigureAwait(false);
    }

    public async Task<bool> MarkVerificationStartedAsync(
        Guid sessionId,
        string source,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrentSessionUnsafe(sessionId) ||
                machine!.State == ConnectionState.Idle)
            {
                return false;
            }

            await machine.RecordEventAsync(
                ConnectionEventKind.VerificationStarted,
                "exit-verification-started",
                new Dictionary<string, string?>
                {
                    ["verificationSource"] = source
                },
                cancellationToken
            ).ConfigureAwait(false);

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkVerifiedAsync(
        string verifiedExit,
        string? expectedExit,
        string source,
        CancellationToken cancellationToken = default)
    {
        Guid sessionId = Snapshot.SessionId;
        if (sessionId == Guid.Empty)
        {
            return;
        }

        _ = await MarkVerifiedAsync(
            sessionId,
            verifiedExit,
            expectedExit,
            source,
            cancellationToken
        ).ConfigureAwait(false);
    }

    public async Task<bool> MarkVerifiedAsync(
        Guid sessionId,
        string verifiedExit,
        string? expectedExit,
        string source,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrentSessionUnsafe(sessionId))
            {
                return false;
            }

            await machine!.MarkVerifiedAsync(
                verifiedExit,
                expectedExit,
                source,
                cancellationToken
            ).ConfigureAwait(false);

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task MarkVerificationFailedAsync(
        string reason,
        string source,
        CancellationToken cancellationToken = default)
    {
        Guid sessionId = Snapshot.SessionId;
        if (sessionId == Guid.Empty)
        {
            return;
        }

        _ = await MarkVerificationFailedAsync(
            sessionId,
            reason,
            source,
            cancellationToken
        ).ConfigureAwait(false);
    }

    public async Task<bool> MarkVerificationFailedAsync(
        Guid sessionId,
        string reason,
        string source,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrentSessionUnsafe(sessionId) ||
                machine!.State == ConnectionState.Idle)
            {
                return false;
            }

            await machine.MarkVerificationFailedAsync(
                reason,
                source,
                cancellationToken
            ).ConfigureAwait(false);

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task BeginDisconnectAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (machine is null ||
                machine.State is ConnectionState.Idle or
                    ConnectionState.Disconnecting)
            {
                return;
            }

            await machine.TransitionAsync(
                ConnectionState.Disconnecting,
                reason,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CompleteDisconnectAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (machine is null)
            {
                return;
            }

            await machine.FinishAsync(
                reason,
                cancellationToken
            ).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task BeginRecoveryAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (machine is null || machine.State == ConnectionState.Idle)
            {
                return;
            }

            await machine.InvalidateVerificationAsync(
                "recovery-invalidated-verification",
                cancellationToken
            ).ConfigureAwait(false);

            if (machine.State != ConnectionState.Recovering)
            {
                await machine.TransitionAsync(
                    ConnectionState.Recovering,
                    reason,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);
            }

            await machine.RecordEventAsync(
                ConnectionEventKind.RecoveryStarted,
                reason,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CompleteRecoveryAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (machine is null)
            {
                return;
            }

            if (machine.State == ConnectionState.Recovering)
            {
                await machine.RecordEventAsync(
                    ConnectionEventKind.RecoveryCompleted,
                    reason,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);

                await machine.FinishAsync(
                    reason,
                    cancellationToken
                ).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        machine?.Dispose();
        journal.DisposeAsync().AsTask().GetAwaiter().GetResult();
        gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private bool IsCurrentSessionUnsafe(Guid sessionId) =>
        sessionId != Guid.Empty &&
        machine is not null &&
        machine.Session.SessionId == sessionId;

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(disposed, this);
}
