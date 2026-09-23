using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GeniaProxy.Services;

namespace GeniaProxy
{
    public partial class Form1
    {
        private readonly CoreMaintenanceService
            version2CoreService = new();

        private readonly Queue<DateTime>
            version2RecentCoreFailures = new();

        private readonly DateTime version2StartedUtc =
            DateTime.UtcNow;

        private ContextMenuStrip? version2ToolsMenu;
        private ToolStripMenuItem? version2TrayProfilesItem;
        private Button? version2ToolsButton;
        private TableLayoutPanel? version2LogHeader;
        private Button? version2LogToggleButton;
        private WindowsJobObject? version2CoreJob;
        private int version2ReconnectGeneration;
        private int version2ExpandedWindowHeight = 460;
        private Size version2ExpandedMinimumSize;
        private bool version2LogCollapsed;
        private bool version2Initialized;

        protected override void OnLoad(EventArgs e)
        {
            InitializeVersion2Interface();
            base.OnLoad(e);
            ApplyVersion2SavedInterfaceState();

            BeginInvoke(
                new Action(async () =>
                    await CompleteVersion2StartupAsync())
            );
        }

        private void InitializeVersion2Interface()
        {
            if (version2Initialized)
            {
                return;
            }

            version2Initialized = true;
            Text = $"GeniaProxy {Application.ProductVersion}";
            importButton.Text = "+ Импорт";

            InitializeVersion2LogPanel();

            version2ToolsButton = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 3, 4, 3),
                Text = "Сервис ▾",
                UseVisualStyleBackColor = true
            };

            statusLayout.Controls.Add(
                version2ToolsButton,
                6,
                0
            );

            InitializeVersion2ToolsMenu();

            version2ToolsButton.Click += (_, _) =>
                version2ToolsMenu?.Show(
                    version2ToolsButton,
                    new Point(0, version2ToolsButton.Height)
                );

            ExtendVersion2TrayMenu();

            coreManager.ProcessExited +=
                Version2CoreManager_ProcessExited;

            startButton.Click += async (_, _) =>
                await AttachVersion2JobAsync();

            stopButton.Click += async (_, _) =>
            {
                Interlocked.Increment(
                    ref version2ReconnectGeneration
                );

                await Task.Delay(150);
                version2CoreJob?.Dispose();
                version2CoreJob = null;
            };

