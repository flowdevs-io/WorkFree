namespace WorkFree.Core;

/// <summary>One frame of the frog animation: text rows plus, per character, what it belongs to (see the Kind constants).</summary>
public sealed record FrogFrame(string[] Lines, string[] Kinds);

/// <summary>
/// The ASCII frog shown while WorkFree works. Each worktree being removed is a fly; the frog hops along
/// and eats one each time a removal finishes. Pure state and text, so the terminal and the desktop app share it.
/// </summary>
public sealed class FrogScene
{
    public const int Width = 64;
    public const int Height = 6;

    public const char KindNone = ' ';
    public const char KindFrog = 'f';
    public const char KindTongue = 't';
    public const char KindFly = 'b';
    public const char KindGround = 'g';
    public const char KindNote = 'n';

    private const int FrogX = 3;
    private const int MouthX = FrogX + 6;
    private const int Reach = 6;
    private const int StandOff = MouthX + Reach;
    private const int FirstFly = 34;
    private const int Spacing = 14;
    private const string GroundPattern = "__,_\"___.__,,___\"_.___";

    private enum Mode { Idle, Search, Meal, Done }

    private readonly object _gate = new();
    private Mode _mode = Mode.Idle;
    private int _tick, _scroll, _total, _eaten, _shown, _tongue, _gulp;
    private bool _caught, _moving;
    private string _note = "";

    /// <summary>The frog hops around looking for flies (used while scanning).</summary>
    public void StartSearch()
    {
        lock (_gate) { Reset(); _mode = Mode.Search; }
    }

    /// <summary>Lines up one fly per worktree about to be removed.</summary>
    public void StartMeal(int flies)
    {
        lock (_gate) { Reset(); _mode = Mode.Meal; _total = Math.Max(0, flies); }
    }

    /// <summary>One removal finished: the frog may swallow the next fly.</summary>
    public void Eat()
    {
        lock (_gate) { if (_eaten < _total) _eaten++; }
    }

    /// <summary>The meal is over; the frog sits back with a short note beside it.</summary>
    public void Celebrate(string note)
    {
        lock (_gate) { Reset(); _mode = Mode.Done; _note = note; }
    }

    /// <summary>True once the frog has swallowed every fly it was told about.</summary>
    public bool CaughtUp
    {
        get { lock (_gate) return _mode != Mode.Meal || _shown >= _total; }
    }

    public int FliesLeft
    {
        get { lock (_gate) return _mode == Mode.Meal ? _total - _shown : 0; }
    }

    /// <summary>Advances the animation by one step (about 90 ms apart looks right).</summary>
    public void Tick()
    {
        lock (_gate)
        {
            _tick++;
            if (_gulp > 0) _gulp--;
            _moving = false;

            if (_mode == Mode.Search)
            {
                _scroll += 2;
                _moving = true;
                return;
            }

            if (_mode != Mode.Meal || _shown >= _total) return;

            // Hurry when removals are finishing faster than the frog can eat.
            var hurry = _eaten - _shown > 2;
            var target = FirstFly + _shown * Spacing - StandOff;
            if (_scroll < target)
            {
                _scroll = Math.Min(target, _scroll + (hurry ? 4 : 2));
                _moving = true;
                return;
            }

            if (!_caught)
            {
                _tongue = Math.Min(Reach, _tongue + (hurry ? Reach : 2));
                _caught = _tongue == Reach;
            }
            else if (_eaten > _shown)
            {
                _tongue = Math.Max(0, _tongue - (hurry ? Reach : 2));
                if (_tongue == 0)
                {
                    _caught = false;
                    _shown++;
                    _gulp = 5;
                }
            }
            // Otherwise the removal is still running: the fly stays stuck on the tongue.
        }
    }

    public FrogFrame Render()
    {
        lock (_gate)
        {
            var text = new char[Height][];
            var kinds = new char[Height][];
            for (var r = 0; r < Height; r++)
            {
                text[r] = new string(' ', Width).ToCharArray();
                kinds[r] = new string(KindNone, Width).ToCharArray();
            }

            void Put(int row, int col, string s, char kind)
            {
                if (row < 0 || row >= Height) return;
                for (var i = 0; i < s.Length; i++)
                {
                    var x = col + i;
                    if (x < 0 || x >= Width || s[i] == ' ') continue;
                    text[row][x] = s[i];
                    kinds[row][x] = kind;
                }
            }

            string Wings(int k) => (_tick + k) % 2 == 0 ? "\\o/" : "-o-";

            for (var x = 0; x < Width; x++)
            {
                text[Height - 1][x] = GroundPattern[(x + _scroll) % GroundPattern.Length];
                kinds[Height - 1][x] = KindGround;
            }

            var happy = _mode == Mode.Done || _gulp > 0;
            var air = _mode switch
            {
                Mode.Done => _tick < 12 && _tick / 2 % 2 == 0,
                _ => _moving && _tick / 2 % 2 == 0,
            };

            if (_mode == Mode.Search)
            {
                for (var k = 0; k < 3; k++)
                {
                    var x = Width - (_tick + k * 25) % (Width + 10);
                    Put((_tick / 4 + k) % 2, x, Wings(k), KindFly);
                }
                if (_tick / 12 % 2 == 0) Put(0, FrogX + 9, "ribbit?", KindNote);
            }
            else if (_mode == Mode.Meal)
            {
                for (var k = _shown; k < _total; k++)
                {
                    var x = FirstFly + k * Spacing - _scroll;
                    if (x >= Width) break;
                    if (k == _shown && _caught) Put(3, MouthX + _tongue, Wings(k), KindFly);
                    else if (k == _shown && x <= StandOff + 6) Put(3, x, Wings(k), KindFly);
                    else Put(1 + (_tick / 3 + k) % 2, x, Wings(k), KindFly);
                }
                if (_tongue > 0) Put(3, MouthX, new string('=', _tongue), KindTongue);

                var left = _total - _shown;
                if (left > 0)
                {
                    var counter = $"\\o/ x {left}";
                    Put(0, Width - counter.Length, counter, KindNote);
                }
                if (_gulp > 0) Put(air ? 0 : 1, FrogX + 9, "*gulp*", KindNote);
            }
            else if (_mode == Mode.Done)
            {
                Put(2, FrogX + 11, "*burp*", KindNote);
                var note = _note.Length > Width - FrogX - 11 ? _note[..(Width - FrogX - 11)] : _note;
                Put(3, FrogX + 11, note, KindNote);
            }

            var top = air ? 1 : 2;
            Put(top, FrogX, happy ? "  ^..^  " : "  @..@  ", KindFrog);
            Put(top + 1, FrogX, _tongue > 0 ? " (---<  " : _gulp > 0 ? " (-vv-) " : " (----) ", KindFrog);
            Put(top + 2, FrogX, "( >__< )", KindFrog);
            Put(top + 3, FrogX, air ? " //  \\\\ " : "^^ ~~ ^^", KindFrog);

            return new FrogFrame(
                text.Select(row => new string(row)).ToArray(),
                kinds.Select(row => new string(row)).ToArray());
        }
    }

    private void Reset()
    {
        _tick = _scroll = _total = _eaten = _shown = _tongue = _gulp = 0;
        _caught = _moving = false;
        _note = "";
    }
}
