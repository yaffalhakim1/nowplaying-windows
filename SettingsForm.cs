namespace NowOnTaskbar;

public class SettingsForm : Form
{
    private readonly OverlayConfig _config;
    private Font _previewFont;
    private Color _mediaTextColor;
    private Color _notifTextColor;
    private Color _bgColor;
    private Color _chromaKeyColor;
    private Label _fontLabel = new();
    private Label _chromaKeyLabel = new();
    private Label _mediaColorLabel = new();
    private Label _notifColorLabel = new();
    private Label _bgColorLabel = new();
    private CheckBox _bgToggle = new();
    private Panel _previewPanel = new();
    private TrackBar _mediaAlphaSlider = new();
    private TrackBar _notifAlphaSlider = new();
    private TrackBar _bgAlphaSlider = new();
    private Label _mediaAlphaLabel = new();
    private Label _notifAlphaLabel = new();
    private Label _bgAlphaLabel = new();

    public SettingsForm(OverlayConfig config)
    {
        _config = config;
        _previewFont = new Font(config.FontFamily, config.FontSize, (FontStyle)config.FontStyle);
        _mediaTextColor = Color.FromArgb(config.MediaTextAlpha, Color.FromArgb(config.MediaTextColorArgb));
        _notifTextColor = Color.FromArgb(config.NotifTextAlpha, Color.FromArgb(config.NotifTextColorArgb));
        _bgColor = Color.FromArgb(config.BackgroundAlpha, Color.FromArgb(config.BackgroundColorArgb));
        _chromaKeyColor = Color.FromArgb(config.TransparencyKeyArgb);

        Text = "NowOnTaskbar Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 560);
        BackColor = SystemColors.Control;

        var y = 16;

        var fontBtn = new Button { Text = "Font...", Location = new Point(16, y), Size = new Size(80, 28) };
        _fontLabel = new Label { Location = new Point(108, y + 4), Size = new Size(290, 20), Text = $"{config.FontFamily} {config.FontSize}pt" };
        fontBtn.Click += (_, _) =>
        {
            using var dlg = new FontDialog { Font = _previewFont, ShowColor = false, MinSize = 6, MaxSize = 24 };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _previewFont = dlg.Font;
                _fontLabel.Text = $"{dlg.Font.FontFamily.Name} {dlg.Font.Size}pt";
                _previewPanel.Invalidate();
            }
        };
        Controls.Add(fontBtn);
        Controls.Add(_fontLabel);
        y += 48;

