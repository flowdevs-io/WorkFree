using System.CommandLine;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using WorkFree;
using WorkFree.Core;

Console.OutputEncoding = Encoding.UTF8;
// Piped output has no terminal width; don't wrap long paths at 80 columns.
if (Console.IsOutputRedirected) AnsiConsole.Profile.Width = 240;

// With no arguments: the interactive screen in a terminal, a plain listing when piped.
if (args.Length == 0)
{
    return Console.IsInputRedirected || Console.IsOutputRedirected
        ? Commands.List(Commands.ResolveRoots([]), 5, json: false, all: false, noSize: false)
        : Tui.Run(Commands.ResolveRoots([]), 5);
}

Argument<string[]> Roots() => new("roots")
{
    Arity = ArgumentArity.ZeroOrMore,
    Description = "Directories to search for repositories. Defaults to the configured roots, then the current directory.",
};
Option<int> Depth() => new("--depth")
{
    Description = "How many directory levels below each root to search.",
    DefaultValueFactory = _ => 5,
};

var root = new RootCommand("WorkFree - find git worktrees, see how much disk they use, and remove the ones you no longer need.");

{
    var roots = Roots();
    var depth = Depth();
    var cmd = new Command("tui", "Open the interactive screen (the default when run with no arguments).") { roots, depth };
    cmd.SetAction(r => Tui.Run(Commands.ResolveRoots(r.GetValue(roots)), r.GetValue(depth)));
    root.Subcommands.Add(cmd);
}
{
    var cmd = new Command("gui", "Open the desktop window (needs workfree-gui installed next to this program).");
    cmd.SetAction(_ => Commands.Gui());
    root.Subcommands.Add(cmd);
}
{
    var roots = Roots();
    var depth = Depth();
    var json = new Option<bool>("--json") { Description = "Print machine-readable JSON." };
    var all = new Option<bool>("--all") { Description = "Include each repository's main worktree." };
    var noSize = new Option<bool>("--no-size") { Description = "Skip measuring disk usage (faster)." };
    var cmd = new Command("list", "List worktrees with size, last activity and state.") { roots, depth, json, all, noSize };
    cmd.Aliases.Add("ls");
    cmd.SetAction(r => Commands.List(Commands.ResolveRoots(r.GetValue(roots)), r.GetValue(depth), r.GetValue(json), r.GetValue(all), r.GetValue(noSize)));
    root.Subcommands.Add(cmd);
}
{
    var paths = new Argument<string[]>("paths") { Arity = ArgumentArity.OneOrMore, Description = "Worktree directories to remove." };
    var force = new Option<bool>("--force", "-f") { Description = "Remove even with uncommitted changes or a lock. Uncommitted work is lost." };
    var deleteBranch = new Option<bool>("--delete-branch") { Description = "Also delete the worktree's branch (only if merged, unless --force)." };
    var cmd = new Command("remove", "Remove specific worktrees.") { paths, force, deleteBranch };
    cmd.Aliases.Add("rm");
    cmd.SetAction(r => Commands.Remove(r.GetValue(paths)!, r.GetValue(force), r.GetValue(deleteBranch)));
    root.Subcommands.Add(cmd);
}
{
    var roots = Roots();
    var depth = Depth();
    var olderThan = new Option<string?>("--older-than") { Description = "Only worktrees with no git activity for this long, e.g. 12h, 30d, 2w, 3mo, 1y." };
    var includeDirty = new Option<bool>("--include-dirty") { Description = "Also remove worktrees with uncommitted changes. That work is lost." };
    var deleteBranch = new Option<bool>("--delete-branch") { Description = "Also delete each worktree's branch if it is merged." };
    var dryRun = new Option<bool>("--dry-run") { Description = "Show what would be removed and stop." };
    var yes = new Option<bool>("--yes", "-y") { Description = "Do not ask for confirmation." };
    var cmd = new Command("clean", "Remove every clean, unlocked worktree (and prune missing ones) under the roots.")
        { roots, depth, olderThan, includeDirty, deleteBranch, dryRun, yes };
    cmd.SetAction(r => Commands.Clean(
        Commands.ResolveRoots(r.GetValue(roots)), r.GetValue(depth), r.GetValue(olderThan),
        r.GetValue(includeDirty), r.GetValue(deleteBranch), r.GetValue(dryRun), r.GetValue(yes)));
    root.Subcommands.Add(cmd);
}
{
    var roots = Roots();
    var depth = Depth();
    var cmd = new Command("prune", "Drop git's records of worktrees whose directories no longer exist.") { roots, depth };
    cmd.SetAction(r => Commands.Prune(Commands.ResolveRoots(r.GetValue(roots)), r.GetValue(depth)));
    root.Subcommands.Add(cmd);
}
{
    var cmd = new Command("roots", "Manage the directories searched by default.");
    var list = new Command("list", "Show configured roots.");
    list.SetAction(_ => Commands.RootsList());
    var addPath = new Argument<string>("path");
    var add = new Command("add", "Add a root directory.") { addPath };
    add.SetAction(r => Commands.RootsAdd(r.GetValue(addPath)!));
    var removePath = new Argument<string>("path");
    var remove = new Command("remove", "Remove a root directory.") { removePath };
    remove.SetAction(r => Commands.RootsRemove(r.GetValue(removePath)!));
    cmd.Subcommands.Add(list);
    cmd.Subcommands.Add(add);
    cmd.Subcommands.Add(remove);
    root.Subcommands.Add(cmd);
}

