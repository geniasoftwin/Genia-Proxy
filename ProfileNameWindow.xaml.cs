using GeniaProxy.Services;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace GeniaProxy
{
    public sealed partial class ProfileNameWindow : Window
    {
        public string ProfileName =>
            ProfileNameTextBox.Text.Trim();

        public ProfileNameWindow(string currentName)
        {
            InitializeComponent();
            ProfileNameTextBox.Text = currentName;

            Loaded += (_, _) =>
            {
                ProfileNameTextBox.SelectAll();
                ProfileNameTextBox.Focus();
            };
        }

        private void RenameButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string normalizedName = ProfileNameValidator.Normalize(
                ProfileNameTextBox.Text
            );

            string? error = ProfileNameValidator.GetValidationError(
                normalizedName
            );

            if (error is not null)
            {
                WpfMessageBox.Show(
                    this,
                    error,
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            ProfileNameTextBox.Text = normalizedName;
            DialogResult = true;
        }
    }
}
