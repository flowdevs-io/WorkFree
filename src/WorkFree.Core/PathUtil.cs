namespace WorkFree.Core;

public static class PathUtil
{
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool Same(string a, string b) => Comparer.Equals(Normalize(a), Normalize(b));

    /// <summary>True when <paramref name="path"/> is <paramref name="parent"/> or lies beneath it.</summary>
    public static bool IsUnder(string path, string parent)
    {
        var p = Normalize(path);
        var root = Normalize(parent);
        if (Comparer.Equals(p, root)) return true;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return p.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>Total size of the files under a directory. Symlinks and junctions are not followed.</summary>
    public static long DirectorySize(string path)
    {
        long total = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                try { total += file.Length; }
                catch (IOException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return total;
    }

    /// <summary>
    /// Deletes a directory tree, clearing read-only flags as it goes. Symlinks and junctions
    /// are removed as links; their targets are left untouched.
    /// </summary>
    public static void DeleteDirectory(string path)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return;
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            dir.Delete(false);
            return;
        }
        foreach (var entry in dir.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo sub)
            {
                DeleteDirectory(sub.FullName);
            }
            else
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReadOnly)) entry.Attributes = FileAttributes.Normal;
                entry.Delete();
            }
        }
        if (dir.Attributes.HasFlag(FileAttributes.ReadOnly)) dir.Attributes = FileAttributes.Directory;
        dir.Delete(false);
    }
}
