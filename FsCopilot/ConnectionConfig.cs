namespace FsCopilot;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

public class ConnectionConfig
{
    public string ServerAddress { get; set; } = "p2p.fscopilot.com";
    public string Username { get; set; } = Environment.UserName;
    public string PeerId { get; set; } = Random.String(8);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = ConnectionConfigJsonContext.Default
    };

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FsCopilot", "config.json");

    public static ConnectionConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<ConnectionConfig>(json, JsonOptions);
                if (cfg != null) return cfg;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Config] Failed to load config");
        }
        return new ConnectionConfig();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Config] Failed to save config to {Path}", ConfigPath);
        }
    }
}

[JsonSerializable(typeof(ConnectionConfig))]
internal partial class ConnectionConfigJsonContext : JsonSerializerContext
{
}