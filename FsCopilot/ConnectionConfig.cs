namespace FsCopilot;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

public class ConnectionConfig
{
    public string ServerAddress { get; set; } = "p2p.fscopilot.com";
    public string Username { get; set; } = Environment.UserName;
    public string PeerId { get; set; } = Random.String(8);

    /// <summary>
    /// How peers link: <see cref="ConnectionModes.Automatic"/> tries a direct link and falls back
    /// to the relay, <see cref="ConnectionModes.Direct"/> never uses the relay,
    /// <see cref="ConnectionModes.Relay"/> never tries a direct link. The command-line switches
    /// (--p2p, --relay, --no-direct) override it for one run.
    /// </summary>
    public string ConnectionMode { get; set; } = ConnectionModes.Automatic;

    /// <summary>Share ground vehicles along with AI aircraft when hosting traffic (--traffic-ground).</summary>
    public bool ShareGroundVehicles { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = ConnectionConfigJsonContext.Default
    };

    public static string ConfigPath => Path.Combine(
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

    /// <summary>Writes the file; false when it could not be written, so the caller can say so.</summary>
    public bool Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(ConfigPath, json);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Config] Failed to save config to {Path}", ConfigPath);
            return false;
        }
    }
}

public static class ConnectionModes
{
    public const string Automatic = "Automatic";
    public const string Direct = "Direct";
    public const string Relay = "Relay";

    public static string Normalize(string? mode) =>
        string.Equals(mode, Direct, StringComparison.OrdinalIgnoreCase) ? Direct :
        string.Equals(mode, Relay, StringComparison.OrdinalIgnoreCase) ? Relay :
        Automatic;
}

[JsonSerializable(typeof(ConnectionConfig))]
internal partial class ConnectionConfigJsonContext : JsonSerializerContext
{
}