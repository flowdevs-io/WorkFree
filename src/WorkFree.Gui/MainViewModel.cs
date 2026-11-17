using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WorkFree.Core;

namespace WorkFree.Gui;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>One linked worktree as shown in the table.</summary>
public sealed class WorktreeRow(Worktree model, DateTimeOffset now) : Observable
{
    private bool _isSelected;

    public Worktree Model { get; } = model;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public string Repo => Model.RepoName;
    public string Name => Model.Name;
    public string Path => Model.Path;
    public string Branch => Model.Branch ?? "(detached)";
    public string SizeText => Format.Size(Model.SizeBytes);
    public string LastActive { get; } = Format.Age(model.LastActivity, now);
    public string State => Model.State;

    /// <summary>Removable without losing anything: clean or already gone, and not locked.</summary>
    public bool IsSafe => Model.IsStale || (!Model.IsLocked && Model.DirtyCount == 0);
    public bool IsClean => !Model.IsStale && !Model.IsLocked && Model.DirtyCount == 0;
    public bool IsDirty => !Model.IsStale && !Model.IsLocked && Model.DirtyCount > 0;
    public bool IsMissing => Model.IsStale;
    public bool IsLocked => !Model.IsStale && Model.IsLocked;
}

public enum SortBy { Size, Age, Repo }

public enum DeleteChoice { Cancel, SafeOnly, ForceAll }

/// <summary>What the confirmation dialog needs to describe a pending delete.</summary>
public sealed record DeleteRequest(int Count, long Bytes, int UnsafeCount);

public sealed class MainViewModel : Observable
{
    private readonly Func<IReadOnlyList<string>, List<Worktree>> _scan;
    private List<Worktree> _all = [];
    private bool _isBusy;
    private string _status = "";
    private int _sortIndex;
    private FrogFrame? _frame;
    private bool _showFrog;
    private bool _animating;
    private int _frogVersion;

    public MainViewModel(Func<IReadOnlyList<string>, List<Worktree>>? scan = null)
    {
        _scan = scan ?? (roots => WorktreeScanner.Scan(roots));
        foreach (var root in ConfigStore.Load().Roots) Roots.Add(root);
    }

    public ObservableCollection<string> Roots { get; } = [];
    public ObservableCollection<WorktreeRow> Rows { get; } = [];
    public string[] SortOptions { get; } = ["Largest first", "Oldest first", "By repository"];

