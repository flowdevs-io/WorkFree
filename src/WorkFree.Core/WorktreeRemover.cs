namespace WorkFree.Core;

public sealed record RemoveResult(Worktree Worktree, bool Success, string Message, long FreedBytes);

public static class WorktreeRemover
{
    /// <summary>
    /// Removes a linked worktree. Without <paramref name="force"/>, worktrees that are locked or
    /// have uncommitted changes are refused. The branch is kept unless <paramref name="deleteBranch"/> is set.
    /// </summary>
    public static RemoveResult Remove(Worktree wt, bool force = false, bool deleteBranch = false)
    {
        if (wt.IsMain) return Fail(wt, "is the main worktree of its repository and cannot be removed");

        if (wt.IsStale)
        {
            var pruned = Prune(wt.RepoPath);
            return pruned.Ok
                ? new RemoveResult(wt, true, "stale entry pruned", 0)
                : Fail(wt, pruned.StdErr.Trim());
        }

        if (PathUtil.IsUnder(Directory.GetCurrentDirectory(), wt.Path))
            return Fail(wt, "contains the current directory");
        if (wt.IsLocked && !force)
            return Fail(wt, "is locked" + (wt.LockReason is null ? "" : $" ({wt.LockReason})"));
        if (wt.DirtyCount is null && !force)
            return Fail(wt, "could not be checked for uncommitted changes");
        if (wt.DirtyCount > 0 && !force)
            return Fail(wt, $"has {wt.DirtyCount} uncommitted change(s)");

        var freed = wt.SizeBytes ?? 0;
        var args = new List<string> { "worktree", "remove" };
        if (force) args.Add("--force");
        if (force && wt.IsLocked) args.Add("--force");
        args.Add(wt.Path);

        var git = GitCli.Run(wt.RepoPath, [.. args]);
        if (!git.Ok || Directory.Exists(wt.Path))
        {
            // git can fail part-way (long paths, files held open, submodules). Finish the job by
            // hand, but only if the worktree is still clean right now: the scan may be minutes old.
            if (!force && Directory.Exists(wt.Path))
            {
                var status = GitCli.Run(wt.Path, "status", "--porcelain");
                if (!status.Ok || status.StdOut.Trim().Length > 0)
                    return Fail(wt, git.Ok ? "has uncommitted changes" : FirstLine(git.StdErr));
            }
            try
            {
                PathUtil.DeleteDirectory(wt.Path);
                Prune(wt.RepoPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var reason = git.Ok ? ex.Message : $"{git.StdErr.Trim()}; {ex.Message}";
                return Fail(wt, reason);
            }
        }

        var message = "removed";
        if (deleteBranch && wt.Branch is not null)
        {
            var branch = GitCli.Run(wt.RepoPath, "branch", force ? "-D" : "-d", wt.Branch);
            message += branch.Ok
                ? $", branch {wt.Branch} deleted"
                : $", branch {wt.Branch} kept ({FirstLine(branch.StdErr)})";
        }
        return new RemoveResult(wt, true, message, freed);
    }

    /// <summary>Drops git's bookkeeping for worktrees whose directories no longer exist.</summary>
    public static GitResult Prune(string repoPath) => GitCli.Run(repoPath, "worktree", "prune");

    private static RemoveResult Fail(Worktree wt, string message) => new(wt, false, message, 0);

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
}

public sealed class CleanPolicy
{
    /// <summary>Only worktrees with no git activity for at least this long.</summary>
    public TimeSpan? OlderThan { get; init; }
    public bool IncludeDirty { get; init; }
    public bool IncludeLocked { get; init; }

    /// <summary>Picks the worktrees a bulk clean should remove.</summary>
    public List<Worktree> Select(IEnumerable<Worktree> worktrees, DateTimeOffset now, string? currentDirectory = null)
    {
        var picked = new List<Worktree>();
        foreach (var wt in worktrees)
        {
            if (wt.IsMain) continue;
            if (wt.IsStale) { picked.Add(wt); continue; }
            if (currentDirectory is not null && PathUtil.IsUnder(currentDirectory, wt.Path)) continue;
            if (wt.IsLocked && !IncludeLocked) continue;
            if (wt.DirtyCount != 0 && !IncludeDirty) continue;
            if (OlderThan is { } age && (wt.LastActivity is null || now - wt.LastActivity < age)) continue;
            picked.Add(wt);
        }
        return picked;
    }
}
