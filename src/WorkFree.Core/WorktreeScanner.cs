namespace WorkFree.Core;

public sealed class ScanOptions
{
    /// <summary>How many directory levels below each root to search for repositories.</summary>
    public int MaxDepth { get; init; } = 5;
    public bool IncludeSize { get; init; } = true;
    public bool IncludeStatus { get; init; } = true;
}

public static class WorktreeScanner
{
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "$RECYCLE.BIN", "System Volume Information", ".venv", "__pycache__",
        ".nuget", ".npm", ".cargo", ".rustup",
    };

    /// <summary>Finds every repository under the given roots and returns all of their worktrees.</summary>
    public static List<Worktree> Scan(IEnumerable<string> roots, ScanOptions? options = null)
    {
        options ??= new ScanOptions();
        var seenRepos = new HashSet<string>(PathUtil.Comparer);
        var result = new List<Worktree>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            var full = PathUtil.Normalize(root);

            // A root inside a repository counts as that repository, and may still hold other repositories beneath it.
            var candidates = FindRepositories(full, options.MaxDepth);
            if (!candidates.Contains(full) && GitCli.Run(full, "rev-parse", "--git-dir").Ok) candidates.Insert(0, full);

            foreach (var candidate in candidates)
            {
                var common = GitCli.Run(candidate, "rev-parse", "--path-format=absolute", "--git-common-dir");
                if (!common.Ok) continue;
                if (!seenRepos.Add(PathUtil.Normalize(common.StdOut.Trim()))) continue;

                var list = GitCli.Run(candidate, "worktree", "list", "--porcelain");
                if (!list.Ok) continue;
                result.AddRange(ParsePorcelain(list.StdOut));
            }
        }

        Parallel.ForEach(result, wt => Enrich(wt, options));
        return result;
    }

    /// <summary>Looks up the worktree whose root is exactly <paramref name="path"/>.</summary>
    public static Worktree? Find(string path, ScanOptions? options = null)
    {
        if (!Directory.Exists(path)) return null;
        var all = Scan([path], new ScanOptions { MaxDepth = 0, IncludeSize = false, IncludeStatus = false });
        var match = all.FirstOrDefault(w => PathUtil.Same(w.Path, path));
        if (match is not null) Enrich(match, options ?? new ScanOptions());
        return match;
    }

    /// <summary>Walks a directory tree and yields directories that look like git repositories or worktrees.</summary>
    public static List<string> FindRepositories(string root, int maxDepth)
    {
        var found = new List<string>();
        Walk(root, 0);
        return found;

        void Walk(string dir, int depth)
        {
            if (LooksLikeRepository(dir))
            {
                found.Add(dir);
                return;
            }
            if (depth >= maxDepth) return;

            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(dir).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

            foreach (var child in children)
            {
                if (SkipDirs.Contains(Path.GetFileName(child))) continue;
                try
                {
                    if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)) continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                Walk(child, depth + 1);
            }
        }
    }

    private static bool LooksLikeRepository(string dir)
    {
        var dotGit = Path.Combine(dir, ".git");
        if (Directory.Exists(dotGit) || File.Exists(dotGit)) return true;
        // Bare repository: HEAD file plus objects/ and refs/ directories.
        return File.Exists(Path.Combine(dir, "HEAD"))
            && Directory.Exists(Path.Combine(dir, "objects"))
            && Directory.Exists(Path.Combine(dir, "refs"));
    }

    /// <summary>Parses the output of <c>git worktree list --porcelain</c>. The first entry is the main worktree.</summary>
    public static List<Worktree> ParsePorcelain(string text)
    {
        var list = new List<Worktree>();
        Worktree? current = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                current = null;
                continue;
            }

            var space = line.IndexOf(' ');
            var key = space < 0 ? line : line[..space];
            var value = space < 0 ? "" : line[(space + 1)..];

            if (key == "worktree")
            {
                current = new Worktree { Path = PathUtil.Normalize(value), IsMain = list.Count == 0 };
                list.Add(current);
                continue;
            }
            if (current is null) continue;

            switch (key)
            {
                case "HEAD": current.Head = value; break;
                case "branch":
                    current.Branch = value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value["refs/heads/".Length..] : value;
                    break;
                case "bare": current.IsBare = true; break;
                case "locked":
                    current.IsLocked = true;
                    current.LockReason = value.Length > 0 ? value : null;
                    break;
                case "prunable": current.IsPrunable = true; break;
            }
        }

        if (list.Count > 0)
        {
            var main = list[0];
            foreach (var wt in list)
            {
                wt.RepoPath = main.Path;
                wt.RepoName = Path.GetFileName(main.Path);
            }
        }
        return list;
    }

    public static void Enrich(Worktree wt, ScanOptions options)
    {
        wt.Exists = Directory.Exists(wt.Path);
        if (!wt.Exists) return;

        wt.LastActivity = LastActivity(wt.Path);
        if (options.IncludeStatus && !wt.IsBare)
        {
            var status = GitCli.Run(wt.Path, "status", "--porcelain");
            if (status.Ok)
                wt.DirtyCount = status.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        }
        if (options.IncludeSize) wt.SizeBytes = PathUtil.DirectorySize(wt.Path);
    }

    /// <summary>Most recent git activity in a worktree, judged by its index, HEAD and reflog timestamps.</summary>
    private static DateTimeOffset? LastActivity(string worktreePath)
    {
        try
        {
            var gitDir = ResolveGitDir(worktreePath);
            DateTime? latest = null;
            foreach (var name in new[] { "index", "HEAD", Path.Combine("logs", "HEAD") })
            {
                var file = Path.Combine(gitDir, name);
                if (!File.Exists(file)) continue;
                var time = File.GetLastWriteTimeUtc(file);
                if (latest is null || time > latest) latest = time;
            }
            latest ??= Directory.GetLastWriteTimeUtc(worktreePath);
            return new DateTimeOffset(latest.Value, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ResolveGitDir(string worktreePath)
    {
        var dotGit = Path.Combine(worktreePath, ".git");
        if (Directory.Exists(dotGit)) return dotGit;
        if (File.Exists(dotGit))
        {
            var first = File.ReadLines(dotGit).FirstOrDefault() ?? "";
            const string prefix = "gitdir:";
            if (first.StartsWith(prefix, StringComparison.Ordinal))
                return Path.GetFullPath(first[prefix.Length..].Trim(), worktreePath);
        }
        return worktreePath;
    }
}
