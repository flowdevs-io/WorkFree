using Spectre.Console;
using Spectre.Console.Rendering;
using WorkFree.Core;

namespace WorkFree;

public enum SortMode { Size, Age, Repo }

public enum TuiAction { None, Quit, Rescan, Prune, DeleteSafe, DeleteForce }

/// <summary>Everything the interactive screen knows, kept free of console I/O so it can be tested.</summary>
public sealed class TuiState
{
    public List<Worktree> Items { get; private set; } = [];
    public HashSet<string> Selected { get; } = new(PathUtil.Comparer);
    public int Cursor { get; private set; }
    public int Offset { get; set; }
    public SortMode Sort { get; private set; } = SortMode.Size;
    public string Message { get; set; } = "";
    public bool Confirming { get; private set; }

    /// <summary>The frog animation, shown while deleting and until the next key press.</summary>
    public FrogScene? Scene { get; set; }

    public Worktree? Current => Items.Count == 0 ? null : Items[Cursor];
    public List<Worktree> SelectedItems => Items.Where(w => Selected.Contains(w.Path)).ToList();

    /// <summary>Removable without losing anything: clean or already gone, and not locked.</summary>
    public static bool IsSafe(Worktree wt) => wt.IsStale || (!wt.IsLocked && wt.DirtyCount == 0);

    public void SetItems(IEnumerable<Worktree> all)
    {
        var linked = all.Where(w => !w.IsMain);
        Items = Sort switch
        {
            SortMode.Size => linked.OrderByDescending(w => w.SizeBytes ?? -1).ToList(),
            SortMode.Age => linked.OrderBy(w => w.LastActivity ?? DateTimeOffset.MinValue).ToList(),
            _ => linked.OrderBy(w => w.RepoName, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Path, StringComparer.OrdinalIgnoreCase).ToList(),
        };
        Selected.IntersectWith(Items.Select(w => w.Path));
        Cursor = Math.Clamp(Cursor, 0, Math.Max(0, Items.Count - 1));
    }

    public TuiAction HandleKey(ConsoleKeyInfo key)
    {
        Scene = null;
        if (Confirming)
        {
            Confirming = false;
            var answer = char.ToLowerInvariant(key.KeyChar);
            if (answer == 'y') return TuiAction.DeleteSafe;
            if (answer == 'f' && SelectedItems.Any(w => !IsSafe(w))) return TuiAction.DeleteForce;
            Message = "Cancelled.";
            return TuiAction.None;
        }

        Message = "";
        switch (key.Key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.K: Move(-1); break;
            case ConsoleKey.DownArrow or ConsoleKey.J: Move(1); break;
            case ConsoleKey.PageUp: Move(-10); break;
            case ConsoleKey.PageDown: Move(10); break;
            case ConsoleKey.Home: Move(-Items.Count); break;
            case ConsoleKey.End: Move(Items.Count); break;
            case ConsoleKey.Spacebar:
                if (Current is { } wt)
                {
                    if (!Selected.Remove(wt.Path)) Selected.Add(wt.Path);
                    Move(1);
                }
                break;
            case ConsoleKey.A:
                var safe = Items.Where(IsSafe).Select(w => w.Path).ToList();
                if (safe.Count > 0 && safe.All(Selected.Contains)) Selected.Clear();
                else Selected.UnionWith(safe);
                break;
            case ConsoleKey.N: Selected.Clear(); break;
            case ConsoleKey.D or ConsoleKey.Delete:
                if (Selected.Count == 0 && Current is { } current) Selected.Add(current.Path);
                Confirming = Selected.Count > 0;
                break;
            case ConsoleKey.S:
                Sort = (SortMode)(((int)Sort + 1) % 3);
                SetItems(Items);
                break;
            case ConsoleKey.R: return TuiAction.Rescan;
            case ConsoleKey.P: return TuiAction.Prune;
            case ConsoleKey.Q or ConsoleKey.Escape: return TuiAction.Quit;
        }
        return TuiAction.None;
    }

    private void Move(int delta) => Cursor = Math.Clamp(Cursor + delta, 0, Math.Max(0, Items.Count - 1));
}

