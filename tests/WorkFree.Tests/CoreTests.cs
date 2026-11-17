using WorkFree.Core;

namespace WorkFree.Tests;

public class ParsingTests
{
    [Fact]
    public void ParsePorcelain_reads_main_linked_locked_and_prunable()
    {
        var root = OperatingSystem.IsWindows() ? "C:/repos" : "/repos";
        var text = $"""
            worktree {root}/app
            HEAD 1111111111111111111111111111111111111111
            branch refs/heads/main

            worktree {root}/app-feature
            HEAD 2222222222222222222222222222222222222222
            branch refs/heads/feature/x
            locked on a usb stick

            worktree {root}/app-gone
            HEAD 3333333333333333333333333333333333333333
            detached
            prunable gitdir file points to non-existent location

            """;

        var list = WorktreeScanner.ParsePorcelain(text);

        Assert.Equal(3, list.Count);
        Assert.True(list[0].IsMain);
        Assert.Equal("main", list[0].Branch);
        Assert.Equal("app", list[0].RepoName);

        Assert.False(list[1].IsMain);
        Assert.Equal("feature/x", list[1].Branch);
        Assert.True(list[1].IsLocked);
        Assert.Equal("on a usb stick", list[1].LockReason);
        Assert.Equal("app", list[1].RepoName);

        Assert.Null(list[2].Branch);
        Assert.True(list[2].IsPrunable);
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5.0 GB")]
    public void Size_formats(long bytes, string expected) => Assert.Equal(expected, Format.Size(bytes));

    [Theory]
    [InlineData("12h", 0.5)]
    [InlineData("30d", 30)]
    [InlineData("2w", 14)]
    [InlineData("3mo", 90)]
    [InlineData("1y", 365)]
    public void Duration_parses(string text, double days)
    {
        Assert.True(Format.TryParseDuration(text, out var span));
        Assert.Equal(days, span.TotalDays);
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("30")]
    [InlineData("-3d")]
    public void Duration_rejects_garbage(string text) => Assert.False(Format.TryParseDuration(text, out _));
}

public class ScanAndRemoveTests
{
    private static List<Worktree> Scan(string root) => WorktreeScanner.Scan([root]);
    private static Worktree Named(IEnumerable<Worktree> list, string name) => list.Single(w => w.Name == name);

    [Fact]
    public void Scan_finds_repositories_nested_in_a_folder_of_another_repository()
    {
        using var outer = new TempRepo();
        var folder = Path.Combine(outer.Main, "sandbox");
        var inner = Path.Combine(folder, "inner");
        Directory.CreateDirectory(inner);
        TempRepo.Git(inner, "init", "-q", "-b", "main");
        TempRepo.Git(inner, "-c", "user.name=t", "-c", "user.email=t@example.com", "commit", "-q", "--allow-empty", "-m", "init");
        TempRepo.Git(inner, "worktree", "add", "-q", Path.Combine(folder, "inner-feature"), "-b", "feature");

        var found = Scan(folder);

        Assert.Contains(found, w => w.Name == "inner-feature" && !w.IsMain);
        Assert.Contains(found, w => PathUtil.Same(w.Path, outer.Main));
    }

    [Fact]
    public void Scan_finds_linked_worktrees_from_a_parent_directory()
    {
        using var repo = new TempRepo();
        repo.AddWorktree("feature-a");
        var dirty = repo.AddWorktree("feature-b");
        File.WriteAllText(Path.Combine(dirty, "scratch.txt"), new string('x', 4096));

        var all = Scan(repo.Root);

        Assert.Equal(3, all.Count);
        Assert.True(Named(all, "main").IsMain);
        var a = Named(all, "feature-a");
        Assert.Equal("feature-a", a.Branch);
        Assert.Equal(0, a.DirtyCount);
        Assert.Equal("clean", a.State);
        Assert.True(a.SizeBytes > 0);
        Assert.NotNull(a.LastActivity);
        var b = Named(all, "feature-b");
        Assert.Equal(1, b.DirtyCount);
        Assert.True(b.SizeBytes >= 4096);
    }

    [Fact]
    public void Scan_from_inside_a_worktree_finds_the_whole_repository()
    {
        using var repo = new TempRepo();
        var wt = repo.AddWorktree("feature-a");

        Assert.Equal(2, Scan(wt).Count);
    }

    [Fact]
    public void Scan_does_not_list_a_repository_twice()
    {
        using var repo = new TempRepo();
        var wt = repo.AddWorktree("feature-a");

        Assert.Equal(2, WorktreeScanner.Scan([repo.Root, repo.Main, wt]).Count);
    }

    [Fact]
    public void Remove_deletes_a_clean_worktree_and_keeps_its_branch()
    {
        using var repo = new TempRepo();
        var path = repo.AddWorktree("feature-a");

        var result = WorktreeRemover.Remove(Named(Scan(repo.Root), "feature-a"));

        Assert.True(result.Success, result.Message);
        Assert.True(result.FreedBytes > 0);
        Assert.False(Directory.Exists(path));
        Assert.Single(Scan(repo.Root));
        Assert.Contains("feature-a", GitCli.Run(repo.Main, "branch", "--list", "feature-a").StdOut);
    }

