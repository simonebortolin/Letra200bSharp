using InTheHand.Bluetooth;
using Letra200bSharp;
using SkiaSharp;

namespace Letra200bSharp.WinForms
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();

            var fontFamilies = SKFontManager.Default.FontFamilies.OrderBy(f => f).ToArray();
            FontFamilyComboBox.Items.AddRange(fontFamilies);
            FontFamilyComboBox.SelectedItem = fontFamilies.Contains("Arial") ? "Arial" : fontFamilies.FirstOrDefault();

            SizeComboBox.SelectedItem = "M";
            StyleComboBox.SelectedItem = "Normal";
            BoxStyleComboBox.SelectedItem = "None";
            SizeComboBox.SelectedIndexChanged += (_, _) => UpdateLine2Availability();
            StyleComboBox.SelectedIndexChanged += (_, _) => UpdateLine2Availability();
            UpdateLine2Availability();

            SizeComboBox.SelectedIndexChanged += (_, _) => UpdateBoxStyleAvailability();
            UpdateBoxStyleAvailability();

            PreRenderedCheckBox.CheckedChanged += (_, _) => UpdateImageNoCutAvailability();
            UpdateImageNoCutAvailability();
        }

        /// <summary>
        /// "No cut" (send the full 32px height as-is, without the 1px top/bottom padding
        /// that keeps content within the printable 30px) only makes sense for an image
        /// that was already deliberately sized for that - i.e. "Pre-rendered" - so keep it
        /// disabled (and unchecked) otherwise.
        /// </summary>
        private void UpdateImageNoCutAvailability()
        {
            ImageNoCutCheckBox.Enabled = PreRenderedCheckBox.Checked;
            if (!PreRenderedCheckBox.Checked)
            {
                ImageNoCutCheckBox.Checked = false;
            }
        }

        /// <summary>
        /// A second line doesn't make sense with L/XL (barely any margin left to split
        /// between two lines) or with the Vertical style (already splits into one line per
        /// character), so disable it - and stop it from being silently included - in those
        /// cases.
        /// </summary>
        private void UpdateLine2Availability()
        {
            var size = GetSelectedTextSize();
            bool allowLine2 = GetSelectedTextStyle() != LetraHelper.TextStyle.Vertical && size != LetraHelper.LabelTextSize.L && size != LetraHelper.LabelTextSize.XL;
            TextLine2Label.Enabled = allowLine2;
            TextLine2TextBox.Enabled = allowLine2;
        }

        /// <summary>
        /// XL fills the entire printable height with no margin around the text (see
        /// <see cref="LetraHelper.LabelTextSize.XL"/>), so there's no room left to draw a
        /// border without it overlapping the text or the printer's unprintable edges - keep
        /// it disabled (and reset to None) for that size.
        /// </summary>
        private void UpdateBoxStyleAvailability()
        {
            bool allowBoxStyle = GetSelectedTextSize() != LetraHelper.LabelTextSize.XL;
            BoxStyleLabel.Enabled = allowBoxStyle;
            BoxStyleComboBox.Enabled = allowBoxStyle;
            if (!allowBoxStyle)
            {
                BoxStyleComboBox.SelectedItem = "None";
            }
        }

        private LetraHelper.TextStyle GetSelectedTextStyle()
        {
            return Enum.Parse<LetraHelper.TextStyle>((string)StyleComboBox.SelectedItem!);
        }

        private LetraHelper.TextBoxStyle GetSelectedBoxStyle()
        {
            return Enum.Parse<LetraHelper.TextBoxStyle>((string)BoxStyleComboBox.SelectedItem!);
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            await RefreshDevices();
        }

        private async Task RefreshDevices()
        {
            BluetoothDevicesListBox.DataSource = new List<BluetoothDevice>();
            RefreshButton.Enabled = false;
            var devices = await LetraPrinter.ScanForDevicesAsync();
            if (devices.Count == 0)
            {
                MessageBox.Show("Dymo LetraTag 200B not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            BluetoothDevicesListBox.DataSource = devices.ToList();
            RefreshButton.Enabled = true;
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            await RefreshDevices();
        }

        private async void BrowseButton_Click(object sender, EventArgs e)
        {
            var ofd = new OpenFileDialog();
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                PathLabel.Text = ofd.FileName;
                var path = ofd.FileName;
                var noCut = ImageNoCutCheckBox.Checked;
                var preRendered = PreRenderedCheckBox.Checked;
                await UpdatePreviewAsync(ImagePreviewPictureBox, () =>
                {
                    var imageBytes = File.ReadAllBytes(path);
                    return LetraHelper.PreviewImage(imageBytes, noCut, preRendered);
                });
            }
        }

        private async void TextPreviewButton_Click(object sender, EventArgs e)
        {
            var text = GetComposedText();
            if (string.IsNullOrEmpty(text))
            {
                TextPreviewPictureBox.Image?.Dispose();
                TextPreviewPictureBox.Image = null;
                return;
            }

            var fontFamily = (string)FontFamilyComboBox.SelectedItem!;
            var size = GetSelectedTextSize();
            var style = GetSelectedTextStyle();
            var upperCase = UpperCaseCheckBox.Checked;
            var widthScale = (float)WidthScaleNumericUpDown.Value;
            var boxStyle = GetSelectedBoxStyle();

            await UpdatePreviewAsync(TextPreviewPictureBox, () =>
                LetraHelper.PreviewImage(text, fontFamily, size, style, upperCase, widthScale, boxStyle, true));
        }

        /// <summary>
        /// Joins the two text lines with <see cref="Environment.NewLine"/> so
        /// <see cref="LetraHelper"/> renders them as separate stacked lines.
        /// </summary>
        private string GetComposedText()
        {
            return !TextLine2TextBox.Enabled || string.IsNullOrEmpty(TextLine2TextBox.Text)
                ? TextTextBox.Text
                : TextTextBox.Text + Environment.NewLine + TextLine2TextBox.Text;
        }

        private LetraHelper.LabelTextSize GetSelectedTextSize()
        {
            return Enum.Parse<LetraHelper.LabelTextSize>((string)SizeComboBox.SelectedItem!);
        }

        /// <summary>
        /// Runs <paramref name="generatePreviewBytes"/> (which calls into LetraHelper's
        /// per-pixel SkiaSharp processing) on a background thread, then decodes the
        /// resulting PNG and assigns it to <paramref name="pictureBox"/> back on the UI
        /// thread, so the form never blocks while a preview is generated.
        /// </summary>
        private static async Task UpdatePreviewAsync(PictureBox pictureBox, Func<byte[]> generatePreviewBytes)
        {
            var previousImage = pictureBox.Image;
            try
            {
                var bitmap = await Task.Run(() =>
                {
                    var previewBytes = generatePreviewBytes();
                    using (var stream = new MemoryStream(previewBytes))
                    using (var decoded = Image.FromStream(stream))
                    {
                        return new Bitmap(decoded);
                    }
                });

                pictureBox.Image = bitmap;
                previousImage?.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to generate preview: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void PrintButton_Click(object sender, EventArgs e)
        {
            if (!File.Exists(PathLabel.Text))
            {
                MessageBox.Show("No image file selected.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var path = PathLabel.Text;
            var noCut = ImageNoCutCheckBox.Checked;
            var preRendered = PreRenderedCheckBox.Checked;
            await PrintJobAsync(PrintButton, () =>
            {
                var imageBytes = File.ReadAllBytes(path);
                return LetraHelper.CreateJob(imageBytes, noCut, preRendered);
            });
        }

        private async void PrintTextButton_Click(object sender, EventArgs e)
        {
            var text = GetComposedText();
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show("No text entered.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var fontFamily = (string)FontFamilyComboBox.SelectedItem!;
            var size = GetSelectedTextSize();
            var style = GetSelectedTextStyle();
            var upperCase = UpperCaseCheckBox.Checked;
            var widthScale = (float)WidthScaleNumericUpDown.Value;
            var boxStyle = GetSelectedBoxStyle();

            await PrintJobAsync(PrintTextButton, () =>
                LetraHelper.CreateJob(text, fontFamily, size, style, upperCase, widthScale, boxStyle, true));
        }

        /// <summary>
        /// Builds the print job on a background thread via <paramref name="buildJob"/> (image
        /// loading / text rendering are all CPU-bound SkiaSharp work, kept off the UI thread),
        /// then streams it to the selected device via <see cref="LetraPrinter"/>.
        /// </summary>
        private async Task PrintJobAsync(Button triggerButton, Func<List<byte[]>> buildJob)
        {
            if (BluetoothDevicesListBox.SelectedItem is not BluetoothDevice device)
            {
                MessageBox.Show("No device selected.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            triggerButton.Enabled = false;
            try
            {
                var job = await Task.Run(buildJob);
                var result = await LetraPrinter.PrintAsync(device, job);
                if (result.Printed)
                {
                    MessageBox.Show(result.Message, "Print result", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(result.Message, "Print failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                triggerButton.Enabled = true;
            }
        }
    }
}
