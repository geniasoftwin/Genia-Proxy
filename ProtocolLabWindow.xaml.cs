using GeniaProxy.Services;
using System.Windows;

namespace GeniaProxy
{
    public sealed partial class ProtocolLabWindow : Window
    {
        private readonly ProtocolLabSession labSession;
        private readonly IReadOnlyList<ProtocolLabUiCapability>
            capabilities;

        private string? selectedCapabilityId;
        private bool experimentalWarningAccepted;
        private bool operationBusy;

        public ProtocolLabWindow(
            Func<bool>? stableSessionIsRunning = null)
        {
            InitializeComponent();

            capabilities =
                ProtocolLabUiModelService.GetCapabilities();

            CapabilityList.ItemsSource = capabilities;

            labSession = new ProtocolLabSession(
                stableSessionIsRunning
            );

            labSession.LogReceived += LabSession_LogReceived;
            labSession.StateChanged += LabSession_StateChanged;
            labSession.UnexpectedExit += LabSession_UnexpectedExit;

            SelectedCapabilityText.Text =
                "Ничего — Lab выключен";

            RefreshControls();
        }

        private void Capability_Checked(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.RadioButton radio ||
                radio.Tag is not string capabilityId)
            {
                return;
            }

            FeatureCapability capability =
                ProtocolLabFeatureCatalog.RequireSelectable(
                    capabilityId
                );

            ProtocolLabUiCapability uiCapability =
                capabilities.First(item =>
                    item.Id.Equals(
                        capability.Id,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            selectedCapabilityId = capability.Id;

            SelectedCapabilityText.Text =
                $"{uiCapability.DisplayName} · " +
                $"{uiCapability.EngineLabel} · " +
                $"{uiCapability.SupportLabel}";

            SecretLabel.Text =
                capability.Id.Equals(
                    "snell",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "Snell v6 PSK"
                    : "Пароль";

            TuicUuidPanel.Visibility =
                capability.Id.Equals(
                    "tuic",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            TlsServerNamePanel.Visibility =
                capability.Id.Equals(
                    "snell",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            LabStatusText.Text =
                "Готов к изолированному запуску";

            RefreshControls();
        }

        private async void LabStartButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (operationBusy)
            {
                return;
            }

            operationBusy = true;
            RefreshControls();

            try
            {
                if (labSession.IsRunning)
                {
                    await labSession.StopAsync();
                    return;
                }

                string capabilityId =
                    selectedCapabilityId
                    ?? throw new InvalidOperationException(
                        "Сначала выберите RuntimeVerified capability."
                    );

                if (!experimentalWarningAccepted)
                {
                    MessageBoxResult answer =
                        System.Windows.MessageBox.Show(
                            this,
                            "Protocol Lab — экспериментальный режим.\n\n" +
                            "Он запускает только локальный loopback proxy, " +
                            "не включает системный proxy и не должен " +
                            "изменять Windows routes/DNS. Stable-сессия " +
                            "GeniaProxy во время запуска Lab должна быть " +
                            "остановлена.\n\nПродолжить?",
                            "GeniaProxy Protocol Lab",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning,
                            MessageBoxResult.No
                        );

                    if (answer != MessageBoxResult.Yes)
                    {
                        LabStatusText.Text =
                            "Запуск отменён пользователем";
                        return;
                    }

                    experimentalWarningAccepted = true;
                }

                int serverPort = ParsePort(
                    ServerPortTextBox.Text,
                    "Порт сервера"
                );

                int localPort = ParsePort(
                    LocalPortTextBox.Text,
                    "Локальный порт"
                );

                var input = new ProtocolLabUiConnectionInput(
                    capabilityId,
                    ServerTextBox.Text,
                    serverPort,
                    localPort,
                    SecretPasswordBox.Password,
                    TuicUuidTextBox.Text,
                    TlsServerNameTextBox.Text
                );

                // Credentials remain in-memory here. The runner writes
                // only a short-lived OS-temp config after the isolation
                // gate succeeds and removes it once the listener is ready.
                string configJson =
                    ProtocolLabUiConfigService.CreateConfig(input);

                LabStatusText.Text =
                    "Проверка конфигурации и запуск...";

                await labSession.StartAsync(
                    capabilityId,
                    configJson,
                    localPort
                );
            }
            catch (Exception ex)
            {
                string safeMessage =
                    TerminalOutputSanitizer.Sanitize(
                        ex.Message
                    );

                LabStatusText.Text =
                    string.IsNullOrWhiteSpace(safeMessage)
                        ? "Protocol Lab: ошибка запуска."
                        : safeMessage;
            }
            finally
            {
                operationBusy = false;
                RefreshControls();
            }
        }

        private void LabSession_LogReceived(string message)
        {
            DispatchUi(() =>
            {
                string safe =
                    TerminalOutputSanitizer.Sanitize(message);

                if (!string.IsNullOrWhiteSpace(safe))
                {
                    LabStatusText.Text = safe;
                }
            });
        }

        private void LabSession_StateChanged(
            ProtocolLabSessionState state)
        {
            DispatchUi(RefreshControls);
        }

        private void LabSession_UnexpectedExit(int exitCode)
        {
            DispatchUi(() =>
            {
                LabStatusText.Text =
                    "Protocol Lab process завершился аварийно " +
                    $"(код {exitCode}).";
                RefreshControls();
            });
        }

        private void RefreshControls()
        {
            bool transition =
                operationBusy ||
                labSession.State is
                    ProtocolLabSessionState.Starting or
                    ProtocolLabSessionState.Stopping;

            bool running = labSession.IsRunning;

            CapabilityList.IsEnabled =
                !transition && !running;

            ConnectionInputsPanel.IsEnabled =
                !transition &&
                !running &&
                selectedCapabilityId is not null;

            LabStartButton.IsEnabled =
                !transition &&
                (running || selectedCapabilityId is not null);

            LabStartButton.Content =
                running
                    ? "ОСТАНОВИТЬ LAB"
                    : "ЗАПУСТИТЬ LAB";

            if (running)
            {
                LabStatusText.Text =
                    $"Работает · {labSession.ActiveCapabilityId}";
                LabEndpointText.Text =
                    $"loopback · 127.0.0.1:{labSession.ActivePort}";
            }
            else
            {
                LabEndpointText.Text =
                    "loopback не активен";

                if (!transition &&
                    labSession.State ==
                        ProtocolLabSessionState.Stopped)
                {
                    LabStatusText.Text = selectedCapabilityId is null
                        ? "Остановлено"
                        : "Готов к изолированному запуску";
                }
            }
        }

        private static int ParsePort(
            string text,
            string fieldName)
        {
            if (!int.TryParse(text, out int value) ||
                value is < 1024 or > 65535)
            {
                throw new FormatException(
                    $"{fieldName} должен быть от 1024 до 65535."
                );
            }

            return value;
        }

        private void DispatchUi(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _ = Dispatcher.BeginInvoke(action);
            }
        }

        private void Window_Closed(
            object? sender,
            EventArgs e)
        {
            SecretPasswordBox.Password = string.Empty;
            TuicUuidTextBox.Text = string.Empty;

            labSession.LogReceived -= LabSession_LogReceived;
            labSession.StateChanged -= LabSession_StateChanged;
            labSession.UnexpectedExit -= LabSession_UnexpectedExit;

            labSession.Dispose();
        }
    }
}
