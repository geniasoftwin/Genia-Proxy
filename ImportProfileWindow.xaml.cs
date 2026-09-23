using GeniaProxy.Services;
using System.Text;
using System.Windows;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace GeniaProxy
{
    public sealed partial class ImportProfileWindow : Window
    {
        public string ProfileName =>
            ProfileNameTextBox.Text.Trim();

        public string SourceText =>
            SourceTextBox.Text.Trim();

        public ImportProfileWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (WpfClipboard.ContainsText(
                        WpfTextDataFormat.UnicodeText))
                {
                    string clipboardText = WpfClipboard.GetText(
                        WpfTextDataFormat.UnicodeText
                    ).Trim();

                    if (!string.IsNullOrWhiteSpace(clipboardText))
                    {
                        SourceTextBox.Text = clipboardText;
                        TrySetProfileNameFromLink(clipboardText);
                    }
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    this,
                    "Не удалось прочитать буфер обмена.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }

            ProfileNameTextBox.SelectAll();
            ProfileNameTextBox.Focus();
        }

        private void LoadJsonButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "Загрузить JSON-профиль sing-box или Xray",
                Filter =
                    "JSON-профиль (*.json)|*.json|" +
                    "Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var file = new FileInfo(dialog.FileName);

                if (file.Length >
                    JsonProfileImportService.MaxProfileBytes)
                {
                    throw new InvalidDataException(
                        "JSON-профиль превышает допустимый размер 1 МБ."
                    );
                }

                SourceTextBox.Text = File.ReadAllText(
                    dialog.FileName,
                    Encoding.UTF8
                );

                ProfileNameTextBox.Text =
                    Path.GetFileNameWithoutExtension(dialog.FileName);
                ProfileNameTextBox.SelectAll();
                ProfileNameTextBox.Focus();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    this,
                    "Не удалось загрузить JSON-профиль.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void ImportButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string normalizedName = ProfileNameValidator.Normalize(
                ProfileNameTextBox.Text
            );

            string? validationError = ProfileNameValidator
                .GetValidationError(normalizedName);

            if (validationError is not null)
            {
                WpfMessageBox.Show(
                    this,
                    validationError,
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                ProfileNameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(SourceTextBox.Text))
            {
                WpfMessageBox.Show(
                    this,
                    "Вставьте ссылку Hysteria2, VLESS + XHTTP + TLS/REALITY или VLESS + REALITY + Vision + RAW/TCP " +
                    "или загрузите JSON-профиль.",
                    "GeniaProxy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                SourceTextBox.Focus();
                return;
            }

            ProfileNameTextBox.Text = normalizedName;
            DialogResult = true;
        }

        private void TrySetProfileNameFromLink(string source)
        {
            if (!source.Contains("://", StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var uri = new Uri(source);
                string fragment = Uri.UnescapeDataString(
                    uri.Fragment.TrimStart('#')
                );

                if (!string.IsNullOrWhiteSpace(fragment))
                {
                    ProfileNameTextBox.Text = fragment;
                }
            }
            catch
            {
                // Пользователь может задать название вручную.
            }
        }
    }
}
