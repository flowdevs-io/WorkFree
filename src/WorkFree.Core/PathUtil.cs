namespace WorkFree.Core;

public static class PathUtil
{
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>True when both paths name the same directory, also when one of them goes through a symlink.</summary>
    public static bool Same(string a, string b) =>
        Comparer.Equals(Normalize(a), Normalize(b)) || Comparer.Equals(RealPath(a), RealPath(b));

    /// <summary>True when <paramref name="path"/> is <paramref name="parent"/> or lies beneath it.</summary>
    public static bool IsUnder(string path, string parent) =>
        IsUnderExact(Normalize(path), Normalize(parent)) || IsUnderExact(RealPath(path), RealPath(parent));

    private static bool IsUnderExact(string p, string root)
    {
        if (Comparer.Equals(p, root)) return true;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return p.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>
    /// The path with every symlinked folder along it resolved, the way git reports paths
    /// (on macOS, /var/... becomes /private/var/...). Parts that do not exist are kept as written.
    /// </summary>
    public static string RealPath(string path)
    {
        var full = Normalize(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return full;

        var current = root;
        foreach (var part in full[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                if (Directory.ResolveLinkTarget(current, returnFinalTarget: true) is { } target) current = target.FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Missing or unreadable: leave this part as written.
            }
        }
        return Path.TrimEndingDirectorySeparator(current);
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
