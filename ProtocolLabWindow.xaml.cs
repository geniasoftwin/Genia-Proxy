using GeniaProxy.Services;
using System.Windows;
using System.Windows.Controls;

namespace GeniaProxy
{
    public sealed partial class ProtocolLabWindow : Window
    {
        public ProtocolLabWindow()
        {
            InitializeComponent();

            CapabilityList.ItemsSource =
                ProtocolLabUiModelService.GetCapabilities();

            SelectedCapabilityText.Text =
                "Ничего — Lab выключен";
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

            SelectedCapabilityText.Text =
                $"{ProtocolLabUiModelService.GetCapabilities()
                    .First(item =>
                        item.Id.Equals(
                            capability.Id,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    .DisplayName} · {capability.EngineFamily} · " +
                $"{capability.SupportState}";

            // Selection is intentionally non-operational in the first
            // Alpha 3 UI milestone. A dedicated isolated session runner
            // will own start/stop in a later commit.
            LabStartButton.IsEnabled = false;
        }
    }
}