    /// <summary>Asks the user to confirm a delete. The window supplies this; the default refuses.</summary>
    public Func<DeleteRequest, Task<DeleteChoice>> ConfirmDelete { get; set; } = _ => Task.FromResult(DeleteChoice.Cancel);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            Raise(nameof(IsIdle));
            Raise(nameof(CanDelete));
        }
    }

    public bool IsIdle => !IsBusy;
    public string Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>The frog that hunts while scanning and eats one fly per removed worktree.</summary>
    public FrogScene Scene { get; } = new();
    public FrogFrame? Frame { get => _frame; set => Set(ref _frame, value); }
    public bool ShowFrog { get => _showFrog; set => Set(ref _showFrog, value); }

    private void BeginFrog(Action<FrogScene> start)
    {
        _frogVersion++;
        start(Scene);
        Frame = Scene.Render();
        ShowFrog = true;
        if (!_animating) _ = AnimateAsync();
    }

    private void EndFrog()
    {
        _frogVersion++;
        ShowFrog = false;
    }

    private async Task HideFrogLaterAsync(int milliseconds)
    {
        var version = ++_frogVersion;
        await Task.Delay(milliseconds);
        if (version == _frogVersion) ShowFrog = false;
    }

    private async Task AnimateAsync()
    {
        _animating = true;
        try
        {
            while (ShowFrog)
            {
                await Task.Delay(90);
                Scene.Tick();
                Frame = Scene.Render();
            }
        }
        finally
        {
            _animating = false;
        }
    }

    public int SortIndex
    {
        get => _sortIndex;
        set
        {
            if (value < 0 || !Set(ref _sortIndex, value)) return;
            Rebuild();
        }
    }

    public bool HasRoots => Roots.Count > 0;
    public bool ShowAddFolderHint => !HasRoots;
    public bool ShowNothingFound => HasRoots && Rows.Count == 0 && !IsBusy;
    public bool HasMissing => Rows.Any(r => r.IsMissing);

    public IEnumerable<WorktreeRow> SelectedRows => Rows.Where(r => r.IsSelected);
    public bool CanDelete => !IsBusy && Rows.Any(r => r.IsSelected);

    public string Summary
    {
        get
        {
            var safe = Rows.Where(r => r.IsSafe).Sum(r => r.Model.SizeBytes ?? 0);
            return $"{Rows.Count} worktree(s), {Format.Size(Rows.Sum(r => r.Model.SizeBytes ?? 0))} total, {Format.Size(safe)} safe to remove";
        }
    }

    public string SelectionSummary
    {
        get
        {
            var selected = SelectedRows.ToList();
            return selected.Count == 0
                ? "Nothing selected"
                : $"{selected.Count} selected, {Format.Size(selected.Sum(r => r.Model.SizeBytes ?? 0))}";
        }
    }

    public async Task ScanAsync()
    {
        if (IsBusy) return;
        if (!HasRoots)
        {
            _all = [];
            Rebuild();
            return;
        }

        IsBusy = true;
        Status = "Scanning for worktrees...";
        Raise(nameof(ShowNothingFound));
        // After a delete the frog is still finishing its meal; leave it alone then.
        var hunting = !ShowFrog;
        if (hunting) BeginFrog(scene => scene.StartSearch());
        try
        {
            var roots = Roots.ToList();
            _all = await Task.Run(() => _scan(roots));
            Status = "";
        }
        catch (Exception ex)
        {
            Status = "Scan failed: " + ex.Message;
        }
        finally
        {
            if (hunting) EndFrog();
            IsBusy = false;
            Rebuild();
        }
    }

    public async Task AddRootAsync(string path)
    {
        var full = PathUtil.Normalize(path);
        if (Roots.Contains(full, PathUtil.Comparer)) return;
        Roots.Add(full);
        SaveRoots();
        await ScanAsync();
    }

    public async Task RemoveRootAsync(string path)
    {
        if (!Roots.Remove(path)) return;
        SaveRoots();
        await ScanAsync();
    }

    public void SelectAllSafe()
    {
        foreach (var row in Rows) row.IsSelected = row.IsSafe;
    }

    public void ClearSelection()
    {
        foreach (var row in Rows) row.IsSelected = false;
    }

    public async Task DeleteSelectedAsync()
    {
        var selected = SelectedRows.ToList();
        if (IsBusy || selected.Count == 0) return;

        var unsafeCount = selected.Count(r => !r.IsSafe);
        var choice = await ConfirmDelete(new DeleteRequest(selected.Count, selected.Sum(r => r.Model.SizeBytes ?? 0), unsafeCount));
        if (choice == DeleteChoice.Cancel) return;

        var force = choice == DeleteChoice.ForceAll;
        var targets = selected.Where(r => force || r.IsSafe).ToList();
        var skipped = selected.Count - targets.Count;

        IsBusy = true;
        long freed = 0;
        var failures = new List<string>();
        BeginFrog(scene => scene.StartMeal(targets.Count));
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var row = targets[i];
                Status = $"Removing {i + 1} of {targets.Count}: {row.Path}";
                var result = await Task.Run(() => WorktreeRemover.Remove(row.Model, force));
                if (result.Success) freed += result.FreedBytes;
                else failures.Add($"{row.Name} {result.Message}");
                Scene.Eat();
            }

            // Fast removals outrun the frog; give it a moment to finish eating.
            var deadline = Environment.TickCount64 + 2500;
            while (!Scene.CaughtUp && Environment.TickCount64 < deadline) await Task.Delay(50);
        }
        finally
        {
            IsBusy = false;
        }

        await ScanAsync();
        var removed = targets.Count - failures.Count;
        var summary = $"Removed {removed} worktree(s), freed {Format.Size(freed)}.";
        if (skipped > 0) summary += $" Skipped {skipped} with uncommitted changes or a lock.";
        if (failures.Count > 0) summary += " Failed: " + string.Join("; ", failures);
        Status = summary;

        Scene.Celebrate($"{removed} worktree(s) gone, {Format.Size(freed)} freed");
        Frame = Scene.Render();
        _ = HideFrogLaterAsync(4000);
    }

    public async Task PruneAsync()
    {
        if (IsBusy) return;
        var repos = Rows.Where(r => r.IsMissing).Select(r => r.Model.RepoPath).Distinct(PathUtil.Comparer).ToList();
        if (repos.Count == 0)
        {
            Status = "Nothing to prune.";
            return;
        }
        await Task.Run(() => { foreach (var repo in repos) WorktreeRemover.Prune(repo); });
        await ScanAsync();
        Status = $"Pruned records of missing worktrees in {repos.Count} repo(s).";
    }

    private void SaveRoots()
    {
        var config = ConfigStore.Load();
        config.Roots = Roots.ToList();
        ConfigStore.Save(config);
        Raise(nameof(HasRoots));
        Raise(nameof(ShowAddFolderHint));
    }

    private void Rebuild()
    {
        var keep = new HashSet<string>(SelectedRows.Select(r => r.Path), PathUtil.Comparer);
        var linked = _all.Where(w => !w.IsMain);
        var sorted = (SortBy)SortIndex switch
        {
            SortBy.Size => linked.OrderByDescending(w => w.SizeBytes ?? -1),
            SortBy.Age => linked.OrderBy(w => w.LastActivity ?? DateTimeOffset.MinValue),
            _ => linked.OrderBy(w => w.RepoName, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Path, StringComparer.OrdinalIgnoreCase),
        };

        foreach (var old in Rows) old.PropertyChanged -= OnRowChanged;
        Rows.Clear();
        var now = DateTimeOffset.Now;
        foreach (var wt in sorted)
        {
            var row = new WorktreeRow(wt, now) { IsSelected = keep.Contains(wt.Path) };
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        Raise(nameof(Summary));
        Raise(nameof(HasMissing));
        Raise(nameof(ShowNothingFound));
        Raise(nameof(ShowAddFolderHint));
        RaiseSelection();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorktreeRow.IsSelected)) RaiseSelection();
    }

    private void RaiseSelection()
    {
        Raise(nameof(SelectionSummary));
        Raise(nameof(CanDelete));
    }
}
