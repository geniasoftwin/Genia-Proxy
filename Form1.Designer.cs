namespace GeniaProxy
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            mainLayout = new TableLayoutPanel();
            topLayout = new TableLayoutPanel();
            profileLabel = new Label();
            profileComboBox = new ComboBox();
            startButton = new Button();
            stopButton = new Button();
            logLabel = new Label();
            logTextBox = new RichTextBox();
            statusLayout = new TableLayoutPanel();
            statusLabel = new Label();
            portLabel = new Label();
            importButton = new Button();
            deleteProfileButton = new Button();
            portNumericUpDown = new NumericUpDown();
            systemProxyCheckBox = new CheckBox();
            mainLayout.SuspendLayout();
            topLayout.SuspendLayout();
            statusLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)portNumericUpDown).BeginInit();
            SuspendLayout();
            //
            // mainLayout
            //
            mainLayout.ColumnCount = 1;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.Controls.Add(topLayout, 0, 0);
            mainLayout.Controls.Add(logLabel, 0, 2);
            mainLayout.Controls.Add(logTextBox, 0, 3);
            mainLayout.Controls.Add(statusLayout, 0, 1);
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.Location = new Point(0, 0);
            mainLayout.Name = "mainLayout";
            mainLayout.Padding = new Padding(15);
            mainLayout.RowCount = 4;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.Size = new Size(720, 460);
            mainLayout.TabIndex = 0;
            //
            // topLayout
            //
            topLayout.ColumnCount = 3;
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            topLayout.Controls.Add(profileLabel, 0, 0);
            topLayout.Controls.Add(profileComboBox, 0, 1);
            topLayout.Controls.Add(startButton, 1, 1);
            topLayout.Controls.Add(stopButton, 2, 1);
            topLayout.Dock = DockStyle.Fill;
            topLayout.Location = new Point(15, 15);
            topLayout.Margin = new Padding(0);
            topLayout.Name = "topLayout";
            topLayout.RowCount = 2;
            topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            topLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            topLayout.Size = new Size(690, 82);
            topLayout.TabIndex = 0;
            //
            // profileLabel
            //
            profileLabel.Anchor = AnchorStyles.Left;
            profileLabel.AutoSize = true;
            profileLabel.Location = new Point(3, 4);
            profileLabel.Name = "profileLabel";
            profileLabel.Size = new Size(55, 15);
            profileLabel.TabIndex = 0;
            profileLabel.Text = "Профиль:";
            profileLabel.TextAlign = ContentAlignment.MiddleCenter;
            //
            // profileComboBox
            //
            profileComboBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            profileComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            profileComboBox.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 204);
            profileComboBox.FormattingEnabled = true;
            profileComboBox.ItemHeight = 17;
            profileComboBox.Location = new Point(3, 40);
            profileComboBox.Margin = new Padding(3, 3, 8, 3);
            profileComboBox.Name = "profileComboBox";
            profileComboBox.Size = new Size(347, 25);
            profileComboBox.TabIndex = 1;
            //
            // startButton
            //
            startButton.Dock = DockStyle.Fill;
            startButton.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point, 204);
            startButton.Location = new Point(362, 28);
            startButton.Margin = new Padding(4, 4, 4, 3);
            startButton.Name = "startButton";
            startButton.Size = new Size(157, 51);
            startButton.TabIndex = 2;
            startButton.Text = "▶  ПОДКЛЮЧИТЬ";
            startButton.UseVisualStyleBackColor = true;
            startButton.Click += StartButton_Click;
            //
            // stopButton
            //
            stopButton.Dock = DockStyle.Fill;
            stopButton.Enabled = false;
            stopButton.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point, 204);
            stopButton.Location = new Point(527, 28);
            stopButton.Margin = new Padding(4, 4, 4, 3);
            stopButton.Name = "stopButton";
            stopButton.Size = new Size(159, 51);
            stopButton.TabIndex = 3;
            stopButton.Text = "■  ОСТАНОВИТЬ";
            stopButton.UseVisualStyleBackColor = true;
            stopButton.Click += StopButton_Click;
            //
            // logLabel
            //
            logLabel.Anchor = AnchorStyles.Left;
            logLabel.AutoSize = true;
            logLabel.Location = new Point(18, 139);
            logLabel.Name = "logLabel";
            logLabel.Size = new Size(30, 15);
            logLabel.TabIndex = 2;
            logLabel.Text = "Лог:";
            //
            // logTextBox
            //
            logTextBox.BackColor = Color.FromArgb(30, 30, 30);
            logTextBox.DetectUrls = false;
            logTextBox.Dock = DockStyle.Fill;
            logTextBox.Font = new Font("Consolas", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 204);
            logTextBox.ForeColor = Color.LightGray;
            logTextBox.Location = new Point(18, 165);
            logTextBox.Name = "logTextBox";
            logTextBox.ReadOnly = true;
            logTextBox.Size = new Size(684, 277);
            logTextBox.TabIndex = 3;
            logTextBox.Text = "";
            logTextBox.WordWrap = false;
            //
            // statusLayout
            //
            statusLayout.ColumnCount = 7;
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            statusLayout.Controls.Add(statusLabel, 0, 0);
            statusLayout.Controls.Add(portLabel, 2, 0);
            statusLayout.Controls.Add(importButton, 4, 0);
            statusLayout.Controls.Add(deleteProfileButton, 5, 0);
            statusLayout.Controls.Add(portNumericUpDown, 3, 0);
            statusLayout.Controls.Add(systemProxyCheckBox, 1, 0);
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.Location = new Point(15, 97);
            statusLayout.Margin = new Padding(0);
            statusLayout.Name = "statusLayout";
            statusLayout.RowCount = 1;
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            statusLayout.Size = new Size(690, 38);
            statusLayout.TabIndex = 4;
            statusLayout.Paint += statusLayout_Paint;
            //
            // statusLabel
            //
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point, 204);
            statusLabel.Location = new Point(3, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(156, 38);
            statusLabel.TabIndex = 2;
            statusLabel.Text = "Статус: Остановлено";
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // portLabel
            //
            portLabel.AutoSize = false;
            portLabel.Dock = DockStyle.Fill;
            portLabel.Location = new Point(307, 0);
            portLabel.Margin = new Padding(3, 0, 3, 0);
            portLabel.Name = "portLabel";
            portLabel.Size = new Size(46, 38);
            portLabel.TabIndex = 3;
            portLabel.Text = "Порт:";
            portLabel.TextAlign = ContentAlignment.MiddleRight;
            //
            // importButton
            //
            importButton.Dock = DockStyle.Fill;
            importButton.Location = new Point(442, 3);
            importButton.Margin = new Padding(4, 3, 4, 3);
            importButton.Name = "importButton";
            importButton.Padding = new Padding(0, 2, 0, 0);
            importButton.Size = new Size(74, 32);
            importButton.TabIndex = 5;
            importButton.Text = "+ Импорт";
            importButton.UseVisualStyleBackColor = true;
            //
            // deleteProfileButton
            //
            deleteProfileButton.Dock = DockStyle.Fill;
            deleteProfileButton.Location = new Point(524, 3);
            deleteProfileButton.Margin = new Padding(4, 3, 4, 3);
            deleteProfileButton.Name = "deleteProfileButton";
            deleteProfileButton.Size = new Size(74, 32);
            deleteProfileButton.TabIndex = 6;
            deleteProfileButton.Text = "Удалить";
            deleteProfileButton.UseVisualStyleBackColor = true;
            //
            // portNumericUpDown
            //
            portNumericUpDown.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 204);
            portNumericUpDown.Anchor = AnchorStyles.None;
            portNumericUpDown.Location = new Point(360, 6);
            portNumericUpDown.Margin = new Padding(3);
            portNumericUpDown.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            portNumericUpDown.Minimum = new decimal(new int[] { 1024, 0, 0, 0 });
            portNumericUpDown.Name = "portNumericUpDown";
            portNumericUpDown.Size = new Size(75, 25);
            portNumericUpDown.TabIndex = 4;
            portNumericUpDown.TextAlign = HorizontalAlignment.Center;
            portNumericUpDown.Value = new decimal(new int[] { 2080, 0, 0, 0 });
            //
            // systemProxyCheckBox
            //
            systemProxyCheckBox.Anchor = AnchorStyles.None;
            systemProxyCheckBox.AutoSize = true;
            systemProxyCheckBox.Location = new Point(166, 9);
            systemProxyCheckBox.Margin = new Padding(3, 0, 3, 0);
            systemProxyCheckBox.Name = "systemProxyCheckBox";
            systemProxyCheckBox.Size = new Size(133, 19);
            systemProxyCheckBox.TabIndex = 7;
            systemProxyCheckBox.Text = "Системный прокси";
            systemProxyCheckBox.UseVisualStyleBackColor = true;
            //
            // Form1
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(720, 460);
            Controls.Add(mainLayout);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MinimumSize = new Size(640, 380);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "GeniaProxy";
            Load += Form1_Load;
            mainLayout.ResumeLayout(false);
            mainLayout.PerformLayout();
            topLayout.ResumeLayout(false);
            topLayout.PerformLayout();
            statusLayout.ResumeLayout(false);
            statusLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)portNumericUpDown).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel mainLayout;
        private TableLayoutPanel topLayout;
        private Label profileLabel;
        private ComboBox profileComboBox;
        private Button startButton;
        private Button stopButton;
        private Label logLabel;
        private RichTextBox logTextBox;
        private TableLayoutPanel statusLayout;
        private Label statusLabel;
        private Label portLabel;
        private NumericUpDown portNumericUpDown;
        private Button deleteProfileButton;
        private Button importButton;
        private CheckBox systemProxyCheckBox;
    }
}