            FormClosed += (_, _) =>
            {
                coreManager.ProcessExited -=
                    Version2CoreManager_ProcessExited;

                version2ToolsMenu?.Dispose();
                version2ToolsMenu = null;

                version2CoreJob?.Dispose();
                version2CoreJob = null;
            };
        }

        private void InitializeVersion2LogPanel()
        {
            if (version2LogHeader is not null)
            {
                return;
            }

            version2ExpandedMinimumSize = MinimumSize;

            version2LogHeader = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            version2LogHeader.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100F)
            );
            version2LogHeader.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 94F)
            );
            version2LogHeader.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100F)
            );

            mainLayout.Controls.Remove(logLabel);

            logLabel.Anchor = AnchorStyles.Left;
            logLabel.Margin = new Padding(3, 0, 3, 0);
            logLabel.Text = "Лог:";

            version2LogToggleButton = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 2, 0, 2),
                Text = "Скрыть ▲",
                UseVisualStyleBackColor = true,
                AccessibleName = "Свернуть или развернуть журнал"
            };

            version2LogToggleButton.Click += (_, _) =>
                SetVersion2LogCollapsed(
                    !version2LogCollapsed,
                    resizeWindow: true,
                    persist: true
                );

            version2LogHeader.Controls.Add(logLabel, 0, 0);
            version2LogHeader.Controls.Add(
                version2LogToggleButton,
                1,
                0
            );

            mainLayout.Controls.Add(
                version2LogHeader,
                0,
                2
            );

            ResizeEnd += (_, _) =>
                CaptureVersion2ExpandedWindowHeight();
        }

        private void ApplyVersion2SavedInterfaceState()
        {
            version2ExpandedWindowHeight = Math.Clamp(
                appSettings.ExpandedWindowHeight,
                380,
                1600
            );

            version2LogCollapsed =
                appSettings.LogCollapsed;

            SetVersion2LogCollapsed(
                appSettings.LogCollapsed,
                resizeWindow: true,
                persist: false
            );
        }

        private void SetVersion2LogCollapsed(
            bool collapsed,
            bool resizeWindow,
            bool persist)
        {
            if (version2LogHeader is null ||
                version2LogToggleButton is null)
            {
                return;
            }

            if (collapsed && !version2LogCollapsed)
            {
                CaptureVersion2ExpandedWindowHeight();
            }

            version2LogCollapsed = collapsed;
            logTextBox.Visible = !collapsed;

            RowStyle logRow = mainLayout.RowStyles[3];
            logRow.SizeType = collapsed
                ? SizeType.Absolute
                : SizeType.Percent;
            logRow.Height = collapsed ? 0F : 100F;

            version2LogToggleButton.Text = collapsed
                ? "Показать ▼"
                : "Скрыть ▲";

            if (collapsed)
            {
                int collapsedHeight =
                    GetVersion2CollapsedWindowHeight();

                MinimumSize = new Size(
                    version2ExpandedMinimumSize.Width,
                    collapsedHeight
                );

                if (resizeWindow &&
                    WindowState == FormWindowState.Normal)
                {
                    Height = collapsedHeight;
                }
            }
            else
            {
                MinimumSize = version2ExpandedMinimumSize;

                if (resizeWindow &&
                    WindowState == FormWindowState.Normal)
                {
                    int screenHeight = Screen
                        .FromControl(this)
                        .WorkingArea.Height;

                    Height = Math.Clamp(
                        version2ExpandedWindowHeight,
                        MinimumSize.Height,
                        Math.Max(
                            MinimumSize.Height,
                            screenHeight
                        )
                    );
                }
            }

            PerformLayout();

            if (!collapsed)
            {
                FlushPendingLog();

                logTextBox.SelectionStart =
                    logTextBox.TextLength;
                logTextBox.SelectionLength = 0;
                logTextBox.ScrollToCaret();
            }

            if (!persist)
            {
                return;
            }

            try
            {
                SaveSettings();
            }
            catch (Exception ex)
            {
                AddLog(
                    "Не удалось сохранить состояние журнала: " +
                    ex.Message
                );
            }
        }

        private int GetVersion2CollapsedWindowHeight()
        {
            float fixedRowsHeight = 0F;

            for (int index = 0; index < 3; index++)
            {
                fixedRowsHeight +=
                    mainLayout.RowStyles[index].Height;
            }

            int nonClientHeight =
                Math.Max(0, Height - ClientSize.Height);

            int requestedHeight = (int)Math.Ceiling(
                fixedRowsHeight +
                mainLayout.Padding.Vertical +
                nonClientHeight +
                8
            );

            return Math.Max(220, requestedHeight);
        }

        private void CaptureVersion2ExpandedWindowHeight()
        {
            if (!version2Initialized ||
                version2LogCollapsed)
            {
                return;
            }

            int candidateHeight = WindowState switch
            {
                FormWindowState.Normal => Height,
                FormWindowState.Maximized => RestoreBounds.Height,
                _ => version2ExpandedWindowHeight
            };

            version2ExpandedWindowHeight = Math.Clamp(
                candidateHeight,
                380,
                1600
            );
        }

        private void InitializeVersion2ToolsMenu()
        {
            var checkProfileItem =
                new ToolStripMenuItem(
                    "Проверить выбранный профиль..."
                );

            checkProfileItem.Click += async (_, _) =>
                await CheckVersion3SelectedProfileAsync();

            var diagnosticsItem =
                new ToolStripMenuItem("Диагностика...");

            diagnosticsItem.Click += async (_, _) =>
                await ShowVersion2DiagnosticsAsync();

            var coreItem =
                new ToolStripMenuItem("Ядро sing-box...");

            coreItem.Click += (_, _) =>
                ShowVersion2CoreManagement();

            var jsonImportItem =
                new ToolStripMenuItem(
                    "Импорт JSON-профиля..."
                );

            jsonImportItem.Click += async (_, _) =>
                await ImportVersion2JsonProfileAsync();

            var exportItem =
                new ToolStripMenuItem(
                    "Экспорт профилей..."
                );

            exportItem.Click += async (_, _) =>
                await ExportVersion2ProfilesAsync();

            var restoreItem =
                new ToolStripMenuItem(
                    "Восстановить профили..."
                );

            restoreItem.Click += async (_, _) =>
                await RestoreVersion2ProfilesAsync();

            var settingsItem =
                new ToolStripMenuItem("Настройки...");

            settingsItem.Click += (_, _) =>
                ShowVersion2Settings();

            version2ToolsMenu = new ContextMenuStrip();
            version2ToolsMenu.Items.Add(checkProfileItem);
            version2ToolsMenu.Items.Add(diagnosticsItem);
            version2ToolsMenu.Items.Add(coreItem);
            version2ToolsMenu.Items.Add(
                new ToolStripSeparator()
            );
            version2ToolsMenu.Items.Add(jsonImportItem);
            version2ToolsMenu.Items.Add(exportItem);
            version2ToolsMenu.Items.Add(restoreItem);
            version2ToolsMenu.Items.Add(
                new ToolStripSeparator()
            );
            version2ToolsMenu.Items.Add(settingsItem);

            version2ToolsMenu.Opening += (_, _) =>
            {
                bool canChangeFiles =
                    !operationInProgress &&
                    !coreManager.IsRunning;

                diagnosticsItem.Enabled =
                    !operationInProgress;
                checkProfileItem.Enabled =
                    !operationInProgress &&
                    profileComboBox.SelectedItem is string;
                coreItem.Enabled = !operationInProgress;
                jsonImportItem.Enabled = canChangeFiles;
                exportItem.Enabled = !operationInProgress;
                restoreItem.Enabled = canChangeFiles;
                settingsItem.Enabled = !operationInProgress;
            };
        }

        private void ExtendVersion2TrayMenu()
        {
            if (trayMenu is null)
            {
                return;
            }

            var reconnectItem =
                new ToolStripMenuItem("Переподключить");

            reconnectItem.Click += async (_, _) =>
                await ReconnectVersion2FromTrayAsync();

            version2TrayProfilesItem =
                new ToolStripMenuItem("Профили");

            version2TrayProfilesItem.DropDownOpening +=
                (_, _) => RebuildVersion2TrayProfiles();

            int insertIndex = Math.Min(
                2,
                trayMenu.Items.Count
            );

            trayMenu.Items.Insert(
                insertIndex,
                reconnectItem
            );

            trayMenu.Items.Insert(
                insertIndex + 1,
                version2TrayProfilesItem
            );
        }

        private void RebuildVersion2TrayProfiles()
        {
            if (version2TrayProfilesItem is null)
            {
                return;
            }

            version2TrayProfilesItem.DropDownItems.Clear();

            foreach (string profile in profileComboBox.Items
                         .OfType<string>())
            {
                var item = new ToolStripMenuItem(profile)
                {
                    Checked = string.Equals(
                        profileComboBox.SelectedItem as string,
                        profile,
                        StringComparison.Ordinal
                    )
                };

                item.Click += async (_, _) =>
                    await ReconnectVersion2FromTrayAsync(
                        profile
                    );

                version2TrayProfilesItem
                    .DropDownItems.Add(item);
            }

            if (version2TrayProfilesItem
                    .DropDownItems.Count == 0)
            {
                version2TrayProfilesItem
                    .DropDownItems.Add(
                        new ToolStripMenuItem(
                            "Профилей нет"
                        )
                        {
                            Enabled = false
                        }
                    );
            }
        }

        private async Task ReconnectVersion2FromTrayAsync(
            string? profileName = null)
        {
            if (operationInProgress)
            {
                return;
            }

            if (coreManager.IsRunning)
            {
                stopButton.PerformClick();

                DateTime deadline =
                    DateTime.UtcNow.AddSeconds(8);

                while ((operationInProgress ||
                        coreManager.IsRunning) &&
                       DateTime.UtcNow < deadline)
                {
                    await Task.Delay(100);
                }
            }

            if (operationInProgress ||
                coreManager.IsRunning ||
                isClosing)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(profileName))
            {
                int profileIndex =
                    profileComboBox.Items.IndexOf(
                        profileName
                    );

                if (profileIndex >= 0)
                {
                    profileComboBox.SelectedIndex =
                        profileIndex;
                }
            }

            if (startButton.Enabled)
            {
                startButton.PerformClick();
            }
        }

        private async Task CompleteVersion2StartupAsync()
        {
            bool coreIsTrusted = false;

            try
            {
                StartupService.SetEnabled(
                    appSettings.StartWithWindows
                );
            }
            catch (Exception ex)
            {
                AddLog(
                    "Не удалось обновить автозапуск: " +
                    ex.Message
                );
            }

            try
            {
                CoreInformation core =
                    await version2CoreService
                        .GetInformationAsync();

                AddLog($"Ядро: {core.Version}");
                coreIsTrusted = core.Exists && core.HashMatches;

                if (!coreIsTrusted)
                {
                    string integrityProblem = !core.Exists
                        ? "Файл sing-box.exe не найден."
                        : string.IsNullOrWhiteSpace(
                            core.ExpectedSha256)
                            ? "Файл sing-box.sha256 отсутствует " +
                              "или повреждён."
                            : "Контрольная сумма sing-box.exe " +
                              "не совпадает с sing-box.sha256.";

                    AddLog(
                        "ВНИМАНИЕ: " + integrityProblem
                    );

                    MessageBox.Show(
                        integrityProblem + "\n\n" +
                        "Запуск ядра заблокирован. Проверьте его " +
                        "через меню «Сервис».",
                        "GeniaProxy",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }
            catch (Exception ex)
            {
                AddLog(
                    "Не удалось проверить ядро: " +
                    ex.Message
                );
            }

            if (appSettings.AutoConnect &&
                coreIsTrusted &&
                !operationInProgress &&
                !coreManager.IsRunning &&
                startButton.Enabled)
            {
                AddLog("Автоматическое подключение...");
                startButton.PerformClick();
            }
        }

        private void Version2CoreManager_ProcessExited(
            int exitCode)
        {
            version2CoreJob?.Dispose();
            version2CoreJob = null;

            TryBeginInvoke(() =>
                ScheduleVersion2Reconnect(exitCode)
            );
        }

        private async Task AttachVersion2JobAsync()
        {
            DateTime deadline =
                DateTime.UtcNow.AddSeconds(20);

            while (!isClosing &&
                   DateTime.UtcNow < deadline)
            {
                if (coreManager.IsRunning)
                {
                    try
                    {
                        if (coreManager.TryGetRunningProcess(
                                out Process? process) &&
                            process is not null)
                        {
                            var job = WindowsJobObject
                                .CreateKillOnClose();

                            try
                            {
                                job.Assign(process);
                            }
                            catch
                            {
                                job.Dispose();
                                throw;
                            }

                            version2CoreJob?.Dispose();
                            version2CoreJob = job;

                            AddLog(
                                "Защита дочернего процесса " +
                                "sing-box включена."
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog(
                            "Не удалось включить защиту " +
                            "процесса sing-box: " + ex.Message
                        );
                    }

                    return;
                }

                if (!operationInProgress)
                {
                    return;
                }

                await Task.Delay(100);
            }
        }

        private async void ScheduleVersion2Reconnect(
            int exitCode)
        {
            if (isClosing ||
                !appSettings.ReconnectOnFailure)
            {
                return;
            }

            int reconnectGeneration =
                Interlocked.Increment(
                    ref version2ReconnectGeneration
                );

            DateTime now = DateTime.UtcNow;
            DateTime cutoff = now.AddMinutes(-2);

            while (version2RecentCoreFailures.Count > 0 &&
                   version2RecentCoreFailures.Peek() < cutoff)
            {
                version2RecentCoreFailures.Dequeue();
            }

            version2RecentCoreFailures.Enqueue(now);

            if (version2RecentCoreFailures.Count > 3)
            {
                AddLog(
                    "Автопереподключение остановлено: " +
                    "более трёх сбоев за две минуты."
                );
                return;
            }

            int delaySeconds = Math.Clamp(
                appSettings.ReconnectDelaySeconds,
                1,
                60
            );

            AddLog(
                $"Сбой sing-box (код {exitCode}). " +
                $"Повтор через {delaySeconds} сек."
            );

            await Task.Delay(
                TimeSpan.FromSeconds(delaySeconds)
            );

            if (isClosing ||
                !appSettings.ReconnectOnFailure ||
                reconnectGeneration !=
                    Volatile.Read(
                        ref version2ReconnectGeneration
                    ) ||
                operationInProgress ||
                coreManager.IsRunning ||
                !startButton.Enabled)
            {
                return;
            }

            AddLog("Автоматическое переподключение...");
            startButton.PerformClick();
        }

        private void ShowVersion2CoreManagement()
        {
            using var dialog = new CoreManagementForm(
                version2CoreService,
                () => coreManager.IsRunning
            );

            dialog.ShowDialog(this);
        }

        private void ShowVersion2Settings()
        {
            using var dialog = new SettingsForm(appSettings);

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                StartupService.SetEnabled(
                    dialog.StartWithWindows
                );

                appSettings.AutoConnect =
                    dialog.AutoConnect;
                appSettings.StartWithWindows =
                    dialog.StartWithWindows;
                appSettings.ReconnectOnFailure =
                    dialog.ReconnectOnFailure;
                appSettings.ReconnectDelaySeconds =
                    dialog.ReconnectDelaySeconds;

                if (!appSettings.ReconnectOnFailure)
                {
                    Interlocked.Increment(
                        ref version2ReconnectGeneration
                    );
                }

                SaveSettings();
                AddLog("Настройки программы сохранены.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось применить настройки.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private async Task ShowVersion2DiagnosticsAsync()
        {
            string diagnostics;

            try
            {
                CoreInformation core =
                    await version2CoreService
                        .GetInformationAsync();

                TimeSpan uptime =
                    DateTime.UtcNow - version2StartedUtc;

                Version? applicationVersion =
                    System.Reflection.Assembly
                        .GetExecutingAssembly()
                        .GetName().Version;

                string systemProxyState =
                    systemProxyService.IsEnabledByApplication
                        ? "включён"
                        : "выключен";

                string pendingProxyRestore =
                    systemProxyService.HasPendingBackup
                        ? "да"
                        : "нет";

                diagnostics = string.Join(
                    Environment.NewLine,
                    "GeniaProxy — диагностика",
                    "========================",
                    $"Версия приложения: {applicationVersion}",
                    $".NET: " +
                    $"{RuntimeInformation.FrameworkDescription}",
                    $"Windows: " +
                    $"{RuntimeInformation.OSDescription}",
                    $"Архитектура: " +
                    $"{RuntimeInformation.ProcessArchitecture}",
                    $"Время работы: " +
                    $"{uptime.ToString(
                        "c",
                        CultureInfo.InvariantCulture)}",
                    string.Empty,
                    $"Профиль: " +
                    $"{profileComboBox.SelectedItem ?? "—"}",
                    $"Локальный порт: " +
                    $"{portNumericUpDown.Value}",
                    $"sing-box запущен: " +
                    $"{(coreManager.IsRunning ? "да" : "нет")}",
                    $"Текущая операция: " +
                    OperationCoordinator.GetDisplayName(
                        operationCoordinator.CurrentOperation
                    ),
                    $"Системный прокси GeniaProxy: " +
                    systemProxyState,
                    $"Ожидает восстановление прокси: " +
                    pendingProxyRestore,
                    string.Empty,
                    $"Версия ядра: {core.Version}",
                    $"SHA-256: {core.Sha256}",
                    $"Контрольная сумма совпадает: " +
                    $"{(core.HashMatches ? "да" : "нет")}",
                    $"Резервное ядро: " +
                    $"{(core.BackupAvailable ? "есть" : "нет")}",
                    string.Empty,
                    $"Папка программы: " +
                    $"{AppContext.BaseDirectory}",
                    $"Настройки: {settingsService.SettingsPath}",
                    $"Профили: {ProfilesDirectory}"
                );
            }
            catch (Exception ex)
            {
                diagnostics =
                    "Не удалось собрать диагностику." +
                    Environment.NewLine + ex;
            }

            using var dialog =
                new DiagnosticsForm(diagnostics);

            dialog.ShowDialog(this);
        }

        private async Task CheckVersion3SelectedProfileAsync()
        {
            if (operationInProgress ||
                profileComboBox.SelectedItem is not string profileName)
            {
                return;
            }

            string profilePath = Path.Combine(
                ProfilesDirectory,
                profileName + ".json"
            );

            string? runtimePath = null;
            CancellationToken cancellationToken = BeginOperation(
                ApplicationOperation.CheckProfile
            );

            SetBusyState();
            statusLabel.Text = "Статус: Проверка профиля...";

            try
            {
                var file = new FileInfo(profilePath);

                if (!file.Exists)
                {
                    throw new FileNotFoundException(
                        "Файл профиля не найден.",
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

                string source = await File.ReadAllTextAsync(
                    profilePath,
                    cancellationToken
                );

                ProfileInspection inspection =
                    ProfileInspectionService.Inspect(source);

                runtimePath = await CreateRuntimeConfigAsync(
                    profilePath,
                    decimal.ToInt32(portNumericUpDown.Value),
                    cancellationToken
                );

                CoreCommandResult result =
                    await coreManager.CheckConfigAsync(
                        runtimePath,
                        cancellationToken
                    );

                string coreDetails = string.Join(
                    Environment.NewLine,
                    result.StandardOutput,
                    result.StandardError
                ).Trim();

                if (coreDetails.Length > 2000)
                {
                    coreDetails = coreDetails[..2000] +
                        Environment.NewLine + "…";
                }

                string report =
                    ProfileInspectionService.FormatSummary(
                        profileName,
                        inspection
                    ) +
                    Environment.NewLine +
                    Environment.NewLine +
                    "Проверка sing-box: " +
                    (result.ExitCode == 0
                        ? "успешно"
                        : $"ошибка, код {result.ExitCode}");

                if (!string.IsNullOrWhiteSpace(coreDetails))
                {
                    report += Environment.NewLine +
                        Environment.NewLine + coreDetails;
                }

                using var dialog = new DiagnosticsForm(
                    report,
                    "Проверка профиля GeniaProxy"
                );

                dialog.ShowDialog(this);

                AddLog(
                    $"Профиль проверен: {profileName}; " +
                    $"код sing-box: {result.ExitCode}."
                );
            }
            catch (OperationCanceledException)
            {
                if (!isClosing)
                {
                    AddLog("Проверка профиля отменена.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось проверить профиль.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(runtimePath))
                {
                    TryDeleteFileSilently(runtimePath);
                }

                EndOperation();

                if (!isClosing)
                {
                    if (coreManager.IsRunning)
                    {
                        SetRunningState();
                    }
                    else
                    {
                        statusLabel.Text = "Статус: Остановлено";
                        SetIdleState();
                    }
                }
            }
        }

        private async Task ImportVersion2JsonProfileAsync()
        {
            if (operationInProgress)
            {
                return;
            }

            using var fileDialog = new OpenFileDialog
            {
                Title = "Импорт JSON-профиля sing-box",
                Filter =
                    "JSON-профиль (*.json)|*.json|" +
                    "Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (fileDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            string suggestedName =
                Path.GetFileNameWithoutExtension(
                    fileDialog.FileName
                );

            using var nameDialog =
                new RenameProfileForm(suggestedName);

            nameDialog.Text = "Название импортируемого профиля";

            if (nameDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            string profileName = nameDialog.ProfileName;
            string destination = Path.Combine(
                ProfilesDirectory,
                profileName + ".json"
            );

            if (File.Exists(destination))
            {
                DialogResult overwrite = MessageBox.Show(
                    $"Профиль «{profileName}» уже существует. " +
                    "Заменить его?",
                    "GeniaProxy",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2
                );

                if (overwrite != DialogResult.Yes)
                {
                    return;
                }
            }

            CancellationToken cancellationToken = BeginOperation(
                ApplicationOperation.ImportProfile
            );

            SetBusyState();

            try
            {
                var sourceFile = new FileInfo(
                    fileDialog.FileName
                );

                if (sourceFile.Length >
                    JsonProfileImportService.MaxProfileBytes)
                {
                    throw new InvalidDataException(
                        "JSON-профиль превышает " +
                        "допустимый размер 1 МБ."
                    );
                }

                string source = await File.ReadAllTextAsync(
                    fileDialog.FileName,
                    cancellationToken
                );

                if (JsonProfileImportService
                        .ContainsInsecureTls(source))
                {
                    DialogResult insecureChoice =
                        MessageBox.Show(
                            "В JSON-профиле отключена " +
                            "проверка TLS-сертификата " +
                            "(insecure=true).\n\n" +
                            "Это снижает защиту от подмены " +
                            "сервера. Продолжить импорт?",
                            "GeniaProxy",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2
                        );

                    if (insecureChoice != DialogResult.Yes)
                    {
                        AddLog(
                            "Импорт небезопасного JSON-профиля " +
                            "отменён."
                        );

                        return;
                    }
                }

                int localPort = decimal.ToInt32(
                    portNumericUpDown.Value
                );

                string normalized =
                    JsonProfileImportService.NormalizeConfig(
                        source,
                        localPort
                    );

                await AtomicFileWriter.WriteAllTextAsync(
                    destination,
                    normalized,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false
                    ),
                    cancellationToken
                );

                LoadProfiles();

                int profileIndex =
                    profileComboBox.Items.IndexOf(
                        profileName
                    );

                if (profileIndex >= 0)
                {
                    profileComboBox.SelectedIndex =
                        profileIndex;
                }

                SaveSettings();
                SetIdleState();

                AddLog(
                    $"JSON-профиль импортирован: " +
                    $"{profileName}"
                );
            }
            catch (OperationCanceledException)
            {
                if (!isClosing)
                {
                    AddLog("Импорт JSON-профиля отменён.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось импортировать JSON-профиль.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                EndOperation();

                if (!isClosing)
                {
                    SetIdleState();
                }
            }
        }

        private async Task ExportVersion2ProfilesAsync()
        {
            if (operationInProgress)
            {
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "Экспорт профилей GeniaProxy",
                Filter =
                    "Резервная копия (*.zip)|*.zip",
                DefaultExt = "zip",
                AddExtension = true,
                FileName = "GeniaProxy-backup-" +
                    DateTime.Now.ToString(
                        "yyyy-MM-dd_HH-mm-ss",
                        CultureInfo.InvariantCulture
                    ) + ".zip"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _ = BeginOperation(
                ApplicationOperation.ExportProfiles
            );

            UseWaitCursor = true;

            try
            {
                var service = new ProfileBackupService(
                    ProfilesDirectory,
                    settingsService.SettingsPath
                );

                BackupExportResult result = await Task.Run(
                    () => service.Export(dialog.FileName)
                );

                AddLog(
                    $"Резервная копия создана: " +
                    $"{dialog.FileName}"
                );

                MessageBox.Show(
                    $"Сохранено профилей: " +
                    $"{result.ProfilesExported}.",
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось создать резервную копию.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                UseWaitCursor = false;
                EndOperation();
            }
        }

        private async Task RestoreVersion2ProfilesAsync()
        {
            if (operationInProgress)
            {
                return;
            }

            using var dialog = new OpenFileDialog
            {
                Title = "Восстановление профилей GeniaProxy",
                Filter =
                    "Резервная копия (*.zip)|*.zip",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            DialogResult choice = MessageBox.Show(
                "Выберите режим восстановления.\n\n" +
                "Да — заменить текущие профили.\n" +
                "Нет — добавить только отсутствующие.\n" +
                "Отмена — ничего не менять.",
                "GeniaProxy",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button3
            );

            if (choice == DialogResult.Cancel)
            {
                return;
            }

            BackupRestoreMode mode = choice == DialogResult.Yes
                ? BackupRestoreMode.Replace
                : BackupRestoreMode.Merge;

            _ = BeginOperation(
                ApplicationOperation.RestoreProfiles
            );

            SetBusyState();
            UseWaitCursor = true;

            try
            {
                var service = new ProfileBackupService(
                    ProfilesDirectory,
                    settingsService.SettingsPath
                );

                BackupRestoreResult result = await Task.Run(
                    () => service.Restore(dialog.FileName, mode)
                );

                LoadProfiles();
                LoadSettings();
                ApplyVersion2SavedInterfaceState();

                if (result.SettingsRestored)
                {
                    StartupService.SetEnabled(
                        appSettings.StartWithWindows
                    );
                }

                SetIdleState();

                AddLog(
                    $"Восстановлено профилей: " +
                    $"{result.ProfilesRestored}; " +
                    $"пропущено: {result.ProfilesSkipped}."
                );

                MessageBox.Show(
                    $"Восстановлено профилей: " +
                    $"{result.ProfilesRestored}.\n" +
                    $"Пропущено: {result.ProfilesSkipped}.",
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось восстановить резервную копию.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                UseWaitCursor = false;
                EndOperation();

                if (!isClosing)
                {
                    SetIdleState();
                }
            }
        }
    }
}