public static class Tui
{
    public static int Run(IReadOnlyList<string> roots, int depth)
    {
        var state = new TuiState();
        var options = new ScanOptions { MaxDepth = depth };
        AnsiConsole.Status().Start("Scanning for worktrees...", _ => state.SetItems(WorktreeScanner.Scan(roots, options)));

        Console.TreatControlCAsInput = true;
        try
        {
            if (AnsiConsole.Profile.Capabilities.AlternateBuffer) AnsiConsole.AlternateScreen(Loop);
            else Loop();
        }
        finally
        {
            Console.TreatControlCAsInput = false;
        }
        return 0;

        void Loop() => AnsiConsole.Live(new Text("")).Start(ctx =>
        {
            void Paint()
            {
                ctx.UpdateTarget(Render(state, roots, Console.WindowHeight, DateTimeOffset.Now));
                ctx.Refresh();
            }

            void Rescan() => state.SetItems(WorktreeScanner.Scan(roots, options));

            while (true)
            {
                Paint();
                var key = Console.ReadKey(intercept: true);
                if (key is { Key: ConsoleKey.C, Modifiers: ConsoleModifiers.Control }) return;

                switch (state.HandleKey(key))
                {
                    case TuiAction.Quit:
                        return;
                    case TuiAction.Rescan:
                        state.Message = "Rescanning...";
                        Paint();
                        Rescan();
                        state.Message = "Rescanned.";
                        break;
                    case TuiAction.Prune:
                        var repos = state.Items.Where(w => w.IsStale).Select(w => w.RepoPath).Distinct(PathUtil.Comparer).ToList();
                        foreach (var repo in repos) WorktreeRemover.Prune(repo);
                        Rescan();
                        state.Message = repos.Count == 0 ? "Nothing to prune." : $"Pruned stale entries in {repos.Count} repo(s).";
                        break;
                    case TuiAction.DeleteSafe:
                        Delete(force: false);
                        break;
                    case TuiAction.DeleteForce:
                        Delete(force: true);
                        break;
                }
            }

            void Delete(bool force)
            {
                var targets = state.SelectedItems.Where(w => force || TuiState.IsSafe(w)).ToList();
                var skipped = state.SelectedItems.Count - targets.Count;
                long freed = 0;
                var failures = new List<string>();
                var scene = new FrogScene();
                scene.StartMeal(targets.Count);
                state.Scene = scene;

                void Frame()
                {
                    scene.Tick();
                    Paint();
                    Thread.Sleep(90);
                }

                for (var i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    state.Message = $"Removing {i + 1}/{targets.Count}: {target.Path}";
                    var removal = Task.Run(() => WorktreeRemover.Remove(target, force));
                    while (!removal.IsCompleted) Frame();
                    var result = removal.Result;
                    if (result.Success) freed += result.FreedBytes;
                    else failures.Add($"{target.Name} {result.Message}");
                    scene.Eat();
                }

                // Fast removals outrun the frog; give it a moment to finish eating.
                var deadline = Environment.TickCount64 + 2500;
                while (!scene.CaughtUp && Environment.TickCount64 < deadline) Frame();

                state.Selected.Clear();
                Rescan();
                var removed = targets.Count - failures.Count;
                var summary = $"Removed {removed} worktree(s), freed {Format.Size(freed)}.";
                if (skipped > 0) summary += $" Skipped {skipped} dirty or locked.";
                if (failures.Count > 0) summary += $" Failed: {string.Join("; ", failures)}";
                state.Message = summary;

                scene.Celebrate($"{removed} worktree(s) gone, {Format.Size(freed)} freed");
                for (var i = 0; i < 14; i++) Frame();
                while (Console.KeyAvailable) Console.ReadKey(intercept: true);
            }
        });
    }

