using GeniaProxy.Models;

namespace GeniaProxy
{
    public sealed class SettingsForm : Form
    {
        private readonly CheckBox autoConnectCheckBox;
        private readonly CheckBox startWithWindowsCheckBox;
        private readonly CheckBox reconnectCheckBox;
        private readonly NumericUpDown reconnectDelay;

        public bool AutoConnect =>
            autoConnectCheckBox.Checked;

        public bool StartWithWindows =>
            startWithWindowsCheckBox.Checked;

        public bool ReconnectOnFailure =>
            reconnectCheckBox.Checked;

        public int ReconnectDelaySeconds =>
            decimal.ToInt32(reconnectDelay.Value);

        public SettingsForm(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Services.ApplicationBrandingService.ApplyIcon(this);

            Text = "Настройки GeniaProxy";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 245);

            autoConnectCheckBox = new CheckBox
            {
                AutoSize = true,
                Text = "Подключаться автоматически при запуске",
                Checked = settings.AutoConnect
            };

            startWithWindowsCheckBox = new CheckBox
            {
                AutoSize = true,
                Text = "Запускать GeniaProxy вместе с Windows",
                Checked = settings.StartWithWindows
            };

            reconnectCheckBox = new CheckBox
            {
                AutoSize = true,
                Text = "Переподключаться при сбое sing-box",
                Checked = settings.ReconnectOnFailure
            };

            reconnectDelay = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 60,
                Value = Math.Clamp(
                    settings.ReconnectDelaySeconds,
                    1,
                    60
                ),
                Width = 70,
                TextAlign = HorizontalAlignment.Center
            };

            var delayLayout = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            delayLayout.Controls.Add(new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0),
                Text = "Задержка перед повтором, секунд:"
            });

            delayLayout.Controls.Add(reconnectDelay);

            var okButton = new Button
            {
                DialogResult = DialogResult.OK,
                Text = "Сохранить",
                Size = new Size(105, 32)
            };

            var cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Text = "Отмена",
                Size = new Size(95, 32)
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(okButton);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 5
            };

            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            layout.Controls.Add(autoConnectCheckBox, 0, 0);
            layout.Controls.Add(startWithWindowsCheckBox, 0, 1);
            layout.Controls.Add(reconnectCheckBox, 0, 2);
            layout.Controls.Add(delayLayout, 0, 3);
            layout.Controls.Add(buttons, 0, 4);

            Controls.Add(layout);
            AcceptButton = okButton;
            CancelButton = cancelButton;

            reconnectCheckBox.CheckedChanged += (_, _) =>
                reconnectDelay.Enabled =
                    reconnectCheckBox.Checked;

            reconnectDelay.Enabled = reconnectCheckBox.Checked;
        }
    }
}
