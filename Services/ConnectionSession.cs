using System.Net;
using System.Net.Sockets;
using System.Text;
using GeniaProxy.Models;

namespace GeniaProxy.Services
{
    public enum ConnectionSessionState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Failed
    }

    public sealed class ConnectionSession : IDisposable
    {
        private static readonly char[] OutputLineSeparators =
            { '\r', '\n' };

        private readonly CoreManager coreManager = new();
        private readonly SystemProxyService systemProxyService = new();
        private readonly WindowsTunNetworkService tunNetworkService = new();
        private readonly SemaphoreSlim operationGate = new(1, 1);
        private readonly object cancellationSync = new();

        private CancellationTokenSource? operationCancellation;
        private WindowsJobObject? coreJob;
        private string? runtimeConfigPath;
        private XrayTunPreparation? xrayTunPreparation;
        private bool disposed;

        public string ProfilesDirectory { get; } = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "profiles"
        );

        public string RuntimeDirectory { get; } = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "runtime"
        );

        public ConnectionSessionState State { get; private set; } =
            ConnectionSessionState.Stopped;

        public bool IsRunning => coreManager.IsRunning;

        public bool HasPendingTunRecovery =>
            tunNetworkService.HasPendingBackup;

        public bool DetailedCoreLoggingEnabled { get; set; }

        public string? ActiveProfile { get; private set; }

        public int ActivePort { get; private set; }

        public ProxyCoreKind? ActiveCore { get; private set; }

        public ConnectionMode? ActiveMode { get; private set; }

        public string ActiveCoreName => ActiveCore is ProxyCoreKind core
            ? ProfileFormatService.FormatCore(core)
            : "—";

        public bool UsesSystemProxy =>
            systemProxyService.IsEnabledByApplication;

        public DateTimeOffset? StartedAt { get; private set; }

        public event Action<string>? LogReceived;

        public event Action<ConnectionSessionState>? StateChanged;

        public event Action<int>? UnexpectedExit;

        public ConnectionSession()
        {
            coreManager.OutputReceived += ForwardCoreOutput;
            coreManager.ProcessExited += HandleUnexpectedExit;
        }

        public async Task InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            Directory.CreateDirectory(ProfilesDirectory);
            CleanupStaleRuntimeFiles();

            if (systemProxyService.HasPendingBackup)
            {
                ProxyRestoreOutcome outcome =
                    systemProxyService.Restore();

                Log(outcome == ProxyRestoreOutcome.Restored
                    ? "Восстановлены системные настройки прокси после предыдущего сеанса."
                    : "Найденная резервная копия прокси обработана без изменения текущих настроек.");
            }

            if (tunNetworkService.HasPendingBackup)
            {
                if (!WindowsElevationService.IsAdministrator)
                {
                    Log(
                        "Найдена незавершённая TUN-сессия. Для восстановления " +
                        "маршрутов/DNS запустите GeniaProxy от имени администратора."
                    );
                }
                else
                {
                    try
                    {
                        TunRestoreOutcome outcome = await tunNetworkService
                            .RestoreAsync(cancellationToken);

                        Log(outcome == TunRestoreOutcome.DnsPreserved
                            ? "TUN routes восстановлены; DNS был изменён другой программой и сохранён."
                            : "Восстановлены маршруты/DNS после предыдущей TUN-сессии.");
                    }
                    catch (Exception ex)
                    {
                        Log(
                            "Не удалось автоматически восстановить TUN-сеть: " +
                            ex.Message
                        );
                    }
                }
            }
        }

        public async Task StartAsync(
            string profileName,
            int localPort,
            ConnectionMode connectionMode,
            CorePreference corePreference,
            TunStackPreference tunStackPreference,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await operationGate.WaitAsync(cancellationToken);

            try
            {
                if (coreManager.IsRunning)
                {
                    throw new InvalidOperationException(
                        "Прокси уже запущен."
                    );
                }

                using CancellationTokenSource linkedCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken
                    );

                SetOperationCancellation(linkedCancellation);
                CancellationToken token = linkedCancellation.Token;

                SetState(ConnectionSessionState.Starting);

                string normalizedName =
                    ProfileNameValidator.Normalize(profileName);

                string? validationError = ProfileNameValidator
                    .GetValidationError(normalizedName);

                if (validationError is not null)
                {
                    throw new InvalidDataException(validationError);
                }

                if (localPort is < 1024 or > 65535)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(localPort),
                        "Локальный порт должен быть от 1024 до 65535."
                    );
                }

                if (connectionMode == ConnectionMode.Tun &&
                    !WindowsElevationService.IsAdministrator)
                {
                    throw new UnauthorizedAccessException(
                        "Для режима TUN запустите GeniaProxy " +
                        "от имени администратора."
                    );
                }

                if (tunNetworkService.HasPendingBackup)
                {
                    throw new InvalidOperationException(
                        "Не восстановлена предыдущая TUN-сессия. " +
                        "Подтвердите UAC-восстановление сети и повторите подключение."
                    );
                }

                string profilePath = Path.Combine(
                    ProfilesDirectory,
                    normalizedName + ".json"
                );

                string source = await ReadProfileAsync(
                    profilePath,
                    token
                );

                ProfileDescriptor descriptor =
                    ProfileFormatService.Inspect(source);

                ProxyCoreKind selectedCore =
                    ProfileFormatService.ResolveCore(
                        descriptor,
                        corePreference
                    );

                coreManager.SelectCore(selectedCore);

                int systemProxyPort =
                    connectionMode == ConnectionMode.SystemProxy &&
                    selectedCore == ProxyCoreKind.Xray
                        ? XrayProfileImportService.GetSystemProxyPort(
                            localPort
                        )
                        : localPort;

                if (connectionMode != ConnectionMode.Tun &&
                    !IsPortAvailable(localPort))
                {
                    throw new InvalidOperationException(
                        $"Порт 127.0.0.1:{localPort} уже используется."
                    );
                }

                if (systemProxyPort != localPort &&
                    !IsPortAvailable(systemProxyPort))
                {
                    throw new InvalidOperationException(
                        $"Служебный HTTP-порт 127.0.0.1:" +
                        $"{systemProxyPort} уже используется."
                    );
                }

                Log($"Выбран профиль: {normalizedName}");
                Log($"Выбрано ядро: {coreManager.DisplayName}");
                Log($"Режим: {FormatConnectionMode(connectionMode)}");

                if (connectionMode == ConnectionMode.Tun)
                {
                    Log(selectedCore == ProxyCoreKind.SingBox
                        ? $"Стек TUN: {FormatTunStack(tunStackPreference)}"
                        : "Xray TUN: Windows routes/DNS управляет GeniaProxy.");
                }

                if (connectionMode != ConnectionMode.Tun)
                {
                    Log($"Локальный адрес: 127.0.0.1:{localPort}");
                }

                if (connectionMode == ConnectionMode.Tun)
                {
                    await WindowsTunNetworkService
                        .EnsureIpv6LeakSafeAsync(token);

                    Log(
                        $"{coreManager.DisplayName} TUN: " +
                        "IPv6 leak guard подтверждён."
                    );
                }

                if (connectionMode == ConnectionMode.Tun &&
                    selectedCore == ProxyCoreKind.Xray)
                {
                    xrayTunPreparation = await WindowsTunNetworkService
                        .PrepareXrayAsync(source, token);

                    Log(
                        $"Xray endpoint: {xrayTunPreparation.OriginalHost}:" +
                        $"{xrayTunPreparation.ProxyPort} -> " +
                        xrayTunPreparation.ProxyEndpoint
                    );
                    Log(
                        $"Физический интерфейс: " +
                        $"{xrayTunPreparation.PhysicalInterfaceAlias} " +
                        $"(ifIndex {xrayTunPreparation.PhysicalInterfaceIndex}), " +
                        $"gateway {xrayTunPreparation.PhysicalGateway}."
                    );
                }

                runtimeConfigPath = await CreateRuntimeConfigAsync(
                    source,
                    localPort,
                    connectionMode,
                    selectedCore,
                    systemProxyPort,
                    tunStackPreference,
                    xrayTunPreparation,
                    token
                );

                Log("Временная конфигурация создана.");

                CoreCommandResult check =
                    await coreManager.CheckConfigAsync(
                        runtimeConfigPath,
                        token
                    );

                if (check.ExitCode != 0)
                {
                    AppendCommandOutput(check.StandardOutput, force: true);
                    AppendCommandOutput(check.StandardError, force: true);

                    throw new InvalidDataException(
                        $"Профиль отклонён проверкой {coreManager.DisplayName} " +
                        $"с кодом {check.ExitCode}."
                    );
                }

                AppendCommandOutput(check.StandardOutput);
                AppendCommandOutput(check.StandardError);
                Log($"Профиль прошёл проверку {coreManager.DisplayName}.");

                coreManager.Start(runtimeConfigPath);

                if (!coreManager.TryGetRunningProcess(
                        out System.Diagnostics.Process? process) ||
                    process is null)
                {
                    throw new InvalidOperationException(
                        $"Не удалось получить процесс {coreManager.DisplayName}."
                    );
                }

                coreJob = WindowsJobObject.CreateKillOnClose();
                coreJob.Assign(process);
                Log($"Защита процесса {coreManager.DisplayName} включена.");

                if (connectionMode == ConnectionMode.Tun)
                {
                    string expectedTunName = selectedCore == ProxyCoreKind.Xray
                        ? WindowsTunNetworkService.XrayTunInterfaceName
                        : WindowsTunNetworkService.SingBoxTunInterfaceName;

                    TunAdapterInfo adapter = await WindowsTunNetworkService
                        .WaitForTunAdapterReadyAsync(
                            expectedTunName,
                            () => coreManager.IsRunning,
                            coreManager.DisplayName,
                            token
                        );

                    Log(
                        $"TUN adapter готов: {adapter.InterfaceAlias} " +
                        $"(ifIndex {adapter.InterfaceIndex}, IPv4 {adapter.Ipv4Address})."
                    );

                    if (selectedCore == ProxyCoreKind.Xray)
                    {
                        XrayTunPreparation preparation =
                            xrayTunPreparation ??
                            throw new InvalidOperationException(
                                "Xray TUN preparation отсутствует."
                            );

                        await tunNetworkService.ApplyXrayAsync(
                            preparation,
                            adapter,
                            token
                        );

                        Log(
                            "Xray TUN routes, DNS и connectivity probe подтверждены."
                        );
                    }
                    else
                    {
                        await WindowsTunNetworkService.VerifySingBoxAsync(
                            adapter,
                            token
                        );

                        Log(
                            "sing-box auto_route/strict_route и connectivity probe подтверждены."
                        );
                    }
                }
                else
                {
                    await WaitForPortReadyAsync(localPort, token);

                    if (systemProxyPort != localPort)
                    {
                        await WaitForTcpPortReadyAsync(
                            systemProxyPort,
                            token
                        );
                    }
                }

                if (connectionMode == ConnectionMode.SystemProxy)
                {
                    systemProxyService.Enable(systemProxyPort);
                    Log("Системный прокси Windows включён.");
                }

                ActiveProfile = normalizedName;
                ActivePort = localPort;
                ActiveCore = selectedCore;
                ActiveMode = connectionMode;
                StartedAt = DateTimeOffset.Now;

                DeleteRuntimeConfig();
                SetState(ConnectionSessionState.Running);
                Log(connectionMode == ConnectionMode.Tun
                    ? "TUN-подключение запущено."
                    : "Локальный прокси запущен.");
            }
            catch
            {
                await CleanupFailedStartAsync();
                SetState(ConnectionSessionState.Failed);
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
                if (!coreManager.IsRunning &&
                    !systemProxyService.HasPendingBackup &&
                    !tunNetworkService.HasPendingBackup)
                {
                    ResetSession();
                    SetState(ConnectionSessionState.Stopped);
                    return;
                }

                SetState(ConnectionSessionState.Stopping);
                // После начала rollback сетевые изменения нельзя оставлять
                // наполовину восстановленными из-за отмены UI-операции.
                await RestoreTunNetworkAsync(CancellationToken.None);
                RestoreSystemProxy();
                await coreManager.StopAsync();

                coreJob?.Dispose();
                coreJob = null;

                DeleteRuntimeConfig();
                ResetSession();
                SetState(ConnectionSessionState.Stopped);
                Log("Подключение остановлено.");
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
                // Операция уже завершилась.
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
                if (State != ConnectionSessionState.Running)
                {
                    return;
                }

                try
                {
                    await RestoreTunNetworkAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log(
                        "Не удалось полностью восстановить TUN-сеть после " +
                        "аварийного завершения ядра: " + ex.Message
                    );
                }

                try
                {
                    RestoreSystemProxy();
                }
                catch (Exception ex)
                {
                    Log(
                        "Не удалось восстановить системный прокси после " +
                        "аварийного завершения ядра: " + ex.Message
                    );
                }

                coreJob?.Dispose();
                coreJob = null;
                DeleteRuntimeConfig();
                ResetSession();
                SetState(ConnectionSessionState.Failed);
                Log($"Процесс {coreManager.DisplayName} завершился с кодом {exitCode}.");
                UnexpectedExit?.Invoke(exitCode);
            }
            catch (Exception ex)
            {
                Log("Ошибка обработки завершения ядра: " + ex.Message);
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
                await RestoreTunNetworkAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log("Не удалось восстановить TUN-сеть: " + ex.Message);
            }

            try
            {
                RestoreSystemProxy();
            }
            catch (Exception ex)
            {
                Log("Не удалось восстановить системный прокси: " + ex.Message);
            }

            try
            {
                await coreManager.StopAsync();
            }
            catch (Exception ex)
            {
                Log($"Не удалось остановить {coreManager.DisplayName}: " + ex.Message);
            }

            coreJob?.Dispose();
            coreJob = null;
            DeleteRuntimeConfig();
            ResetSession();
        }

        private async Task<string> CreateRuntimeConfigAsync(
            string source,
            int localPort,
            ConnectionMode connectionMode,
            ProxyCoreKind coreKind,
            int systemProxyPort,
            TunStackPreference tunStackPreference,
            XrayTunPreparation? xrayPreparation,
            CancellationToken cancellationToken)
        {
            string runtimeJson = coreKind == ProxyCoreKind.Xray
                ? XrayProfileImportService.NormalizeConfig(
                    source,
                    localPort,
                    connectionMode,
                    systemProxyPort,
                    xrayPreparation?.ProxyEndpoint.ToString()
                )
                : JsonProfileImportService.NormalizeConfig(
                    source,
                    localPort,
                    connectionMode,
                    tunStackPreference
                );

            Directory.CreateDirectory(RuntimeDirectory);

            string path = Path.Combine(
                RuntimeDirectory,
                $"runtime-{Guid.NewGuid():N}.json"
            );

            await AtomicFileWriter.WriteAllTextAsync(
                path,
                runtimeJson,
                new UTF8Encoding(false),
                cancellationToken
            );

            return path;
        }

        private static async Task<string> ReadProfileAsync(
            string profilePath,
            CancellationToken cancellationToken)
        {
            var profile = new FileInfo(profilePath);

            if (!profile.Exists)
            {
                throw new FileNotFoundException(
                    "Файл профиля не найден.",
                    profilePath
                );
            }

            if (profile.Length > JsonProfileImportService.MaxProfileBytes)
            {
                throw new InvalidDataException(
                    "Профиль превышает допустимый размер 1 МБ."
                );
            }

            return await File.ReadAllTextAsync(
                profilePath,
                cancellationToken
            );
        }

        private async Task WaitForPortReadyAsync(
            int port,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(7);

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!coreManager.IsRunning)
                {
                    throw new InvalidOperationException(
                        $"{coreManager.DisplayName} завершился до готовности локального прокси."
                    );
                }

                try
                {
                    using var client = new TcpClient();
                    using var attempt =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken
                        );

                    attempt.CancelAfter(TimeSpan.FromMilliseconds(700));

                    await client.ConnectAsync(
                        IPAddress.Loopback,
                        port,
                        attempt.Token
                    );

                    NetworkStream stream = client.GetStream();
                    byte[] request = [0x05, 0x01, 0x00];
                    byte[] response = new byte[2];

                    await stream.WriteAsync(request, attempt.Token);
                    await stream.ReadExactlyAsync(response, attempt.Token);

                    if (response[0] == 0x05 &&
                        response[1] == 0x00 &&
                        coreManager.IsRunning)
                    {
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (SocketException)
                {
                    // Порт ещё не готов.
                }
                catch (IOException)
                {
                    // Порт открыт, но mixed/SOCKS ещё не отвечает.
                }

                await Task.Delay(120, cancellationToken);
            }

            throw new TimeoutException(
                $"Локальный прокси не ответил на порту {port}."
            );
        }

        private async Task WaitForTcpPortReadyAsync(
            int port,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(7);

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!coreManager.IsRunning)
                {
                    throw new InvalidOperationException(
                        $"{coreManager.DisplayName} завершился до " +
                        "готовности системного прокси."
                    );
                }

                try
                {
                    using var client = new TcpClient();
                    using var attempt =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken
                        );

                    attempt.CancelAfter(TimeSpan.FromMilliseconds(700));
                    await client.ConnectAsync(
                        IPAddress.Loopback,
                        port,
                        attempt.Token
                    );
                    return;
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (SocketException)
                {
                    // Служебный HTTP inbound ещё не готов.
                }

                await Task.Delay(120, cancellationToken);
            }

            throw new TimeoutException(
                $"Системный HTTP-прокси не ответил на порту {port}."
            );
        }

        private static bool IsPortAvailable(int port)
        {
            TcpListener? listener = null;

            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Server.ExclusiveAddressUse = true;
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

        private async Task RestoreTunNetworkAsync(
            CancellationToken cancellationToken)
        {
            if (!tunNetworkService.HasPendingBackup)
            {
                xrayTunPreparation = null;
                return;
            }

            if (!WindowsElevationService.IsAdministrator)
            {
                xrayTunPreparation = null;
                Log(
                    "Ожидается elevated-восстановление предыдущей TUN-сессии; " +
                    "обычный USER-процесс не изменяет системные DNS/routes."
                );
                return;
            }

            TunRestoreOutcome outcome = await tunNetworkService
                .RestoreAsync(cancellationToken);

            xrayTunPreparation = null;

            if (outcome == TunRestoreOutcome.Restored)
            {
                Log("Прежние TUN routes/DNS восстановлены.");
            }
            else if (outcome == TunRestoreOutcome.DnsPreserved)
            {
                Log(
                    "TUN routes восстановлены; DNS был изменён другой программой и сохранён."
                );
            }
        }

        private void RestoreSystemProxy()
        {
            if (!systemProxyService.HasPendingBackup &&
                !systemProxyService.IsEnabledByApplication)
            {
                return;
            }

            ProxyRestoreOutcome outcome = systemProxyService.Restore();

            if (outcome == ProxyRestoreOutcome.Restored)
            {
                Log("Прежние системные настройки прокси восстановлены.");
            }
            else if (outcome == ProxyRestoreOutcome.CurrentSettingsPreserved)
            {
                Log("Системный прокси изменён другой программой; текущие настройки сохранены.");
            }
        }

        private void CleanupStaleRuntimeFiles()
        {
            DeleteRuntimeConfig();

            try
            {
                if (!Directory.Exists(RuntimeDirectory))
                {
                    return;
                }

                foreach (string path in Directory.GetFiles(
                             RuntimeDirectory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(path);

                    if ((name.StartsWith("runtime-", StringComparison.OrdinalIgnoreCase) &&
                         name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ||
                        (name.StartsWith(".runtime-", StringComparison.OrdinalIgnoreCase) &&
                         name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
                    {
                        TryDelete(path);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Не удалось очистить временные файлы: " + ex.Message);
            }
        }

        private void DeleteRuntimeConfig()
        {
            string? path = Interlocked.Exchange(
                ref runtimeConfigPath,
                null
            );

            if (!string.IsNullOrWhiteSpace(path))
            {
                TryDelete(path);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Повторная очистка будет выполнена при следующем запуске.
            }
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
                operationCancellation = null;
            }
        }

        private void SetState(ConnectionSessionState state)
        {
            if (disposed)
            {
                return;
            }

            State = state;
            StateChanged?.Invoke(state);
        }

        private void ResetSession()
        {
            ActiveProfile = null;
            ActivePort = 0;
            ActiveCore = null;
            ActiveMode = null;
            StartedAt = null;
            xrayTunPreparation = null;
        }

        private void AppendCommandOutput(
            string output,
            bool force = false)
        {
            foreach (string line in output.Split(
                         OutputLineSeparators,
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                ForwardCoreOutput(line, force);
            }
        }

        private void ForwardCoreOutput(string line)
        {
            ForwardCoreOutput(line, force: false);
        }

        private void ForwardCoreOutput(
            string line,
            bool force)
        {
            if (!force &&
                !DetailedCoreLoggingEnabled &&
                !IsImportantCoreOutput(line))
            {
                return;
            }

            Log($"[{coreManager.DisplayName}] " + line);
        }

        private static bool IsImportantCoreOutput(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string value = line.Trim();

            // Xray SOCKS can emit this when a local client opens a connection
            // and closes it before sending a request. Keep it available in the
            // detailed core log, but do not surface it as an important error.
            if (value.Contains(
                    "proxy/socks: failed to read request > EOF",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Xray/Wintun writes this while probing/creating a new adapter.
            // It is benign when startup continues with "Creating adapter".
            if (value.Contains(
                    "Failed to find matching adapter name",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return value.Contains("[Error]", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains(" level=error", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains(" FATAL", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("FATAL", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("panic", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("unable to", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatConnectionMode(ConnectionMode mode)
        {
            return mode switch
            {
                ConnectionMode.SystemProxy => "Системный прокси",
                ConnectionMode.Tun => "TUN",
                _ => "Локальный прокси"
            };
        }

        private static string FormatTunStack(
            TunStackPreference preference)
        {
            return preference switch
            {
                TunStackPreference.System => "system",
                TunStackPreference.GVisor => "gVisor",
                _ => "mixed"
            };
        }

        private void Log(string message)
        {
            LogReceived?.Invoke(message);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CancelCurrentOperation();

            coreManager.OutputReceived -= ForwardCoreOutput;
            coreManager.ProcessExited -= HandleUnexpectedExit;

            try
            {
                if (tunNetworkService.HasPendingBackup &&
                    WindowsElevationService.IsAdministrator)
                {
                    tunNetworkService
                        .RestoreAsync(CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
            }
            catch
            {
                // Backup остаётся на диске для восстановления при старте.
            }

            try
            {
                RestoreSystemProxy();
            }
            catch
            {
                // Закрытие не должно оставлять процесс приложения зависшим.
            }

            coreJob?.Dispose();
            coreJob = null;
            coreManager.Dispose();
            operationGate.Dispose();
            operationCancellation?.Dispose();
            DeleteRuntimeConfig();
        }
    }
}
