using System.Text.Json;

namespace SerialPortTerminal.Application;

/// <summary>
/// Persistent application preferences that are independent of any particular serial device.
/// </summary>
public sealed class AppSettings
{
    private const string SettingsFileName = "SerialPortTerminal.settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Directory used for automatically captured transport diagnostic logs. Relative paths are
    /// resolved from the application directory so a default installation remains portable.
    /// </summary>
    public string LogFolder { get; set; } = "Logs";

    public string LogFilePrefix { get; set; } = "spt-";

    public int LogFilesToKeep { get; set; } = 25;

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, SettingsFileName);

    public string ResolvedLogFolder => Path.IsPathRooted(LogFolder)
        ? Path.GetFullPath(LogFolder)
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, LogFolder));

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            // A malformed or inaccessible settings file must not prevent use of a diagnostic tool.
            return new AppSettings();
        }
    }

    public void Save()
    {
        Normalize();
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private void Normalize()
    {
        if (string.IsNullOrWhiteSpace(LogFolder))
            LogFolder = "Logs";
        if (string.IsNullOrWhiteSpace(LogFilePrefix))
            LogFilePrefix = "spt-";
        LogFilesToKeep = Math.Max(1, LogFilesToKeep);
    }
}
