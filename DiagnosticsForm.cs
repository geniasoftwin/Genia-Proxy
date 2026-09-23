using GeniaProxy.Services;
using System.Globalization;
using System.Text;

namespace GeniaProxy
{
    public sealed class DiagnosticsForm : Form
    {
        public DiagnosticsForm(
            string diagnostics,
            string title = "Диагностика GeniaProxy")
        {
            ApplicationBrandingService.ApplyIcon(this);

            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimumSize = new Size(650, 430);
            ClientSize = new Size(760, 520);

            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9.5F),
                Text = diagnostics
            };

            var copyButton = new Button
            {
                Text = "Копировать",
                Size = new Size(110, 32)
            };

            copyButton.Click += (_, _) =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(textBox.Text))
                    {
                        Clipboard.SetText(textBox.Text);
                    }
                }
                catch (Exception ex)
                {
                    ShowSaveError(
                        "Не удалось скопировать отчёт.",
                        ex
                    );
                }
            };

            var saveButton = new Button
            {
                Text = "Сохранить...",
                Size = new Size(110, 32)
            };

            saveButton.Click += (_, _) =>
                SaveReport(textBox.Text);

            var closeButton = new Button
            {
                Text = "Закрыть",
                DialogResult = DialogResult.OK,
                Size = new Size(100, 32)
            };

            ApplyStrictButtonStyle(copyButton);
            ApplyStrictButtonStyle(saveButton);
            ApplyStrictButtonStyle(closeButton);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(6),
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(saveButton);
            buttons.Controls.Add(copyButton);

            Controls.Add(textBox);
            Controls.Add(buttons);
            AcceptButton = closeButton;
        }

        private void SaveReport(string report)
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Сохранить диагностический отчёт",
                Filter = "Текстовый файл (*.txt)|*.txt",
                DefaultExt = "txt",
                AddExtension = true,
                FileName = "GeniaProxy-diagnostics-" +
                    DateTime.Now.ToString(
                        "yyyy-MM-dd_HH-mm-ss",
                        CultureInfo.InvariantCulture
                    ) + ".txt"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                AtomicFileWriter.WriteAllText(
                    dialog.FileName,
                    report,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false
                    )
                );
            }
            catch (Exception ex)
            {
                ShowSaveError(
                    "Не удалось сохранить отчёт.",
                    ex
                );
            }
        }

        private void ShowSaveError(
            string message,
            Exception exception)
        {
            MessageBox.Show(
                this,
                message + "\n\n" + exception.Message,
                "GeniaProxy",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
        private static void ApplyStrictButtonStyle(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
        }

    }
}