    [Fact]
    public void Remove_refuses_dirty_worktree_unless_forced()
    {
        using var repo = new TempRepo();
        var path = repo.AddWorktree("feature-a");
        File.WriteAllText(Path.Combine(path, "scratch.txt"), "unsaved work");
        var wt = Named(Scan(repo.Root), "feature-a");

        var refused = WorktreeRemover.Remove(wt);
        Assert.False(refused.Success);
        Assert.True(File.Exists(Path.Combine(path, "scratch.txt")));

        var forced = WorktreeRemover.Remove(wt, force: true);
        Assert.True(forced.Success, forced.Message);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Remove_does_not_delete_work_that_appeared_after_the_scan()
    {
        using var repo = new TempRepo();
        var path = repo.AddWorktree("feature-a");
        var wt = Named(Scan(repo.Root), "feature-a");
        File.WriteAllText(Path.Combine(path, "late.txt"), "written after the scan");

        var result = WorktreeRemover.Remove(wt);

        Assert.False(result.Success);
        Assert.True(File.Exists(Path.Combine(path, "late.txt")));
    }

    [Fact]
    public void Remove_refuses_the_main_worktree()
    {
        using var repo = new TempRepo();

        var result = WorktreeRemover.Remove(Named(Scan(repo.Root), "main"), force: true);

        Assert.False(result.Success);
        Assert.True(Directory.Exists(repo.Main));
    }

    [Fact]
    public void Remove_can_delete_the_branch_too()
    {
        using var repo = new TempRepo();
        repo.AddWorktree("feature-a");

        var result = WorktreeRemover.Remove(Named(Scan(repo.Root), "feature-a"), deleteBranch: true);

        Assert.True(result.Success, result.Message);
        Assert.Equal("", GitCli.Run(repo.Main, "branch", "--list", "feature-a").StdOut.Trim());
    }

    [Fact]
    public void Manually_deleted_worktree_shows_as_missing_and_is_pruned()
    {
        using var repo = new TempRepo();
        var path = repo.AddWorktree("feature-a");
        PathUtil.DeleteDirectory(path);

        var wt = Named(Scan(repo.Root), "feature-a");
        Assert.True(wt.IsStale);
        Assert.Equal("missing", wt.State);

        Assert.True(WorktreeRemover.Remove(wt).Success);
        Assert.Single(Scan(repo.Root));
    }

    [Fact]
    public void Locked_worktree_is_refused_without_force()
    {
        using var repo = new TempRepo();
        var path = repo.AddWorktree("feature-a");
        TempRepo.Git(repo.Main, "worktree", "lock", path);
        var wt = Named(Scan(repo.Root), "feature-a");

        Assert.Equal("locked", wt.State);
        Assert.False(WorktreeRemover.Remove(wt).Success);
        Assert.True(WorktreeRemover.Remove(wt, force: true).Success);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void Paths_reached_through_a_symlink_count_as_the_same_folder()
    {
        using var repo = new TempRepo();
        var real = repo.AddWorktree("feature-a");
        var link = Path.Combine(repo.Root, "link-to-root");
        try { Directory.CreateSymbolicLink(link, repo.Root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // Windows without symlink rights

        var viaLink = Path.Combine(link, "feature-a");

        Assert.True(PathUtil.Same(viaLink, real));
        Assert.True(PathUtil.IsUnder(Path.Combine(viaLink, "src"), real));
        Assert.False(PathUtil.Same(viaLink, repo.Main));
        Assert.Equal("feature-a", WorktreeScanner.Find(viaLink)?.Branch);
    }

    [Fact]
    public void Find_matches_only_a_worktree_root()
    {
        using var repo = new TempRepo();
        var path = repo.AddWorktree("feature-a");
        Directory.CreateDirectory(Path.Combine(path, "sub"));

        Assert.Equal("feature-a", WorktreeScanner.Find(path)?.Branch);
        Assert.Null(WorktreeScanner.Find(Path.Combine(path, "sub")));
        Assert.Null(WorktreeScanner.Find(Path.Combine(repo.Root, "nope")));
    }
}

public class CleanPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);

    private static Worktree Wt(string name, int? dirty = 0, int ageDays = 10, bool main = false, bool locked = false, bool exists = true) => new()
    {
        Path = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "wf-policy", name)),
        IsMain = main,
        IsLocked = locked,
        Exists = exists,
        DirtyCount = dirty,
        LastActivity = Now.AddDays(-ageDays),
    };

    [Fact]
    public void Default_policy_takes_clean_and_missing_only()
    {
        var all = new[]
        {
            Wt("main", main: true), Wt("clean"), Wt("dirty", dirty: 2), Wt("unknown", dirty: null),
            Wt("locked", locked: true), Wt("gone", exists: false),
        };

        var picked = new CleanPolicy().Select(all, Now).Select(w => w.Name).ToList();

        Assert.Equal(["clean", "gone"], picked);
    }

    [Fact]
    public void Older_than_filters_recent_worktrees()
    {
        var all = new[] { Wt("old", ageDays: 40), Wt("fresh", ageDays: 2) };

        var picked = new CleanPolicy { OlderThan = TimeSpan.FromDays(30) }.Select(all, Now);

        Assert.Equal("old", Assert.Single(picked).Name);
    }

    [Fact]
    public void Include_dirty_and_current_directory_guard()
    {
        var here = Wt("here");
        var all = new[] { here, Wt("dirty", dirty: 3) };

        var picked = new CleanPolicy { IncludeDirty = true }.Select(all, Now, Path.Combine(here.Path, "src"));

        Assert.Equal("dirty", Assert.Single(picked).Name);
    }
}
