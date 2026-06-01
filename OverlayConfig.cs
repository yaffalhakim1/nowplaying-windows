using System.Text.Json;

namespace NowOnTaskbar;

public class OverlayConfig
{
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 9f;
    public int FontStyle { get; set; } = 0;
    public int MediaTextColorArgb { get; set; } = unchecked((int)0xF0FFFFFF);
    public int MediaTextAlpha { get; set; } = 255;
    public int NotifTextColorArgb { get; set; } = unchecked((int)0xFFB4DCFF);
    public int NotifTextAlpha { get; set; } = 255;
    public bool ShowBackground { get; set; } = true;
    public int BackgroundColorArgb { get; set; } = unchecked((int)0xB41A1A2E);
    public int BackgroundAlpha { get; set; } = 180;
    public bool NotificationsEnabled { get; set; } = true;

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private static readonly string _settingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NowOnTaskbar");

    private static readonly string _settingsPath =
        Path.Combine(_settingsDir, "settings.json");

    public static OverlayConfig Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<OverlayConfig>(json, _jsonOptions) ?? new OverlayConfig();
            }
        }
        catch { }
        return new OverlayConfig();
    }

    public void Save()
    {
        try
        {
            if (!Directory.Exists(_settingsDir))
                Directory.CreateDirectory(_settingsDir);
            var json = JsonSerializer.Serialize(this, _jsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }
}
