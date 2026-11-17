namespace WorkFree.Core;

public sealed class Worktree
{
    /// <summary>Path of the repository's main worktree (or the bare repository).</summary>
    public string RepoPath { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Head { get; set; }
    /// <summary>Short branch name, or null when HEAD is detached.</summary>
    public string? Branch { get; set; }
    public bool IsMain { get; set; }
    public bool IsBare { get; set; }
    public bool IsLocked { get; set; }
    public string? LockReason { get; set; }
    public bool IsPrunable { get; set; }
    public bool Exists { get; set; }
    public long? SizeBytes { get; set; }
    /// <summary>Number of uncommitted or untracked entries; null when not checked.</summary>
    public int? DirtyCount { get; set; }
    public DateTimeOffset? LastActivity { get; set; }

    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>True when the directory is gone and only git's bookkeeping remains.</summary>
    public bool IsStale => !IsMain && (IsPrunable || !Exists);

    public string State =>
        IsMain ? "main"
        : IsStale ? "missing"
        : IsLocked ? "locked"
        : DirtyCount is null ? "unknown"
        : DirtyCount > 0 ? $"dirty ({DirtyCount})"
        : "clean";
}
