using GeniaProxy.Models;
using System.Globalization;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace GeniaProxy
{
    public sealed partial class SettingsWindow : Window
    {
        public bool AutoConnect =>
            AutoConnectCheckBox.IsChecked == true;

        public bool StartWithWindows =>
            StartWithWindowsCheckBox.IsChecked == true;

        public bool ReconnectOnFailure =>
            ReconnectCheckBox.IsChecked == true;

        public bool CollapseLog =>
            CollapseLogCheckBox.IsChecked == true;

        public int ReconnectDelaySeconds { get; private set; }

        public CorePreference CorePreference =>
            CorePreferenceComboBox.SelectedIndex switch
            {
                1 => GeniaProxy.Models.CorePreference.SingBox,
                2 => GeniaProxy.Models.CorePreference.Xray,
                _ => GeniaProxy.Models.CorePreference.Automatic
            };

        public TunStackPreference TunStackPreference =>
            TunStackComboBox.SelectedIndex switch
            {
                1 => GeniaProxy.Models.TunStackPreference.System,
                2 => GeniaProxy.Models.TunStackPreference.GVisor,
                _ => GeniaProxy.Models.TunStackPreference.Mixed
            };

        public SettingsWindow(
            AppSettings settings,
            ProfileRuntimeSettings? profileSettings = null)
        {
            ArgumentNullException.ThrowIfNull(settings);
            InitializeComponent();

            AutoConnectCheckBox.IsChecked = settings.AutoConnect;
            StartWithWindowsCheckBox.IsChecked =
                settings.StartWithWindows;
            ReconnectCheckBox.IsChecked = settings.ReconnectOnFailure;
            CollapseLogCheckBox.IsChecked = settings.LogCollapsed;
            ReconnectDelayTextBox.Text = settings
                .ReconnectDelaySeconds
                .ToString(CultureInfo.InvariantCulture);

            CorePreference effectiveCore = profileSettings?.CorePreference
                ?? settings.CorePreference;
            TunStackPreference effectiveTunStack =
                profileSettings?.TunStackPreference
                ?? settings.TunStackPreference;

            CorePreferenceComboBox.SelectedIndex =
                effectiveCore switch
                {
                    GeniaProxy.Models.CorePreference.SingBox => 1,
                    GeniaProxy.Models.CorePreference.Xray => 2,
                    _ => 0
                };

            TunStackComboBox.SelectedIndex = effectiveTunStack switch
            {
                GeniaProxy.Models.TunStackPreference.System => 1,
                GeniaProxy.Models.TunStackPreference.GVisor => 2,
                _ => 0
            };

            UpdateReconnectDelayState();
        }

        private void ReconnectCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
        {
            UpdateReconnectDelayState();
        }

        private void UpdateReconnectDelayState()
        {
            if (ReconnectDelayTextBox is not null)
            {
                ReconnectDelayTextBox.IsEnabled =
                    ReconnectOnFailure;
            }
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!int.TryParse(
                    ReconnectDelayTextBox.Text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int delay) ||
                delay is < 1 or > 60)
            {
                WpfMessageBox.Show(
                    this,
                    "Укажите задержку переподключения от 1 до 60 секунд.",
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                ReconnectDelayTextBox.Focus();
                ReconnectDelayTextBox.SelectAll();
                return;
            }

            ReconnectDelaySeconds = delay;
            DialogResult = true;
        }
    }
}
