using System.Globalization;
using GeniaProxy.Services;

namespace GeniaProxy
{
    public sealed class CoreManagementForm : Form
    {
        private readonly CoreMaintenanceService service;
        private readonly Func<bool> isCoreRunning;
        private readonly TextBox informationTextBox;
        private readonly Button updateButton;
        private readonly Button rollbackButton;

        public CoreManagementForm(
            CoreMaintenanceService service,
            Func<bool> isCoreRunning)
        {
            ApplicationBrandingService.ApplyIcon(this);

            this.service = service;
            this.isCoreRunning = isCoreRunning;

            Text = "Ядро " + service.DisplayName;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(690, 390);

            informationTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9.5F)
            };

            updateButton = new Button
            {
                Text = "Обновить ядро...",
                Size = new Size(145, 32)
            };

            rollbackButton = new Button
            {
                Text = "Откатить",
                Size = new Size(105, 32)
            };

            var refreshButton = new Button
            {
                Text = "Обновить сведения",
                Size = new Size(145, 32)
            };

            var closeButton = new Button
            {
                Text = "Закрыть",
                DialogResult = DialogResult.OK,
                Size = new Size(95, 32)
            };

            ApplyStrictButtonStyle(updateButton);
            ApplyStrictButtonStyle(rollbackButton);
            ApplyStrictButtonStyle(refreshButton);
            ApplyStrictButtonStyle(closeButton);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                Padding = new Padding(8),
                WrapContents = false
            };

            buttons.Controls.Add(updateButton);
            buttons.Controls.Add(rollbackButton);
            buttons.Controls.Add(refreshButton);
            buttons.Controls.Add(closeButton);

            Controls.Add(informationTextBox);
            Controls.Add(buttons);
            AcceptButton = closeButton;

            Shown += async (_, _) =>
                await RefreshInformationAsync();

            refreshButton.Click += async (_, _) =>
                await RefreshInformationAsync();

            updateButton.Click += async (_, _) =>
                await UpdateCoreAsync();

            rollbackButton.Click += async (_, _) =>
                await RollbackCoreAsync();
        }

        private async Task RefreshInformationAsync()
        {
            SetBusy(true);

            try
            {
                CoreInformation information =
                    await service.GetInformationAsync();

                informationTextBox.Text = FormatInformation(
                    information
                );

                rollbackButton.Enabled =
                    information.BackupAvailable &&
                    !isCoreRunning();
            }
            catch (Exception ex)
            {
                informationTextBox.Text =
                    "Ошибка получения сведений:" +
                    Environment.NewLine + ex.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task UpdateCoreAsync()
        {
            if (!EnsureMaintenanceRunsUnelevated())
            {
                return;
            }

            if (!EnsureCoreStopped())
            {
                return;
            }

            using var dialog = new OpenFileDialog
            {
                Title = "Выберите новый " +
                    Path.GetFileName(service.ExecutablePath),
                Filter =
                    Path.GetFileName(service.ExecutablePath) + "|" +
                    Path.GetFileName(service.ExecutablePath) + "|" +
                    "Приложения (*.exe)|*.exe",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                "Выбранный EXE-файл будет запущен для проверки " +
                "версии. Используйте только официальное ядро " +
                service.DisplayName + ", " +
                "полученный из доверенного источника.\n\n" +
                "Текущее проверенное ядро будет сохранено как " +
                "резервная копия. Продолжить?",
                "GeniaProxy",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2
            );

            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            SetBusy(true);

            try
            {
                CoreInformation previousInformation =
                    await service.GetInformationAsync();

                CoreInformation information =
                    await service.UpdateAsync(dialog.FileName);

                informationTextBox.Text = FormatInformation(
                    information
                );

                string previousVersion =
                    previousInformation.Exists
                        ? previousInformation.Version
                        : "не установлено";

                MessageBox.Show(
                    "Ядро " + service.DisplayName + " обновлено.\n\n" +
                    previousVersion +
                    "\n→\n" +
                    information.Version,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ядро не было заменено.\n\n" + ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                SetBusy(false);
                await RefreshInformationAsync();
            }
        }

        private async Task RollbackCoreAsync()
        {
            if (!EnsureMaintenanceRunsUnelevated())
            {
                return;
            }

            if (!EnsureCoreStopped())
            {
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                "Вернуть предыдущее ядро " + service.DisplayName + "?",
                "GeniaProxy",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2
            );

            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            SetBusy(true);

            try
            {
                CoreInformation information =
                    await service.RollbackAsync();

                informationTextBox.Text = FormatInformation(
                    information
                );

                MessageBox.Show(
                    "Предыдущее ядро восстановлено.",
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось выполнить откат.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                SetBusy(false);
                await RefreshInformationAsync();
            }
        }

        private static bool EnsureMaintenanceRunsUnelevated()
        {
            if (!WindowsElevationService.IsAdministrator)
            {
                return true;
            }

            MessageBox.Show(
                "Обновление и откат ядра отключены в режиме ADMIN.\n\n" +
                "Закройте elevated-копию GeniaProxy, запустите программу " +
                "обычно (USER) и повторите операцию. Это предотвращает " +
                "запуск выбранного или резервного EXE с повышенными " +
                "правами до проверки версии и хэша.",
                "GeniaProxy",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );

            return false;
        }

        private bool EnsureCoreStopped()
        {
            if (!isCoreRunning())
            {
                return true;
            }

            MessageBox.Show(
                "Сначала остановите прокси.",
                "GeniaProxy",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );

            return false;
        }

        private void SetBusy(bool busy)
        {
            UseWaitCursor = busy;
            updateButton.Enabled = !busy && !isCoreRunning();
            rollbackButton.Enabled =
                !busy && service.HasBackup && !isCoreRunning();
        }

        private static string FormatInformation(
            CoreInformation information)
        {
            string hashStatus;

            if (string.IsNullOrWhiteSpace(
                    information.ExpectedSha256))
            {
                hashStatus = "контрольная сумма отсутствует";
            }
            else
            {
                hashStatus = information.HashMatches
                    ? "совпадает"
                    : "НЕ СОВПАДАЕТ";
            }

            return string.Join(
                Environment.NewLine,
                $"Файл: {(information.Exists ? "найден" : "не найден")}",
                $"Версия: {information.Version}",
                $"Размер: {information.SizeBytes.ToString(
                    "N0",
                    CultureInfo.InvariantCulture)} байт",
                $"Изменён: {information.LastWriteTime?.ToString(
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture) ?? "—"}",
                string.Empty,
                $"SHA-256: {information.Sha256}",
                $"Ожидается: {information.ExpectedSha256}",
                $"Проверка: {hashStatus}",
                string.Empty,
                $"Резервная копия: " +
                $"{(information.BackupAvailable ? "есть" : "нет")}"
            );
        }
        private static void ApplyStrictButtonStyle(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
        }

    }
}
