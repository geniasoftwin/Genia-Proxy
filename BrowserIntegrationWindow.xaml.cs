using GeniaProxy.Services;
using System.Diagnostics;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace GeniaProxy
{
    public sealed partial class BrowserIntegrationWindow : Window
    {
        private readonly BrowserIntegrationService service;

        public BrowserIntegrationWindow(
            BrowserIntegrationService service)
        {
            ArgumentNullException.ThrowIfNull(service);
            this.service = service;
            InitializeComponent();
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            BrowserIntegrationStatus status = service.GetStatus();

            StatusText.Text = status.Summary;
            StatusDetailsText.Text =
                $"Transport: встроенный loopback HTTP · NativeHost: отсутствует\n" +
                $"Endpoint: {status.DirectBridgeEndpoint}\n" +
                $"Switcher folder: {(status.SwitcherReady ? "ready" : "missing/unsupported")}\n" +
                $"Multi-profile: каждый профиль Chrome подключается напрямую к GeniaProxy.exe";
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            RefreshStatus();
        }

        private void OpenSwitcherFolderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                string directory = BrowserIntegrationService.GetSwitcherFolder();
                OpenExplorer(directory);
                RefreshStatus();
            }
            catch (Exception ex)
            {
                ShowError(
                    "Не удалось открыть папку Switcher.\n\n" + ex.Message
                );
            }
        }

        private static void OpenExplorer(string directory)
        {
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(directory);
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    ArgumentList = { directory },
                    UseShellExecute = true
                }
            );
        }

        private void ShowError(string message)
        {
            WpfMessageBox.Show(
                this,
                message,
                "GeniaProxy",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }
}
