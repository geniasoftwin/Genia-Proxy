using GeniaProxy.Services;

namespace GeniaProxy
{
    public partial class ImportProfileForm : Form
    {
        public string ProfileName =>
            profileNameTextBox.Text.Trim();

        public string SourceText =>
            sourceTextBox.Text.Trim();

        public ImportProfileForm()
        {
            InitializeComponent();
            ApplicationBrandingService.ApplyIcon(this);

            Load += ImportProfileForm_Load;
            saveButton.Click += SaveButton_Click;
        }

        private void ImportProfileForm_Load(
            object? sender,
            EventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string clipboardText =
                        Clipboard.GetText(
                            TextDataFormat.UnicodeText
                        ).Trim();

                    if (!string.IsNullOrWhiteSpace(
                        clipboardText))
                    {
                        sourceTextBox.Text =
                            clipboardText;

                        TrySetProfileName(
                            clipboardText
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось прочитать буфер обмена.\n\n" +
                    ex.Message,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }

            profileNameTextBox.SelectAll();
            profileNameTextBox.Focus();
        }

        private void TrySetProfileName(
            string source)
        {
            if (!source.Contains(
                "://",
                StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var uri = new Uri(source);

                string fragment =
                    Uri.UnescapeDataString(
                        uri.Fragment.TrimStart('#')
                    );

                if (!string.IsNullOrWhiteSpace(
                    fragment))
                {
                    profileNameTextBox.Text =
                        fragment;
                }
            }
            catch
            {
                // Название пользователь введёт сам.
            }
        }

        private void SaveButton_Click(
            object? sender,
            EventArgs e)
        {
            string profileName =
                ProfileNameValidator.Normalize(
                    profileNameTextBox.Text
                );

            string source =
                sourceTextBox.Text.Trim();

            string? validationError =
                ProfileNameValidator
                    .GetValidationError(profileName);

            if (validationError is not null)
            {
                MessageBox.Show(
                    validationError,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                profileNameTextBox.Focus();
                return;
            }

            profileNameTextBox.Text = profileName;

            if (string.IsNullOrWhiteSpace(source))
            {
                MessageBox.Show(
                    "Вставьте ссылку Hysteria2 или JSON-профиль.",
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                sourceTextBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
