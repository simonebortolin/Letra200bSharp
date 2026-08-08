namespace Letra200bSharp.WinForms
{
    partial class MainForm
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
            BluetoothDevicesListBox = new ListBox();
            DevicesLabel = new Label();
            RefreshButton = new Button();
            MainTabControl = new TabControl();
            ImageTabPage = new TabPage();
            ImagePreviewPictureBox = new PictureBox();
            ImageNoCutCheckBox = new CheckBox();
            PreRenderedCheckBox = new CheckBox();
            PrintButton = new Button();
            BrowseButton = new Button();
            PathLabel = new Label();
            ImageLabel = new Label();
            TextTabPage = new TabPage();
            TextPreviewPictureBox = new PictureBox();
            PrintTextButton = new Button();
            UpperCaseCheckBox = new CheckBox();
            WidthScaleNumericUpDown = new NumericUpDown();
            WidthScaleLabel = new Label();
            BoxStyleComboBox = new ComboBox();
            BoxStyleLabel = new Label();
            StyleComboBox = new ComboBox();
            StyleLabel = new Label();
            SizeComboBox = new ComboBox();
            SizeLabel = new Label();
            FontFamilyComboBox = new ComboBox();
            FontFamilyLabel = new Label();
            TextLine2TextBox = new TextBox();
            TextLine2Label = new Label();
            TextPreviewButton = new Button();
            TextTextBox = new TextBox();
            TextLabel = new Label();
            MainTabControl.SuspendLayout();
            ImageTabPage.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)ImagePreviewPictureBox).BeginInit();
            TextTabPage.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)TextPreviewPictureBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)WidthScaleNumericUpDown).BeginInit();
            SuspendLayout();
            //
            // BluetoothDevicesListBox
            //
            BluetoothDevicesListBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            BluetoothDevicesListBox.DisplayMember = "Name";
            BluetoothDevicesListBox.FormattingEnabled = true;
            BluetoothDevicesListBox.ItemHeight = 15;
            BluetoothDevicesListBox.Location = new Point(12, 34);
            BluetoothDevicesListBox.Name = "BluetoothDevicesListBox";
            BluetoothDevicesListBox.Size = new Size(581, 154);
            BluetoothDevicesListBox.TabIndex = 0;
            BluetoothDevicesListBox.ValueMember = "Id";
            //
            // DevicesLabel
            //
            DevicesLabel.AutoSize = true;
            DevicesLabel.Location = new Point(12, 9);
            DevicesLabel.Name = "DevicesLabel";
            DevicesLabel.Size = new Size(50, 15);
            DevicesLabel.TabIndex = 1;
            DevicesLabel.Text = "Devices:";
            //
            // RefreshButton
            //
            RefreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            RefreshButton.Enabled = false;
            RefreshButton.Location = new Point(518, 5);
            RefreshButton.Name = "RefreshButton";
            RefreshButton.Size = new Size(75, 23);
            RefreshButton.TabIndex = 2;
            RefreshButton.Text = "Refresh";
            RefreshButton.UseVisualStyleBackColor = true;
            RefreshButton.Click += RefreshButton_Click;
            //
            // MainTabControl
            //
            MainTabControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            MainTabControl.Controls.Add(ImageTabPage);
            MainTabControl.Controls.Add(TextTabPage);
            MainTabControl.Location = new Point(12, 197);
            MainTabControl.Name = "MainTabControl";
            MainTabControl.SelectedIndex = 0;
            MainTabControl.Size = new Size(581, 270);
            MainTabControl.TabIndex = 3;
            //
            // ImageTabPage
            //
            ImageTabPage.Controls.Add(ImagePreviewPictureBox);
            ImageTabPage.Controls.Add(ImageNoCutCheckBox);
            ImageTabPage.Controls.Add(PreRenderedCheckBox);
            ImageTabPage.Controls.Add(PrintButton);
            ImageTabPage.Controls.Add(BrowseButton);
            ImageTabPage.Controls.Add(PathLabel);
            ImageTabPage.Controls.Add(ImageLabel);
            ImageTabPage.Location = new Point(4, 24);
            ImageTabPage.Name = "ImageTabPage";
            ImageTabPage.Padding = new Padding(3);
            ImageTabPage.Size = new Size(573, 242);
            ImageTabPage.TabIndex = 0;
            ImageTabPage.Text = "Image";
            ImageTabPage.UseVisualStyleBackColor = true;
            //
            // ImagePreviewPictureBox
            //
            ImagePreviewPictureBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ImagePreviewPictureBox.BackColor = Color.White;
            ImagePreviewPictureBox.BorderStyle = BorderStyle.FixedSingle;
            ImagePreviewPictureBox.Location = new Point(12, 67);
            ImagePreviewPictureBox.Name = "ImagePreviewPictureBox";
            ImagePreviewPictureBox.Size = new Size(549, 133);
            ImagePreviewPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            ImagePreviewPictureBox.TabIndex = 6;
            ImagePreviewPictureBox.TabStop = false;
            //
            // ImageNoCutCheckBox
            //
            ImageNoCutCheckBox.AutoSize = true;
            ImageNoCutCheckBox.Enabled = false;
            ImageNoCutCheckBox.Location = new Point(110, 40);
            ImageNoCutCheckBox.Name = "ImageNoCutCheckBox";
            ImageNoCutCheckBox.Size = new Size(65, 19);
            ImageNoCutCheckBox.TabIndex = 5;
            ImageNoCutCheckBox.Text = "No cut";
            ImageNoCutCheckBox.UseVisualStyleBackColor = true;
            //
            // PreRenderedCheckBox
            //
            PreRenderedCheckBox.AutoSize = true;
            PreRenderedCheckBox.Location = new Point(12, 40);
            PreRenderedCheckBox.Name = "PreRenderedCheckBox";
            PreRenderedCheckBox.Size = new Size(92, 19);
            PreRenderedCheckBox.TabIndex = 4;
            PreRenderedCheckBox.Text = "Pre-rendered";
            PreRenderedCheckBox.UseVisualStyleBackColor = true;
            //
            // PrintButton
            //
            PrintButton.Anchor = AnchorStyles.Bottom;
            PrintButton.Location = new Point(237, 208);
            PrintButton.Name = "PrintButton";
            PrintButton.Size = new Size(75, 23);
            PrintButton.TabIndex = 3;
            PrintButton.Text = "Print";
            PrintButton.UseVisualStyleBackColor = true;
            PrintButton.Click += PrintButton_Click;
            //
            // BrowseButton
            //
            BrowseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            BrowseButton.Location = new Point(486, 8);
            BrowseButton.Name = "BrowseButton";
            BrowseButton.Size = new Size(75, 23);
            BrowseButton.TabIndex = 2;
            BrowseButton.Text = "Browse...";
            BrowseButton.UseVisualStyleBackColor = true;
            BrowseButton.Click += BrowseButton_Click;
            //
            // PathLabel
            //
            PathLabel.AutoSize = true;
            PathLabel.Location = new Point(61, 12);
            PathLabel.Name = "PathLabel";
            PathLabel.Size = new Size(47, 15);
            PathLabel.TabIndex = 1;
            PathLabel.Text = "<Path>";
            //
            // ImageLabel
            //
            ImageLabel.AutoSize = true;
            ImageLabel.Location = new Point(12, 12);
            ImageLabel.Name = "ImageLabel";
            ImageLabel.Size = new Size(43, 15);
            ImageLabel.TabIndex = 0;
            ImageLabel.Text = "Image:";
            //
            // TextTabPage
            //
            TextTabPage.Controls.Add(TextPreviewPictureBox);
            TextTabPage.Controls.Add(PrintTextButton);
            TextTabPage.Controls.Add(UpperCaseCheckBox);
            TextTabPage.Controls.Add(BoxStyleComboBox);
            TextTabPage.Controls.Add(BoxStyleLabel);
            TextTabPage.Controls.Add(WidthScaleNumericUpDown);
            TextTabPage.Controls.Add(WidthScaleLabel);
            TextTabPage.Controls.Add(StyleComboBox);
            TextTabPage.Controls.Add(StyleLabel);
            TextTabPage.Controls.Add(SizeComboBox);
            TextTabPage.Controls.Add(SizeLabel);
            TextTabPage.Controls.Add(FontFamilyComboBox);
            TextTabPage.Controls.Add(FontFamilyLabel);
            TextTabPage.Controls.Add(TextLine2TextBox);
            TextTabPage.Controls.Add(TextLine2Label);
            TextTabPage.Controls.Add(TextPreviewButton);
            TextTabPage.Controls.Add(TextTextBox);
            TextTabPage.Controls.Add(TextLabel);
            TextTabPage.Location = new Point(4, 24);
            TextTabPage.Name = "TextTabPage";
            TextTabPage.Padding = new Padding(3);
            TextTabPage.Size = new Size(573, 242);
            TextTabPage.TabIndex = 1;
            TextTabPage.Text = "Text";
            TextTabPage.UseVisualStyleBackColor = true;
            //
            // TextPreviewPictureBox
            //
            TextPreviewPictureBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            TextPreviewPictureBox.BackColor = Color.White;
            TextPreviewPictureBox.BorderStyle = BorderStyle.FixedSingle;
            TextPreviewPictureBox.Location = new Point(12, 117);
            TextPreviewPictureBox.Name = "TextPreviewPictureBox";
            TextPreviewPictureBox.Size = new Size(549, 80);
            TextPreviewPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            TextPreviewPictureBox.TabIndex = 13;
            TextPreviewPictureBox.TabStop = false;
            //
            // PrintTextButton
            //
            PrintTextButton.Anchor = AnchorStyles.Bottom;
            PrintTextButton.Location = new Point(237, 208);
            PrintTextButton.Name = "PrintTextButton";
            PrintTextButton.Size = new Size(75, 23);
            PrintTextButton.TabIndex = 12;
            PrintTextButton.Text = "Print";
            PrintTextButton.UseVisualStyleBackColor = true;
            PrintTextButton.Click += PrintTextButton_Click;
            //
            // UpperCaseCheckBox
            //
            UpperCaseCheckBox.AutoSize = true;
            UpperCaseCheckBox.Location = new Point(12, 93);
            UpperCaseCheckBox.Name = "UpperCaseCheckBox";
            UpperCaseCheckBox.Size = new Size(92, 19);
            UpperCaseCheckBox.TabIndex = 9;
            UpperCaseCheckBox.Text = "ALL UPPERCASE";
            UpperCaseCheckBox.UseVisualStyleBackColor = true;
            //
            // BoxStyleLabel
            //
            BoxStyleLabel.AutoSize = true;
            BoxStyleLabel.Location = new Point(120, 96);
            BoxStyleLabel.Name = "BoxStyleLabel";
            BoxStyleLabel.Size = new Size(32, 15);
            BoxStyleLabel.TabIndex = 18;
            BoxStyleLabel.Text = "Box:";
            //
            // BoxStyleComboBox
            //
            BoxStyleComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            BoxStyleComboBox.FormattingEnabled = true;
            BoxStyleComboBox.Items.AddRange(new object[] { "None", "Underline", "Square", "Pointed", "Rounded", "Edged", "Crocodile" });
            BoxStyleComboBox.Location = new Point(156, 93);
            BoxStyleComboBox.Name = "BoxStyleComboBox";
            BoxStyleComboBox.Size = new Size(95, 23);
            BoxStyleComboBox.TabIndex = 19;
            //
            // WidthScaleNumericUpDown
            //
            WidthScaleNumericUpDown.DecimalPlaces = 1;
            WidthScaleNumericUpDown.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            WidthScaleNumericUpDown.Location = new Point(505, 65);
            WidthScaleNumericUpDown.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            WidthScaleNumericUpDown.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            WidthScaleNumericUpDown.Name = "WidthScaleNumericUpDown";
            WidthScaleNumericUpDown.Size = new Size(56, 23);
            WidthScaleNumericUpDown.TabIndex = 16;
            WidthScaleNumericUpDown.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // WidthScaleLabel
            //
            WidthScaleLabel.AutoSize = true;
            WidthScaleLabel.Location = new Point(465, 68);
            WidthScaleLabel.Name = "WidthScaleLabel";
            WidthScaleLabel.Size = new Size(42, 15);
            WidthScaleLabel.TabIndex = 17;
            WidthScaleLabel.Text = "Width:";
            //
            // StyleLabel
            //
            StyleLabel.AutoSize = true;
            StyleLabel.Location = new Point(345, 68);
            StyleLabel.Name = "StyleLabel";
            StyleLabel.Size = new Size(38, 15);
            StyleLabel.TabIndex = 8;
            StyleLabel.Text = "Style:";
            //
            // StyleComboBox
            //
            StyleComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            StyleComboBox.FormattingEnabled = true;
            StyleComboBox.Items.AddRange(new object[] { "Normal", "Bold", "Italic", "Outline", "Shadow", "Vertical" });
            StyleComboBox.Location = new Point(388, 65);
            StyleComboBox.Name = "StyleComboBox";
            StyleComboBox.Size = new Size(75, 23);
            StyleComboBox.TabIndex = 7;
            //
            // SizeComboBox
            //
            SizeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            SizeComboBox.FormattingEnabled = true;
            SizeComboBox.Items.AddRange(new object[] { "XS", "S", "M", "L", "XL" });
            SizeComboBox.Location = new Point(278, 65);
            SizeComboBox.Name = "SizeComboBox";
            SizeComboBox.Size = new Size(55, 23);
            SizeComboBox.TabIndex = 6;
            //
            // SizeLabel
            //
            SizeLabel.AutoSize = true;
            SizeLabel.Location = new Point(242, 68);
            SizeLabel.Name = "SizeLabel";
            SizeLabel.Size = new Size(34, 15);
            SizeLabel.TabIndex = 5;
            SizeLabel.Text = "Size:";
            //
            // FontFamilyComboBox
            //
            FontFamilyComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            FontFamilyComboBox.FormattingEnabled = true;
            FontFamilyComboBox.Location = new Point(60, 65);
            FontFamilyComboBox.Name = "FontFamilyComboBox";
            FontFamilyComboBox.Size = new Size(170, 23);
            FontFamilyComboBox.TabIndex = 4;
            //
            // FontFamilyLabel
            //
            FontFamilyLabel.AutoSize = true;
            FontFamilyLabel.Location = new Point(12, 68);
            FontFamilyLabel.Name = "FontFamilyLabel";
            FontFamilyLabel.Size = new Size(34, 15);
            FontFamilyLabel.TabIndex = 3;
            FontFamilyLabel.Text = "Font:";
            //
            // TextLine2TextBox
            //
            TextLine2TextBox.Location = new Point(60, 36);
            TextLine2TextBox.Name = "TextLine2TextBox";
            TextLine2TextBox.Size = new Size(300, 23);
            TextLine2TextBox.TabIndex = 14;
            //
            // TextLine2Label
            //
            TextLine2Label.AutoSize = true;
            TextLine2Label.Location = new Point(12, 39);
            TextLine2Label.Name = "TextLine2Label";
            TextLine2Label.Size = new Size(44, 15);
            TextLine2Label.TabIndex = 15;
            TextLine2Label.Text = "Line 2:";
            //
            // TextPreviewButton
            //
            TextPreviewButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            TextPreviewButton.Location = new Point(486, 8);
            TextPreviewButton.Name = "TextPreviewButton";
            TextPreviewButton.Size = new Size(75, 23);
            TextPreviewButton.TabIndex = 2;
            TextPreviewButton.Text = "Preview";
            TextPreviewButton.UseVisualStyleBackColor = true;
            TextPreviewButton.Click += TextPreviewButton_Click;
            //
            // TextTextBox
            //
            TextTextBox.Location = new Point(60, 9);
            TextTextBox.Name = "TextTextBox";
            TextTextBox.Size = new Size(300, 23);
            TextTextBox.TabIndex = 1;
            TextTextBox.Text = "Hello world";
            //
            // TextLabel
            //
            TextLabel.AutoSize = true;
            TextLabel.Location = new Point(12, 12);
            TextLabel.Name = "TextLabel";
            TextLabel.Size = new Size(32, 15);
            TextLabel.TabIndex = 0;
            TextLabel.Text = "Text:";
            //
            // MainForm
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(605, 479);
            Controls.Add(MainTabControl);
            Controls.Add(RefreshButton);
            Controls.Add(DevicesLabel);
            Controls.Add(BluetoothDevicesListBox);
            Name = "MainForm";
            Text = "Dymo LetraTag 200B WinForms";
            Load += Form1_Load;
            MainTabControl.ResumeLayout(false);
            ImageTabPage.ResumeLayout(false);
            ImageTabPage.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)ImagePreviewPictureBox).EndInit();
            TextTabPage.ResumeLayout(false);
            TextTabPage.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)TextPreviewPictureBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)WidthScaleNumericUpDown).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ListBox BluetoothDevicesListBox;
        private Label DevicesLabel;
        private Button RefreshButton;
        private TabControl MainTabControl;
        private TabPage ImageTabPage;
        private Label ImageLabel;
        private Button BrowseButton;
        private Button PrintButton;
        private Label PathLabel;
        private PictureBox ImagePreviewPictureBox;
        private CheckBox PreRenderedCheckBox;
        private CheckBox ImageNoCutCheckBox;
        private TabPage TextTabPage;
        private Label TextLabel;
        private TextBox TextTextBox;
        private Label TextLine2Label;
        private TextBox TextLine2TextBox;
        private Button TextPreviewButton;
        private Label FontFamilyLabel;
        private ComboBox FontFamilyComboBox;
        private Label SizeLabel;
        private ComboBox SizeComboBox;
        private Label StyleLabel;
        private ComboBox StyleComboBox;
        private CheckBox UpperCaseCheckBox;
        private Label BoxStyleLabel;
        private ComboBox BoxStyleComboBox;
        private Label WidthScaleLabel;
        private NumericUpDown WidthScaleNumericUpDown;
        private Button PrintTextButton;
        private PictureBox TextPreviewPictureBox;
    }
}
