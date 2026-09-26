using GeniaProxy.ControlPlane;
using GeniaProxy.Models;
using GeniaProxy.Services;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfClipboard = System.Windows.Clipboard;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace GeniaProxy
{
    public sealed partial class MainWindow : Window, IDisposable
    {
        private const double CompactWindowHeight = 530;
        private const double DefaultExpandedWindowHeight = 720;

        private static readonly WpfBrush AccentBrush =
            new WpfSolidColorBrush(WpfColor.FromRgb(40, 217, 197));

        private static readonly WpfBrush MutedBrush =
            new WpfSolidColorBrush(WpfColor.FromRgb(156, 173, 184));

        private static readonly WpfBrush DangerBrush =
            new WpfSolidColorBrush(WpfColor.FromRgb(255, 90, 98));

        private readonly ConnectionSession connectionSession = new();
        private readonly CoreMaintenanceService coreMaintenanceService = new();
        private readonly CoreMaintenanceService xrayMaintenanceService =
            new(ProxyCoreKind.Xray);
        private readonly SettingsService settingsService = new();
        private readonly BrowserIntegrationService browserIntegrationService = new();
        private readonly BrowserDirectBridgeService browserDirectBridgeService;
        private readonly ControlPlaneCoordinator? controlPlaneCoordinator;
        private readonly DispatcherTimer sessionTimer = new()
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        private readonly Queue<DateTimeOffset> recentCoreFailures = new();

        private System.Windows.Forms.NotifyIcon? trayIcon;
        private System.Windows.Forms.ContextMenuStrip? trayMenu;
        private System.Windows.Forms.ToolStripMenuItem? trayStatusItem;
        private System.Windows.Forms.ToolStripMenuItem? trayToggleItem;
        private System.Drawing.Icon? trayImage;

        private AppSettings settings = new();
        private bool closeInProgress;
        private bool closeAllowed;
        private bool resourcesDisposed;
        private bool isLogVisible;
        private double expandedWindowHeight = DefaultExpandedWindowHeight;
        private int reconnectGeneration;
        private int endpointResolutionGeneration;
        private CancellationTokenSource? controlPlaneVerificationCancellation;
        private bool isOperationBusy;
        private bool isApplyingProfileSettings;
        private string? activeSettingsProfile;
        private readonly bool connectAfterElevation;

        public MainWindow(bool connectAfterElevation = false)
        {
            this.connectAfterElevation = connectAfterElevation;
            browserDirectBridgeService = new BrowserDirectBridgeService(
                () => Dispatcher.Invoke(CreateBrowserDirectBridgeStatus)
            );

            InitializeComponent();
            InitializeTray();

            try
            {
                controlPlaneCoordinator = new ControlPlaneCoordinator(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "data",
                        "diagnostics",
                        "session-journal.jsonl"
                    )
                );
            }
            catch
            {
                // Alpha 1 observability must never prevent the frozen
                // 4.4.0 networking baseline from starting.
                controlPlaneCoordinator = null;
            }

            connectionSession.LogReceived +=
                ConnectionSession_LogReceived;

            connectionSession.StateChanged +=
                ConnectionSession_StateChanged;

            connectionSession.UnexpectedExit +=
                ConnectionSession_UnexpectedExit;

            ProtocolLabSelectionAudit.SelectionLogged +=
                ProtocolLabSelectionAudit_SelectionLogged;

            sessionTimer.Tick += (_, _) =>
                UpdateSessionTime();
        }

        private async void Window_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                await connectionSession.InitializeAsync();

                if (connectionSession.HasPendingTunRecovery &&
                    !WindowsElevationService.IsAdministrator)
                {
                    await OfferPendingTunRecoveryAsync();
                }

                settings = settingsService.Load();

                try
                {
                    settings.StartWithWindows = StartupService.IsEnabled;
                }
                catch (Exception ex)
                {
                    AddLog(
                        "Не удалось прочитать состояние автозапуска: " +
                        ex.Message
                    );
                }

                expandedWindowHeight = Math.Max(
                    settings.ExpandedWindowHeight,
                    DefaultExpandedWindowHeight
                );

                PortTextBox.Text = settings.LocalPort.ToString(
                    CultureInfo.InvariantCulture
                );

                if (settings.UseSystemProxy &&
                    settings.ConnectionMode == ConnectionMode.LocalProxy)
                {
                    settings.ConnectionMode = ConnectionMode.SystemProxy;
                }

                SelectConnectionMode(settings.ConnectionMode);

                LoadProfiles(settings.LastProfile);
                UpdateState(ConnectionSessionState.Stopped);
                UpdateElevationBadge();

                AddLog("GeniaProxy 4.5.0 Alpha 3 Protocol Lab UI (Xray 26.3.27 / sing-box 1.14.1) запущен.");
                AddLog(
                    $"DNS readiness baseline: mode {TimingAbExperiment.Mode}; " +
                    $"barrier before direct UDP DNS probe = {TimingAbExperiment.BarrierMilliseconds} ms."
                );
                AddLog(
                    "Режим прав: " +
                    (WindowsElevationService.IsAdministrator
                        ? "Администратор."
                        : "Обычный пользователь.")
                );
                AddLog(
                    "Папка профилей: " +
                    connectionSession.ProfilesDirectory
                );

                AddLog(
                    "Protocol Lab Alpha 3: отдельный UI boundary; AnyTLS/TUIC/Snell v6 runtime-verified; Whitelist/Xray experimental design-only; Lab выключен по умолчанию."
                );

                try
                {
                    browserDirectBridgeService.Start();
                    AddLog(
                        "Browser Direct Bridge: встроенный loopback API запущен на " +
                        browserDirectBridgeService.Endpoint + "."
                    );
                }
                catch (Exception ex)
                {
                    AddLog(
                        "Browser Direct Bridge: не удалось запустить loopback API: " +
                        ex.Message
                    );
                }

                if (!settings.LogCollapsed)
                {
                    SetLogVisibility(isVisible: true, resizeWindow: true);
                }

                bool shouldConnect =
                    connectAfterElevation || settings.AutoConnect;

                if (shouldConnect &&
                    ProfileComboBox.SelectedItem is string)
                {
                    await Task.Yield();
                    AddLog(
                        connectAfterElevation
                            ? "Продолжаем TUN после перезапуска с правами администратора..."
                            : "Автоматическое подключение..."
                    );
                    await StartConnectionAsync();
                }
            }
            catch (Exception ex)
            {
                AddLog("Ошибка инициализации: " + ex.Message);

                System.Windows.MessageBox.Show(
                    this,
                    "Не удалось инициализировать GeniaProxy.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        public void ActivateFromSecondInstance()
        {
            RestoreFromTray();
        }

        private void InitializeTray()
        {
            string? executablePath = Environment.ProcessPath;

            if (!string.IsNullOrWhiteSpace(executablePath) &&
                File.Exists(executablePath))
            {
                trayImage = System.Drawing.Icon.ExtractAssociatedIcon(
                    executablePath
                );
            }

            trayImage ??= (System.Drawing.Icon)
                System.Drawing.SystemIcons.Shield.Clone();

            trayStatusItem = new System.Windows.Forms.ToolStripMenuItem(
                "Остановлено"
            )
            {
                Enabled = false
            };

            var openItem = new System.Windows.Forms.ToolStripMenuItem(
                "Открыть GeniaProxy"
            );
            openItem.Click += (_, _) =>
                Dispatcher.BeginInvoke(new Action(RestoreFromTray));

            trayToggleItem =
                new System.Windows.Forms.ToolStripMenuItem("Подключить");
            trayToggleItem.Click += (_, _) =>
                Dispatcher.BeginInvoke(
                    new Action(() =>
                        _ = ToggleConnectionFromTrayAsync())
                );

            var exitItem = new System.Windows.Forms.ToolStripMenuItem(
                "Выход"
            );
            exitItem.Click += (_, _) =>
                Dispatcher.BeginInvoke(new Action(ExitFromTray));

            trayMenu = new System.Windows.Forms.ContextMenuStrip();
            trayMenu.Items.Add(trayStatusItem);
            trayMenu.Items.Add(
                new System.Windows.Forms.ToolStripSeparator()
            );
            trayMenu.Items.Add(openItem);
            trayMenu.Items.Add(trayToggleItem);
            trayMenu.Items.Add(
                new System.Windows.Forms.ToolStripSeparator()
            );
            trayMenu.Items.Add(exitItem);

            trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = trayImage,
                Text = "GeniaProxy — остановлено",
                ContextMenuStrip = trayMenu,
                Visible = true
            };

            trayIcon.DoubleClick += (_, _) =>
                Dispatcher.BeginInvoke(new Action(RestoreFromTray));

            UpdateTrayState();
        }

        private void Window_StateChanged(
            object? sender,
            EventArgs e)
        {
            if (WindowState != WindowState.Minimized ||
                closeInProgress)
            {
                return;
            }

            ShowInTaskbar = false;
            Hide();
        }

        private void RestoreFromTray()
        {
            if (resourcesDisposed)
            {
                return;
            }

            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private async Task ToggleConnectionFromTrayAsync()
        {
            if (isOperationBusy || resourcesDisposed)
            {
                return;
            }

            if (connectionSession.IsRunning)
            {
                Interlocked.Increment(ref reconnectGeneration);
                await StopConnectionAsync();
            }
            else
            {
                RestoreFromTray();
                await StartConnectionAsync();
            }
        }

        private void ExitFromTray()
        {
            RestoreFromTray();
            Close();
        }

        private void UpdateTrayState()
        {
            if (trayIcon is null ||
                trayStatusItem is null ||
                trayToggleItem is null)
            {
                return;
            }

            bool running = connectionSession.IsRunning;

            trayStatusItem.Text = isOperationBusy
                ? "Выполняется операция..."
                : running
                    ? $"Подключено: {connectionSession.ActiveCoreName}"
                    : "Остановлено";

            trayToggleItem.Text = running
                ? "Остановить"
                : "Подключить";
            trayToggleItem.Enabled = !isOperationBusy;

            string tooltip = running
                ? $"GeniaProxy — {connectionSession.ActiveCoreName}"
                : "GeniaProxy — остановлено";

            trayIcon.Text = tooltip.Length <= 63
                ? tooltip
                : tooltip[..63];
        }

        private async void ToggleConnectionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (connectionSession.IsRunning)
            {
                Interlocked.Increment(ref reconnectGeneration);
                await StopConnectionAsync();
            }
            else
            {
                await StartConnectionAsync();
            }
        }

        private async Task StartConnectionAsync()
        {
            if (ProfileComboBox.SelectedItem is not string profileName)
            {
                ShowWarning("Сначала выберите или импортируйте профиль.");
                return;
            }

            if (!int.TryParse(
                    PortTextBox.Text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int localPort) ||
                localPort is < 1024 or > 65535)
            {
                ShowWarning("Укажите локальный порт от 1024 до 65535.");
                PortTextBox.Focus();
                return;
            }

            ConnectionMode connectionMode =
                GetSelectedConnectionMode();

            if (connectionSession.HasPendingTunRecovery &&
                !WindowsElevationService.IsAdministrator)
            {
                bool recovered = await OfferPendingTunRecoveryAsync();

                if (!recovered)
                {
                    return;
                }
            }

            if (connectionMode == ConnectionMode.Tun &&
                !WindowsElevationService.IsAdministrator)
            {
                SaveSettings(profileName, localPort);
                OfferTunElevationRestart();
                return;
            }

            SetOperationUi(isBusy: true);

            try
            {
                CancelControlPlaneVerification();
                SaveSettings(profileName, localPort);

                await TryControlPlaneAsync(
                    () => controlPlaneCoordinator!.BeginSessionAsync(
                        profileName,
                        FormatControlPlaneMode(connectionMode)
                    ),
                    "begin-session"
                );

                await connectionSession.StartAsync(
                    profileName,
                    localPort,
                    connectionMode,
                    settings.CorePreference,
                    settings.TunStackPreference
                );

                await TryControlPlaneAsync(
                    () => controlPlaneCoordinator!.MarkNetworkReadyAsync(
                        FormatControlPlaneCore(connectionSession.ActiveCore),
                        FormatControlPlaneMode(connectionMode),
                        connectionMode == ConnectionMode.Tun
                    ),
                    "network-ready"
                );

                if (connectionMode == ConnectionMode.Tun)
                {
                    StartAutomaticTunControlPlaneVerification();
                }

                sessionTimer.Start();
                UpdateSessionTime();
            }
            catch (OperationCanceledException)
            {
                CancelControlPlaneVerification();
                await TryControlPlaneAsync(
                    () => RecoverAndCompleteControlPlaneAsync(
                        "start-cancelled"
                    ),
                    "start-cancelled"
                );
                AddLog("Запуск отменён.");
            }
            catch (Exception ex)
            {
                CancelControlPlaneVerification();
                await TryControlPlaneAsync(
                    () => RecoverAndCompleteControlPlaneAsync(
                        "start-failed"
                    ),
                    "start-failed"
                );
                AddLog("Ошибка запуска: " + ex.Message);

                string target = connectionMode switch
                {
                    ConnectionMode.Tun => "TUN-подключение",
                    ConnectionMode.SystemProxy => "системный прокси",
                    _ => "локальный прокси"
                };

                System.Windows.MessageBox.Show(
                    this,
                    $"Не удалось запустить {target}.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                SetOperationUi(isBusy: false);
                UpdateState(connectionSession.State);
            }
        }

        private async Task<bool> OfferPendingTunRecoveryAsync()
        {
            if (!connectionSession.HasPendingTunRecovery)
            {
                return true;
            }

            if (WindowsElevationService.IsAdministrator)
            {
                return !connectionSession.HasPendingTunRecovery;
            }

            AddLog(
                "Найдена незавершённая TUN-сессия. Требуется одноразовое " +
                "elevated-восстановление DNS/routes."
            );

            MessageBoxResult choice =
                System.Windows.MessageBox.Show(
                    this,
                    "После аварийного завершения осталась TUN recovery-сессия.\n\n" +
                    "GeniaProxy может безопасно восстановить прежние DNS/routes " +
                    "через одноразовый UAC helper, не перезапуская основное окно " +
                    "с правами администратора.\n\nВосстановить сеть сейчас?",
                    "GeniaProxy — восстановление TUN",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Yes
                );

            if (choice != MessageBoxResult.Yes)
            {
                AddLog(
                    "TUN recovery отложен пользователем; новые подключения " +
                    "заблокированы до восстановления системной сети."
                );
                return false;
            }

            ConnectionStatusText.Text = "Восстановление";
            ConnectionStatusText.Foreground = AccentBrush;
            ConnectionDetailText.Text = "возврат DNS/routes через UAC";
            SetOperationUi(isBusy: true);

            try
            {
                (TunRecoveryElevationResult result, string? errorMessage) =
                    await WindowsElevationService
                        .RecoverTunNetworkAsAdministratorAsync();

                switch (result)
                {
                    case TunRecoveryElevationResult.Recovered:
                        if (connectionSession.HasPendingTunRecovery)
                        {
                            AddLog(
                                "Elevated recovery завершился, но recovery snapshot " +
                                "остался на диске."
                            );
                            ShowWarning(
                                "Сеть не была полностью восстановлена. Новые " +
                                "подключения остаются заблокированными."
                            );
                            return false;
                        }

                        AddLog(
                            "Аварийная TUN-сессия восстановлена: прежние DNS/routes возвращены."
                        );
                        return true;

                    case TunRecoveryElevationResult.Cancelled:
                        AddLog(
                            "Запрос UAC для TUN recovery отменён пользователем."
                        );
                        return false;

                    default:
                        AddLog(
                            "Не удалось выполнить elevated TUN recovery: " +
                            (errorMessage ?? "неизвестная ошибка")
                        );
                        ShowWarning(
                            "Не удалось восстановить системные DNS/routes.\n\n" +
                            (errorMessage ?? "Неизвестная ошибка.")
                        );
                        return false;
                }
            }
            finally
            {
                SetOperationUi(isBusy: false);
                UpdateState(connectionSession.State);
            }
        }

        private void OfferTunElevationRestart()
        {
            ConnectionStatusText.Text = "TUN недоступен";
            ConnectionStatusText.Foreground = DangerBrush;
            ConnectionDetailText.Text =
                "требуются права администратора";

            AddLog(
                "TUN не запущен: требуются права администратора."
            );

            MessageBoxResult choice =
                System.Windows.MessageBox.Show(
                    this,
                    "Для режима TUN требуются права администратора.\n\n" +
                    "Перезапустить GeniaProxy с правами администратора " +
                    "и продолжить подключение?",
                    "GeniaProxy — TUN",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes
                );

            if (choice != MessageBoxResult.Yes)
            {
                AddLog(
                    "Перезапуск с правами администратора отменён пользователем."
                );
                return;
            }

            ElevationRestartResult result =
                WindowsElevationService.RestartAsAdministrator(
                    connectAfterElevation: true,
                    out string? errorMessage
                );

            switch (result)
            {
                case ElevationRestartResult.Started:
                    AddLog(
                        "Запущен перезапуск GeniaProxy с правами администратора."
                    );
                    CloseForElevationHandoff();
                    break;

                case ElevationRestartResult.Cancelled:
                    ConnectionDetailText.Text =
                        "запрос UAC отменён";
                    AddLog(
                        "Запрос UAC отменён; GeniaProxy продолжает работу без TUN."
                    );
                    break;

                default:
                    ConnectionDetailText.Text =
                        "не удалось запросить права администратора";
                    AddLog(
                        "Не удалось перезапустить GeniaProxy от администратора: " +
                        (errorMessage ?? "неизвестная ошибка")
                    );
                    ShowWarning(
                        "Не удалось перезапустить GeniaProxy с правами " +
                        "администратора.\n\n" +
                        (errorMessage ?? "Неизвестная ошибка.")
                    );
                    break;
            }
        }

        private void CloseForElevationHandoff()
        {
            // Этот путь вызывается до старта TUN: сеть ещё не изменена.
            // Если параллельно всё же идёт операция/ядро, оставляем обычный
            // graceful shutdown. В штатном USER -> ADMIN handoff окно
            // скрывается сразу и не ждёт общего shutdown-path.
            if (connectionSession.IsRunning ||
                connectionSession.State is
                    ConnectionSessionState.Starting or
                    ConnectionSessionState.Stopping)
            {
                Close();
                return;
            }

            try
            {
                SaveCurrentSettings();
            }
            catch (Exception ex)
            {
                AddLog(
                    "Не удалось повторно сохранить настройки перед UAC handoff: " +
                    ex.Message
                );
            }

            Interlocked.Increment(ref reconnectGeneration);
            closeAllowed = true;

            // Пользователь не должен видеть старое USER-окно, пока Windows
            // освобождает single-instance mutex для elevated-копии.
            Hide();
            Dispose();
            Close();
        }

        private void UpdateElevationBadge()
        {
            bool administrator =
                WindowsElevationService.IsAdministrator;

            ElevationBadgeText.Text = administrator
                ? "ADMIN"
                : "USER";
            ElevationBadgeText.Foreground = administrator
                ? AccentBrush
                : MutedBrush;
            ElevationBadge.BorderBrush = administrator
                ? AccentBrush
                : MutedBrush;
            ElevationBadge.ToolTip = administrator
                ? "GeniaProxy запущен с правами администратора. TUN доступен."
                : "Обычный режим. При запуске TUN GeniaProxy предложит перезапуск через UAC.";
        }

        private async Task StopConnectionAsync()
        {
            SetOperationUi(isBusy: true);

            try
            {
                CancelControlPlaneVerification();

                await TryControlPlaneAsync(
                    () => controlPlaneCoordinator!.BeginDisconnectAsync(
                        "user-disconnect"
                    ),
                    "disconnect-start"
                );

                await connectionSession.StopAsync();

                await TryControlPlaneAsync(
                    () => controlPlaneCoordinator!.CompleteDisconnectAsync(
                        "network-cleanup-complete"
                    ),
                    "disconnect-complete"
                );
            }
            catch (Exception ex)
            {
                await TryControlPlaneAsync(
                    () => controlPlaneCoordinator!.BeginRecoveryAsync(
                        "disconnect-failed"
                    ),
                    "disconnect-recovery"
                );
                AddLog("Ошибка остановки: " + ex.Message);

                System.Windows.MessageBox.Show(
                    this,
                    "Не удалось полностью остановить прокси.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                sessionTimer.Stop();
                UpdateSessionTime();
                SetOperationUi(isBusy: false);
                UpdateState(connectionSession.State);
            }
        }

        private void ConnectionSession_LogReceived(string line)
        {
            Dispatcher.BeginInvoke(
                new Action(() => AddLog(line))
            );
        }

        private void ConnectionSession_StateChanged(
            ConnectionSessionState state)
        {
            Dispatcher.BeginInvoke(
                new Action(() => UpdateState(state))
            );
        }

        private void ConnectionSession_UnexpectedExit(int exitCode)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                    _ = HandleUnexpectedExitAsync(exitCode))
            );
        }

        private async Task HandleUnexpectedExitAsync(int exitCode)
        {
            CancelControlPlaneVerification();
            sessionTimer.Stop();
            UpdateSessionTime();
            SetOperationUi(isBusy: false);

            await TryControlPlaneAsync(
                () => controlPlaneCoordinator!.BeginRecoveryAsync(
                    $"core-exit-{exitCode}"
                ),
                "unexpected-core-exit"
            );

            if (closeInProgress || resourcesDisposed)
            {
                return;
            }

            if (!settings.ReconnectOnFailure)
            {
                await TryControlPlaneAsync(
                    () => controlPlaneCoordinator!.CompleteRecoveryAsync(
                        "reconnect-disabled"
                    ),
                    "recovery-complete"
                );

                System.Windows.MessageBox.Show(
                    this,
                    "Процесс ядра неожиданно завершился " +
                    $"с кодом {exitCode}.",
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset cutoff = now.AddMinutes(-2);

            while (recentCoreFailures.Count > 0 &&
                   recentCoreFailures.Peek() < cutoff)
            {
                recentCoreFailures.Dequeue();
            }

            recentCoreFailures.Enqueue(now);

            if (recentCoreFailures.Count > 3)
            {
                await TryControlPlaneAsync(
                    () => controlPlaneCoordinator!.CompleteRecoveryAsync(
                        "reconnect-circuit-breaker"
                    ),
                    "recovery-circuit-breaker"
                );

                AddLog(
                    "Автопереподключение остановлено: " +
                    "более трёх сбоев за две минуты."
                );
                ShowWarning(
                    "Автопереподключение остановлено после нескольких " +
                    "сбоев ядра. Проверьте профиль и диагностику."
                );
                return;
            }

            int generation = Interlocked.Increment(
                ref reconnectGeneration
            );
            int delaySeconds = Math.Clamp(
                settings.ReconnectDelaySeconds,
                1,
                60
            );

            AddLog(
                $"Сбой ядра (код {exitCode}). " +
                $"Повтор через {delaySeconds} сек."
            );

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

            if (closeInProgress ||
                resourcesDisposed ||
                !settings.ReconnectOnFailure ||
                generation != Volatile.Read(ref reconnectGeneration) ||
                connectionSession.IsRunning ||
                ProfileComboBox.SelectedItem is not string)
            {
                return;
            }

            AddLog("Автоматическое переподключение...");
            await StartConnectionAsync();
        }

        private void UpdateState(ConnectionSessionState state)
        {
            switch (state)
            {
                case ConnectionSessionState.Starting:
                    ConnectionStatusText.Text = "Запуск";
                    ConnectionStatusText.Foreground = AccentBrush;
                    ConnectionDetailText.Text =
                        "проверка профиля и локального порта";
                    RouteSummaryText.Text = "Подготовка защищённого маршрута...";
                    ConnectionIndicator.Background = AccentBrush;
                    ConnectionIndicator.BorderBrush = AccentBrush;
                    ToggleConnectionButton.Content = "ПОДОЖДИТЕ";
                    break;

                case ConnectionSessionState.Running:
                    ConnectionStatusText.Text = "Подключение запущено";
                    ConnectionStatusText.Foreground = AccentBrush;
                    ConnectionDetailText.Text =
                        connectionSession.ActiveMode == ConnectionMode.Tun
                            ? $"{connectionSession.ActiveCoreName} · TUN"
                            : $"{connectionSession.ActiveCoreName} слушает " +
                              $"127.0.0.1:{connectionSession.ActivePort}";
                    ToggleConnectionButton.Content = "ОСТАНОВИТЬ";
                    ToggleConnectionButton.BorderBrush = DangerBrush;
                    ToggleConnectionButton.Foreground = DangerBrush;
                    CoreValueText.Text = connectionSession.ActiveCoreName;
                    ProtocolValueText.Text = SelectedProtocolText.Text;
                    ModeValueText.Text = connectionSession.ActiveMode switch
                    {
                        ConnectionMode.Tun => "TUN",
                        ConnectionMode.SystemProxy => "SYSTEM PROXY",
                        _ => "LOCAL PROXY"
                    };
                    LocalProxyValueText.Text =
                        connectionSession.ActiveMode == ConnectionMode.Tun
                            ? "TUN · весь трафик"
                            : $"SOCKS5 · 127.0.0.1:{connectionSession.ActivePort}";
                    RouteSummaryText.Text =
                        connectionSession.ActiveMode == ConnectionMode.Tun
                            ? $"ПК → TUN → {connectionSession.ActiveCoreName} → сервер → Интернет"
                            : $"ПК → SOCKS5 → {connectionSession.ActiveCoreName} → сервер → Интернет";
                    ConnectionIndicator.Background = AccentBrush;
                    ConnectionIndicator.BorderBrush = AccentBrush;
                    SystemProxyValueText.Text =
                        connectionSession.UsesSystemProxy
                            ? "Включён GeniaProxy"
                            : "Не изменяется";
                    ExitIpValueText.Text = "НЕ ПРОВЕРЕН";
                    break;

                case ConnectionSessionState.Stopping:
                    ConnectionStatusText.Text = "Остановка";
                    ConnectionStatusText.Foreground = MutedBrush;
                    ConnectionDetailText.Text =
                        "восстановление системных настроек";
                    RouteSummaryText.Text = "Откат маршрутов и DNS...";
                    ConnectionIndicator.Background = MutedBrush;
                    ConnectionIndicator.BorderBrush = MutedBrush;
                    ToggleConnectionButton.Content = "ПОДОЖДИТЕ";
                    break;

                case ConnectionSessionState.Failed:
                    ConnectionStatusText.Text = "Ошибка";
                    ConnectionStatusText.Foreground = DangerBrush;
                    ConnectionDetailText.Text =
                        GetSelectedConnectionMode() == ConnectionMode.Tun
                            ? "TUN не запущен"
                            : "ядро не запущено";
                    RouteSummaryText.Text = "Маршрут не активен · проверьте журнал";
                    ConnectionIndicator.Background = DangerBrush;
                    ConnectionIndicator.BorderBrush = DangerBrush;
                    ResetStoppedValues(preserveRouteSummary: true);
                    break;

                default:
                    ConnectionStatusText.Text = "Остановлено";
                    ConnectionStatusText.Foreground = MutedBrush;
                    ConnectionDetailText.Text = "ядро остановлено";
                    ConnectionIndicator.Background = MutedBrush;
                    ConnectionIndicator.BorderBrush = MutedBrush;
                    ResetStoppedValues();
                    break;
            }

            UpdateModeHint();
            UpdateGuardBadge();
            UpdateTrayState();
        }

        private void ResetStoppedValues(bool preserveRouteSummary = false)
        {
            ToggleConnectionButton.Content = "ПОДКЛЮЧИТЬ";
            ToggleConnectionButton.BorderBrush = AccentBrush;
            ToggleConnectionButton.Foreground = AccentBrush;
            CoreValueText.Text = "—";
            ProtocolValueText.Text = "—";
            ModeValueText.Text = "—";
            SystemProxyValueText.Text = "Не изменяется";
            SessionValueText.Text = "00:00:00";
            ExitIpValueText.Text = "—";

            if (!preserveRouteSummary)
            {
                RouteSummaryText.Text = "Маршрут не активен";
            }

            if (TryGetPort(out int port))
            {
                LocalProxyValueText.Text =
                    $"SOCKS5 · 127.0.0.1:{port}";
            }
        }

        private void UpdateModeHint()
        {
            if (ModeHintText is null)
            {
                return;
            }

            ConnectionMode mode = connectionSession.IsRunning &&
                connectionSession.ActiveMode is ConnectionMode activeMode
                    ? activeMode
                    : GetSelectedConnectionMode();

            if (mode == ConnectionMode.Tun)
            {
                ModeHintText.Text = connectionSession.IsRunning
                    ? "TUN активен · полный маршрут"
                    : "TUN выбран · полный маршрут";
                ModeHintText.Foreground = AccentBrush;
                return;
            }

            ModeHintText.Text = "TUN доступен · полный маршрут";
            ModeHintText.Foreground = MutedBrush;
        }

        private void UpdateGuardBadge()
        {
            if (GuardBadge is null || GuardBadgeText is null)
            {
                return;
            }

            bool tunGuardActive = connectionSession.IsRunning &&
                connectionSession.ActiveMode == ConnectionMode.Tun;

            GuardBadgeText.Text = tunGuardActive
                ? "GUARD TUN"
                : "GUARD READY";
            GuardBadgeText.Foreground = tunGuardActive
                ? AccentBrush
                : MutedBrush;
            GuardBadge.BorderBrush = tunGuardActive
                ? AccentBrush
                : MutedBrush;
            GuardBadge.ToolTip = tunGuardActive
                ? "HF4 TUN guard активен: IPv6 fail-closed и hardened recovery snapshot. Это не WFP Kill Switch."
                : "HF4 security baseline готов. WFP Strict Kill Switch будет отдельным этапом 4.3.";
        }

        private void SetOperationUi(bool isBusy)
        {
            isOperationBusy = isBusy;
            OperationProgress.Visibility = isBusy
                ? Visibility.Visible
                : Visibility.Collapsed;

            ToggleConnectionButton.IsEnabled = !isBusy;
            ProfileComboBox.IsEnabled = !isBusy &&
                !connectionSession.IsRunning;
            ImportProfileButton.IsEnabled = !isBusy &&
                !connectionSession.IsRunning;
            ServiceButton.IsEnabled = !isBusy;
            PortTextBox.IsEnabled = !isBusy &&
                !connectionSession.IsRunning &&
                GetSelectedConnectionMode() != ConnectionMode.Tun;
            ConnectionModeComboBox.IsEnabled = !isBusy &&
                !connectionSession.IsRunning;
            UpdateTrayState();
        }

        private void LoadProfiles(string? preferredProfile = null)
        {
            string? current = preferredProfile ??
                ProfileComboBox.SelectedItem as string;

            Directory.CreateDirectory(
                connectionSession.ProfilesDirectory
            );

            string[] profiles = Directory.GetFiles(
                    connectionSession.ProfilesDirectory,
                    "*.json",
                    SearchOption.TopDirectoryOnly
                )
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToArray();

            ProfileComboBox.ItemsSource = profiles;

            if (profiles.Length == 0)
            {
                SelectedProtocolText.Text = "Не определён";
                RemoteServerValueText.Text = "—";
                RemoteServerValueText.ToolTip = null;
                ExitIpValueText.Text = "—";
                ToggleConnectionButton.IsEnabled = false;
                return;
            }

            string selected = profiles.FirstOrDefault(name =>
                string.Equals(
                    name,
                    current,
                    StringComparison.OrdinalIgnoreCase
                )) ?? profiles[0];

            ProfileComboBox.SelectedItem = selected;
            ToggleConnectionButton.IsEnabled = true;
        }

        private void ProfileComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (activeSettingsProfile is string previousProfile &&
                !string.Equals(
                    previousProfile,
                    ProfileComboBox.SelectedItem as string,
                    StringComparison.OrdinalIgnoreCase))
            {
                SaveProfileRuntimeSettings(previousProfile);
            }

            activeSettingsProfile =
                ProfileComboBox.SelectedItem as string;

            if (activeSettingsProfile is string profileName)
            {
                ApplyProfileRuntimeSettings(profileName);
            }

            UpdateSelectedProtocol();
        }

        private void UpdateSelectedProtocol()
        {
            if (ProfileComboBox.SelectedItem is not string profileName)
            {
                Interlocked.Increment(ref endpointResolutionGeneration);
                SelectedProtocolText.Text = "Не определён";
                RemoteServerValueText.Text = "—";
                RemoteServerValueText.ToolTip = null;
                ExitIpValueText.Text = "—";
                return;
            }

            try
            {
                string path = Path.Combine(
                    connectionSession.ProfilesDirectory,
                    profileName + ".json"
                );

                string source = File.ReadAllText(path, Encoding.UTF8);
                ProfileInspection inspection =
                    ProfileInspectionService.Inspect(source);

                SelectedProtocolText.Text =
                    FormatProfileSummary(inspection);

                BeginRemoteServerResolution(profileName, source);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref endpointResolutionGeneration);
                SelectedProtocolText.Text = "Ошибка профиля";
                RemoteServerValueText.Text = "—";
                RemoteServerValueText.ToolTip = null;
                ExitIpValueText.Text = "—";
                AddLog("Не удалось определить протокол: " + ex.Message);
            }
        }

        private void BeginRemoteServerResolution(
            string profileName,
            string source)
        {
            int generation = Interlocked.Increment(
                ref endpointResolutionGeneration
            );

            ExitIpValueText.Text = "—";

            ProfileEndpoint? endpoint;

            try
            {
                endpoint = ProfileEndpointService.Inspect(source);
            }
            catch
            {
                endpoint = null;
            }

            if (endpoint is null)
            {
                RemoteServerValueText.Text = "—";
                RemoteServerValueText.ToolTip = null;
                return;
            }

            string endpointLabel = endpoint.Port is int port
                ? $"{endpoint.Host}:{port}"
                : endpoint.Host;

            RemoteServerValueText.Text = endpoint.Host;
            RemoteServerValueText.ToolTip = endpointLabel;

            _ = ResolveRemoteServerAsync(
                profileName,
                endpoint,
                endpointLabel,
                generation
            );
        }

        private async Task ResolveRemoteServerAsync(
            string profileName,
            ProfileEndpoint endpoint,
            string endpointLabel,
            int generation)
        {
            try
            {
                using var cancellation =
                    new CancellationTokenSource(TimeSpan.FromSeconds(3));

                ResolvedProfileEndpoint? resolved =
                    await ProfileEndpointService.ResolveAsync(
                        endpoint,
                        cancellation.Token
                    );

                if (generation !=
                        Volatile.Read(ref endpointResolutionGeneration) ||
                    !string.Equals(
                        ProfileComboBox.SelectedItem as string,
                        profileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (resolved is not null)
                {
                    RemoteServerValueText.Text =
                        resolved.Address.ToString();
                    RemoteServerValueText.ToolTip = endpointLabel;
                }
            }
            catch (OperationCanceledException)
            {
                // DNS resolve нужен только для UI и не влияет на подключение.
            }
            catch (Exception)
            {
                // При ошибке DNS оставляем hostname из профиля.
            }
        }

        private static string FormatProtocolName(string protocol)
        {
            return protocol.ToLowerInvariant() switch
            {
                "hysteria2" => "Hysteria2",
                "wireguard" => "WireGuard",
                "shadowsocks" => "Shadowsocks",
                "trojan" => "Trojan",
                "vmess" => "VMess",
                "vless" => "VLESS",
                _ => protocol
            };
        }

        private static string FormatProfileProtocols(
            IEnumerable<string> protocols)
        {
            string[] values = protocols
                .Where(protocol => protocol.ToLowerInvariant() is not
                    ("direct" or "block" or "dns" or
                     "selector" or "urltest" or
                     "freedom" or "blackhole"))
                .Select(FormatProtocolName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return values.Length == 0
                ? "Служебный профиль"
                : string.Join(", ", values);
        }

        private static string FormatProfileSummary(
            ProfileInspection inspection)
        {
            var parts = new List<string>();

            string protocols = FormatProfileProtocols(
                inspection.Protocols
            );

            if (!protocols.Equals(
                    "Служебный профиль",
                    StringComparison.Ordinal))
            {
                parts.Add(protocols);
            }

            bool usesXhttp = inspection.Transports.Any(value =>
                value.Equals("xhttp", StringComparison.OrdinalIgnoreCase)
            );

            parts.AddRange(inspection.Transports.Select(value =>
                value.Equals("xhttp", StringComparison.OrdinalIgnoreCase)
                    ? "XHTTP"
                    : value.ToUpperInvariant()));

            parts.AddRange(inspection.Security.Select(value =>
                value.ToUpperInvariant()));

            // Every Xray XHTTP profile is normalized by GeniaProxy with
            // XHTTP extra.xmux defaults before runtime. Surface that useful
            // transport fact instead of internal freedom/blackhole outbounds.
            if (inspection.Core == ProxyCoreKind.Xray && usesXhttp)
            {
                parts.Add("XMUX");
            }

            if (parts.Count == 0)
            {
                parts.Add(ProfileFormatService.FormatCore(inspection.Core));
            }

            return string.Join(
                " · ",
                parts.Distinct(StringComparer.OrdinalIgnoreCase)
            );
        }

        private void ImportProfileButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new ImportProfileWindow
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ImportProfile(
                        dialog.ProfileName,
                        dialog.SourceText
                    );
                }
                catch (Exception ex)
                {
                    AddLog("Ошибка импорта: " + ex.Message);
                    ShowWarning(
                        "Не удалось импортировать профиль.\n\n" +
                        ex.Message
                    );
                }
            }
        }

        private void ImportProfile(
            string profileName,
            string source)
        {
            string normalizedName =
                ProfileNameValidator.Normalize(profileName);

            string? validationError = ProfileNameValidator
                .GetValidationError(normalizedName);

            if (validationError is not null)
            {
                throw new InvalidDataException(validationError);
            }

            int localPort = TryGetPort(out int selectedPort)
                ? selectedPort
                : 2080;

            string trimmedSource = source.Trim();
            bool isJson = trimmedSource.StartsWith('{');
            bool isVless = trimmedSource.StartsWith(
                "vless://",
                StringComparison.OrdinalIgnoreCase
            );

            ProfileDescriptor? jsonDescriptor = isJson
                ? ProfileFormatService.Inspect(source)
                : null;

            bool insecure = isJson
                ? jsonDescriptor!.Core == ProxyCoreKind.Xray
                    ? XrayProfileImportService.ContainsInsecureTls(source)
                    : JsonProfileImportService.ContainsInsecureTls(source)
                : isVless
                    ? XrayProfileImportService.RequestsInsecureTls(source)
                    : Hysteria2ImportService.RequestsInsecureTls(source);

            if (insecure)
            {
                MessageBoxResult choice =
                    System.Windows.MessageBox.Show(
                        this,
                        "Профиль отключает проверку TLS-сертификата " +
                        "(insecure=true). Это снижает защиту от " +
                        "подмены сервера.\n\nПродолжить импорт?",
                        "GeniaProxy",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No
                    );

                if (choice != MessageBoxResult.Yes)
                {
                    AddLog("Импорт небезопасного профиля отменён.");
                    return;
                }
            }

            string json = isJson
                ? jsonDescriptor!.Core == ProxyCoreKind.Xray
                    ? XrayProfileImportService.NormalizeConfig(
                        source,
                        localPort,
                        ConnectionMode.LocalProxy
                    )
                    : JsonProfileImportService.NormalizeConfig(
                        source,
                        localPort
                    )
                : isVless
                    ? XrayProfileImportService.CreateVlessXhttpConfig(
                        source,
                        localPort
                    )
                    : Hysteria2ImportService.CreateSingBoxConfig(
                        source,
                        localPort
                    );

            string destination = Path.Combine(
                connectionSession.ProfilesDirectory,
                normalizedName + ".json"
            );

            if (File.Exists(destination))
            {
                MessageBoxResult overwrite =
                    System.Windows.MessageBox.Show(
                        this,
                        $"Профиль «{normalizedName}» уже существует. " +
                        "Заменить его?",
                        "GeniaProxy",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.No
                    );

                if (overwrite != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            AtomicFileWriter.WriteAllText(
                destination,
                json,
                new UTF8Encoding(false)
            );

            LoadProfiles(normalizedName);
            SaveCurrentSettings();
            AddLog("Импортирован профиль: " + normalizedName);
        }

        private void RefreshProfilesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;
            LoadProfiles();
            AddLog("Список профилей обновлён.");
        }

        private void ProtocolLabButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var window = new ProtocolLabWindow(
                () => connectionSession.IsRunning
            )
            {
                Owner = this
            };

            window.ShowDialog();
        }

        private void ServiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            bool profileChangesAllowed =
                !connectionSession.IsRunning;

            ImportProfileMenuItem.IsEnabled =
                profileChangesAllowed;
            RefreshProfilesMenuItem.IsEnabled =
                profileChangesAllowed;
            RestoreProfilesMenuItem.IsEnabled =
                profileChangesAllowed;
            bool profileSelected =
                ProfileComboBox.SelectedItem is string;
            ProfileInformationMenuItem.IsEnabled = profileSelected;
            ShareProfileMenuItem.IsEnabled = profileSelected;
            RenameProfileMenuItem.IsEnabled =
                profileChangesAllowed && profileSelected;
            DeleteProfileMenuItem.IsEnabled =
                profileChangesAllowed && profileSelected;
            ConnectionTestMenuItem.IsEnabled =
                connectionSession.IsRunning && !isOperationBusy;

            ServicePopup.IsOpen = !ServicePopup.IsOpen;
        }

        private void ImportProfileMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;
            ImportProfileButton_Click(sender, e);
        }

        private void ProfileInformationMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;
            if (ProfileComboBox.SelectedItem is not string profileName)
            {
                ShowWarning("Сначала выберите или импортируйте профиль.");
                return;
            }

            try
            {
                string profilePath = Path.Combine(
                    connectionSession.ProfilesDirectory,
                    profileName + ".json"
                );

                var file = new FileInfo(profilePath);

                if (!file.Exists)
                {
                    throw new FileNotFoundException(
                        "Файл выбранного профиля не найден.",
                        profilePath
                    );
                }

                if (file.Length >
                    JsonProfileImportService.MaxProfileBytes)
                {
                    throw new InvalidDataException(
                        "Профиль превышает допустимый размер 1 МБ."
                    );
                }

                string source = File.ReadAllText(
                    profilePath,
                    Encoding.UTF8
                );

                ProfileInspection inspection =
                    ProfileInspectionService.Inspect(source);

                string report = ProfileInspectionService.FormatSummary(
                    profileName,
                    inspection
                );

                using var dialog = new DiagnosticsForm(
                    report,
                    "Сведения о профиле GeniaProxy"
                );

                dialog.ShowDialog();
                AddLog("Просмотрены сведения о профиле: " + profileName);
            }
            catch (Exception ex)
            {
                AddLog("Ошибка чтения профиля: " + ex.Message);
                ShowWarning(
                    "Не удалось прочитать выбранный профиль.\n\n" +
                    ex.Message
                );
            }
        }

        private void ShareProfileMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;

            if (ProfileComboBox.SelectedItem is not string profileName)
            {
                ShowWarning("Сначала выберите профиль.");
                return;
            }

            MessageBoxResult confirmation =
                System.Windows.MessageBox.Show(
                    this,
                    "QR-код будет содержать компактную мобильную ссылку, " +
                    "включая адрес сервера и секрет подключения.\n\n" +
                    "Показывайте его только доверенному получателю. " +
                    "Продолжить?",
                    "GeniaProxy",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No
                );

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                string path = Path.Combine(
                    connectionSession.ProfilesDirectory,
                    profileName + ".json"
                );

                var file = new FileInfo(path);

                if (!file.Exists)
                {
                    throw new FileNotFoundException(
                        "Файл выбранного профиля не найден.",
                        path
                    );
                }

                if (file.Length >
                    JsonProfileImportService.MaxProfileBytes)
                {
                    throw new InvalidDataException(
                        "Профиль превышает допустимый размер 1 МБ."
                    );
                }

                string source = File.ReadAllText(path, Encoding.UTF8);
                ProfileQrData qrData = ProfileQrService.Create(
                    profileName,
                    source
                );

                var dialog = new ShareProfileWindow(qrData)
                {
                    Owner = this
                };

                dialog.ShowDialog();
                AddLog(
                    "Мобильный QR профиля сформирован локально: " +
                    profileName
                );
            }
            catch (Exception ex)
            {
                AddLog("Ошибка формирования QR: " + ex.Message);
                ShowWarning(
                    "Не удалось сформировать QR-код профиля.\n\n" +
                    ex.Message
                );
            }
        }

        private void RenameProfileMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;
            if (connectionSession.IsRunning)
            {
                ShowWarning("Сначала остановите прокси.");
                return;
            }

            if (ProfileComboBox.SelectedItem is not string currentName)
            {
                ShowWarning("Сначала выберите профиль.");
                return;
            }

            var dialog = new ProfileNameWindow(currentName)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var service = new ProfileFileService(
                    connectionSession.ProfilesDirectory
                );

                service.Rename(currentName, dialog.ProfileName);
                SaveProfileRuntimeSettings(currentName);

                if (settings.ProfileSettings.Remove(
                        currentName,
                        out ProfileRuntimeSettings? profileSettings))
                {
                    settings.ProfileSettings[dialog.ProfileName] =
                        profileSettings;
                }

                activeSettingsProfile = null;
                LoadProfiles(dialog.ProfileName);
                SaveCurrentSettings();
                AddLog(
                    $"Профиль переименован: {currentName} → " +
                    dialog.ProfileName
                );
            }
            catch (Exception ex)
            {
                AddLog("Ошибка переименования профиля: " + ex.Message);
                ShowWarning(
                    "Не удалось переименовать профиль.\n\n" +
                    ex.Message
                );
            }
        }

        private void DeleteProfileMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;
            if (connectionSession.IsRunning)
            {
                ShowWarning("Сначала остановите прокси.");
                return;
            }

            if (ProfileComboBox.SelectedItem is not string profileName)
            {
                ShowWarning("Сначала выберите профиль.");
                return;
            }

            MessageBoxResult confirmation =
                System.Windows.MessageBox.Show(
                    this,
                    $"Удалить профиль «{profileName}»?\n\n" +
                    "Это действие нельзя отменить. Создайте резервную " +
                    "копию, если профиль может понадобиться.",
                    "GeniaProxy",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No
                );

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var service = new ProfileFileService(
                    connectionSession.ProfilesDirectory
                );

                service.Delete(profileName);
                settings.LastProfile = string.Empty;
                settings.ProfileSettings.Remove(profileName);
                activeSettingsProfile = null;
                LoadProfiles();
                SaveCurrentSettings();
                AddLog("Профиль удалён: " + profileName);
            }
            catch (Exception ex)
            {
                AddLog("Ошибка удаления профиля: " + ex.Message);
                ShowWarning(
                    "Не удалось удалить профиль.\n\n" +
                    ex.Message
                );
            }
        }

        private async void ExportProfilesMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;
            using var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Title = "Резервная копия GeniaProxy",
                Filter = "Резервная копия (*.zip)|*.zip",
                DefaultExt = "zip",
                AddExtension = true,
                FileName = "GeniaProxy-backup-" +
                    DateTime.Now.ToString(
                        "yyyy-MM-dd_HH-mm-ss",
                        CultureInfo.InvariantCulture
                    ) + ".zip"
            };

            if (dialog.ShowDialog() !=
                System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            SetOperationUi(isBusy: true);

            try
            {
                var service = new ProfileBackupService(
                    connectionSession.ProfilesDirectory,
                    settingsService.SettingsPath
                );

                BackupExportResult result = await Task.Run(
                    () => service.Export(dialog.FileName)
                );

                AddLog("Резервная копия создана: " + dialog.FileName);

                System.Windows.MessageBox.Show(
                    this,
                    $"Сохранено профилей: {result.ProfilesExported}.",
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                AddLog("Ошибка резервного копирования: " + ex.Message);
                ShowWarning(
                    "Не удалось создать резервную копию.\n\n" +
                    ex.Message
                );
            }
            finally
            {
                SetOperationUi(isBusy: false);
            }
        }

        private async void RestoreProfilesMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;
            if (connectionSession.IsRunning)
            {
                ShowWarning("Сначала остановите прокси.");
                return;
            }

            using var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Восстановление GeniaProxy",
                Filter = "Резервная копия (*.zip)|*.zip",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() !=
                System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            MessageBoxResult choice = System.Windows.MessageBox.Show(
                this,
                "Выберите режим восстановления.\n\n" +
                "Да — заменить текущие профили.\n" +
                "Нет — добавить только отсутствующие.\n" +
                "Отмена — ничего не менять.",
                "GeniaProxy",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel
            );

            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            BackupRestoreMode mode = choice == MessageBoxResult.Yes
                ? BackupRestoreMode.Replace
                : BackupRestoreMode.Merge;

            SetOperationUi(isBusy: true);

            try
            {
                var service = new ProfileBackupService(
                    connectionSession.ProfilesDirectory,
                    settingsService.SettingsPath
                );

                BackupRestoreResult result = await Task.Run(
                    () => service.Restore(dialog.FileName, mode)
                );

                settings = settingsService.Load();
                PortTextBox.Text = settings.LocalPort.ToString(
                    CultureInfo.InvariantCulture
                );
                SelectConnectionMode(settings.ConnectionMode);
                LoadProfiles(settings.LastProfile);

                if (result.SettingsRestored)
                {
                    StartupService.SetEnabled(settings.StartWithWindows);
                }

                AddLog(
                    $"Восстановлено профилей: {result.ProfilesRestored}; " +
                    $"пропущено: {result.ProfilesSkipped}."
                );

                System.Windows.MessageBox.Show(
                    this,
                    $"Восстановлено профилей: {result.ProfilesRestored}.\n" +
                    $"Пропущено: {result.ProfilesSkipped}.",
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                AddLog("Ошибка восстановления: " + ex.Message);
                ShowWarning(
                    "Не удалось восстановить резервную копию.\n\n" +
                    ex.Message
                );
            }
            finally
            {
                SetOperationUi(isBusy: false);
            }
        }

        private void CoreManagementMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;
            using var dialog = new CoreManagementForm(
                coreMaintenanceService,
                () => connectionSession.IsRunning
            );

            dialog.ShowDialog();
        }

        private void XrayManagementMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;
            using var dialog = new CoreManagementForm(
                xrayMaintenanceService,
                () => connectionSession.IsRunning
            );

            dialog.ShowDialog();
        }

        private async void DiagnosticsMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;
            SetOperationUi(isBusy: true);

            string diagnostics;

            try
            {
                CoreInformation core = await coreMaintenanceService
                    .GetInformationAsync();
                CoreInformation xray = await xrayMaintenanceService
                    .GetInformationAsync();

                string applicationVersion =
                    typeof(MainWindow).Assembly
                        .GetCustomAttributes(
                            typeof(System.Reflection
                                .AssemblyInformationalVersionAttribute),
                            inherit: false
                        )
                        .OfType<System.Reflection
                            .AssemblyInformationalVersionAttribute>()
                        .FirstOrDefault()
                        ?.InformationalVersion ?? "не определена";

                ControlPlaneSnapshot controlPlane =
                    controlPlaneCoordinator?.Snapshot ??
                    ControlPlaneSnapshot.Empty;

                diagnostics = string.Join(
                    Environment.NewLine,
                    "GeniaProxy — диагностика",
                    "========================",
                    $"Версия приложения: {applicationVersion}",
                    $".NET: {RuntimeInformation.FrameworkDescription}",
                    $"Windows: {RuntimeInformation.OSDescription}",
                    $"Архитектура: {RuntimeInformation.ProcessArchitecture}",
                    string.Empty,
                    $"Профиль: {ProfileComboBox.SelectedItem ?? "—"}",
                    $"Локальный адрес: {LocalProxyValueText.Text}",
                    $"Активное ядро: {connectionSession.ActiveCoreName}",
                    $"Ядро запущено: " +
                        (connectionSession.IsRunning ? "да" : "нет"),
                    $"Системный прокси GeniaProxy: " +
                        (connectionSession.UsesSystemProxy
                            ? "включён"
                            : "выключен"),
                    string.Empty,
                    "Control Plane Alpha 1",
                    $"State: {controlPlane.State}",
                    $"Session ID: " +
                        (controlPlane.SessionId == Guid.Empty
                            ? "—"
                            : controlPlane.SessionId.ToString("D")),
                    $"State since UTC: " +
                        (controlPlane.StateSinceUtc == DateTimeOffset.MinValue
                            ? "—"
                            : controlPlane.StateSinceUtc.ToString("O", CultureInfo.InvariantCulture)),
                    $"Profile: {controlPlane.ProfileName ?? "—"}",
                    $"Core: {controlPlane.Core ?? "—"}",
                    $"Mode: {controlPlane.Mode ?? "—"}",
                    $"Verified exit: {controlPlane.VerifiedExit ?? "—"}",
                    $"Verified at UTC: " +
                        (controlPlane.VerifiedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "—"),
                    $"Verification succeeded: " +
                        (controlPlane.VerificationSucceeded?.ToString() ?? "—"),
                    $"Verification fresh: {controlPlane.VerificationFresh}",
                    $"Verification source: {controlPlane.VerificationSource ?? "—"}",
                    $"Last reason: {controlPlane.LastReason ?? "—"}",
                    string.Empty,
                    $"sing-box: {core.Version}",
                    $"sing-box SHA-256: {core.Sha256}",
                    $"sing-box контрольная сумма: " +
                        (core.HashMatches ? "да" : "нет"),
                    $"sing-box резервная копия: " +
                        (core.BackupAvailable ? "есть" : "нет"),
                    string.Empty,
                    $"Xray: {xray.Version}",
                    $"Xray SHA-256: {xray.Sha256}",
                    $"Xray контрольная сумма: " +
                        (xray.HashMatches ? "да" : "нет"),
                    $"Xray резервная копия: " +
                        (xray.BackupAvailable ? "есть" : "нет"),
                    string.Empty,
                    $"Папка программы: {AppContext.BaseDirectory}",
                    $"Настройки: {settingsService.SettingsPath}",
                    $"Профили: {connectionSession.ProfilesDirectory}"
                );
            }
            catch (Exception ex)
            {
                diagnostics =
                    "Не удалось собрать диагностику." +
                    Environment.NewLine + ex;
            }
            finally
            {
                SetOperationUi(isBusy: false);
            }

            using var dialog = new DiagnosticsForm(diagnostics);
            dialog.ShowDialog();
        }

        private async void ConnectionTestMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;

            if (!connectionSession.IsRunning ||
                connectionSession.ActiveMode is not ConnectionMode mode ||
                connectionSession.ActiveProfile is not string profileName)
            {
                ShowWarning("Сначала подключите профиль.");
                return;
            }

            SetOperationUi(isBusy: true);

            Guid verificationSessionId =
                controlPlaneCoordinator?.Snapshot.SessionId ?? Guid.Empty;

            await TryControlPlaneAsync(
                () => controlPlaneCoordinator!.MarkVerificationStartedAsync(
                    verificationSessionId,
                    "manager-channel-test"
                ),
                "verification-start"
            );

            try
            {
                using var cancellation = new CancellationTokenSource(
                    TimeSpan.FromSeconds(45)
                );
                ConnectionTestResult result =
                    await ConnectionTestService.RunAsync(
                        connectionSession.ActivePort,
                        mode,
                        20,
                        cancellation.Token
                    );

                ProfileRuntimeSettings profileSettings =
                    GetOrCreateProfileRuntimeSettings(profileName);
                profileSettings.LastTestUtc = DateTimeOffset.UtcNow;
                profileSettings.LastTestSucceeded = result.IsSuccess;
                profileSettings.LastTestAverageMilliseconds =
                    result.AverageParallelMilliseconds;
                profileSettings.LastExitIp = result.ExitIp;
                SaveCurrentSettings();

                if (result.IsSuccess &&
                    !string.IsNullOrWhiteSpace(result.ExitIp))
                {
                    await TryControlPlaneAsync(
                        () => controlPlaneCoordinator!.MarkVerifiedAsync(
                            verificationSessionId,
                            result.ExitIp,
                            expectedExit: null,
                            source: "manager-channel-test"
                        ),
                        "verification-success"
                    );
                }
                else
                {
                    await TryControlPlaneAsync(
                        () => controlPlaneCoordinator!.MarkVerificationFailedAsync(
                            verificationSessionId,
                            "channel-test-not-successful",
                            "manager-channel-test"
                        ),
                        "verification-failed"
                    );
                }

                ExitIpValueText.Text = string.IsNullOrWhiteSpace(result.ExitIp)
                    ? "—"
                    : result.ExitIp;

                string privacyStatus = result.Ipv6Privacy.LeakDetected
                    ? "IPv6 LEAK"
                    : mode == ConnectionMode.Tun
                        ? "IPv6 leak не обнаружен"
                        : "для полной приватности рекомендуется TUN";

                AddLog(
                    $"Проверка канала: " +
                    $"{result.SuccessfulParallelRequests}/20, " +
                    $"{result.AverageParallelMilliseconds:F0} мс, " +
                    $"IP {result.ExitIp}, {privacyStatus}."
                );

                using var dialog = new DiagnosticsForm(
                    result.FormatReport(
                        profileName,
                        mode,
                        connectionSession.ActivePort
                    ),
                    "Проверка канала GeniaProxy"
                );
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                await TryControlPlaneAsync(
                    () => controlPlaneCoordinator!.MarkVerificationFailedAsync(
                        verificationSessionId,
                        "channel-test-exception",
                        "manager-channel-test"
                    ),
                    "verification-exception"
                );
                AddLog("Ошибка проверки канала: " + ex.Message);
                ShowWarning(
                    "Не удалось завершить проверку канала.\n\n" +
                    ex.Message
                );
            }
            finally
            {
                SetOperationUi(isBusy: false);
            }
        }

        private BrowserDirectBridgeStatus CreateBrowserDirectBridgeStatus()
        {
            string profile = connectionSession.ActiveProfile
                ?? settings.LastProfile
                ?? string.Empty;

            string engine = connectionSession.ActiveCore switch
            {
                ProxyCoreKind.SingBox => "sing-box",
                ProxyCoreKind.Xray => "xray",
                _ => string.Empty
            };

            string mode = connectionSession.ActiveMode switch
            {
                ConnectionMode.Tun => "tun",
                ConnectionMode.SystemProxy => "system",
                ConnectionMode.LocalProxy => "local",
                _ => string.Empty
            };

            bool connected =
                connectionSession.State == ConnectionSessionState.Running &&
                connectionSession.IsRunning;

            BrowserDirectLocalProxy? localProxy = null;
            if (connected && mode is "local" or "system" &&
                connectionSession.ActivePort is >= 1 and <= 65535)
            {
                localProxy = new BrowserDirectLocalProxy(
                    "socks5",
                    "127.0.0.1",
                    connectionSession.ActivePort
                );
            }

            BrowserDirectEndpoint? endpoint = null;
            if (!string.IsNullOrWhiteSpace(profile))
            {
                try
                {
                    string profilePath = Path.Combine(
                        connectionSession.ProfilesDirectory,
                        profile + ".json"
                    );
                    if (File.Exists(profilePath))
                    {
                        string source = File.ReadAllText(profilePath);
                        ProfileEndpoint? inspected =
                            ProfileEndpointService.Inspect(source);
                        if (inspected is not null)
                        {
                            endpoint = new BrowserDirectEndpoint(
                                inspected.Host,
                                inspected.Port
                            );
                        }
                    }
                }
                catch
                {
                    // Endpoint is diagnostic metadata only. A malformed or
                    // concurrently replaced profile must not break the bridge.
                }
            }

            string? expectedExitIp = null;
            string? verifiedExitIp = null;
            string? verifiedExitAt = null;
            bool? verifiedExitSucceeded = null;

            ControlPlaneSnapshot controlPlane =
                controlPlaneCoordinator?.Snapshot ??
                ControlPlaneSnapshot.Empty;

            bool controlPlaneMatchesActiveSession =
                connected &&
                controlPlane.SessionId != Guid.Empty &&
                string.Equals(
                    controlPlane.ProfileName,
                    profile,
                    StringComparison.Ordinal
                ) &&
                string.Equals(
                    controlPlane.Mode,
                    mode,
                    StringComparison.Ordinal
                );

            if (controlPlaneMatchesActiveSession)
            {
                expectedExitIp = string.IsNullOrWhiteSpace(
                    controlPlane.ExpectedExit
                )
                    ? null
                    : controlPlane.ExpectedExit.Trim();

                verifiedExitSucceeded =
                    controlPlane.VerificationSucceeded;

                if (controlPlane.VerificationSucceeded == true &&
                    !string.IsNullOrWhiteSpace(controlPlane.VerifiedExit) &&
                    controlPlane.VerifiedAtUtc is DateTimeOffset verifiedAt)
                {
                    verifiedExitIp = controlPlane.VerifiedExit.Trim();
                    verifiedExitAt = verifiedAt
                        .ToUniversalTime()
                        .ToString("O", CultureInfo.InvariantCulture);
                }
            }

            long uptimeSeconds = connectionSession.StartedAt is DateTimeOffset startedAt &&
                connected
                ? Math.Max(0L, (long)(DateTimeOffset.UtcNow - startedAt).TotalSeconds)
                : 0L;

            string coherence = connected && !string.IsNullOrWhiteSpace(engine)
                ? "match"
                : "unknown";

            return new BrowserDirectBridgeStatus(
                Type: "status",
                Protocol: BrowserDirectBridgeService.ProtocolVersion,
                Connected: connected,
                Version: "4.5.0",
                BridgeVersion: BrowserDirectBridgeService.BridgeVersion,
                Engine: engine,
                ExpectedEngine: engine,
                ObservedEngine: engine,
                CoreCoherence: coherence,
                Profile: profile,
                Mode: mode,
                LocalProxy: localProxy,
                Endpoint: endpoint,
                ExpectedExitIp: expectedExitIp,
                VerifiedExitIp: verifiedExitIp,
                VerifiedExitAt: verifiedExitAt,
                VerifiedExitSucceeded: verifiedExitSucceeded,
                UptimeSeconds: uptimeSeconds
            );
        }

        private void BrowserIntegrationMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;

            var dialog = new BrowserIntegrationWindow(
                browserIntegrationService
            )
            {
                Owner = this
            };

            dialog.ShowDialog();
        }

        private void SettingsMenuItem_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServicePopup.IsOpen = false;
            ProfileRuntimeSettings? profileSettings =
                ProfileComboBox.SelectedItem is string profileName
                    ? GetOrCreateProfileRuntimeSettings(profileName)
                    : null;

            var dialog = new SettingsWindow(settings, profileSettings)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                StartupService.SetEnabled(dialog.StartWithWindows);

                settings.AutoConnect = dialog.AutoConnect;
                settings.StartWithWindows = dialog.StartWithWindows;
                settings.ReconnectOnFailure =
                    dialog.ReconnectOnFailure;
                settings.ReconnectDelaySeconds =
                    dialog.ReconnectDelaySeconds;
                settings.CorePreference = dialog.CorePreference;
                settings.TunStackPreference = dialog.TunStackPreference;

                if (profileSettings is not null)
                {
                    profileSettings.CorePreference = dialog.CorePreference;
                    profileSettings.TunStackPreference =
                        dialog.TunStackPreference;
                }

                if (!settings.ReconnectOnFailure)
                {
                    Interlocked.Increment(ref reconnectGeneration);
                }

                SetLogVisibility(
                    isVisible: !dialog.CollapseLog,
                    resizeWindow: true
                );
                SaveCurrentSettings();
                AddLog("Настройки программы сохранены.");
            }
            catch (Exception ex)
            {
                AddLog("Ошибка сохранения настроек: " + ex.Message);
                ShowWarning(
                    "Не удалось применить настройки.\n\n" +
                    ex.Message
                );
            }
        }

        private void ToggleLogButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetLogVisibility(
                isVisible: !isLogVisible,
                resizeWindow: true
            );
            SaveCurrentSettings();
        }

        private void SetLogVisibility(
            bool isVisible,
            bool resizeWindow)
        {
            if (!isVisible)
            {
                if (WindowState == WindowState.Normal)
                {
                    expandedWindowHeight = Math.Max(
                        ActualHeight,
                        DefaultExpandedWindowHeight
                    );
                }

                isLogVisible = false;
                LogCard.Visibility = Visibility.Collapsed;
                LogRow.Height = new GridLength(0);
                ToggleLogButton.Content = "Показать журнал";

                if (resizeWindow && WindowState == WindowState.Normal)
                {
                    Height = CompactWindowHeight;
                }

                return;
            }

            isLogVisible = true;
            LogCard.Visibility = Visibility.Visible;
            LogRow.Height = new GridLength(1, GridUnitType.Star);
            ToggleLogButton.Content = "Скрыть журнал";

            if (resizeWindow && WindowState == WindowState.Normal)
            {
                Height = expandedWindowHeight;
            }

            LogTextBox.ScrollToEnd();
        }

        private void UpdateSessionTime()
        {
            if (connectionSession.StartedAt is not DateTimeOffset startedAt ||
                !connectionSession.IsRunning)
            {
                SessionValueText.Text = "00:00:00";
                return;
            }

            TimeSpan elapsed = DateTimeOffset.Now - startedAt;

            SessionValueText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}",
                (int)elapsed.TotalHours,
                elapsed.Minutes,
                elapsed.Seconds
            );
        }

        private void AddLog(string message)
        {
            string line =
                $"[{DateTime.Now:HH:mm:ss}] {message}";

            if (LogTextBox.Text.Length > 120_000)
            {
                LogTextBox.Text = LogTextBox.Text[^80_000..];
            }

            LogTextBox.AppendText(
                (LogTextBox.Text.Length == 0
                    ? string.Empty
                    : Environment.NewLine) +
                line
            );

            LogTextBox.ScrollToEnd();
        }

        private void LogContextMenu_Opened(
            object sender,
            RoutedEventArgs e)
        {
            bool hasLog = !string.IsNullOrEmpty(LogTextBox.Text);
            CopyAllLogButton.IsEnabled = hasLog;
            ClearLogButton.IsEnabled = hasLog;
            UpdateDetailedCoreLogMenuHeader();
        }

        private void DetailedCoreLogButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            connectionSession.DetailedCoreLoggingEnabled =
                !connectionSession.DetailedCoreLoggingEnabled;

            UpdateDetailedCoreLogMenuHeader();

            AddLog(connectionSession.DetailedCoreLoggingEnabled
                ? "Подробный журнал ядра включён (новые сообщения)."
                : "Подробный журнал ядра выключен.");
        }

        private void UpdateDetailedCoreLogMenuHeader()
        {
            DetailedCoreLogButton.Header =
                connectionSession.DetailedCoreLoggingEnabled
                    ? "Подробный журнал ядра: включён"
                    : "Подробный журнал ядра: выключен";
        }

        private void CopyAllLogButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(LogTextBox.Text))
            {
                return;
            }

            try
            {
                WpfClipboard.SetText(LogTextBox.Text);
            }
            catch (Exception ex)
            {
                ShowWarning(
                    "Не удалось скопировать журнал.\n\n" + ex.Message
                );
            }
        }

        private void ClearLogButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LogTextBox.Clear();
        }

        private void SaveSettings(
            string profileName,
            int localPort)
        {
            settings.LastProfile = profileName;
            settings.LocalPort = localPort;
            settings.ConnectionMode = GetSelectedConnectionMode();
            settings.UseSystemProxy =
                settings.ConnectionMode == ConnectionMode.SystemProxy;
            SaveProfileRuntimeSettings(profileName);
            settings.LogCollapsed = !isLogVisible;
            settings.ExpandedWindowHeight = (int)Math.Clamp(
                expandedWindowHeight,
                450,
                1600
            );

            settingsService.Save(settings);
        }

        private void SaveCurrentSettings()
        {
            settings.LastProfile =
                ProfileComboBox.SelectedItem as string ?? string.Empty;

            if (TryGetPort(out int port))
            {
                settings.LocalPort = port;
            }

            settings.ConnectionMode = GetSelectedConnectionMode();
            settings.UseSystemProxy =
                settings.ConnectionMode == ConnectionMode.SystemProxy;
            if (settings.LastProfile.Length > 0)
            {
                SaveProfileRuntimeSettings(settings.LastProfile);
            }
            settings.LogCollapsed = !isLogVisible;
            settings.ExpandedWindowHeight = (int)Math.Clamp(
                expandedWindowHeight,
                450,
                1600
            );

            settingsService.Save(settings);
        }

        private bool TryGetPort(out int port)
        {
            return int.TryParse(
                PortTextBox.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out port
            ) && port is >= 1024 and <= 65535;
        }

        private ConnectionMode GetSelectedConnectionMode()
        {
            return ConnectionModeComboBox.SelectedIndex switch
            {
                1 => ConnectionMode.SystemProxy,
                2 => ConnectionMode.Tun,
                _ => ConnectionMode.LocalProxy
            };
        }

        private void SelectConnectionMode(ConnectionMode mode)
        {
            ConnectionModeComboBox.SelectedIndex = mode switch
            {
                ConnectionMode.SystemProxy => 1,
                ConnectionMode.Tun => 2,
                _ => 0
            };
        }

        private void ConnectionModeComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (PortTextBox is null ||
                SystemProxyValueText is null ||
                LocalProxyValueText is null)
            {
                return;
            }

            ConnectionMode mode = GetSelectedConnectionMode();
            PortTextBox.IsEnabled = !isOperationBusy &&
                !connectionSession.IsRunning &&
                mode != ConnectionMode.Tun;

            SystemProxyValueText.Text = mode switch
            {
                ConnectionMode.SystemProxy => "Прокси Windows",
                ConnectionMode.Tun => "Маршрут Windows",
                _ => "Не изменяется"
            };

            LocalProxyValueText.Text = mode == ConnectionMode.Tun
                ? "TUN · весь трафик"
                : TryGetPort(out int port)
                    ? $"SOCKS5 · 127.0.0.1:{port}"
                    : "SOCKS5 · 127.0.0.1:2080";

            UpdateModeHint();
            UpdateGuardBadge();

            if (!connectionSession.IsRunning &&
                mode == ConnectionMode.Tun &&
                !WindowsElevationService.IsAdministrator)
            {
                ConnectionDetailText.Text =
                    "для TUN нужны права администратора";
            }
            else if (!connectionSession.IsRunning &&
                connectionSession.State == ConnectionSessionState.Stopped)
            {
                ConnectionDetailText.Text = "ядро остановлено";
            }

            if (!isApplyingProfileSettings &&
                activeSettingsProfile is string profileName)
            {
                SaveProfileRuntimeSettings(profileName);
            }
        }

        private void PortTextBox_LostFocus(
            object sender,
            RoutedEventArgs e)
        {
            if (!isApplyingProfileSettings &&
                activeSettingsProfile is string profileName)
            {
                SaveProfileRuntimeSettings(profileName);
            }
        }

        private ProfileRuntimeSettings GetOrCreateProfileRuntimeSettings(
            string profileName)
        {
            if (!settings.ProfileSettings.TryGetValue(
                    profileName,
                    out ProfileRuntimeSettings? result))
            {
                result = new ProfileRuntimeSettings
                {
                    LocalPort = settings.LocalPort,
                    ConnectionMode = settings.ConnectionMode,
                    CorePreference = settings.CorePreference,
                    TunStackPreference = settings.TunStackPreference
                };
                settings.ProfileSettings[profileName] = result;
            }

            return result;
        }

        private void ApplyProfileRuntimeSettings(string profileName)
        {
            ProfileRuntimeSettings profileSettings =
                GetOrCreateProfileRuntimeSettings(profileName);

            isApplyingProfileSettings = true;

            try
            {
                PortTextBox.Text = profileSettings.LocalPort.ToString(
                    CultureInfo.InvariantCulture
                );
                SelectConnectionMode(profileSettings.ConnectionMode);
                settings.CorePreference = profileSettings.CorePreference;
                settings.TunStackPreference =
                    profileSettings.TunStackPreference;
            }
            finally
            {
                isApplyingProfileSettings = false;
            }
        }

        private void SaveProfileRuntimeSettings(string profileName)
        {
            ProfileRuntimeSettings profileSettings =
                GetOrCreateProfileRuntimeSettings(profileName);

            if (TryGetPort(out int port))
            {
                profileSettings.LocalPort = port;
            }

            profileSettings.ConnectionMode = GetSelectedConnectionMode();
            profileSettings.CorePreference = settings.CorePreference;
            profileSettings.TunStackPreference =
                settings.TunStackPreference;
        }

        private void ProtocolLabSelectionAudit_SelectionLogged(
            string message)
        {
            if (Dispatcher.CheckAccess())
            {
                AddLog(message);
                return;
            }

            _ = Dispatcher.BeginInvoke(
                new Action(() => AddLog(message))
            );
        }

        private void ShowWarning(string message)
        {
            System.Windows.MessageBox.Show(
                this,
                message,
                "GeniaProxy",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        private async void Window_Closing(
            object? sender,
            CancelEventArgs e)
        {
            if (closeAllowed)
            {
                return;
            }

            e.Cancel = true;

            if (closeInProgress)
            {
                return;
            }

            closeInProgress = true;
            Interlocked.Increment(ref reconnectGeneration);
            CancelControlPlaneVerification();
            SetOperationUi(isBusy: true);
            Title = "GeniaProxy — завершение работы";
            connectionSession.CancelCurrentOperation();

            try
            {
                await TryControlPlaneAsync(
                    () => controlPlaneCoordinator!.BeginDisconnectAsync(
                        "application-close"
                    ),
                    "close-disconnect-start"
                );

                await connectionSession.StopAsync();

                await TryControlPlaneAsync(
                    () => controlPlaneCoordinator!.CompleteDisconnectAsync(
                        "application-close-complete"
                    ),
                    "close-disconnect-complete"
                );

                SaveCurrentSettings();
            }
            catch (Exception ex)
            {
                AddLog("Ошибка безопасного закрытия: " + ex.Message);

                MessageBoxResult choice =
                    System.Windows.MessageBox.Show(
                        this,
                        "Не удалось полностью остановить прокси или " +
                        "восстановить настройки Windows.\n\n" +
                        ex.Message +
                        "\n\nЗакрыть программу принудительно?",
                        "GeniaProxy",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Error,
                        MessageBoxResult.No
                    );

                if (choice != MessageBoxResult.Yes)
                {
                    closeInProgress = false;
                    SetOperationUi(isBusy: false);
                    Title = $"GeniaProxy 4.5.0 Alpha 3 Protocol Lab [{TimingAbExperiment.Mode}]";
                    return;
                }
            }

            // WPF forbids calling Close() from inside the Closing handler,
            // including an async continuation of that handler. The original
            // close request was cancelled above so graceful shutdown could run;
            // schedule the final close on a later Dispatcher turn after this
            // handler has fully returned.
            closeAllowed = true;

            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    Dispose();
                    Close();
                })
            );
        }

        private void StartAutomaticTunControlPlaneVerification()
        {
            if (controlPlaneCoordinator is null ||
                connectionSession.ActiveMode != ConnectionMode.Tun ||
                !connectionSession.IsRunning)
            {
                return;
            }

            CancelControlPlaneVerification();

            Guid sessionId = controlPlaneCoordinator.Snapshot.SessionId;
            if (sessionId == Guid.Empty)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            controlPlaneVerificationCancellation = cancellation;

            _ = RunAutomaticTunControlPlaneVerificationAsync(
                sessionId,
                connectionSession.ActivePort,
                cancellation
            );
        }

        private async Task RunAutomaticTunControlPlaneVerificationAsync(
            Guid sessionId,
            int localPort,
            CancellationTokenSource cancellation)
        {
            const string source = "manager-tun-auto";
            CancellationToken token = cancellation.Token;

            try
            {
                if (controlPlaneCoordinator is null)
                {
                    return;
                }

                bool started = await controlPlaneCoordinator
                    .MarkVerificationStartedAsync(
                        sessionId,
                        source,
                        token
                    );

                if (!started)
                {
                    return;
                }

                ConnectionTestResult result =
                    await ConnectionTestService.RunAsync(
                        localPort,
                        ConnectionMode.Tun,
                        parallelRequests: 1,
                        cancellationToken: token
                    );

                if (result.IsSuccess &&
                    !string.IsNullOrWhiteSpace(result.ExitIp))
                {
                    bool applied = await controlPlaneCoordinator
                        .MarkVerifiedAsync(
                            sessionId,
                            result.ExitIp,
                            expectedExit: null,
                            source: source,
                            cancellationToken: token
                        );

                    if (applied)
                    {
                        ExitIpValueText.Text = result.ExitIp;
                        AddLog(
                            "Control Plane TUN VERIFIED: " +
                            $"exit {result.ExitIp}, " +
                            $"{result.AverageParallelMilliseconds:F0} мс."
                        );
                    }
                }
                else
                {
                    bool applied = await controlPlaneCoordinator
                        .MarkVerificationFailedAsync(
                            sessionId,
                            "tun-auto-verification-not-successful",
                            source,
                            token
                        );

                    if (applied)
                    {
                        AddLog(
                            "Control Plane TUN: автоматическая проверка " +
                            "exit пока не подтверждена."
                        );
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Disconnect/reconnect cancels this session-bound probe.
            }
            catch (ObjectDisposedException)
            {
                // Application shutdown can dispose the coordinator while the
                // diagnostic probe is being cancelled.
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested &&
                    controlPlaneCoordinator is not null)
                {
                    try
                    {
                        bool applied = await controlPlaneCoordinator
                            .MarkVerificationFailedAsync(
                                sessionId,
                                "tun-auto-verification-exception",
                                source
                            );

                        if (applied)
                        {
                            AddLog(
                                "Control Plane TUN verification: " +
                                ex.Message
                            );
                        }
                    }
                    catch
                    {
                        // Observability must not affect the frozen network path.
                    }
                }
            }
            finally
            {
                _ = Interlocked.CompareExchange(
                    ref controlPlaneVerificationCancellation,
                    null,
                    cancellation
                );

                cancellation.Dispose();
            }
        }

        private void CancelControlPlaneVerification()
        {
            CancellationTokenSource? cancellation =
                Interlocked.Exchange(
                    ref controlPlaneVerificationCancellation,
                    null
                );

            if (cancellation is null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task TryControlPlaneAsync(
            Func<Task> operation,
            string operationName)
        {
            if (controlPlaneCoordinator is null)
            {
                return;
            }

            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                AddLog(
                    $"Control Plane {operationName}: {ex.Message}"
                );
            }
        }

        private async Task RecoverAndCompleteControlPlaneAsync(
            string reason)
        {
            if (controlPlaneCoordinator is null)
            {
                return;
            }

            await controlPlaneCoordinator.BeginRecoveryAsync(reason);
            await controlPlaneCoordinator.CompleteRecoveryAsync(reason);
        }

        private static string FormatControlPlaneMode(
            ConnectionMode mode) =>
            mode switch
            {
                ConnectionMode.Tun => "tun",
                ConnectionMode.SystemProxy => "system",
                _ => "local"
            };

        private static string FormatControlPlaneCore(
            ProxyCoreKind? core) =>
            core switch
            {
                ProxyCoreKind.SingBox => "sing-box",
                ProxyCoreKind.Xray => "xray",
                _ => "unknown"
            };

        public void Dispose()
        {
            if (resourcesDisposed)
            {
                return;
            }

            resourcesDisposed = true;
            Interlocked.Increment(ref reconnectGeneration);
            CancelControlPlaneVerification();
            sessionTimer.Stop();

            if (trayIcon is not null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }

            trayMenu?.Dispose();
            trayMenu = null;
            trayImage?.Dispose();
            trayImage = null;
            connectionSession.LogReceived -=
                ConnectionSession_LogReceived;
            connectionSession.StateChanged -=
                ConnectionSession_StateChanged;
            connectionSession.UnexpectedExit -=
                ConnectionSession_UnexpectedExit;
            ProtocolLabSelectionAudit.SelectionLogged -=
                ProtocolLabSelectionAudit_SelectionLogged;
            browserDirectBridgeService.Dispose();
            try
            {
                controlPlaneCoordinator?.Dispose();
            }
            catch
            {
                // Diagnostics shutdown must not interfere with network cleanup.
            }
            connectionSession.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
