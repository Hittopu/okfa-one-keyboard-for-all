using System.Security.Cryptography;
using System.Text.Json;

namespace KeyboardBridge.Windows.Trust;

public sealed class TrustedClientStore
{
    private const string AppDataFolderName = "okfa";
    private const string LegacyAppDataFolderName = "KeyboardBridge.Windows";
    private readonly string _filePath;
    private readonly Dictionary<ulong, TrustedClientRecord> _recordsByClientId = new();

    public TrustedClientStore()
    {
        var root = ResolveAppDataRoot();
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "trusted-clients.json");
        Load();
    }

    public int TrustedCount => _recordsByClientId.Count;

    public bool IsTrusted(ulong clientId) => _recordsByClientId.TryGetValue(clientId, out var record) && record.AutoAccept;

    public void MarkApproved(ulong clientId, string alias)
    {
        var now = DateTimeOffset.UtcNow;
        if (_recordsByClientId.TryGetValue(clientId, out var existing))
        {
            _recordsByClientId[clientId] = existing with
            {
                Alias = alias,
                LastSeenAt = now,
                AutoAccept = true,
            };
        }
        else
        {
            _recordsByClientId[clientId] = new TrustedClientRecord(
                ClientId: clientId,
                Alias: alias,
                FirstApprovedAt: now,
                LastSeenAt: now,
                AutoAccept: true
            );
        }

        Save();
    }

    public void MarkSeen(ulong clientId)
    {
        if (!_recordsByClientId.TryGetValue(clientId, out var existing))
        {
            return;
        }

        _recordsByClientId[clientId] = existing with
        {
            LastSeenAt = DateTimeOffset.UtcNow,
        };

        Save();
    }

    public void Clear()
    {
        _recordsByClientId.Clear();
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var records = JsonSerializer.Deserialize<List<TrustedClientRecord>>(json, JsonOptions()) ?? [];
            _recordsByClientId.Clear();
            foreach (var record in records)
            {
                _recordsByClientId[record.ClientId] = record;
            }
        }
        catch
        {
            _recordsByClientId.Clear();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                _recordsByClientId.Values.OrderBy(static record => record.FirstApprovedAt).ToArray(),
                JsonOptions()
            );
            File.WriteAllText(_filePath, json);
        }
        catch
        {
        }
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

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
    };

    public sealed record TrustedClientRecord(
        ulong ClientId,
        string Alias,
        DateTimeOffset FirstApprovedAt,
        DateTimeOffset LastSeenAt,
        bool AutoAccept
    );
}
