using System.Text.Json;

namespace KeyboardBridge.Windows.Settings;

public sealed record WindowsAppSettings(bool AutoConnect);

public sealed class WindowsAppSettingsStore
{
    private const string AppDataFolderName = "okfa";
    private const string LegacyAppDataFolderName = "KeyboardBridge.Windows";
    private readonly string _filePath;

    public WindowsAppSettingsStore()
    {
        var root = ResolveAppDataRoot();
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "ui-settings.json");
        Settings = Load() ?? new WindowsAppSettings(AutoConnect: true);
        Save();
    }

    public WindowsAppSettings Settings { get; private set; }

    public void UpdateAutoConnect(bool autoConnect)
    {
        Settings = Settings with { AutoConnect = autoConnect };
        Save();
    }

    private WindowsAppSettings? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WindowsAppSettings>(File.ReadAllText(_filePath));
        }
        catch
        {
            return null;
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(_filePath, json);
    }

    private static string ResolveAppDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var currentRoot = Path.Combine(localAppData, AppDataFolderName);
        if (Directory.Exists(currentRoot))
        {
            return currentRoot;
        }

        var legacyRoot = Path.Combine(localAppData, LegacyAppDataFolderName);
        return Directory.Exists(legacyRoot) ? legacyRoot : currentRoot;
    }
}
