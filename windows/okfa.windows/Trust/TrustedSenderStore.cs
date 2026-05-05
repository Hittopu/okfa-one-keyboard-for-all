using System.Security.Cryptography;

namespace KeyboardBridge.Windows.Trust;

public sealed class TrustedSenderStore
{
    private const string AppDataFolderName = "okfa";
    private const string LegacyAppDataFolderName = "KeyboardBridge.Windows";
    private readonly string _filePath;
    private readonly PersistedState _state;

    public TrustedSenderStore()
    {
        var root = ResolveAppDataRoot();
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "state.json");
        _state = Load() ?? CreateInitialState();
        Save();
    }

    public ulong ClientId => _state.ClientId;

    private PersistedState? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(_filePath));
        }
        catch
        {
            return null;
        }
    }

    private PersistedState CreateInitialState()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var clientId = BitConverter.ToUInt64(bytes);
        return new PersistedState(clientId);
    }

    private void Save()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(_state, new System.Text.Json.JsonSerializerOptions
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

    private sealed record PersistedState(ulong ClientId);
}
