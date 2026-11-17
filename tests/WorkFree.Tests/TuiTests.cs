using Spectre.Console;
using WorkFree.Core;

namespace WorkFree.Tests;

public class TuiTests
{
    private static Worktree Wt(string name, long size, int? dirty = 0, bool main = false) => new()
    {
        Path = PathUtil.Normalize(Path.Combine(Path.GetTempPath(), "wf-tui", name)),
        RepoName = "app",
        Branch = name,
        IsMain = main,
        Exists = true,
        DirtyCount = dirty,
        SizeBytes = size,
        LastActivity = DateTimeOffset.UnixEpoch,
    };

    private static TuiState State()
    {
        var state = new TuiState();
        state.SetItems([Wt("main", 999, main: true), Wt("small", 10), Wt("big", 3000), Wt("dirty", 200, dirty: 4)]);
        return state;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char ch = '\0') => new(ch, key, false, false, false);

    [Fact]
    public void Items_exclude_main_and_sort_by_size()
    {
        Assert.Equal(["big", "dirty", "small"], State().Items.Select(w => w.Name));
    }

    [Fact]
    public void Select_all_takes_only_safe_worktrees_and_toggles_off()
    {
        var state = State();

        state.HandleKey(Key(ConsoleKey.A, 'a'));
        Assert.Equal(["big", "small"], state.SelectedItems.Select(w => w.Name));

        state.HandleKey(Key(ConsoleKey.A, 'a'));
        Assert.Empty(state.Selected);
    }

    [Fact]
    public void Delete_needs_confirmation_and_any_other_key_cancels()
    {
        var state = State();

        Assert.Equal(TuiAction.None, state.HandleKey(Key(ConsoleKey.D, 'd')));
        Assert.True(state.Confirming);
        Assert.Equal("big", Assert.Single(state.SelectedItems).Name);

        Assert.Equal(TuiAction.None, state.HandleKey(Key(ConsoleKey.Enter, '\r')));
        Assert.False(state.Confirming);

        state.HandleKey(Key(ConsoleKey.D, 'd'));
        Assert.Equal(TuiAction.DeleteSafe, state.HandleKey(Key(ConsoleKey.Y, 'y')));
    }

    [Fact]
    public void Force_is_only_offered_when_something_unsafe_is_selected()
    {
        var state = State();
        state.HandleKey(Key(ConsoleKey.D, 'd'));
        Assert.Equal(TuiAction.None, state.HandleKey(Key(ConsoleKey.F, 'f')));

        state.Selected.Clear();
        state.HandleKey(Key(ConsoleKey.DownArrow));
        state.HandleKey(Key(ConsoleKey.D, 'd'));
        Assert.Equal("dirty", Assert.Single(state.SelectedItems).Name);
        Assert.Equal(TuiAction.DeleteForce, state.HandleKey(Key(ConsoleKey.F, 'f')));
    }

    [Fact]
    public void Cursor_stays_in_range_and_space_selects()
    {
        var state = State();
        state.HandleKey(Key(ConsoleKey.UpArrow));
        Assert.Equal(0, state.Cursor);
        state.HandleKey(Key(ConsoleKey.End));
        Assert.Equal(2, state.Cursor);
        state.HandleKey(Key(ConsoleKey.Spacebar, ' '));
        Assert.Equal("small", Assert.Single(state.SelectedItems).Name);
        Assert.Equal(TuiAction.Quit, state.HandleKey(Key(ConsoleKey.Q, 'q')));
    }

    [Fact]
    public void Render_fits_the_window_and_scrolls_to_the_cursor()
    {
        var state = new TuiState();
        state.SetItems(Enumerable.Range(0, 100).Select(i => Wt($"wt-{i:000}", 1000 - i)));
        state.HandleKey(Key(ConsoleKey.End));

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 120;
        console.Write(Tui.Render(state, ["/roots"], height: 30, DateTimeOffset.UnixEpoch.AddDays(3)));

        var lines = writer.ToString().TrimEnd().Split('\n');
        Assert.True(lines.Length <= 30, $"rendered {lines.Length} lines");
        Assert.Contains("wt-099", writer.ToString());
        Assert.DoesNotContain("wt-000", writer.ToString());
        Assert.Contains("100 worktree(s)", writer.ToString());
    }
}
