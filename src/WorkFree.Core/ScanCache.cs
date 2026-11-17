using System.Text.Json;

namespace WorkFree.Core;

/// <summary>The result of one scan, kept so the next start can show something immediately.</summary>
public sealed class ScanSnapshot
{
    public List<string> Roots { get; set; } = [];
    public DateTimeOffset ScannedAt { get; set; }
    public List<Worktree> Worktrees { get; set; } = [];
}

/// <summary>
/// Remembers the last scan on disk. It is only a starting picture: callers still rescan, and removal
/// re-checks every worktree, so a stale entry can never cause a wrong delete.
/// </summary>
public static class ScanCache
{
    /// <summary>Cache file location: beside a custom WORKFREE_CONFIG file, otherwise in the per-user local data folder.</summary>
    public static string FilePath =>
        Environment.GetEnvironmentVariable("WORKFREE_CONFIG") is { Length: > 0 } custom
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(custom))!, "cache.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "workfree", "cache.json");

    /// <summary>Returns the last scan if it covered exactly these folders, otherwise null.</summary>
    public static ScanSnapshot? Load(IReadOnlyList<string> roots)
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var snapshot = JsonSerializer.Deserialize(File.ReadAllText(FilePath), WorkFreeJson.Default.ScanSnapshot);
            if (snapshot is null) return null;
            var wanted = new HashSet<string>(roots.Select(PathUtil.Normalize), PathUtil.Comparer);
            return wanted.SetEquals(snapshot.Roots.Select(PathUtil.Normalize)) ? snapshot : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public static void Save(IReadOnlyList<string> roots, List<Worktree> worktrees)
    {
        try
        {
            var snapshot = new ScanSnapshot { Roots = [.. roots], ScannedAt = DateTimeOffset.Now, Worktrees = worktrees };
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot, WorkFreeJson.Default.ScanSnapshot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is a convenience; failing to write it must never break a scan.
        }
    }
}
