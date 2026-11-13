using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoorsHandler.Desktop.Theming;

/// <summary>User preferences kept between runs in %LOCALAPPDATA%\DoorsHandler\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// The chosen theme. Until the user picks one (first run, or a settings file from an older
    /// version that no longer loads), it matches the current Windows light/dark setting.
    /// </summary>
    public AppTheme Theme { get; set; } = ThemeManager.SystemPrefersDark() ? AppTheme.Dark : AppTheme.Light;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoorsHandler", "settings.json");

    /// <summary>Loads the settings; a missing or unreadable file gives the defaults.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // Corrupt or inaccessible settings must never stop the app from starting.
        }

        return new AppSettings();
    }

    /// <summary>Saves the settings; failures are ignored because preferences are not critical.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Read-only profile, disk full, ...: the theme still applies for this session.
        }
    }
}