        var mediaColorBtn = new Button { Text = "Color...", Location = new Point(16, y), Size = new Size(80, 28) };
        _mediaColorLabel = new Label { Location = new Point(108, y + 4), Size = new Size(40, 20), BackColor = _mediaTextColor, BorderStyle = BorderStyle.FixedSingle };
        var mediaAlphaLabel = new Label { Text = "Alpha", Location = new Point(160, y + 4), Size = new Size(40, 20) };
        _mediaAlphaSlider = new TrackBar { Minimum = 0, Maximum = 255, Value = _mediaTextColor.A, Location = new Point(200, y), Size = new Size(140, 30), TickFrequency = 16 };
        _mediaAlphaLabel = new Label { Location = new Point(348, y + 4), Size = new Size(40, 20), Text = _mediaTextColor.A.ToString() };
        _mediaAlphaSlider.ValueChanged += (_, _) => { _mediaAlphaLabel.Text = _mediaAlphaSlider.Value.ToString(); _previewPanel.Invalidate(); };
        mediaColorBtn.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _mediaTextColor, AllowFullOpen = true, FullOpen = true, AnyColor = true };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _mediaTextColor = Color.FromArgb(_mediaAlphaSlider.Value, dlg.Color);
                _mediaColorLabel.BackColor = _mediaTextColor;
                _previewPanel.Invalidate();
            }
        };
        Controls.Add(mediaColorBtn);
        Controls.Add(_mediaColorLabel);
        Controls.Add(mediaAlphaLabel);
        Controls.Add(_mediaAlphaSlider);
        Controls.Add(_mediaAlphaLabel);
        y += 48;

        var notifColorBtn = new Button { Text = "Color...", Location = new Point(16, y), Size = new Size(80, 28) };
        _notifColorLabel = new Label { Location = new Point(108, y + 4), Size = new Size(40, 20), BackColor = _notifTextColor, BorderStyle = BorderStyle.FixedSingle };
        var notifAlphaLabel = new Label { Text = "Alpha", Location = new Point(160, y + 4), Size = new Size(40, 20) };
        _notifAlphaSlider = new TrackBar { Minimum = 0, Maximum = 255, Value = _notifTextColor.A, Location = new Point(200, y), Size = new Size(140, 30), TickFrequency = 16 };
        _notifAlphaLabel = new Label { Location = new Point(348, y + 4), Size = new Size(40, 20), Text = _notifTextColor.A.ToString() };
        _notifAlphaSlider.ValueChanged += (_, _) => { _notifAlphaLabel.Text = _notifAlphaSlider.Value.ToString(); _previewPanel.Invalidate(); };
        notifColorBtn.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _notifTextColor, AllowFullOpen = true, FullOpen = true, AnyColor = true };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _notifTextColor = Color.FromArgb(_notifAlphaSlider.Value, dlg.Color);
                _notifColorLabel.BackColor = _notifTextColor;
                _previewPanel.Invalidate();
            }
        };
        Controls.Add(notifColorBtn);
        Controls.Add(_notifColorLabel);
        Controls.Add(notifAlphaLabel);
        Controls.Add(_notifAlphaSlider);
        Controls.Add(_notifAlphaLabel);
        y += 48;

        _bgToggle = new CheckBox { Text = "Show background", Location = new Point(16, y), Size = new Size(150, 24), Checked = config.ShowBackground };
        _bgToggle.CheckedChanged += (_, _) => { _previewPanel.Invalidate(); };
        Controls.Add(_bgToggle);
        y += 32;

        var bgColorBtn = new Button { Text = "Color...", Location = new Point(16, y), Size = new Size(80, 28) };
        _bgColorLabel = new Label { Location = new Point(108, y + 4), Size = new Size(40, 20), BackColor = _bgColor, BorderStyle = BorderStyle.FixedSingle };
        var bgAlphaLabel = new Label { Text = "Alpha", Location = new Point(160, y + 4), Size = new Size(40, 20) };
        _bgAlphaSlider = new TrackBar { Minimum = 0, Maximum = 255, Value = _bgColor.A, Location = new Point(200, y), Size = new Size(140, 30), TickFrequency = 16 };
        _bgAlphaLabel = new Label { Location = new Point(348, y + 4), Size = new Size(40, 20), Text = _bgColor.A.ToString() };
        _bgAlphaSlider.ValueChanged += (_, _) => { _bgAlphaLabel.Text = _bgAlphaSlider.Value.ToString(); _previewPanel.Invalidate(); };
        bgColorBtn.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _bgColor, AllowFullOpen = true, FullOpen = true, AnyColor = true };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _bgColor = Color.FromArgb(_bgAlphaSlider.Value, dlg.Color);
                _bgColorLabel.BackColor = _bgColor;
                _previewPanel.Invalidate();
            }
        };
        Controls.Add(bgColorBtn);
        Controls.Add(_bgColorLabel);
        Controls.Add(bgAlphaLabel);
        Controls.Add(_bgAlphaSlider);
        Controls.Add(_bgAlphaLabel);
        y += 48;

        var chromaKeyBtn = new Button { Text = "Key color...", Location = new Point(16, y), Size = new Size(80, 28) };
        _chromaKeyLabel = new Label { Location = new Point(108, y + 4), Size = new Size(40, 20), BackColor = _chromaKeyColor, BorderStyle = BorderStyle.FixedSingle };
        chromaKeyBtn.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _chromaKeyColor, AllowFullOpen = true, FullOpen = true, AnyColor = true };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _chromaKeyColor = dlg.Color;
                _chromaKeyLabel.BackColor = _chromaKeyColor;
                _previewPanel.Invalidate();
            }
        };
        Controls.Add(chromaKeyBtn);
        Controls.Add(_chromaKeyLabel);
        y += 48;

        var previewLabel = new Label { Text = "Preview", Location = new Point(16, y), AutoSize = true, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
        Controls.Add(previewLabel);
        y += 24;

        _previewPanel = new Panel { Location = new Point(16, y), Size = new Size(388, 40), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        _previewPanel.Paint += PreviewPanel_Paint;
        Controls.Add(_previewPanel);
        y += 56;

        var saveBtn = new Button { Text = "Save", Location = new Point(228, y), Size = new Size(80, 30), DialogResult = DialogResult.OK };
        var cancelBtn = new Button { Text = "Cancel", Location = new Point(324, y), Size = new Size(80, 30), DialogResult = DialogResult.Cancel };
        var resetBtn = new Button { Text = "Reset Defaults", Location = new Point(16, y), Size = new Size(110, 30) };
        resetBtn.Click += (_, _) =>
        {
            var defaults = new OverlayConfig();
            _previewFont = new Font(defaults.FontFamily, defaults.FontSize, (FontStyle)defaults.FontStyle);
            _mediaTextColor = Color.FromArgb(defaults.MediaTextAlpha, Color.FromArgb(defaults.MediaTextColorArgb));
            _notifTextColor = Color.FromArgb(defaults.NotifTextAlpha, Color.FromArgb(defaults.NotifTextColorArgb));
            _bgColor = Color.FromArgb(defaults.BackgroundAlpha, Color.FromArgb(defaults.BackgroundColorArgb));
            _chromaKeyColor = Color.FromArgb(defaults.TransparencyKeyArgb);
            _bgToggle.Checked = defaults.ShowBackground;
            _fontLabel.Text = $"{defaults.FontFamily} {defaults.FontSize}pt";
            _mediaColorLabel.BackColor = _mediaTextColor;
            _notifColorLabel.BackColor = _notifTextColor;
            _bgColorLabel.BackColor = _bgColor;
            _mediaAlphaSlider.Value = defaults.MediaTextAlpha;
            _notifAlphaSlider.Value = defaults.NotifTextAlpha;
            _bgAlphaSlider.Value = defaults.BackgroundAlpha;
            _chromaKeyColor = Color.FromArgb(defaults.TransparencyKeyArgb);
            _chromaKeyLabel.BackColor = _chromaKeyColor;
            _previewPanel.Invalidate();
        };
        Controls.Add(saveBtn);
        Controls.Add(cancelBtn);
        Controls.Add(resetBtn);

        AcceptButton = saveBtn;
        CancelButton = cancelBtn;
    }

    private void PreviewPanel_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Black);

        if (_bgToggle.Checked)
        {
            using var bgBrush = new SolidBrush(_bgColor);
            g.FillRectangle(bgBrush, _previewPanel.ClientRectangle);
        }

        var mediaDisplay = "♫  Imagine — John Lennon";
        var notifDisplay = "✉  Mom: Dinner at 7?";

        TextRenderer.DrawText(g, mediaDisplay, _previewFont, new Rectangle(0, 0, _previewPanel.Width, 20),
            _mediaTextColor, Color.Transparent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(g, notifDisplay, _previewFont, new Rectangle(0, 20, _previewPanel.Width, 20),
            _notifTextColor, Color.Transparent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    public void ApplyToConfig()
    {
        _config.FontFamily = _previewFont.FontFamily.Name;
        _config.FontSize = _previewFont.Size;
        _config.FontStyle = (int)_previewFont.Style;
        _config.MediaTextColorArgb = Color.FromArgb(255, _mediaTextColor).ToArgb();
        _config.MediaTextAlpha = _mediaAlphaSlider.Value;
        _config.NotifTextColorArgb = Color.FromArgb(255, _notifTextColor).ToArgb();
        _config.NotifTextAlpha = _notifAlphaSlider.Value;
        _config.ShowBackground = _bgToggle.Checked;
        _config.BackgroundColorArgb = Color.FromArgb(255, _bgColor).ToArgb();
        _config.BackgroundAlpha = _bgAlphaSlider.Value;
        _config.TransparencyKeyArgb = Color.FromArgb(255, _chromaKeyColor).ToArgb();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _previewFont.Dispose();
        base.Dispose(disposing);
    }
}
