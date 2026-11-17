using Avalonia;
using Avalonia.Headless;
using WorkFree.Core;
using WorkFree.Gui;

namespace WorkFree.Tests;

/// <summary>Keeps tests away from the real user config file.</summary>
internal static class TestConfig
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "workfree-tests", "config-" + Guid.NewGuid().ToString("N"));

    public static void Isolate() => Environment.SetEnvironmentVariable("WORKFREE_CONFIG", Path.Combine(Dir, "config.json"));
}

public class MainViewModelTests
{
    public MainViewModelTests() => TestConfig.Isolate();

    private static Worktree Wt(string name, long size, int? dirty = 0, int ageDays = 1, bool main = false, bool exists = true) => new()
    {
        Path = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "wf-gui", name)),
        RepoPath = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "wf-gui", "main")),
        RepoName = "app",
        Branch = name,
        IsMain = main,
        Exists = exists,
        DirtyCount = dirty,
        SizeBytes = size,
        LastActivity = DateTimeOffset.Now.AddDays(-ageDays),
    };

    private static async Task<MainViewModel> Loaded(params Worktree[] worktrees)
    {
        var vm = new MainViewModel(_ => [.. worktrees]);
        vm.Roots.Clear();
        vm.Roots.Add("fake-root");
        await vm.ScanAsync();
        return vm;
    }

    [Fact]
    public async Task Rows_exclude_main_and_follow_the_chosen_sort()
    {
        var vm = await Loaded(Wt("main", 9999, main: true), Wt("small", 10, ageDays: 30), Wt("big", 5000, ageDays: 2));

        Assert.Equal(["big", "small"], vm.Rows.Select(r => r.Name));
        vm.SortIndex = (int)SortBy.Age;
        Assert.Equal(["small", "big"], vm.Rows.Select(r => r.Name));
        Assert.Contains("2 worktree(s)", vm.Summary);
    }

    [Fact]
    public async Task Select_all_safe_skips_dirty_and_selection_survives_a_resort()
    {
        var vm = await Loaded(Wt("clean", 100), Wt("dirty", 300, dirty: 2), Wt("gone", 0, exists: false));
        Assert.False(vm.CanDelete);

        vm.SelectAllSafe();

        Assert.Equal(["clean", "gone"], vm.SelectedRows.Select(r => r.Name).Order());
        Assert.True(vm.CanDelete);
        Assert.StartsWith("2 selected", vm.SelectionSummary);

        vm.SortIndex = (int)SortBy.Repo;
        Assert.Equal(2, vm.SelectedRows.Count());

        vm.ClearSelection();
        Assert.False(vm.CanDelete);
        Assert.Equal("Nothing selected", vm.SelectionSummary);
    }

    [Fact]
    public async Task Delete_asks_first_and_cancel_leaves_everything_alone()
    {
        using var repo = new TempRepo();
        var path = repo.AddWorktree("feature-a");
        var vm = new MainViewModel();
        vm.Roots.Clear();
        vm.Roots.Add(repo.Root);
        await vm.ScanAsync();
        vm.SelectAllSafe();

        DeleteRequest? asked = null;
        vm.ConfirmDelete = r => { asked = r; return Task.FromResult(DeleteChoice.Cancel); };
        await vm.DeleteSelectedAsync();

        Assert.Equal(new DeleteRequest(1, vm.Rows[0].Model.SizeBytes ?? -1, 0), asked);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public async Task Delete_safe_only_keeps_dirty_worktrees_and_force_removes_them()
    {
        using var repo = new TempRepo();
        var clean = repo.AddWorktree("feature-clean");
        var dirty = repo.AddWorktree("feature-dirty");
        File.WriteAllText(Path.Combine(dirty, "wip.txt"), "unsaved");
        var vm = new MainViewModel();
        vm.Roots.Clear();
        vm.Roots.Add(repo.Root);
        await vm.ScanAsync();
        foreach (var row in vm.Rows) row.IsSelected = true;

        DeleteRequest? asked = null;
        vm.ConfirmDelete = r => { asked = r; return Task.FromResult(DeleteChoice.SafeOnly); };
        await vm.DeleteSelectedAsync();

        Assert.Equal(1, asked!.UnsafeCount);
        Assert.False(Directory.Exists(clean));
        Assert.True(File.Exists(Path.Combine(dirty, "wip.txt")));
        Assert.Equal("feature-dirty", Assert.Single(vm.Rows).Name);
        Assert.Contains("Removed 1 worktree(s)", vm.Status);
        Assert.Contains("Skipped 1", vm.Status);

        vm.Rows[0].IsSelected = true;
        vm.ConfirmDelete = _ => Task.FromResult(DeleteChoice.ForceAll);
        await vm.DeleteSelectedAsync();

        Assert.False(Directory.Exists(dirty));
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Adding_and_removing_a_folder_persists_and_rescans()
    {
        using var repo = new TempRepo();
        repo.AddWorktree("feature-a");
        var vm = new MainViewModel();
        foreach (var existing in vm.Roots.ToList()) await vm.RemoveRootAsync(existing);
        Assert.True(vm.ShowAddFolderHint);

        await vm.AddRootAsync(repo.Root);

        Assert.Single(vm.Rows);
        Assert.False(vm.ShowAddFolderHint);
        Assert.Contains(PathUtil.Normalize(repo.Root), new MainViewModel().Roots);

        await vm.RemoveRootAsync(PathUtil.Normalize(repo.Root));
        Assert.Empty(vm.Rows);
        Assert.DoesNotContain(PathUtil.Normalize(repo.Root), new MainViewModel().Roots);
    }

    [Fact]
    public async Task Prune_clears_missing_worktrees()
    {
        using var repo = new TempRepo();
        PathUtil.DeleteDirectory(repo.AddWorktree("feature-a"));
        var vm = new MainViewModel();
        vm.Roots.Clear();
        vm.Roots.Add(repo.Root);
        await vm.ScanAsync();
        Assert.True(vm.HasMissing);

        await vm.PruneAsync();

        Assert.Empty(vm.Rows);
        Assert.False(vm.HasMissing);
    }

    /// <summary>Renders the real window and dialog off-screen and saves PNGs for a human to look at.</summary>
    [Fact]
    public async Task Window_and_dialog_render()
    {
        var vm = await Loaded(
            Wt("main", 1, main: true),
            Wt("agent-fix-login-timeout", 1_840_000_000, ageDays: 41),
            Wt("agent-refactor-billing", 912_000_000, dirty: 7, ageDays: 3),
            Wt("feature-dark-mode", 640_500_000, ageDays: 12),
            Wt("spike-old-experiment", 0, exists: false, ageDays: 90),
            Wt("hotfix-2291", 88_000_000, ageDays: 0));
        vm.Rows[0].IsSelected = true;
        vm.Rows[2].IsSelected = true;

        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);

        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = vm };
            window.Show();
            window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "main.png"));

            // Mid-meal: two flies swallowed, the third on the tongue.
            vm.Scene.StartMeal(5);
            vm.Scene.Eat();
            vm.Scene.Eat();
            for (var i = 0; i < 400 && vm.Scene.FliesLeft > 3; i++) vm.Scene.Tick();
            for (var i = 0; i < 12; i++) vm.Scene.Tick();
            vm.Frame = vm.Scene.Render();
            vm.ShowFrog = true;
            window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "frog-eating.png"));

            vm.Scene.Celebrate("3 worktree(s) gone, 2.6 GB freed");
            for (var i = 0; i < 20; i++) vm.Scene.Tick();
            vm.Frame = vm.Scene.Render();
            window.CaptureRenderedFrame()!.Save(Path.Combine(dir, "frog-done.png"));
            vm.ShowFrog = false;

            var dialog = ConfirmDialog.Build(new DeleteRequest(3, 2_840_000_000, 1));
            dialog.Show();
            dialog.CaptureRenderedFrame()!.Save(Path.Combine(dir, "confirm.png"));
            dialog.Close();
            window.Close();
        }, CancellationToken.None);
        // The await above resumes on the session's own UI thread; disposing from there would wait on itself.
        await Task.Run(session.Dispose);

        Assert.True(new FileInfo(Path.Combine(dir, "main.png")).Length > 5000);
    }
}

public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
