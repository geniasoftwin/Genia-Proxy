using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GeniaProxy.Models;

namespace GeniaProxy.Services
{
    public enum ProtocolLabSessionState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Failed
    }

    /// <summary>
    /// Runs one Protocol Lab capability as an isolated loopback
    /// local-proxy session. This class intentionally owns neither
    /// SystemProxyService nor WindowsTunNetworkService.
    /// </summary>
    public sealed class ProtocolLabSession : IDisposable
    {
        private readonly CoreManager coreManager = new();
        private readonly SemaphoreSlim operationGate = new(1, 1);
        private readonly Func<bool> stableSessionIsRunning;
        private readonly object cancellationSync = new();

        private CancellationTokenSource? operationCancellation;
        private WindowsJobObject? coreJob;
        private string? sessionDirectory;
        private string? runtimeConfigPath;
        private bool disposed;

        public ProtocolLabSession(
            Func<bool>? stableSessionIsRunning = null)
        {
            this.stableSessionIsRunning =
                stableSessionIsRunning ?? (() => false);

            coreManager.SelectCore(ProxyCoreKind.SingBox);
            coreManager.ProcessExited += HandleUnexpectedExit;
        }

        public ProtocolLabSessionState State { get; private set; } =
            ProtocolLabSessionState.Stopped;

        public bool IsRunning => coreManager.IsRunning;

        public string? ActiveCapabilityId { get; private set; }

        public int ActivePort { get; private set; }

        public DateTimeOffset? StartedAt { get; private set; }

        public event Action<string>? LogReceived;

        public event Action<ProtocolLabSessionState>? StateChanged;

        public event Action<int>? UnexpectedExit;

        public async Task StartAsync(
            string capabilityId,
            string configJson,
            int localPort,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await operationGate.WaitAsync(cancellationToken);

            try
            {
                if (coreManager.IsRunning)
                {
                    throw new InvalidOperationException(
                        "Protocol Lab session is already running."
                    );
                }

                if (stableSessionIsRunning())
                {
                    throw new InvalidOperationException(
                        "Protocol Lab cannot start while a stable " +
                        "GeniaProxy session is running."
                    );
                }

                FeatureCapability capability =
                    ProtocolLabFeatureCatalog.RequireSelectable(
                        capabilityId
                    );

                if (capability.EngineFamily !=
                        ProtocolLabEngineFamily.SingBox ||
                    capability.SupportState !=
                        ProtocolLabSupportState.RuntimeVerified)
                {
                    throw new NotSupportedException(
                        "Protocol Lab runner accepts only " +
                        "runtime-verified sing-box capabilities."
                    );
                }

                if (localPort is < 1024 or > 65535)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(localPort),
                        "Protocol Lab local port must be from " +
                        "1024 to 65535."
                    );
                }

                if (!IsPortAvailable(localPort))
                {
                    throw new InvalidOperationException(
                        $"Protocol Lab port 127.0.0.1:{localPort} " +
                        "is already in use."
                    );
                }

                // This is the authoritative fail-closed boundary before
                // any temporary file is written or engine is started.
                ProtocolLabConfigSafetyService
                    .ValidateLocalProxyIsolation(
                        capability.Id,
                        configJson
                    );

                using CancellationTokenSource linkedCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken
                    );

                SetOperationCancellation(linkedCancellation);
                CancellationToken token = linkedCancellation.Token;

                SetState(ProtocolLabSessionState.Starting);

                CreateSessionDirectory();

                runtimeConfigPath = Path.Combine(
                    sessionDirectory!,
                    "config.json"
                );

                await File.WriteAllTextAsync(
                    runtimeConfigPath,
                    configJson,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false
                    ),
                    token
                );

                CoreCommandResult check =
                    await coreManager.CheckConfigAsync(
                        runtimeConfigPath,
                        token
                    );

                if (check.ExitCode != 0)
                {
                    throw new InvalidDataException(
                        "Protocol Lab config was rejected by " +
                        "the pinned sing-box engine."
                    );
                }

                Log(
                    "Protocol Lab config passed pinned sing-box " +
                    "validation."
                );

                coreManager.Start(runtimeConfigPath);

                if (!coreManager.TryGetRunningProcess(
                        out Process? process) ||
                    process is null)
                {
                    throw new InvalidOperationException(
                        "Protocol Lab could not obtain the " +
                        "sing-box process."
                    );
                }

                coreJob = WindowsJobObject.CreateKillOnClose();
                coreJob.Assign(process);

                await WaitForPortReadyAsync(
                    localPort,
                    token
                );

                ActiveCapabilityId = capability.Id;
                ActivePort = localPort;
                StartedAt = DateTimeOffset.Now;

                // sing-box has already parsed the config. Remove secrets
                // from disk as soon as the session is confirmed ready.
                DeleteRuntimeArtifacts();

                SetState(ProtocolLabSessionState.Running);
                Log(
                    $"Protocol Lab {capability.Id} is running on " +
                    $"127.0.0.1:{localPort}."
                );
            }
            catch
            {
                await CleanupFailedStartAsync();
                SetState(ProtocolLabSessionState.Failed);
                throw;
            }
            finally
            {
                ClearOperationCancellation();
                operationGate.Release();
            }
        }

        public async Task StopAsync(
            CancellationToken cancellationToken = default)
        {
            CancelCurrentOperation();
            await operationGate.WaitAsync(cancellationToken);

            try
            {
                if (!coreManager.IsRunning)
                {
                    DeleteRuntimeArtifacts();
                    ResetSession();
                    SetState(ProtocolLabSessionState.Stopped);
                    return;
                }

                SetState(ProtocolLabSessionState.Stopping);

                await coreManager.StopAsync();

                coreJob?.Dispose();
                coreJob = null;

                DeleteRuntimeArtifacts();
                ResetSession();

                SetState(ProtocolLabSessionState.Stopped);
                Log("Protocol Lab session stopped.");
            }
            finally
            {
                operationGate.Release();
            }
        }

        public void CancelCurrentOperation()
        {
            CancellationTokenSource? cancellation;

            lock (cancellationSync)
            {
                cancellation = operationCancellation;
            }

            try
            {
                cancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Operation already completed.
            }
        }

        private async void HandleUnexpectedExit(int exitCode)
        {
            if (disposed)
            {
                return;
            }

            try
            {
                await operationGate.WaitAsync();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                if (State != ProtocolLabSessionState.Running)
                {
                    return;
                }

                coreJob?.Dispose();
                coreJob = null;

                DeleteRuntimeArtifacts();
                ResetSession();

                SetState(ProtocolLabSessionState.Failed);
                Log(
                    "Protocol Lab sing-box process exited " +
                    $"unexpectedly with code {exitCode}."
                );
                UnexpectedExit?.Invoke(exitCode);
            }
            finally
            {
                operationGate.Release();
            }
        }

        private async Task CleanupFailedStartAsync()
        {
            try
            {
                await coreManager.StopAsync();
            }
            catch
            {
                // Preserve the original start failure.
            }

            coreJob?.Dispose();
            coreJob = null;

            DeleteRuntimeArtifacts();
            ResetSession();
        }

        private void CreateSessionDirectory()
        {
            DeleteRuntimeArtifacts();

            sessionDirectory = Path.Combine(
                Path.GetTempPath(),
                "GeniaProxy",
                "ProtocolLab",
                $"{Environment.ProcessId}-" +
                Guid.NewGuid().ToString("N")
            );

            Directory.CreateDirectory(sessionDirectory);
        }

        private void DeleteRuntimeArtifacts()
        {
            string? configPath = runtimeConfigPath;
            string? directory = sessionDirectory;

            runtimeConfigPath = null;
            sessionDirectory = null;

            if (!string.IsNullOrWhiteSpace(configPath))
            {
                try
                {
                    if (File.Exists(configPath))
                    {
                        File.Delete(configPath);
                    }
                }
                catch
                {
                    // Stop/failure cleanup continues with directory cleanup.
                }
            }

            if (!string.IsNullOrWhiteSpace(directory))
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(
                            directory,
                            recursive: true
                        );
                    }
                }
                catch
                {
                    // Best effort: never replace the original session error.
                }
            }
        }

        private async Task WaitForPortReadyAsync(
            int port,
            CancellationToken cancellationToken)
        {
            DateTimeOffset deadline =
                DateTimeOffset.UtcNow.AddSeconds(10);

            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!coreManager.IsRunning)
                {
                    throw new InvalidOperationException(
                        "Protocol Lab sing-box exited before " +
                        "the loopback listener became ready."
                    );
                }

                try
                {
                    using var client = new TcpClient();

                    await client.ConnectAsync(
                        IPAddress.Loopback,
                        port,
                        cancellationToken
                    );

                    return;
                }
                catch (SocketException)
                {
                    // Listener is not ready yet.
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(100),
                    cancellationToken
                );
            }

            throw new TimeoutException(
                $"Protocol Lab listener 127.0.0.1:{port} " +
                "did not become ready within 10 seconds."
            );
        }

        private static bool IsPortAvailable(int port)
        {
            TcpListener? listener = null;

            try
            {
                listener = new TcpListener(
                    IPAddress.Loopback,
                    port
                );

                listener.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                listener?.Stop();
            }
        }

        private void ResetSession()
        {
            ActiveCapabilityId = null;
            ActivePort = 0;
            StartedAt = null;
        }

        private void SetState(ProtocolLabSessionState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        private void Log(string message)
        {
            LogReceived?.Invoke(message);
        }

        private void SetOperationCancellation(
            CancellationTokenSource cancellation)
        {
            lock (cancellationSync)
            {
                operationCancellation = cancellation;
            }
        }

        private void ClearOperationCancellation()
        {
            lock (cancellationSync)
            {
                operationCancellation?.Dispose();
                operationCancellation = null;
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(
                disposed,
                this
            );
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CancelCurrentOperation();

            coreManager.ProcessExited -= HandleUnexpectedExit;

            try
            {
                coreManager.Dispose();
            }
            finally
            {
                coreJob?.Dispose();
                coreJob = null;

                DeleteRuntimeArtifacts();
                ResetSession();

                operationCancellation?.Dispose();
                operationCancellation = null;
                operationGate.Dispose();
            }
        }
    }
}
