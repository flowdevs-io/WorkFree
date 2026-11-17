using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkFree.Core;

public sealed class Config
{
    /// <summary>Directories searched for repositories when none are given on the command line.</summary>
    public List<string> Roots { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(List<Worktree>))]
[JsonSerializable(typeof(ScanSnapshot))]
public sealed partial class WorkFreeJson : JsonSerializerContext;

public static class ConfigStore
{
    /// <summary>Config file location; override with the WORKFREE_CONFIG environment variable.</summary>
    public static string FilePath =>
        Environment.GetEnvironmentVariable("WORKFREE_CONFIG") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "workfree", "config.json");

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), WorkFreeJson.Default.Config) ?? new Config();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new Config();
    }

    public static void Save(Config config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, WorkFreeJson.Default.Config));
    }
}
