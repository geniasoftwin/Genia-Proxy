namespace GeniaProxy
{
    partial class ImportProfileForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            mainLayout = new TableLayoutPanel();
            profileNameLabel = new Label();
            profileNameTextBox = new TextBox();
            sourceLabel = new Label();
            sourceTextBox = new TextBox();
            buttonsLayout = new FlowLayoutPanel();
            cancelButton = new Button();
            saveButton = new Button();
            mainLayout.SuspendLayout();
            buttonsLayout.SuspendLayout();
            SuspendLayout();
            //
            // mainLayout
            //
            mainLayout.ColumnCount = 1;
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.Controls.Add(profileNameLabel, 0, 0);
            mainLayout.Controls.Add(profileNameTextBox, 0, 1);
            mainLayout.Controls.Add(sourceLabel, 0, 2);
            mainLayout.Controls.Add(sourceTextBox, 0, 3);
            mainLayout.Controls.Add(buttonsLayout, 0, 4);
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.Location = new Point(0, 0);
            mainLayout.Name = "mainLayout";
            mainLayout.Padding = new Padding(12);
            mainLayout.RowCount = 5;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            mainLayout.Size = new Size(604, 341);
            mainLayout.TabIndex = 0;
            //
            // profileNameLabel
            //
            profileNameLabel.Anchor = AnchorStyles.Left;
            profileNameLabel.AutoSize = true;
            profileNameLabel.Location = new Point(15, 17);
            profileNameLabel.Name = "profileNameLabel";
            profileNameLabel.Size = new Size(115, 15);
            profileNameLabel.TabIndex = 0;
            profileNameLabel.Text = "Название профиля:";
            //
            // profileNameTextBox
            //
            profileNameTextBox.Dock = DockStyle.Fill;
            profileNameTextBox.Location = new Point(15, 40);
            profileNameTextBox.Name = "profileNameTextBox";
            profileNameTextBox.Size = new Size(574, 23);
            profileNameTextBox.TabIndex = 1;
            profileNameTextBox.Text = "Новый профиль";
            //
            // sourceLabel
            //
            sourceLabel.Anchor = AnchorStyles.Left;
            sourceLabel.AutoSize = true;
            sourceLabel.Location = new Point(15, 77);
            sourceLabel.Name = "sourceLabel";
            sourceLabel.Size = new Size(186, 15);
            sourceLabel.TabIndex = 2;
            sourceLabel.Text = "Ссылка Hysteria2 или JSON-профиль:";
            //
            // sourceTextBox
            //
            sourceTextBox.AcceptsReturn = true;
            sourceTextBox.Dock = DockStyle.Fill;
            sourceTextBox.Font = new Font("Consolas", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 204);
            sourceTextBox.Location = new Point(15, 100);
            sourceTextBox.Multiline = true;
            sourceTextBox.Name = "sourceTextBox";
            sourceTextBox.ScrollBars = ScrollBars.Both;
            sourceTextBox.Size = new Size(574, 181);
            sourceTextBox.TabIndex = 3;
            sourceTextBox.WordWrap = false;
            //
            // buttonsLayout
            //
            buttonsLayout.Controls.Add(cancelButton);
            buttonsLayout.Controls.Add(saveButton);
            buttonsLayout.Dock = DockStyle.Fill;
            buttonsLayout.FlowDirection = FlowDirection.RightToLeft;
            buttonsLayout.Location = new Point(15, 287);
            buttonsLayout.Name = "buttonsLayout";
            buttonsLayout.Size = new Size(574, 39);
            buttonsLayout.TabIndex = 4;
            buttonsLayout.WrapContents = false;
            //
            // cancelButton
            //
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Location = new Point(471, 3);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(100, 30);
            cancelButton.TabIndex = 0;
            cancelButton.Text = "Отмена";
            cancelButton.UseVisualStyleBackColor = true;
            //
            // saveButton
            //
            saveButton.Location = new Point(355, 3);
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(110, 30);
            saveButton.TabIndex = 1;
            saveButton.Text = "Сохранить";
            saveButton.UseVisualStyleBackColor = true;
            //
            // ImportProfileForm
            //
            AcceptButton = saveButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new Size(604, 341);
            Controls.Add(mainLayout);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ImportProfileForm";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Импорт профиля";
            mainLayout.ResumeLayout(false);
            mainLayout.PerformLayout();
            buttonsLayout.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel mainLayout;
        private Label profileNameLabel;
        private TextBox profileNameTextBox;
        private Label sourceLabel;
        private TextBox sourceTextBox;
        private FlowLayoutPanel buttonsLayout;
        private Button cancelButton;
        private Button saveButton;
    }
}