    public static IRenderable Render(TuiState state, IReadOnlyList<string> roots, int height, DateTimeOffset now)
    {
        var selected = state.SelectedItems;
        var header = new Markup(
            $"[bold]WorkFree[/]  {state.Items.Count} worktree(s), {Format.Size(state.Items.Sum(w => w.SizeBytes ?? 0))} total" +
            $"  |  selected {selected.Count} ({Format.Size(selected.Sum(w => w.SizeBytes ?? 0))})" +
            $"  |  sorted by {state.Sort.ToString().ToLowerInvariant()}");

        IRenderable body;
        if (state.Items.Count == 0)
        {
            body = new Markup($"\n[yellow]No linked worktrees found under:[/] {Markup.Escape(string.Join(", ", roots))}\n");
        }
        else
        {
            var visible = Math.Max(3, height - 10 - (state.Scene is null ? 0 : FrogScene.Height));
            if (state.Cursor < state.Offset) state.Offset = state.Cursor;
            if (state.Cursor >= state.Offset + visible) state.Offset = state.Cursor - visible + 1;
            state.Offset = Math.Clamp(state.Offset, 0, Math.Max(0, state.Items.Count - visible));

            var table = new Table().Border(TableBorder.Rounded).Expand();
            table.AddColumn(new TableColumn("").NoWrap());
            table.AddColumn(new TableColumn("Repo").NoWrap());
            table.AddColumn(new TableColumn("Worktree").NoWrap());
            table.AddColumn(new TableColumn("Branch").NoWrap());
            table.AddColumn(new TableColumn("Size").RightAligned().NoWrap());
            table.AddColumn(new TableColumn("Last active").NoWrap());
            table.AddColumn(new TableColumn("State").NoWrap());

            for (var i = state.Offset; i < Math.Min(state.Items.Count, state.Offset + visible); i++)
            {
                var wt = state.Items[i];
                var style = i == state.Cursor ? new Style(decoration: Decoration.Invert) : Style.Plain;
                IRenderable Cell(string text, Style? s = null) => new Text(text, s ?? style) { Overflow = Overflow.Ellipsis };

                table.AddRow(
                    Cell(state.Selected.Contains(wt.Path) ? "[x]" : "[ ]"),
                    Cell(wt.RepoName),
                    Cell(wt.Name),
                    Cell(wt.Branch ?? "(detached)"),
                    Cell(Format.Size(wt.SizeBytes)),
                    Cell(Format.Age(wt.LastActivity, now)),
                    Cell(wt.State, style.Combine(new Style(StateColor(wt)))));
            }
            body = table;
        }

        string footer;
        if (state.Confirming)
        {
            var unsafeCount = selected.Count(w => !TuiState.IsSafe(w));
            footer = $"[bold red]Delete {selected.Count} worktree(s), {Format.Size(selected.Sum(w => w.SizeBytes ?? 0))}?[/] ";
            footer += unsafeCount == 0
                ? "[bold]y[/] delete  |  any other key cancels"
                : $"{unsafeCount} dirty or locked: [bold]y[/] delete only the safe ones  |  [bold]f[/] force-delete all (uncommitted work is lost)  |  any other key cancels";
        }
        else
        {
            footer = "[grey]up/down[/] move  [grey]space[/] select  [grey]a[/] all safe  [grey]n[/] none  [grey]d[/] delete  [grey]p[/] prune missing  [grey]s[/] sort  [grey]r[/] rescan  [grey]q[/] quit";
        }

        var rows = new List<IRenderable> { header, body };
        if (state.Scene is { } scene) rows.Add(FrogMarkup(scene.Render()));
        rows.Add(new Text(state.Current?.Path ?? "", new Style(Color.Grey)) { Overflow = Overflow.Ellipsis });
        rows.Add(new Text(state.Message, new Style(Color.Yellow)) { Overflow = Overflow.Ellipsis });
        rows.Add(new Markup(footer));
        return new Rows(rows);
    }

    /// <summary>Colours one animation frame for the terminal.</summary>
    public static Markup FrogMarkup(FrogFrame frame)
    {
        var text = new System.Text.StringBuilder();
        for (var row = 0; row < frame.Lines.Length; row++)
        {
            if (row > 0) text.Append('\n');
            var line = frame.Lines[row];
            var kinds = frame.Kinds[row];
            var start = 0;
            for (var i = 1; i <= line.Length; i++)
            {
                if (i < line.Length && kinds[i] == kinds[start]) continue;
                var colour = kinds[start] switch
                {
                    FrogScene.KindFrog => "bold green",
                    FrogScene.KindTongue => "bold deeppink2",
                    FrogScene.KindFly => "bold white",
                    FrogScene.KindGround => "darkgreen",
                    _ => "yellow",
                };
                text.Append('[').Append(colour).Append(']').Append(Markup.Escape(line[start..i])).Append("[/]");
                start = i;
            }
        }
        return new Markup(text.ToString());
    }

    public static Color StateColor(Worktree wt) =>
        wt.IsStale ? Color.Red
        : wt.IsLocked ? Color.Blue
        : wt.DirtyCount > 0 ? Color.Yellow
        : wt.DirtyCount == 0 ? Color.Green
        : Color.Grey;
}