return root.Parse(args).Invoke();

internal static class Commands
{
    public static IReadOnlyList<string> ResolveRoots(string[]? given)
    {
        if (given is { Length: > 0 }) return given;
        var configured = ConfigStore.Load().Roots;
        return configured.Count > 0 ? configured : [Directory.GetCurrentDirectory()];
    }

    public static int Gui()
    {
        var name = OperatingSystem.IsWindows() ? "workfree-gui.exe" : "workfree-gui";
        var path = Path.Combine(AppContext.BaseDirectory, name);
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error[/] the desktop app was not found at {path}. Download the workfree-gui package for your platform, or run 'workfree' for the terminal screen.");
            return 1;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = false })?.Dispose();
        return 0;
    }

    public static int List(IReadOnlyList<string> roots, int depth, bool json, bool all, bool noSize)
    {
        var worktrees = WorktreeScanner.Scan(roots, new ScanOptions { MaxDepth = depth, IncludeSize = !noSize });
        var shown = worktrees.Where(w => all || !w.IsMain)
            .OrderBy(w => w.RepoName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(w => w.IsMain)
            .ThenByDescending(w => w.SizeBytes ?? 0)
            .ToList();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(shown, WorkFreeJson.Default.ListWorktree));
            return 0;
        }
        if (shown.Count == 0)
        {
            AnsiConsole.MarkupLineInterpolated($"No linked worktrees found under: {string.Join(", ", roots)}");
            return 0;
        }

        AnsiConsole.Write(BuildTable(shown));
        var linked = shown.Where(w => !w.IsMain).ToList();
        var reclaimable = linked.Where(TuiState.IsSafe).Sum(w => w.SizeBytes ?? 0);
        var summary = $"{linked.Count} linked worktree(s) in {linked.Select(w => w.RepoPath).Distinct(PathUtil.Comparer).Count()} repo(s)";
        if (!noSize) summary += $", {Format.Size(linked.Sum(w => w.SizeBytes ?? 0))} total, {Format.Size(reclaimable)} safely reclaimable";
        Console.WriteLine(summary + ".");
        return 0;
    }

    public static int Remove(string[] paths, bool force, bool deleteBranch)
    {
        var failed = 0;
        foreach (var path in paths)
        {
            var wt = WorktreeScanner.Find(path);
            if (wt is null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]error[/] {path} is not the root of a git worktree (for directories that are already gone, use 'workfree prune')");
                failed++;
                continue;
            }
            if (!Report(WorktreeRemover.Remove(wt, force, deleteBranch))) failed++;
        }
        return failed == 0 ? 0 : 1;
    }

    public static int Clean(IReadOnlyList<string> roots, int depth, string? olderThan, bool includeDirty, bool deleteBranch, bool dryRun, bool yes)
    {
        TimeSpan? age = null;
        if (olderThan is not null)
        {
            if (!Format.TryParseDuration(olderThan, out var parsed))
            {
                AnsiConsole.MarkupLineInterpolated($"[red]error[/] could not understand --older-than '{olderThan}'. Use a number and a unit: 12h, 30d, 2w, 3mo, 1y.");
                return 2;
            }
            age = parsed;
        }

        var worktrees = WorktreeScanner.Scan(roots, new ScanOptions { MaxDepth = depth });
        var policy = new CleanPolicy { OlderThan = age, IncludeDirty = includeDirty };
        var targets = policy.Select(worktrees, DateTimeOffset.Now, Directory.GetCurrentDirectory());
        if (targets.Count == 0)
        {
            Console.WriteLine("Nothing to clean.");
            return 0;
        }

        AnsiConsole.Write(BuildTable(targets));
        var total = Format.Size(targets.Sum(w => w.SizeBytes ?? 0));
        if (dryRun)
        {
            Console.WriteLine($"Dry run: would remove {targets.Count} worktree(s), freeing {total}.");
            return 0;
        }
        if (!yes)
        {
            if (Console.IsInputRedirected)
            {
                AnsiConsole.MarkupLine("[red]error[/] refusing to delete without confirmation; pass --yes to proceed.");
                return 2;
            }
            Console.Write($"Remove {targets.Count} worktree(s), freeing {total}? [y/N] ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Cancelled.");
                return 0;
            }
        }

        long freed = 0;
        var failed = 0;
        foreach (var wt in targets)
        {
            var result = WorktreeRemover.Remove(wt, force: includeDirty && wt.DirtyCount > 0, deleteBranch);
            if (Report(result)) freed += result.FreedBytes;
            else failed++;
        }
        Console.WriteLine($"Freed {Format.Size(freed)}.");
        return failed == 0 ? 0 : 1;
    }

    public static int Prune(IReadOnlyList<string> roots, int depth)
    {
        var worktrees = WorktreeScanner.Scan(roots, new ScanOptions { MaxDepth = depth, IncludeSize = false, IncludeStatus = false });
        var stale = worktrees.Where(w => w.IsStale).ToList();
        var failed = 0;
        foreach (var repo in stale.Select(w => w.RepoPath).Distinct(PathUtil.Comparer))
        {
            var result = WorktreeRemover.Prune(repo);
            if (result.Ok) AnsiConsole.MarkupLineInterpolated($"[green]pruned[/] {repo}");
            else
            {
                AnsiConsole.MarkupLineInterpolated($"[red]failed[/] {repo}: {result.StdErr.Trim()}");
                failed++;
            }
        }
        Console.WriteLine(stale.Count == 0 ? "Nothing to prune." : $"{stale.Count} stale worktree record(s) found.");
        return failed == 0 ? 0 : 1;
    }

    public static int RootsList()
    {
        var roots = ConfigStore.Load().Roots;
        if (roots.Count == 0) Console.WriteLine("No roots configured; the current directory is used.");
        foreach (var r in roots) Console.WriteLine(r);
        return 0;
    }

    public static int RootsAdd(string path)
    {
        if (!Directory.Exists(path))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error[/] {path} is not a directory");
            return 1;
        }
        var config = ConfigStore.Load();
        var full = PathUtil.Normalize(path);
        if (!config.Roots.Contains(full, PathUtil.Comparer)) config.Roots.Add(full);
        ConfigStore.Save(config);
        Console.WriteLine($"Added {full}");
        return 0;
    }

    public static int RootsRemove(string path)
    {
        var config = ConfigStore.Load();
        var full = PathUtil.Normalize(path);
        var removed = config.Roots.RemoveAll(r => PathUtil.Comparer.Equals(r, full));
        ConfigStore.Save(config);
        Console.WriteLine(removed > 0 ? $"Removed {full}" : $"{full} was not a configured root");
        return removed > 0 ? 0 : 1;
    }

    private static bool Report(RemoveResult result)
    {
        if (result.Success)
            AnsiConsole.MarkupLineInterpolated($"[green]ok[/] {result.Worktree.Path}: {result.Message} ({Format.Size(result.FreedBytes)})");
        else
            AnsiConsole.MarkupLineInterpolated($"[red]skipped[/] {result.Worktree.Path}: {result.Message}");
        return result.Success;
    }

    private static Table BuildTable(IEnumerable<Worktree> worktrees)
    {
        var now = DateTimeOffset.Now;
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Repo");
        table.AddColumn("Worktree");
        table.AddColumn("Branch");
        table.AddColumn(new TableColumn("Size").RightAligned());
        table.AddColumn("Last active");
        table.AddColumn("State");
        foreach (var wt in worktrees)
        {
            table.AddRow(
                new Text(wt.RepoName),
                new Text(wt.Path),
                new Text(wt.Branch ?? "(detached)"),
                new Text(Format.Size(wt.SizeBytes)),
                new Text(Format.Age(wt.LastActivity, now)),
                new Text(wt.State, new Style(wt.IsMain ? Color.Grey : Tui.StateColor(wt))));
        }
        return table;
    }
}
