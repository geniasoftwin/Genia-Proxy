using GeniaProxy.Services;

namespace GeniaProxy
{
    public sealed class RenameProfileForm : Form
    {
        private readonly TextBox nameTextBox =
            new();

        public string ProfileName =>
            nameTextBox.Text.Trim();

        public RenameProfileForm(
            string currentName)
        {
            ApplicationBrandingService.ApplyIcon(this);

            Text = "Переименовать профиль";

            StartPosition =
                FormStartPosition.CenterParent;

            FormBorderStyle =
                FormBorderStyle.FixedDialog;

            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            ClientSize =
                new Size(380, 135);

            var label =
                new Label
                {
                    Text = "Новое имя профиля:",
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    TextAlign =
                        ContentAlignment.BottomLeft
                };

            nameTextBox.Text = currentName;
            nameTextBox.Dock = DockStyle.Fill;

            var renameButton =
                new Button
                {
                    Text = "Переименовать",
                    AutoSize = true
                };

            var cancelButton =
                new Button
                {
                    Text = "Отмена",
                    AutoSize = true,
                    DialogResult =
                        DialogResult.Cancel
                };

            renameButton.Click +=
                RenameButton_Click;

            var buttonsPanel =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection =
                        FlowDirection.RightToLeft,
                    WrapContents = false,
                    AutoSize = true
                };

            buttonsPanel.Controls.Add(
                cancelButton
            );

            buttonsPanel.Controls.Add(
                renameButton
            );

            var layout =
                new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(12),
                    ColumnCount = 1,
                    RowCount = 3
                };

            layout.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    28
                )
            );

            layout.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    34
                )
            );

            layout.RowStyles.Add(
                new RowStyle(
                    SizeType.Percent,
                    100
                )
            );

            layout.Controls.Add(
                label,
                0,
                0
            );

            layout.Controls.Add(
                nameTextBox,
                0,
                1
            );

            layout.Controls.Add(
                buttonsPanel,
                0,
                2
            );

            Controls.Add(layout);

            AcceptButton = renameButton;
            CancelButton = cancelButton;

            Shown += (_, _) =>
            {
                nameTextBox.Focus();
                nameTextBox.SelectAll();
            };
        }

        private void RenameButton_Click(
            object? sender,
            EventArgs e)
        {
            string candidate =
                ProfileNameValidator.Normalize(
                    nameTextBox.Text
                );

            string? validationError =
                ProfileNameValidator
                    .GetValidationError(candidate);

            if (validationError is not null)
            {
                MessageBox.Show(
                    validationError,
                    "GeniaProxy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                nameTextBox.Focus();
                nameTextBox.SelectAll();

                return;
            }

            nameTextBox.Text = candidate;

            DialogResult =
                DialogResult.OK;

            Close();
        }

    }
}
