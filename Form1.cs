using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using GeniaProxy.Models;
using GeniaProxy.Services;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GeniaProxy
{
    public partial class Form1 : Form
    {
        private const int MaxLogCharacters = 200_000;
        private const int LogTrimTarget = 160_000;
        private const int MaxLogBatchLines = 500;
        private const int MaxPendingLogLines = 5_000;
        private const int LogFlushDelayMilliseconds = 75;

        private readonly CoreManager coreManager = new();
        private readonly SettingsService settingsService = new();
        private readonly SystemProxyService systemProxyService = new();
        private readonly OperationCoordinator
            operationCoordinator = new();
        private readonly ConcurrentQueue<string>
            pendingLogLines = new();

        private AppSettings appSettings = new();
        private NotifyIcon? trayIcon;
        private ContextMenuStrip? trayMenu;
        private ToolStripMenuItem? trayToggleItem;
        private ContextMenuStrip? logMenu;
        private ContextMenuStrip? profileMenu;

        private bool trayHintShown;
        private bool operationInProgress =>
            operationCoordinator.IsBusy;
        private volatile bool isClosing;
        private int logFlushScheduled;
        private int pendingLogLineCount;
        private string? activeRuntimeConfigPath;

        private static string ProfilesDirectory =>
            Path.Combine(
                AppContext.BaseDirectory,
                "data",
                "profiles"
            );

        private static string RuntimeDirectory =>
            Path.Combine(
                AppContext.BaseDirectory,
                "data",
                "runtime"
            );

        private static string LegacyRuntimeConfigPath =>
            Path.Combine(
                ProfilesDirectory,
                "_active.runtime"
            );

        public Form1()
        {
            InitializeComponent();

            coreManager.OutputReceived +=
                CoreManager_OutputReceived;

            coreManager.ProcessExited +=
                CoreManager_ProcessExited;

            FormClosing += Form1_FormClosing;

            importButton.Click +=
                ImportButton_Click;

            deleteProfileButton.Click +=
                DeleteProfileButton_Click;

            InitializeTray();
            InitializeLogContextMenu();
            InitializeProfileContextMenu();

            Resize += Form1_Resize;
        }

        private void InitializeTray()
        {
            var openItem =
                new ToolStripMenuItem("Открыть");

            openItem.Click +=
                (_, _) => RestoreFromTray();

            trayToggleItem =
                new ToolStripMenuItem("Запустить");

            trayToggleItem.Click +=
                (_, _) => ToggleProxyFromTray();

            var exitItem =
                new ToolStripMenuItem("Выход");

            exitItem.Click +=
                (_, _) => Close();

            trayMenu =
                new ContextMenuStrip();

            trayMenu.Items.Add(openItem);
            trayMenu.Items.Add(trayToggleItem);
            trayMenu.Items.Add(
                new ToolStripSeparator()
            );
            trayMenu.Items.Add(exitItem);

            System.Drawing.Icon applicationIcon =
                Icon
                ?? System.Drawing.Icon.ExtractAssociatedIcon(
                    Application.ExecutablePath
                )
                ?? System.Drawing.SystemIcons.Application;

            trayIcon =
                new NotifyIcon
                {
                    Icon = applicationIcon,
                    Text = "GeniaProxy — остановлено",
                    ContextMenuStrip = trayMenu,
                    Visible = true
                };

            trayIcon.DoubleClick +=
                (_, _) => RestoreFromTray();

            UpdateTrayState();
        }

        private void Form1_Resize(
            object? sender,
            EventArgs e)
        {
            if (WindowState !=
                FormWindowState.Minimized)
            {
                return;
            }

            Hide();
            ShowInTaskbar = false;

            if (!trayHintShown)
            {
                // Не показываем системный balloon-tip: Windows может
                // сопровождать его звуком даже при скрытом уведомлении.
                trayHintShown = true;
            }
        }

        private void RestoreFromTray()
        {
            if (IsDisposed)
                return;

            ShowInTaskbar = true;

            if (!Visible)
                Show();

            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            Focus();

            // Даём Windows завершить восстановление окна.
            TryBeginInvoke(() =>
            {
                PerformLayout();

                if (!logTextBox.Visible)
                {
                    return;
                }

                logTextBox.Invalidate(true);
                logTextBox.Refresh();

                logTextBox.SelectionStart =
                    logTextBox.TextLength;

                logTextBox.SelectionLength = 0;
                logTextBox.ScrollToCaret();
            });
        }

        private void ToggleProxyFromTray()
        {
            if (coreManager.IsRunning)
            {
                if (stopButton.Enabled)
                {
                    stopButton.PerformClick();
                }

                return;
            }

            if (startButton.Enabled)
            {
                startButton.PerformClick();
            }
            else
            {
                RestoreFromTray();
            }
        }

        private void UpdateTrayState(
            bool busy = false)
        {
            if (trayIcon is null ||
                trayToggleItem is null)
            {
                return;
            }

            if (busy)
            {
                trayIcon.Text =
                    "GeniaProxy — выполнение операции";

                trayToggleItem.Enabled = false;

                return;
            }

            if (coreManager.IsRunning)
            {
                trayIcon.Text =
                    "GeniaProxy — прокси работает";

                trayToggleItem.Text =
                    "Остановить";

                trayToggleItem.Enabled = true;
            }
            else
            {
                trayIcon.Text =
                    "GeniaProxy — остановлено";

                trayToggleItem.Text =
                    "Запустить";

                trayToggleItem.Enabled =
                    profileComboBox.Items.Count > 0;
            }
        }

        private void InitializeLogContextMenu()
        {
            var copyAllItem =
                new ToolStripMenuItem("Копировать всё");

            copyAllItem.Click +=
                (_, _) => CopyAllLog();

            var clearItem =
                new ToolStripMenuItem("Очистить");

            clearItem.Click +=
                (_, _) => ClearLog();

            var saveItem =
                new ToolStripMenuItem("Сохранить журнал...");

            saveItem.Click +=
                (_, _) => SaveLogToFile();

            logMenu =
                new ContextMenuStrip();

            logMenu.Items.Add(copyAllItem);
            logMenu.Items.Add(clearItem);
            logMenu.Items.Add(
                new ToolStripSeparator()
            );
            logMenu.Items.Add(saveItem);

            logMenu.Opening += (_, _) =>
            {
                FlushPendingLog();

                bool hasText =
                    !string.IsNullOrEmpty(
                        logTextBox.Text
                    );

                copyAllItem.Enabled = hasText;
                clearItem.Enabled = hasText;
                saveItem.Enabled = hasText;
            };

            logTextBox.ContextMenuStrip =
                logMenu;
        }

        private void CopyAllLog()
        {
            FlushPendingLog();

            if (string.IsNullOrEmpty(
                    logTextBox.Text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(
                    logTextBox.Text
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось скопировать журнал.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private void SaveLogToFile()
        {
            FlushPendingLog();

            if (string.IsNullOrEmpty(
                    logTextBox.Text))
            {
                return;
            }

            using var dialog =
                new SaveFileDialog
                {
                    Title = "Сохранить журнал GeniaProxy",
                    Filter =
                        "Текстовый файл (*.txt)|*.txt|" +
                        "Все файлы (*.*)|*.*",

                    DefaultExt = "txt",
                    AddExtension = true,

                    FileName =
                        "GeniaProxy-log-" +
                        DateTime.Now.ToString(
                            "yyyy-MM-dd_HH-mm-ss",
                            CultureInfo.InvariantCulture
                        ) +
                        ".txt"
                };

            if (dialog.ShowDialog(this) !=
                DialogResult.OK)
            {
                return;
            }

            try
            {
                File.WriteAllText(
                    dialog.FileName,
                    logTextBox.Text
                );

                AddLog(
                    $"Журнал сохранён: {dialog.FileName}"
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось сохранить журнал.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }



        private void InitializeProfileContextMenu()
        {
            var renameItem =
                new ToolStripMenuItem(
                    "Переименовать..."
                );

            renameItem.Click +=
                (_, _) => RenameSelectedProfile();

            var deleteItem =
                new ToolStripMenuItem(
                    "Удалить"
                );

            deleteItem.Click +=
                (_, _) =>
                    deleteProfileButton.PerformClick();

            var openFolderItem =
                new ToolStripMenuItem(
                    "Открыть папку профилей"
                );

            openFolderItem.Click +=
                (_, _) => OpenProfilesFolder();

            profileMenu =
                new ContextMenuStrip();

            profileMenu.Items.Add(renameItem);
            profileMenu.Items.Add(deleteItem);

            profileMenu.Items.Add(
                new ToolStripSeparator()
            );

            profileMenu.Items.Add(openFolderItem);

            profileMenu.Opening += (_, _) =>
            {
                bool hasProfile =
                    profileComboBox.SelectedItem
                    is string;

                bool canModify =
                    hasProfile &&
                    !coreManager.IsRunning &&
                    !operationInProgress;

                renameItem.Enabled = canModify;
                deleteItem.Enabled = canModify;
                openFolderItem.Enabled = true;
            };

            profileComboBox.ContextMenuStrip =
                profileMenu;
        }

        private void RenameSelectedProfile()
        {
            if (coreManager.IsRunning ||
                operationInProgress)
            {
                MessageBox.Show(
                    "Сначала остановите прокси.",
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            if (profileComboBox.SelectedItem
                is not string oldName)
            {
                MessageBox.Show(
                    "Профиль не выбран.",
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            using var dialog =
                new RenameProfileForm(oldName);

            if (dialog.ShowDialog(this) !=
                DialogResult.OK)
            {
                return;
            }

            string newName =
                dialog.ProfileName;

            if (string.Equals(
                    oldName,
                    newName,
                    StringComparison.Ordinal))
            {
                return;
            }

            string oldPath =
                Path.Combine(
                    ProfilesDirectory,
                    oldName + ".json"
                );

            string newPath =
                Path.Combine(
                    ProfilesDirectory,
                    newName + ".json"
                );

            try
            {
                if (!File.Exists(oldPath))
                {
                    throw new FileNotFoundException(
                        "Файл профиля не найден.",
                        oldPath
                    );
                }

                bool sameWindowsPath =
                    string.Equals(
                        oldPath,
                        newPath,
                        StringComparison.OrdinalIgnoreCase
                    );

                if (!sameWindowsPath &&
                    File.Exists(newPath))
                {
                    MessageBox.Show(
                        $"Профиль «{newName}» уже существует.",
                        "GeniaProxy",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );

                    return;
                }

                if (sameWindowsPath)
                {
                    string temporaryPath =
                        Path.Combine(
                            ProfilesDirectory,
                            $".rename-{Guid.NewGuid():N}.tmp"
                        );

                    File.Move(
                        oldPath,
                        temporaryPath
                    );

                    try
                    {
                        File.Move(
                            temporaryPath,
                            newPath
                        );
                    }
                    catch
                    {
                        if (File.Exists(temporaryPath) &&
                            !File.Exists(oldPath))
                        {
                            File.Move(
                                temporaryPath,
                                oldPath
                            );
                        }

                        throw;
                    }
                }
                else
                {
                    File.Move(
                        oldPath,
                        newPath
                    );
                }

                if (string.Equals(
                        appSettings.LastProfile,
                        oldName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    appSettings.LastProfile =
                        newName;
                }

                LoadProfiles();

                int newIndex =
                    profileComboBox.Items.IndexOf(
                        newName
                    );

                if (newIndex >= 0)
                {
                    profileComboBox.SelectedIndex =
                        newIndex;
                }

                SaveSettings();
                SetIdleState();

                AddLog(
                    $"Профиль переименован: " +
                    $"{oldName} → {newName}"
                );
            }
            catch (Exception ex)
            {
                AddLog(
                    "Ошибка переименования профиля: " +
                    ex.Message
                );

                MessageBox.Show(
                    "Не удалось переименовать профиль.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private static void OpenProfilesFolder()
        {
            try
            {
                Directory.CreateDirectory(
                    ProfilesDirectory
                );

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName =
                            ProfilesDirectory,

                        UseShellExecute = true
                    }
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось открыть папку профилей.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void Form1_Load(
            object? sender,
            EventArgs e)
        {
            TryDeleteStaleRuntimeConfigs();
            RestoreProxyAfterUnexpectedShutdown();

            LoadProfiles();
            LoadSettings();

            statusLabel.Text =
                "Статус: Остановлено";

            SetIdleState();

            AddLog("GeniaProxy запущен.");
            AddLog(
                $"Папка профилей: {ProfilesDirectory}"
            );
            AddLog(
                $"Локальный порт: {portNumericUpDown.Value}"
            );
            AddLog(
                $"Системный прокси: " +
                $"{(systemProxyCheckBox.Checked ? "включён" : "выключен")}"
            );
            AddLog(
                $"Настройки: {settingsService.SettingsPath}"
            );
            AddLog("Ожидание запуска ядра...");
        }

        // --------------------------------------------------
        // Восстановление прокси после аварийного завершения
        // --------------------------------------------------

        private void RestoreProxyAfterUnexpectedShutdown()
        {
            if (!systemProxyService.HasPendingBackup)
            {
                return;
            }

            try
            {
                ProxyRestoreOutcome outcome =
                    systemProxyService.Restore();

                if (outcome ==
                    ProxyRestoreOutcome.Restored)
                {
                    AddLog(
                        "Восстановлены прежние системные " +
                        "настройки прокси после " +
                        "незавершённого сеанса."
                    );
                }
                else if (outcome ==
                    ProxyRestoreOutcome
                        .CurrentSettingsPreserved)
                {
                    AddLog(
                        "Системный прокси был изменён вне " +
                        "GeniaProxy. Текущие настройки сохранены."
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось восстановить прежние " +
                    "настройки системного прокси.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        // --------------------------------------------------
        // Настройки программы
        // --------------------------------------------------

        private void LoadSettings()
        {
            appSettings = settingsService.Load();

            int minimumPort =
                decimal.ToInt32(
                    portNumericUpDown.Minimum
                );

            int maximumPort =
                decimal.ToInt32(
                    portNumericUpDown.Maximum
                );

            int safePort = Math.Clamp(
                appSettings.LocalPort,
                minimumPort,
                maximumPort
            );

            portNumericUpDown.Value = safePort;

            systemProxyCheckBox.Checked =
                appSettings.UseSystemProxy;

            if (string.IsNullOrWhiteSpace(
                    appSettings.LastProfile))
            {
                return;
            }

            int profileIndex =
                profileComboBox.Items.IndexOf(
                    appSettings.LastProfile
                );

            if (profileIndex >= 0)
            {
                profileComboBox.SelectedIndex =
                    profileIndex;
            }
        }

        private void SaveSettings()
        {
            appSettings.LocalPort =
                decimal.ToInt32(
                    portNumericUpDown.Value
                );

            appSettings.LastProfile =
                profileComboBox.SelectedItem as string
                ?? string.Empty;

            appSettings.UseSystemProxy =
                systemProxyCheckBox.Checked;

            CaptureVersion2ExpandedWindowHeight();

            appSettings.LogCollapsed =
                version2LogCollapsed;

            appSettings.ExpandedWindowHeight =
                version2ExpandedWindowHeight;

            settingsService.Save(appSettings);
        }

        // --------------------------------------------------
        // Список профилей
        // --------------------------------------------------

        private void LoadProfiles()
        {
            string? previouslySelectedProfile =
                profileComboBox.SelectedItem as string;

            profileComboBox.Items.Clear();

            Directory.CreateDirectory(
                ProfilesDirectory
            );

            string[] profileFiles =
                Directory.GetFiles(
                    ProfilesDirectory,
                    "*.json"
                );

            foreach (string file in profileFiles
                         .OrderBy(
                             Path.GetFileName,
                             StringComparer.OrdinalIgnoreCase))
            {
                string? profileName =
                    Path.GetFileNameWithoutExtension(file);

                if (string.IsNullOrWhiteSpace(profileName) ||
                    ProfileNameValidator.GetValidationError(
                        profileName) is not null)
                {
                    AddLog(
                        $"Пропущен профиль с недопустимым " +
                        $"именем: {Path.GetFileName(file)}"
                    );

                    continue;
                }

                if (new FileInfo(file).Length >
                    JsonProfileImportService.MaxProfileBytes)
                {
                    AddLog(
                        $"Пропущен слишком большой профиль: " +
                        $"{profileName}"
                    );

                    continue;
                }

                profileComboBox.Items.Add(profileName);
            }

            if (profileComboBox.Items.Count == 0)
            {
                AddLog("JSON-профили не найдены.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(
                    previouslySelectedProfile))
            {
                int previousIndex =
                    profileComboBox.Items.IndexOf(
                        previouslySelectedProfile
                    );

                if (previousIndex >= 0)
                {
                    profileComboBox.SelectedIndex =
                        previousIndex;

                    return;
                }
            }

            profileComboBox.SelectedIndex = 0;
        }

        // --------------------------------------------------
        // Импорт профиля
        // --------------------------------------------------

        private async void ImportButton_Click(
            object? sender,
            EventArgs e)
        {
            if (operationInProgress ||
                coreManager.IsRunning)
            {
                return;
            }

            CancellationToken cancellationToken =
                BeginOperation(
                    ApplicationOperation.ImportProfile
                );

            SetBusyState();

            try
            {
                using var importForm =
                    new ImportProfileForm();

                DialogResult result =
                    importForm.ShowDialog(this);

                if (result != DialogResult.OK)
                {
                    return;
                }

                cancellationToken
                    .ThrowIfCancellationRequested();

                string profileName =
                    ProfileNameValidator.Normalize(
                        importForm.ProfileName
                    );

                string? validationError =
                    ProfileNameValidator
                        .GetValidationError(profileName);

                if (validationError is not null)
                {
                    throw new InvalidDataException(
                        validationError
                    );
                }

                string source =
                    importForm.SourceText.Trim();

                int localPort =
                    decimal.ToInt32(
                        portNumericUpDown.Value
                    );

                string profilePath =
                    Path.Combine(
                        ProfilesDirectory,
                        profileName + ".json"
                    );

                string configJson;

                if (source.StartsWith(
                        "hy2://",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    source.StartsWith(
                        "hysteria2://",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (Hysteria2ImportService
                            .RequestsInsecureTls(source))
                    {
                        DialogResult insecureChoice =
                            MessageBox.Show(
                                "В ссылке отключена проверка " +
                                "TLS-сертификата (insecure=true).\n\n" +
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
                                "Импорт небезопасного профиля " +
                                "отменён."
                            );

                            return;
                        }
                    }

                    configJson =
                        Hysteria2ImportService
                            .CreateSingBoxConfig(
                                source,
                                localPort
                            );
                }
                else
                {
                    throw new NotSupportedException(
                        "Сейчас поддерживается импорт " +
                        "ссылок hy2:// и hysteria2://."
                    );
                }

                if (File.Exists(profilePath))
                {
                    DialogResult overwriteResult =
                        MessageBox.Show(
                            $"Профиль «{profileName}» " +
                            "уже существует.\n\n" +
                            "Заменить его?",
                            "GeniaProxy",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2
                        );

                    if (overwriteResult != DialogResult.Yes)
                    {
                        AddLog("Импорт профиля отменён.");
                        return;
                    }
                }

                await AtomicFileWriter
                    .WriteAllTextAsync(
                    profilePath,
                    configJson,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier:
                            false
                    ),
                    cancellationToken
                );

                AddLog(
                    $"Профиль сохранён: {profileName}"
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

                MessageBox.Show(
                    $"Профиль «{profileName}» " +
                    "успешно импортирован.",
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (OperationCanceledException)
            {
                if (!isClosing)
                {
                    AddLog("Импорт профиля отменён.");
                }
            }
            catch (NotSupportedException ex)
            {
                AddLog(
                    "Неподдерживаемый формат импорта: " +
                    ex.Message
                );

                MessageBox.Show(
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
            catch (FormatException ex)
            {
                AddLog(
                    $"Ошибка формата ссылки: {ex.Message}"
                );

                MessageBox.Show(
                    "Не удалось разобрать ссылку.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
            catch (Exception ex)
            {
                AddLog(
                    $"Ошибка импорта профиля: {ex.Message}"
                );

                MessageBox.Show(
                    "Не удалось сохранить профиль.\n\n" +
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

        // --------------------------------------------------
        // Удаление профиля
        // --------------------------------------------------

        private void DeleteProfileButton_Click(
            object? sender,
            EventArgs e)
        {
            if (coreManager.IsRunning ||
                operationInProgress)
            {
                MessageBox.Show(
                    "Сначала остановите прокси.",
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            if (profileComboBox.SelectedItem
                is not string profileName)
            {
                MessageBox.Show(
                    "Профиль не выбран.",
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            DialogResult confirmation =
                MessageBox.Show(
                    $"Удалить профиль «{profileName}»?\n\n" +
                    "Это действие нельзя отменить.",
                    "GeniaProxy",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2
                );

            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            string profilePath =
                Path.Combine(
                    ProfilesDirectory,
                    profileName + ".json"
                );

            try
            {
                if (!File.Exists(profilePath))
                {
                    throw new FileNotFoundException(
                        "Файл профиля уже отсутствует.",
                        profilePath
                    );
                }

                File.Delete(profilePath);

                AddLog(
                    $"Профиль удалён: {profileName}"
                );

                if (string.Equals(
                        appSettings.LastProfile,
                        profileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    appSettings.LastProfile =
                        string.Empty;
                }

                LoadProfiles();
                SetIdleState();
                SaveSettings();
            }
            catch (Exception ex)
            {
                AddLog(
                    $"Ошибка удаления профиля: {ex.Message}"
                );

                MessageBox.Show(
                    "Не удалось удалить профиль.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        // --------------------------------------------------
        // Запуск
        // --------------------------------------------------

        private async void StartButton_Click(
            object? sender,
            EventArgs e)
        {
            if (operationInProgress ||
                coreManager.IsRunning)
            {
                return;
            }

            if (profileComboBox.SelectedItem
                is not string profileName)
            {
                AddLog("Профиль не выбран.");
                return;
            }

            int localPort =
                decimal.ToInt32(
                    portNumericUpDown.Value
                );

            if (!IsPortAvailable(localPort))
            {
                statusLabel.Text =
                    "Статус: Порт занят";

                AddLog(
                    $"Порт 127.0.0.1:{localPort} " +
                    "уже используется."
                );

                MessageBox.Show(
                    $"Порт {localPort} уже занят.\n\n" +
                    "Выберите другой порт и повторите запуск.",
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                return;
            }

            string profilePath =
                Path.Combine(
                    ProfilesDirectory,
                    profileName + ".json"
                );

            CancellationToken cancellationToken =
                BeginOperation(
                    ApplicationOperation.StartConnection
                );

            SetBusyState();

            statusLabel.Text =
                "Статус: Подготовка профиля...";

            AddLog(
                $"Выбран профиль: {profileName}"
            );

            AddLog(
                $"Выбран порт: {localPort}"
            );

            try
            {
                SaveSettings();

                TryDeleteRuntimeConfig();

                string runtimePath =
                    await CreateRuntimeConfigAsync(
                        profilePath,
                        localPort,
                        cancellationToken
                    );

                activeRuntimeConfigPath = runtimePath;

                AddLog(
                    "Временная конфигурация создана."
                );

                statusLabel.Text =
                    "Статус: Проверка профиля...";

                CoreCommandResult checkResult =
                    await coreManager.CheckConfigAsync(
                        runtimePath,
                        cancellationToken
                    );

                cancellationToken
                    .ThrowIfCancellationRequested();

                AppendCoreOutput(
                    checkResult.StandardOutput
                );

                AppendCoreOutput(
                    checkResult.StandardError
                );

                if (checkResult.ExitCode != 0)
                {
                    statusLabel.Text =
                        "Статус: Ошибка профиля";

                    AddLog(
                        "Проверка завершилась с кодом " +
                        $"{checkResult.ExitCode}."
                    );

                    return;
                }

                AddLog(
                    "Профиль прошёл проверку."
                );

                AddLog(
                    $"Запуск прокси на " +
                    $"127.0.0.1:{localPort}..."
                );

                coreManager.Start(runtimePath);

                await WaitForPortReadyAsync(
                    localPort,
                    cancellationToken
                );

                cancellationToken
                    .ThrowIfCancellationRequested();

                if (systemProxyCheckBox.Checked)
                {
                    systemProxyService.Enable(localPort);

                    AddLog(
                        "Системный прокси Windows включён: " +
                        $"127.0.0.1:{localPort}"
                    );
                }

                statusLabel.Text =
                    $"Статус: Запущено, порт {localPort}";

                SetRunningState();

                // После запуска sing-box уже прочитал конфигурацию.
                // Удаляем временный файл с секретами как можно раньше.
                TryDeleteRuntimeConfig();
            }
            catch (OperationCanceledException)
            {
                if (!isClosing)
                {
                    statusLabel.Text =
                        "Статус: Запуск отменён";

                    AddLog("Запуск отменён.");

                    await CleanupFailedStartAsync();
                }
            }
            catch (FileNotFoundException ex)
            {
                statusLabel.Text =
                    "Статус: Файл не найден";

                AddLog(ex.Message);

                if (!string.IsNullOrWhiteSpace(ex.FileName))
                {
                    AddLog(ex.FileName);
                }

                await CleanupFailedStartAsync();
            }
            catch (InvalidDataException ex)
            {
                statusLabel.Text =
                    "Статус: Ошибка профиля";

                AddLog(
                    "Профиль отклонён: " +
                    ex.Message
                );

                await CleanupFailedStartAsync();
            }
            catch (JsonException ex)
            {
                statusLabel.Text =
                    "Статус: Ошибка JSON";

                AddLog(
                    "Ошибка чтения профиля: " +
                    ex.Message
                );

                await CleanupFailedStartAsync();
            }
            catch (Exception ex)
            {
                statusLabel.Text =
                    "Статус: Ошибка запуска";

                AddLog(
                    $"Ошибка: {ex.Message}"
                );

                await CleanupFailedStartAsync();
            }
            finally
            {
                EndOperation();

                if (!coreManager.IsRunning)
                {
                    TryDeleteRuntimeConfig();

                    if (!isClosing)
                    {
                        SetIdleState();
                    }
                }
            }
        }

        private async Task CleanupFailedStartAsync()
        {
            try
            {
                RestoreSystemProxyIfNeeded();
            }
            catch (Exception ex)
            {
                AddLog(
                    "Не удалось восстановить системный прокси: " +
                    ex.Message
                );
            }

            try
            {
                await coreManager.StopAsync();
            }
            catch (Exception ex)
            {
                AddLog(
                    "Не удалось остановить ядро после ошибки: " +
                    ex.Message
                );
            }
        }

        // --------------------------------------------------
        // Ожидание открытия порта
        // --------------------------------------------------

        private async Task WaitForPortReadyAsync(
            int port,
            CancellationToken cancellationToken)
        {
            DateTime deadline =
                DateTime.UtcNow.AddSeconds(7);

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                if (!coreManager.IsRunning)
                {
                    throw new InvalidOperationException(
                        "Процесс sing-box завершился до " +
                        "готовности локального SOCKS5-прокси."
                    );
                }

                try
                {
                    using var client = new TcpClient();

                    using var attemptCancellation =
                        CancellationTokenSource
                            .CreateLinkedTokenSource(
                                cancellationToken
                            );

                    attemptCancellation.CancelAfter(
                        TimeSpan.FromMilliseconds(700)
                    );

                    await client.ConnectAsync(
                        IPAddress.Loopback,
                        port,
                        attemptCancellation.Token
                    );

                    NetworkStream stream = client.GetStream();

                    byte[] request = [0x05, 0x01, 0x00];

                    await stream.WriteAsync(
                        request,
                        attemptCancellation.Token
                    );

                    byte[] response = new byte[2];

                    await stream.ReadExactlyAsync(
                        response,
                        attemptCancellation.Token
                    );

                    if (response[0] == 0x05 &&
                        response[1] == 0x00 &&
                        coreManager.IsRunning)
                    {
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    cancellationToken
                        .ThrowIfCancellationRequested();
                }
                catch (SocketException)
                {
                    // Порт ещё не открыт.
                }
                catch (IOException)
                {
                    // Порт открыт, но SOCKS5 ещё не готов.
                }

                await Task.Delay(
                    120,
                    cancellationToken
                );
            }

            throw new TimeoutException(
                $"Локальный SOCKS5-прокси не открыл " +
                $"порт {port} или не ответил на проверку."
            );
        }

        // --------------------------------------------------
        // Подготовка временной конфигурации
        // --------------------------------------------------

        private static async Task<string>
            CreateRuntimeConfigAsync(
                string profilePath,
                int localPort,
                CancellationToken cancellationToken)
        {
            if (!File.Exists(profilePath))
            {
                throw new FileNotFoundException(
                    "Файл профиля не найден.",
                    profilePath
                );
            }

            var profileFile = new FileInfo(profilePath);

            if (profileFile.Length >
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

            string runtimeJson =
                JsonProfileImportService.NormalizeConfig(
                    source,
                    localPort
                );

            Directory.CreateDirectory(RuntimeDirectory);

            string runtimePath = Path.Combine(
                RuntimeDirectory,
                $"runtime-{Guid.NewGuid():N}.json"
            );

            await AtomicFileWriter.WriteAllTextAsync(
                runtimePath,
                runtimeJson,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false
                ),
                cancellationToken
            );

            return runtimePath;
        }

        // --------------------------------------------------
        // Проверка порта
        // --------------------------------------------------

        private static bool IsPortAvailable(
            int port)
        {
            TcpListener? listener = null;

            try
            {
                listener = new TcpListener(
                    IPAddress.Loopback,
                    port
                );

                listener.Server.ExclusiveAddressUse =
                    true;

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

        // --------------------------------------------------
        // Остановка
        // --------------------------------------------------

        private async void StopButton_Click(
            object? sender,
            EventArgs e)
        {
            if (operationInProgress)
            {
                return;
            }

            BeginOperation(
                ApplicationOperation.StopConnection
            );
            SetBusyState();

            statusLabel.Text =
                "Статус: Остановка...";

            AddLog(
                "Остановка ядра sing-box..."
            );

            try
            {
                RestoreSystemProxyIfNeeded();

                await coreManager.StopAsync();

                statusLabel.Text =
                    "Статус: Остановлено";

                AddLog("Ядро остановлено.");
            }
            catch (Exception ex)
            {
                statusLabel.Text =
                    "Статус: Ошибка остановки";

                AddLog(
                    "Ошибка остановки: " +
                    ex.Message
                );
            }
            finally
            {
                EndOperation();

                if (!coreManager.IsRunning)
                {
                    TryDeleteRuntimeConfig();
                }

                if (!isClosing)
                {
                    if (coreManager.IsRunning)
                    {
                        SetRunningState();
                    }
                    else
                    {
                        SetIdleState();
                    }
                }
            }
        }

        private void RestoreSystemProxyIfNeeded()
        {
            if (!systemProxyService.IsEnabledByApplication &&
                !systemProxyService.HasPendingBackup)
            {
                return;
            }

            ProxyRestoreOutcome outcome =
                systemProxyService.Restore();

            if (outcome == ProxyRestoreOutcome.Restored)
            {
                AddLog(
                    "Прежние системные настройки " +
                    "прокси восстановлены."
                );
            }
            else if (outcome ==
                ProxyRestoreOutcome
                    .CurrentSettingsPreserved)
            {
                AddLog(
                    "Системный прокси был изменён другой " +
                    "программой. Текущие настройки сохранены."
                );
            }
        }

        // --------------------------------------------------
        // Состояния интерфейса
        // --------------------------------------------------

        private void SetBusyState()
        {
            startButton.Enabled = false;
            stopButton.Enabled = false;

            profileComboBox.Enabled = false;
            portNumericUpDown.Enabled = false;
            systemProxyCheckBox.Enabled = false;

            importButton.Enabled = false;
            deleteProfileButton.Enabled = false;

            UpdateTrayState(busy: true);
        }

        private void SetRunningState()
        {
            startButton.Enabled = false;
            stopButton.Enabled = true;

            profileComboBox.Enabled = false;
            portNumericUpDown.Enabled = false;
            systemProxyCheckBox.Enabled = false;

            importButton.Enabled = false;
            deleteProfileButton.Enabled = false;

            UpdateTrayState();
        }

        private void SetIdleState()
        {
            bool hasProfiles =
                profileComboBox.Items.Count > 0;

            startButton.Enabled = hasProfiles;
            stopButton.Enabled = false;

            profileComboBox.Enabled = true;
            portNumericUpDown.Enabled = true;
            systemProxyCheckBox.Enabled = true;

            importButton.Enabled = true;

            deleteProfileButton.Enabled =
                hasProfiles;

            UpdateTrayState();
        }

        private CancellationToken BeginOperation(
            ApplicationOperation operation)
        {
            return operationCoordinator.Begin(operation);
        }

        private void EndOperation()
        {
            operationCoordinator.End();
        }

        // --------------------------------------------------
        // События ядра
        // --------------------------------------------------

        private void CoreManager_OutputReceived(
            string line)
        {
            AddLog(
                $"[sing-box] {line}"
            );
        }

        private void CoreManager_ProcessExited(
            int exitCode)
        {
            TryBeginInvoke(() =>
            {
                AddLog(
                    "Процесс sing-box завершён. " +
                    $"Код: {exitCode}"
                );

                try
                {
                    RestoreSystemProxyIfNeeded();
                }
                catch (Exception ex)
                {
                    AddLog(
                        "Не удалось восстановить " +
                        "системный прокси: " +
                        ex.Message
                    );

                    MessageBox.Show(
                        "Ядро завершилось, но прежние " +
                        "настройки системного прокси " +
                        "не удалось восстановить.\n\n" +
                        ex.Message,
                        "GeniaProxy",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }

                if (operationInProgress)
                {
                    return;
                }

                TryDeleteRuntimeConfig();

                statusLabel.Text =
                    "Статус: Остановлено";

                SetIdleState();
            });
        }

        // --------------------------------------------------
        // Журнал
        // --------------------------------------------------

        private void AppendCoreOutput(
            string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string normalized =
                text.Replace("\r\n", "\n");

            foreach (string line
                     in normalized.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    AddLog(
                        $"[sing-box] {line.TrimEnd()}"
                    );
                }
            }
        }

        private void AddLog(string message)
        {
            if (isClosing || IsDisposed)
            {
                return;
            }

            string safeMessage =
                TerminalOutputSanitizer.Sanitize(message)
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();

            if (string.IsNullOrWhiteSpace(safeMessage))
            {
                return;
            }

            string time =
                DateTime.Now.ToString(
                    "HH:mm:ss",
                    CultureInfo.InvariantCulture
                );

            pendingLogLines.Enqueue(
                $"[{time}] {safeMessage}" +
                Environment.NewLine
            );

            int pendingCount =
                Interlocked.Increment(
                    ref pendingLogLineCount
                );

            while (pendingCount >
                       MaxPendingLogLines &&
                   pendingLogLines.TryDequeue(out _))
            {
                pendingCount =
                    Interlocked.Decrement(
                        ref pendingLogLineCount
                    );
            }

            ScheduleLogFlush();
        }

        private void ScheduleLogFlush()
        {
            if (Interlocked.CompareExchange(
                    ref logFlushScheduled,
                    1,
                    0) != 0)
            {
                return;
            }

            _ = FlushPendingLogAfterDelayAsync();
        }

        private async Task FlushPendingLogAfterDelayAsync()
        {
            try
            {
                await Task.Delay(
                    LogFlushDelayMilliseconds
                );

                if (isClosing ||
                    IsDisposed ||
                    !IsHandleCreated)
                {
                    Interlocked.Exchange(
                        ref logFlushScheduled,
                        0
                    );

                    return;
                }

                BeginInvoke(
                    new Action(FlushPendingLog)
                );
            }
            catch (ObjectDisposedException)
            {
                Interlocked.Exchange(
                    ref logFlushScheduled,
                    0
                );
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(
                    ref logFlushScheduled,
                    0
                );
            }
            catch
            {
                Interlocked.Exchange(
                    ref logFlushScheduled,
                    0
                );
            }
        }

        private void FlushPendingLog()
        {
            if (isClosing ||
                IsDisposed ||
                logTextBox.IsDisposed)
            {
                ClearPendingLogQueue();

                Interlocked.Exchange(
                    ref logFlushScheduled,
                    0
                );

                return;
            }

            var batch = new StringBuilder();

            int lineCount = 0;

            while (lineCount < MaxLogBatchLines &&
                   pendingLogLines.TryDequeue(
                       out string? line))
            {
                batch.Append(line);
                lineCount++;

                Interlocked.Decrement(
                    ref pendingLogLineCount
                );
            }

            if (batch.Length > 0)
            {
                logTextBox.AppendText(
                    batch.ToString()
                );

                TrimLogIfNeeded();

                if (logTextBox.Visible)
                {
                    logTextBox.SelectionStart =
                        logTextBox.TextLength;

                    logTextBox.SelectionLength = 0;
                    logTextBox.ScrollToCaret();
                }
            }

            Interlocked.Exchange(
                ref logFlushScheduled,
                0
            );

            if (!pendingLogLines.IsEmpty)
            {
                ScheduleLogFlush();
            }
        }

        private void TrimLogIfNeeded()
        {
            if (logTextBox.TextLength <=
                MaxLogCharacters)
            {
                return;
            }

            int charactersToRemove =
                logTextBox.TextLength -
                LogTrimTarget;

            string currentText =
                logTextBox.Text;

            int nextLineBreak =
                currentText.IndexOf(
                    '\n',
                    charactersToRemove
                );

            if (nextLineBreak >= 0)
            {
                charactersToRemove =
                    nextLineBreak + 1;
            }

            logTextBox.Select(
                0,
                charactersToRemove
            );

            logTextBox.SelectedText =
                string.Empty;
        }

        private void ClearLog()
        {
            ClearPendingLogQueue();
            logTextBox.Clear();
        }

        private void ClearPendingLogQueue()
        {
            while (pendingLogLines.TryDequeue(out _))
            {
                Interlocked.Decrement(
                    ref pendingLogLineCount
                );
            }
        }

        private void TryBeginInvoke(Action action)
        {
            if (isClosing ||
                IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(action);
            }
            catch (ObjectDisposedException)
            {
                // Форма уже закрыта.
            }
            catch (InvalidOperationException)
            {
                // Дескриптор формы уже уничтожается.
            }
        }

        private void TryDeleteRuntimeConfig()
        {
            string? runtimePath = Interlocked.Exchange(
                ref activeRuntimeConfigPath,
                null
            );

            if (string.IsNullOrWhiteSpace(runtimePath))
            {
                return;
            }

            try
            {
                if (File.Exists(runtimePath))
                {
                    File.Delete(runtimePath);
                }
            }
            catch (Exception ex)
            {
                activeRuntimeConfigPath = runtimePath;

                if (!isClosing)
                {
                    AddLog(
                        "Не удалось удалить временную " +
                        "конфигурацию: " + ex.Message
                    );
                }
            }
        }

        private void TryDeleteStaleRuntimeConfigs()
        {
            TryDeleteRuntimeConfig();

            TryDeleteFileSilently(
                LegacyRuntimeConfigPath
            );

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
                    string fileName = Path.GetFileName(path);

                    bool isRuntimeConfig =
                        fileName.StartsWith(
                            "runtime-",
                            StringComparison.OrdinalIgnoreCase) &&
                        fileName.EndsWith(
                            ".json",
                            StringComparison.OrdinalIgnoreCase);

                    bool isAtomicTemporaryFile =
                        fileName.StartsWith(
                            ".runtime-",
                            StringComparison.OrdinalIgnoreCase) &&
                        fileName.EndsWith(
                            ".tmp",
                            StringComparison.OrdinalIgnoreCase);

                    if (isRuntimeConfig ||
                        isAtomicTemporaryFile)
                    {
                        TryDeleteFileSilently(path);
                    }
                }

                if (Directory.Exists(ProfilesDirectory))
                {
                    foreach (string path in Directory.GetFiles(
                                 ProfilesDirectory,
                                 ".*.json.*.tmp",
                                 SearchOption.TopDirectoryOnly))
                    {
                        TryDeleteFileSilently(path);
                    }
                }

                if (!Directory.EnumerateFileSystemEntries(
                        RuntimeDirectory).Any())
                {
                    Directory.Delete(RuntimeDirectory);
                }
            }
            catch (Exception ex)
            {
                if (!isClosing)
                {
                    AddLog(
                        "Не удалось полностью очистить " +
                        "временные конфигурации: " +
                        ex.Message
                    );
                }
            }
        }

        private static void TryDeleteFileSilently(
            string path)
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
                // Повторная очистка будет выполнена при закрытии
                // или при следующем запуске приложения.
            }
        }

        // --------------------------------------------------
        // Закрытие программы
        // --------------------------------------------------

        private void Form1_FormClosing(
            object? sender,
            FormClosingEventArgs e)
        {
            if (isClosing)
            {
                return;
            }

            try
            {
                SaveSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось сохранить настройки " +
                    "GeniaProxy.\n\n" +
                    ex.Message +
                    "\n\nПрограмма продолжит закрытие.",
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }

            while (true)
            {
                try
                {
                    RestoreSystemProxyIfNeeded();
                    break;
                }
                catch (Exception ex)
                {
                    DialogResult choice =
                        MessageBox.Show(
                            "Не удалось восстановить прежние " +
                            "настройки системного прокси.\n\n" +
                            ex.Message +
                            "\n\nПрервать — оставить программу " +
                            "открытой.\nПовтор — попробовать ещё " +
                            "раз.\nПропустить — закрыть программу " +
                            "без восстановления прокси.",
                            "GeniaProxy",
                            MessageBoxButtons
                                .AbortRetryIgnore,
                            MessageBoxIcon.Error,
                            MessageBoxDefaultButton.Button1
                        );

                    if (choice == DialogResult.Retry)
                    {
                        continue;
                    }

                    if (choice == DialogResult.Abort)
                    {
                        e.Cancel = true;
                        return;
                    }

                    break;
                }
            }

            isClosing = true;

            operationCoordinator.Cancel();

            coreManager.OutputReceived -=
                CoreManager_OutputReceived;

            coreManager.ProcessExited -=
                CoreManager_ProcessExited;

            coreManager.Dispose();

            operationCoordinator.Dispose();

            TryDeleteStaleRuntimeConfigs();
            ClearPendingLogQueue();

            if (trayIcon is not null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }

            trayMenu?.Dispose();
            trayMenu = null;
            trayToggleItem = null;

            logTextBox.ContextMenuStrip = null;

            logMenu?.Dispose();
            logMenu = null;

            profileComboBox.ContextMenuStrip = null;

            profileMenu?.Dispose();
            profileMenu = null;
        }

        // Оставь этот метод, если событие Paint
        // подключено в Form1.Designer.cs.
        private void statusLayout_Paint(
            object sender,
            PaintEventArgs e)
        {
        }

        public void ActivateFromSecondInstance()
        {
            if (isClosing || IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                TryBeginInvoke(
                    ActivateFromSecondInstance
                );

                return;
            }

            RestoreFromTray();

            TopMost = true;
            TopMost = false;

            Focus();
        }

    }
}
