using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.UI
{
    /// <summary>
    /// The Game Corner slot machine (pbSlotMachine): bet 1-3 coins (Up / Down), pull with Action, stop each reel with
    /// Action. Three of a kind on a played line pays out; Replay gives a free pull. Cancel leaves.
    /// </summary>
    public class SlotMachineScene : Scene
    {
        static readonly string[] Symbols = { "7", "BAR", "STAR", "BELL", "CHERRY", "REPLAY" };
        static readonly int[] Payout = { 100, 50, 15, 10, 6, 0 };
        static readonly Color[] SymbolColors = { new Color(224, 40, 40), new Color(40, 40, 48), new Color(232, 184, 32), new Color(232, 152, 40), new Color(208, 32, 96), new Color(48, 128, 224) };
        readonly int[][] _reels = new int[3][];
        readonly double[] _pos = new double[3];
        readonly bool[] _spinning = new bool[3];
        readonly int _difficulty;
        int _bet = 1, _stopIndex = 3, _win;
        bool _replay;
        string _msg = "Up / Down: bet   Action: pull   Cancel: quit";

        public SlotMachineScene(Game g, int difficulty) : base(g)
        {
            Opaque = true; _difficulty = Math.Max(0, difficulty);
            var rng = new Random(g.Rng.Next());
            for (int r = 0; r < 3; r++)
            {
                var strip = new List<int>();
                int[] counts = { 2, 3, 4, 4, 5, 3 };
                for (int s = 0; s < Symbols.Length; s++) for (int k = 0; k < counts[s]; k++) strip.Add(s);
                _reels[r] = strip.OrderBy(_ => rng.Next()).ToArray();
                _pos[r] = rng.Next(strip.Count);
            }
        }

        int SymbolAt(int reel, int row) { var s = _reels[reel]; int i = ((int)Math.Floor(_pos[reel]) + row) % s.Length; if (i < 0) i += s.Length; return s[i]; }

        protected override void Update()
        {
            if (_spinning.Any(x => x))
            {
                for (int r = 0; r < 3; r++) if (_spinning[r]) _pos[r] = (_pos[r] + 0.35) % _reels[r].Length;
                if (Input.Trigger(Button.Action) && _stopIndex < 3)
                {
                    int r = _stopIndex++;
                    // Harder machines nudge away from wins more often
                    int target = (int)Math.Ceiling(_pos[r]);
                    if (Game.Rng.Next(100) < 10 + _difficulty * 10) target += 1;
                    _pos[r] = target % _reels[r].Length; _spinning[r] = false;
                    Game.Audio.PlaySe("Slots stop");
                    if (_stopIndex == 3) Settle();
                }
                return;
            }
            if (Input.Trigger(Button.Cancel)) { Cancel(); Close(); return; }
            if (Input.Repeat(Button.Up) && _bet < 3) { _bet++; Cursor(); }
            if (Input.Repeat(Button.Down) && _bet > 1) { _bet--; Cursor(); }
            if (Input.Trigger(Button.Action))
            {
                if (!_replay)
                {
                    if (Game.State.Coins < _bet) { Buzz(); _msg = Game.State.Coins == 0 ? "You don't have any coins." : "Not enough coins for that bet."; return; }
                    Game.State.Coins -= _bet;
                }
                _replay = false; _win = 0;
                for (int r = 0; r < 3; r++) _spinning[r] = true;
                _stopIndex = 0; _msg = "Action: stop the reels";
                Game.Audio.PlaySe("Slots coin");
            }
        }

        void Settle()
        {
            var lines = new List<int[]> { new[] { 1, 1, 1 } };
            if (_bet >= 2) { lines.Add(new[] { 0, 0, 0 }); lines.Add(new[] { 2, 2, 2 }); }
            if (_bet >= 3) { lines.Add(new[] { 0, 1, 2 }); lines.Add(new[] { 2, 1, 0 }); }
            int win = 0; bool replay = false;
            foreach (var l in lines)
            {
                int a = SymbolAt(0, l[0]), b = SymbolAt(1, l[1]), c = SymbolAt(2, l[2]);
                if (a == b && b == c) { if (a == 5) replay = true; else win += Payout[a]; }
                else if (a == 4) win += 2; // a cherry on the left pays a little
            }
            _win = win; _replay = replay;
            if (win > 0)
            {
                Game.State.Coins = Math.Min(99999, Game.State.Coins + win);
                Game.Audio.PlayMe(win >= 50 ? "Slots big win" : "Slots win");
                _msg = "You won " + win + " coins!";
            }
            else _msg = replay ? "Replay! Pull again for free." : "No luck. Try again!";
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(120, 32, 48), new Color(40, 8, 24));
            int rw = 110, rh = 72, x0 = (g.Width - rw * 3 - 20) / 2, y0 = 40;
            Ui.Panel(g, x0 - 12, y0 - 12, rw * 3 + 44, rh * 3 + 24, new Color(40, 40, 48), new Color(232, 200, 64));
            for (int r = 0; r < 3; r++)
                for (int row = 0; row < 3; row++)
                {
                    int x = x0 + r * (rw + 10), y = y0 + row * rh;
                    g.FillRect(x, y, rw, rh - 4, new Color(248, 248, 240));
                    int sym = SymbolAt(r, row);
                    string label = Symbols[sym];
                    g.DrawText(x + rw / 2 - g.TextWidth(label) / 2, y + rh / 2 - 18, label, SymbolColors[sym], new Color(200, 200, 200));
                }
            // Played lines
            g.FillRect(x0 - 10, y0 + rh + rh / 2 - 3, 6, 6, new Color(248, 64, 64));
            if (_bet >= 2) { g.FillRect(x0 - 10, y0 + rh / 2 - 3, 6, 6, new Color(248, 64, 64)); g.FillRect(x0 - 10, y0 + rh * 2 + rh / 2 - 3, 6, 6, new Color(248, 64, 64)); }
            Ui.Box(g, 4, g.Height - 112, g.Width - 8, 108);
            Ui.Text(g, 22, g.Height - 100, "Coins: " + Game.State.Coins + "    Bet: " + _bet + (_replay ? "    REPLAY" : ""));
            Ui.Text(g, 22, g.Height - 68, _msg);
            Ui.Text(g, 22, g.Height - 38, "7 100  BAR 50  STAR 15  BELL 10  CHERRY 6");
        }
    }

    /// <summary>
    /// Voltorb Flip (pbVoltorbFlip): flip the 2s and 3s on a 5×5 board using the row and column totals; a Voltorb ends
    /// the round. Clearing the board pays the product of the flipped numbers and raises the level (1-8).
    /// </summary>
    public class VoltorbFlipScene : Scene
    {
        readonly int[,] _tile = new int[5, 5]; // 0 = Voltorb
        readonly bool[,] _open = new bool[5, 5];
        readonly bool[,] _mark = new bool[5, 5];
        int _cx, _cy, _level, _round = 1;
        bool _over;
        string _msg;

        public VoltorbFlipScene(Game g) : base(g)
        {
            Opaque = true;
            _level = Math.Max(1, Math.Min(8, g.State.Extras.VoltorbLevel));
            NewBoard();
        }

        int Coins { get { int p = 1; bool any = false; for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++) if (_open[x, y] && _tile[x, y] > 1) { p *= _tile[x, y]; any = true; } return any ? p : 0; } }

        void NewBoard()
        {
            int twos = 3 + _level / 2, threes = 1 + _level / 2, volts = 6 + _level;
            var cells = Enumerable.Range(0, 25).OrderBy(_ => Game.Rng.Next()).ToList();
            for (int i = 0; i < 25; i++) { int c = cells[i]; _tile[c % 5, c / 5] = i < volts ? 0 : i < volts + twos ? 2 : i < volts + twos + threes ? 3 : 1; }
            Array.Clear(_open, 0, 25); Array.Clear(_mark, 0, 25);
            _over = false;
            _msg = "Action: flip  Shift: mark  Cancel: quit";
        }

        bool Cleared() { for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++) if (_tile[x, y] > 1 && !_open[x, y]) return false; return true; }

        protected override void Update()
        {
            if (_over)
            {
                if (Input.Trigger(Button.Action)) { _round++; NewBoard(); }
                if (Input.Trigger(Button.Cancel)) { Close(); }
                return;
            }
            if (Input.Repeat(Button.Left)) { _cx = (_cx + 4) % 5; Cursor(); }
            if (Input.Repeat(Button.Right)) { _cx = (_cx + 1) % 5; Cursor(); }
            if (Input.Repeat(Button.Up)) { _cy = (_cy + 4) % 5; Cursor(); }
            if (Input.Repeat(Button.Down)) { _cy = (_cy + 1) % 5; Cursor(); }
            if (Input.Trigger(Button.Run) && !_open[_cx, _cy]) { _mark[_cx, _cy] = !_mark[_cx, _cy]; Cursor(); }
            if (Input.Trigger(Button.Cancel))
            {
                int keep = Coins;
                if (keep > 0) { Game.State.Coins = Math.Min(99999, Game.State.Coins + keep); }
                Game.State.Extras.VoltorbLevel = _level;
                Close(); return;
            }
            if (Input.Trigger(Button.Action) && !_open[_cx, _cy] && !_mark[_cx, _cy])
            {
                _open[_cx, _cy] = true;
                if (_tile[_cx, _cy] == 0)
                {
                    Game.Audio.PlaySe("Voltorb Flip explosion");
                    int flipped = 0; for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++) if (_open[x, y] && _tile[x, y] > 0) flipped++;
                    if (flipped < _level) _level = Math.Max(1, flipped);
                    for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++) _open[x, y] = true;
                    _msg = "Voltorb! Coins lost. Action: again";
                    _over = true;
                }
                else
                {
                    Game.Audio.PlaySe("Voltorb Flip tile");
                    if (Cleared())
                    {
                        int won = Coins;
                        Game.State.Coins = Math.Min(99999, Game.State.Coins + won);
                        Game.Audio.PlayMe("Voltorb Flip win");
                        _level = Math.Min(8, _level + 1);
                        for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++) _open[x, y] = true;
                        _msg = "Cleared! +" + won + " coins. Action: next";
                        _over = true;
                    }
                }
                Game.State.Extras.VoltorbLevel = _level;
            }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(40, 152, 96), new Color(16, 88, 56));
            int s = 46, x0 = 30, y0 = 18;
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 5; x++)
                {
                    int px = x0 + x * (s + 4), py = y0 + y * (s + 4);
                    var bg = _open[x, y] ? (_tile[x, y] == 0 ? new Color(232, 96, 72) : new Color(248, 248, 232)) : new Color(64, 128, 96);
                    g.FillRect(px, py, s, s, bg);
                    if (x == _cx && y == _cy && !_over) { g.FillRect(px - 3, py - 3, s + 6, 3, new Color(248, 64, 48)); g.FillRect(px - 3, py + s, s + 6, 3, new Color(248, 64, 48)); g.FillRect(px - 3, py, 3, s, new Color(248, 64, 48)); g.FillRect(px + s, py, 3, s, new Color(248, 64, 48)); }
                    if (_open[x, y]) { string t = _tile[x, y] == 0 ? "V" : _tile[x, y].ToString(); g.DrawText(px + s / 2 - g.TextWidth(t) / 2, py + 8, t, Ui.Dark, Ui.DarkShadow); }
                    else if (_mark[x, y]) g.DrawText(px + s / 2 - g.TextWidth("?") / 2, py + 8, "?", new Color(248, 200, 64), new Color(40, 80, 60));
                }
            // Row and column hints: total points / Voltorbs
            for (int i = 0; i < 5; i++)
            {
                int rs = 0, rv = 0, cs = 0, cv = 0;
                for (int j = 0; j < 5; j++) { if (_tile[j, i] == 0) rv++; else rs += _tile[j, i]; if (_tile[i, j] == 0) cv++; else cs += _tile[i, j]; }
                int hx = x0 + 5 * (s + 4) + 4, hy = y0 + i * (s + 4);
                g.FillRect(hx, hy, 60, s, new Color(232, 200, 120));
                g.DrawText(hx + 6, hy - 2, rs.ToString("00"), Ui.Dark, Ui.DarkShadow);
                g.DrawText(hx + 6, hy + 20, "V" + rv, new Color(200, 48, 32), Ui.DarkShadow);
                int vx = x0 + i * (s + 4), vy = y0 + 5 * (s + 4) + 2;
                g.FillRect(vx, vy, s, 50, new Color(232, 200, 120));
                g.DrawText(vx + 4, vy - 2, cs.ToString("00"), Ui.Dark, Ui.DarkShadow);
                g.DrawText(vx + 4, vy + 22, "V" + cv, new Color(200, 48, 32), Ui.DarkShadow);
            }
            int ix = x0 + 5 * (s + 4) + 76;
            Ui.Panel(g, ix, y0, g.Width - ix - 8, 120, new Color(248, 248, 248), new Color(32, 80, 56));
            Ui.Text(g, ix + 12, y0 + 6, "Level " + _level);
            Ui.Text(g, ix + 12, y0 + 38, "Round: " + Coins);
            Ui.Text(g, ix + 12, y0 + 70, "Coins: " + Game.State.Coins);
            Ui.Box(g, 4, g.Height - 64, g.Width - 8, 60);
            g.DrawText(18, g.Height - 52, _msg, Ui.Dark, Ui.DarkShadow);
        }
    }
}

namespace PokeCreator.Runtime.UI
{
    /// <summary>
    /// Tile puzzles (pbTilePuzzle(game, board, width, height)): the picture Graphics/UI/Tile Puzzle/&lt;board&gt; cut into
    /// tiles. Games 3 slides tiles into the gap (Mystic Square); 1 and 5 swap tiles; 2, 4, 6 and 7 also rotate them
    /// (Shift). Cancel gives up. The result is true when solved.
    /// </summary>
    public class TilePuzzleScene : Scene
    {
        readonly int _w, _h, _game;
        readonly int[] _tile; readonly int[] _rot;
        readonly string _board;
        readonly Action<bool> _done;
        int _cx, _cy, _held = -1;
        bool _solved; int _solvedTimer;

        bool Sliding { get { return _game == 3; } }
        bool Rotating { get { return _game == 2 || _game == 4 || _game == 6 || _game == 7; } }

        public TilePuzzleScene(Game g, int game, string board, int w, int h, Action<bool> done) : base(g)
        {
            Opaque = true; _game = Math.Max(1, Math.Min(7, game)); _board = board ?? ""; _done = done;
            _w = w > 1 ? Math.Min(6, w) : (Sliding ? 4 : 4); _h = h > 1 ? Math.Min(6, h) : 4;
            int n = _w * _h;
            _tile = Enumerable.Range(0, n).ToArray(); _rot = new int[n];
            var rng = new Random(g.Rng.Next());
            if (Sliding)
            {
                // Random legal slides keep the puzzle solvable; the last tile is the gap
                int gap = n - 1;
                for (int k = 0; k < 200; k++)
                {
                    int gx = gap % _w, gy = gap / _w;
                    var opts = new List<int>();
                    if (gx > 0) opts.Add(gap - 1); if (gx < _w - 1) opts.Add(gap + 1); if (gy > 0) opts.Add(gap - _w); if (gy < _h - 1) opts.Add(gap + _w);
                    int o = opts[rng.Next(opts.Count)];
                    _tile[gap] = _tile[o]; _tile[o] = n - 1; gap = o;
                }
            }
            else
            {
                for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = _tile[i]; _tile[i] = _tile[j]; _tile[j] = t; }
                if (Rotating) for (int i = 0; i < n; i++) _rot[i] = rng.Next(4);
            }
            if (Solved()) { int t = _tile[0]; _tile[0] = _tile[1]; _tile[1] = t; }
        }

        bool Solved() { for (int i = 0; i < _tile.Length; i++) if (_tile[i] != i || _rot[i] != 0) return false; return true; }

        protected override void Update()
        {
            if (_solved) { if (++_solvedTimer > 60 || Input.Trigger(Button.Action)) { Close(); _done(true); } return; }
            if (Input.Trigger(Button.Cancel)) { Cancel(); Close(); _done(false); return; }
            if (Input.Repeat(Button.Left)) { _cx = (_cx + _w - 1) % _w; Cursor(); }
            if (Input.Repeat(Button.Right)) { _cx = (_cx + 1) % _w; Cursor(); }
            if (Input.Repeat(Button.Up)) { _cy = (_cy + _h - 1) % _h; Cursor(); }
            if (Input.Repeat(Button.Down)) { _cy = (_cy + 1) % _h; Cursor(); }
            int i = _cy * _w + _cx, n = _tile.Length;
            if (Rotating && Input.Trigger(Button.Run)) { _rot[i] = (_rot[i] + 1) % 4; Cursor(); }
            if (Input.Trigger(Button.Action))
            {
                if (Sliding)
                {
                    int gap = Array.IndexOf(_tile, n - 1);
                    int gx = gap % _w, gy = gap / _w;
                    if (Math.Abs(gx - _cx) + Math.Abs(gy - _cy) == 1) { _tile[gap] = _tile[i]; _tile[i] = n - 1; Decide(); }
                    else Buzz();
                }
                else if (_held < 0) { _held = i; Decide(); }
                else
                {
                    int t = _tile[_held]; _tile[_held] = _tile[i]; _tile[i] = t;
                    int r = _rot[_held]; _rot[_held] = _rot[i]; _rot[i] = r;
                    _held = -1; Decide();
                }
            }
            if (Solved()) { _solved = true; Game.Audio.PlayMe("Puzzle solved"); }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(104, 88, 64), new Color(48, 40, 32));
            var tex = g.Load(Game.Project.FindAsset("Graphics/UI/Tile Puzzle", _board) ?? Game.Project.FindAsset("Graphics/Pictures", _board));
            int size = Math.Min((g.Width - 40) / _w, (g.Height - 90) / _h), x0 = (g.Width - size * _w) / 2, y0 = 16;
            int n = _tile.Length;
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    int i = y * _w + x, t = _tile[i];
                    int px = x0 + x * size, py = y0 + y * size;
                    if (Sliding && t == n - 1 && !_solved) { g.FillRect(px, py, size - 2, size - 2, new Color(32, 24, 16)); continue; }
                    if (tex != null)
                    {
                        int tw = tex.Width / _w, th = tex.Height / _h;
                        g.DrawEx(tex, (t % _w) * tw, (t / _w) * th, tw, th, px + size / 2.0, py + size / 2.0, (size - 2.0) / tw, (size - 2.0) / th, -90 * _rot[i], false, 255, 0);
                    }
                    else
                    {
                        g.FillRect(px, py, size - 2, size - 2, new Color((byte)(150 + (t * 53) % 100), (byte)(150 + (t * 97) % 100), (byte)(150 + (t * 31) % 100)));
                        string label = (t + 1).ToString();
                        g.DrawText(px + size / 2 - g.TextWidth(label) / 2, py + size / 2 - 14, label, Ui.Dark, Ui.DarkShadow);
                        if (_rot[i] != 0) g.FillRect(px + (_rot[i] == 1 ? size - 10 : 2), py + (_rot[i] == 2 ? size - 10 : 2), 6, 6, new Color(200, 40, 40));
                    }
                    if (i == _held) g.FillRect(px, py, size - 2, 4, new Color(64, 200, 255));
                    if (x == _cx && y == _cy && !_solved)
                    {
                        var c = new Color(248, 64, 48);
                        g.FillRect(px - 2, py - 2, size + 2, 3, c); g.FillRect(px - 2, py + size - 3, size + 2, 3, c);
                        g.FillRect(px - 2, py, 3, size - 2, c); g.FillRect(px + size - 3, py, 3, size - 2, c);
                    }
                }
            Ui.Box(g, 4, g.Height - 64, g.Width - 8, 60);
            Ui.Text(g, 18, g.Height - 52, _solved ? "Puzzle solved!" : Sliding ? "Action: slide  Cancel: give up" : Rotating ? "Action: swap  Shift: turn  Cancel: give up" : "Action: swap  Cancel: give up");
        }
    }

    /// <summary>
    /// The mining game (pbMiningGame): dig into a wall with the pickaxe or the hammer (Shift switches) to uncover the
    /// items buried in it before the wall collapses. Everything fully uncovered is yours.
    /// </summary>
    public class MiningScene : Scene
    {
        const int W = 13, H = 10;
        readonly int[,] _rock = new int[W, H];
        readonly List<Buried> _items = new List<Buried>();
        int _cx = 6, _cy = 5, _wall = 0;
        bool _hammer, _over;
        string _msg = "Action: dig  Shift: tool  Cancel: stop";
        class Buried { public string Item; public int X, Y, W, H; public bool Found; }
        static readonly string[][] Treasure =
        {
            new[] { "HELIXFOSSIL", "4", "4" }, new[] { "DOMEFOSSIL", "5", "4" }, new[] { "ROOTFOSSIL", "4", "4" }, new[] { "CLAWFOSSIL", "4", "4" }, new[] { "OLDAMBER", "4", "4" },
            new[] { "FIRESTONE", "3", "3" }, new[] { "WATERSTONE", "3", "3" }, new[] { "THUNDERSTONE", "3", "3" }, new[] { "LEAFSTONE", "3", "3" }, new[] { "MOONSTONE", "4", "2" }, new[] { "SUNSTONE", "3", "3" },
            new[] { "REDSHARD", "3", "3" }, new[] { "BLUESHARD", "3", "3" }, new[] { "YELLOWSHARD", "3", "3" }, new[] { "GREENSHARD", "3", "3" },
            new[] { "HEARTSCALE", "2", "2" }, new[] { "HEARTSCALE", "2", "2" }, new[] { "HARDSTONE", "2", "2" }, new[] { "EVERSTONE", "4", "2" }, new[] { "STARPIECE", "3", "3" },
            new[] { "REVIVE", "3", "3" }, new[] { "MAXREVIVE", "3", "3" }, new[] { "LIGHTCLAY", "4", "4" }, new[] { "DAMPROCK", "3", "3" }, new[] { "HEATROCK", "4", "3" }, new[] { "ICYROCK", "4", "4" }, new[] { "SMOOTHROCK", "4", "4" },
        };

        public MiningScene(Game g) : base(g)
        {
            Opaque = true;
            var rng = new Random(g.Rng.Next());
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) _rock[x, y] = 2 + rng.Next(5);
            var pool = Treasure.Where(t => g.Project.Items.Any(i => i.Id == t[0])).ToList();
            int count = 2 + rng.Next(3);
            for (int k = 0; k < count * 20 && _items.Count < count && pool.Count > 0; k++)
            {
                var t = pool[rng.Next(pool.Count)];
                int w = int.Parse(t[1]), h = int.Parse(t[2]);
                int x = rng.Next(W - w + 1), y = rng.Next(H - h + 1);
                if (_items.Any(b => x < b.X + b.W && b.X < x + w && y < b.Y + b.H && b.Y < y + h)) continue;
                _items.Add(new Buried { Item = t[0], X = x, Y = y, W = w, H = h });
            }
        }

        void Hit(int x, int y, int amount) { if (x >= 0 && y >= 0 && x < W && y < H) _rock[x, y] = Math.Max(0, _rock[x, y] - amount); }

        protected override void Update()
        {
            if (_over) { if (Input.Trigger(Button.Action) || Input.Trigger(Button.Cancel)) Close(); return; }
            if (Input.Repeat(Button.Left) && _cx > 0) { _cx--; Cursor(); }
            if (Input.Repeat(Button.Right) && _cx < W - 1) { _cx++; Cursor(); }
            if (Input.Repeat(Button.Up) && _cy > 0) { _cy--; Cursor(); }
            if (Input.Repeat(Button.Down) && _cy < H - 1) { _cy++; Cursor(); }
            if (Input.Trigger(Button.Run)) { _hammer = !_hammer; Cursor(); }
            if (Input.Trigger(Button.Cancel)) { Finish("You stopped digging."); return; }
            if (!Input.Trigger(Button.Action)) return;
            Game.Audio.PlaySe(_hammer ? "Mining hammer" : "Mining pick");
            if (_hammer)
            {
                Hit(_cx, _cy, 2);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (dx != 0 || dy != 0) Hit(_cx + dx, _cy + dy, 1);
                _wall += 4 + Game.Rng.Next(2);
            }
            else
            {
                Hit(_cx, _cy, 2);
                Hit(_cx - 1, _cy, 1); Hit(_cx + 1, _cy, 1); Hit(_cx, _cy - 1, 1); Hit(_cx, _cy + 1, 1);
                _wall += 2 + Game.Rng.Next(2);
            }
            foreach (var b in _items.Where(b => !b.Found))
            {
                bool clear = true;
                for (int x = b.X; x < b.X + b.W && clear; x++) for (int y = b.Y; y < b.Y + b.H; y++) if (_rock[x, y] > 0) { clear = false; break; }
                if (clear) { b.Found = true; Game.Audio.PlaySe("Mining found all"); }
            }
            if (_items.All(b => b.Found)) Finish("Everything was dug up!");
            else if (_wall >= 100) Finish("The wall collapsed!");
        }

        void Finish(string why)
        {
            _over = true;
            var found = _items.Where(b => b.Found).ToList();
            foreach (var b in found) Game.State.AddItem(b.Item, 1);
            var names = found.Select(b => { var it = Game.Project.Items.FirstOrDefault(i => i.Id == b.Item); return it != null ? it.Name : b.Item; }).ToList();
            _msg = why + (names.Count > 0 ? " You found: " + string.Join(", ", names) + "." : " You didn't find anything.");
            Game.Message.Show(_msg);
        }

        public override void Draw(Gfx g)
        {
            g.FillRect(0, 0, g.Width, g.Height, new Color(56, 40, 32));
            int s = 28, x0 = (g.Width - W * s) / 2, y0 = 30;
            // Wall meter
            g.FillRect(x0, 8, W * s, 16, new Color(40, 24, 16));
            g.FillRect(x0 + 2, 10, Math.Min(W * s - 4, (W * s - 4) * _wall / 100), 12, _wall > 75 ? new Color(232, 64, 48) : new Color(232, 176, 64));
            foreach (var b in _items)
                for (int x = b.X; x < b.X + b.W; x++) for (int y = b.Y; y < b.Y + b.H; y++)
                    g.FillRect(x0 + x * s, y0 + y * s, s, s, b.Found ? new Color(120, 220, 160) : new Color(200, 200, 216));
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    int r = _rock[x, y];
                    if (r <= 0) continue;
                    int shade = 150 - r * 16;
                    g.FillRect(x0 + x * s, y0 + y * s, s - 1, s - 1, new Color((byte)shade, (byte)(shade * 3 / 4), (byte)(shade / 2)));
                }
            var c = _hammer ? new Color(64, 160, 255) : new Color(248, 64, 48);
            int cx = x0 + _cx * s, cy = y0 + _cy * s;
            g.FillRect(cx, cy, s, 3, c); g.FillRect(cx, cy + s - 3, s, 3, c); g.FillRect(cx, cy, 3, s, c); g.FillRect(cx + s - 3, cy, 3, s, c);
            Ui.Box(g, 4, g.Height - 50, g.Width - 8, 46);
            g.DrawText(18, g.Height - 40, (_hammer ? "Hammer. " : "Pickaxe. ") + (_over ? "Action: leave" : "Action: dig  Shift: tool  Cancel: stop"), Ui.Dark, Ui.DarkShadow);
        }
    }

    /// <summary>
    /// A duel (pbDuel(trainerType, name)): both fighters have 10 HP and pick a move each turn. Attack beats Rest, Defend
    /// blocks Attack, Precise Attack gets through Defend, Fierce Attack hits hard but leaves you open, Jump dodges.
    /// The result is true if the player wins.
    /// </summary>
    public class DuelScene : Scene
    {
        static readonly string[] Moves = { "Attack", "Defend", "Precise Attack", "Fierce Attack", "Rest", "Jump", "Surrender" };
        readonly string _foe; readonly Action<bool> _done;
        int _hp = 10, _foeHp = 10, _sel;
        bool _foeOpen, _meOpen, _over;
        string _log = "Choose your move.";

        public DuelScene(Game g, string foe, Action<bool> done) : base(g) { Opaque = true; _foe = foe; _done = done; }

        int Damage(int move, int other, bool otherOpen)
        {
            int d;
            switch (move)
            {
                case 0: d = other == 1 ? 0 : other == 5 ? 0 : 2; break;
                case 2: d = other == 5 ? 0 : 1; break;
                case 3: d = other == 1 ? 1 : other == 5 ? 0 : 4; break;
                default: d = 0; break;
            }
            return d > 0 && otherOpen ? d + 1 : d;
        }

        protected override void Update()
        {
            if (_over) { if (Input.Trigger(Button.Action)) { Close(); _done(_foeHp <= 0 && _hp > 0); } return; }
            if (Input.Repeat(Button.Down)) { _sel = (_sel + 1) % Moves.Length; Cursor(); }
            if (Input.Repeat(Button.Up)) { _sel = (_sel + Moves.Length - 1) % Moves.Length; Cursor(); }
            if (!Input.Trigger(Button.Action)) return;
            Decide();
            if (_sel == 6) { _hp = 0; _log = "You surrendered."; _over = true; return; }
            int foe = Game.Rng.Next(6);
            if (_foeHp <= 3 && Game.Rng.Next(2) == 0) foe = 4;
            int dealt = Damage(_sel, foe, _foeOpen), taken = Damage(foe, _sel, _meOpen);
            if (_sel == 4) _hp = Math.Min(10, _hp + 2);
            if (foe == 4) _foeHp = Math.Min(10, _foeHp + 2);
            _foeHp -= dealt; _hp -= taken;
            _meOpen = _sel == 3; _foeOpen = foe == 3;
            _log = Moves[_sel] + " vs " + Moves[foe] + " (" + dealt + "/" + taken + ")";
            if (_hp <= 0 || _foeHp <= 0) { _over = true; _log += _foeHp <= 0 && _hp > 0 ? "  You win!" : "  You lost..."; Game.Audio.PlayMe(_foeHp <= 0 && _hp > 0 ? "Duel win" : "Duel lose"); }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(200, 160, 96), new Color(96, 64, 32));
            Ui.Panel(g, 12, 12, 230, 70, new Color(248, 248, 248), new Color(80, 56, 32));
            Ui.Text(g, 24, 18, Game.State.PlayerName); Ui.Text(g, 24, 46, "HP " + Math.Max(0, _hp) + "/10");
            Ui.Panel(g, g.Width - 242, 12, 230, 70, new Color(248, 248, 248), new Color(80, 56, 32));
            Ui.Text(g, g.Width - 230, 18, _foe); Ui.Text(g, g.Width - 230, 46, "HP " + Math.Max(0, _foeHp) + "/10");
            Ui.Panel(g, 12, 96, 230, Moves.Length * 28 + 12, new Color(248, 248, 248), new Color(80, 56, 32));
            for (int i = 0; i < Moves.Length; i++) { if (i == _sel) Ui.Arrow(g, 22, 108 + i * 28); Ui.Text(g, 42, 102 + i * 28, Moves[i]); }
            Ui.Box(g, 4, g.Height - 64, g.Width - 8, 60);
            g.DrawText(18, g.Height - 52, _log, Ui.Dark, Ui.DarkShadow);
        }
    }
}

namespace PokeCreator.Runtime.UI
{
    /// <summary>Triple Triad cards: one per species, side values (1-10) from its base stats.</summary>
    public static class TriadCards
    {
        public static int[] Values(Game g, string species)
        {
            var d = g.Project.Species.FirstOrDefault(s => s.Id == species && s.Form == 0);
            if (d == null || d.BaseStats == null || d.BaseStats.Count < 6) return new[] { 1, 1, 1, 1 };
            Func<int, int> v = st => Math.Max(1, Math.Min(10, (st + 9) / 15));
            return new[] { v(d.BaseStats[1]), v(d.BaseStats[2]), v(d.BaseStats[4]), v(d.BaseStats[3]) }; // N E S W
        }
        public static int Price(Game g, string species) { var v = Values(g, species); return v.Sum() * 30; }
        public static Dictionary<string, int> Owned(Game g)
        {
            if (g.State.Extras.TriadCards == null) g.State.Extras.TriadCards = new Dictionary<string, int>();
            return g.State.Extras.TriadCards;
        }
        public static int Count(Game g) { return Owned(g).Values.Sum(); }
        public static void Give(Game g, string species, int n)
        {
            var o = Owned(g); int have; o.TryGetValue(species, out have);
            have += n; if (have <= 0) o.Remove(species); else o[species] = Math.Min(99, have);
        }
        public static string Name(Game g, string species) { var d = g.Project.Species.FirstOrDefault(s => s.Id == species); return d != null ? d.Name : species; }

        public static void DrawCard(Gfx g, Game game, string species, int x, int y, int w, int h, bool mine)
        {
            g.FillRect(x, y, w, h, mine ? new Color(120, 168, 240) : new Color(240, 128, 128));
            g.FillRect(x + 2, y + 2, w - 4, h - 4, mine ? new Color(176, 208, 248) : new Color(248, 184, 184));
            var v = Values(game, species);
            Func<int, string> t = n => n >= 10 ? "A" : n.ToString();
            g.DrawText(x + w / 2 - 6, y + 2, t(v[0]), Ui.Dark, Ui.DarkShadow);
            g.DrawText(x + w - 18, y + h / 2 - 12, t(v[1]), Ui.Dark, Ui.DarkShadow);
            g.DrawText(x + w / 2 - 6, y + h - 28, t(v[2]), Ui.Dark, Ui.DarkShadow);
            g.DrawText(x + 6, y + h / 2 - 12, t(v[3]), Ui.Dark, Ui.DarkShadow);
            var icon = g.Load(game.Project.FindAsset("Graphics/Pokemon/Icons", species));
            if (icon != null) { int fw = Math.Min(icon.Width, icon.Height); g.Draw(icon, 0, 0, fw, icon.Height, x + w / 2 - fw / 2, y + h / 2 - icon.Height / 2); }
        }
    }

    /// <summary>
    /// A Triple Triad duel (pbTriadDuel): each player places 5 cards on a 3×3 board; a card captures neighbouring
    /// opponent cards whose touching side is lower. Most cards wins; the winner takes one of the loser's cards.
    /// </summary>
    public class TriadDuelScene : Scene
    {
        readonly string _foe; readonly Action<int> _done; // 1 win, 2 lose, 3 draw
        readonly List<string> _hand, _foeHand;
        readonly string[] _board = new string[9];
        readonly bool[] _mine = new bool[9];
        int _sel, _cell = 4; bool _choosingCell, _myTurn = true, _over;
        string _msg = "Choose a card.";

        public TriadDuelScene(Game g, string foe, int minLevel, int maxLevel, Action<int> done) : base(g)
        {
            Opaque = true; _foe = foe; _done = done;
            var owned = TriadCards.Owned(g).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
            _hand = owned.OrderByDescending(s => TriadCards.Values(g, s).Sum()).ThenBy(_ => g.Rng.Next()).Take(5).ToList();
            // The opponent's hand: species whose card strength is near the player's
            int target = _hand.Count > 0 ? (int)_hand.Average(s => TriadCards.Values(g, s).Sum()) : 16;
            var pool = g.Project.Species.Where(s => s.Form == 0 && s.BaseStats != null && s.BaseStats.Count == 6).Select(s => s.Id)
                .Where(id => Math.Abs(TriadCards.Values(g, id).Sum() - target) <= 4).ToList();
            if (pool.Count < 5) pool = g.Project.Species.Where(s => s.Form == 0).Select(s => s.Id).ToList();
            _foeHand = Enumerable.Range(0, 5).Select(_ => pool[g.Rng.Next(pool.Count)]).ToList();
        }


        void Place(int cell, string card, bool mine)
        {
            _board[cell] = card; _mine[cell] = mine;
            var v = TriadCards.Values(Game, card);
            int x = cell % 3, y = cell / 3;
            // N, E, S, W neighbours: my side index vs their opposite side
            int[][] dirs = { new[] { 0, -1, 0, 2 }, new[] { 1, 0, 1, 3 }, new[] { 0, 1, 2, 0 }, new[] { -1, 0, 3, 1 } };
            foreach (var d in dirs)
            {
                int nx = x + d[0], ny = y + d[1];
                if (nx < 0 || ny < 0 || nx > 2 || ny > 2) continue;
                int n = ny * 3 + nx;
                if (_board[n] == null || _mine[n] == mine) continue;
                if (v[d[2]] > TriadCards.Values(Game, _board[n])[d[3]]) _mine[n] = mine;
            }
        }

        int Score(bool mine) { return Enumerable.Range(0, 9).Count(i => _board[i] != null && _mine[i] == mine) + (mine ? _hand.Count : _foeHand.Count); }

        void FoeMove()
        {
            int bestCell = -1, bestCard = 0, best = int.MinValue;
            for (int c = 0; c < _foeHand.Count; c++)
                for (int cell = 0; cell < 9; cell++)
                {
                    if (_board[cell] != null) continue;
                    var saveB = (string[])_board.Clone(); var saveM = (bool[])_mine.Clone();
                    Place(cell, _foeHand[c], false);
                    int s = Enumerable.Range(0, 9).Count(i => _board[i] != null && !_mine[i]) * 10 - TriadCards.Values(Game, _foeHand[c]).Sum() + Game.Rng.Next(3);
                    Array.Copy(saveB, _board, 9); Array.Copy(saveM, _mine, 9);
                    if (s > best) { best = s; bestCell = cell; bestCard = c; }
                }
            if (bestCell < 0) return;
            Place(bestCell, _foeHand[bestCard], false);
            _foeHand.RemoveAt(bestCard);
        }

        void CheckEnd()
        {
            if (_board.Any(b => b == null)) return;
            _over = true;
            int me = Score(true), them = Score(false);
            if (me > them)
            {
                string prize = _foeHand.Concat(Enumerable.Range(0, 9).Where(i => !_mine[i]).Select(i => _board[i])).Concat(_board.Where(b => b != null)).FirstOrDefault();
                if (prize != null) TriadCards.Give(Game, prize, 1);
                _msg = "You won! You got " + TriadCards.Name(Game, prize) + "'s card.";
            }
            else if (me < them)
            {
                var lose = _hand.Concat(_board.Where((b, i) => b != null && TriadCards.Owned(Game).ContainsKey(b))).FirstOrDefault();
                if (lose != null && TriadCards.Owned(Game).ContainsKey(lose)) TriadCards.Give(Game, lose, -1);
                _msg = "You lost..." + (lose != null ? " " + _foe + " took " + TriadCards.Name(Game, lose) + "'s card." : "");
            }
            else _msg = "It's a draw.";
        }

        protected override void Update()
        {
            if (_over) { if (Input.Trigger(Button.Action)) { int me = Score(true), them = Score(false); Close(); _done(me > them ? 1 : me < them ? 2 : 3); } return; }
            if (!_myTurn) { if (Frame % 30 == 0) { FoeMove(); _myTurn = true; _msg = "Choose a card."; CheckEnd(); } return; }
            if (!_choosingCell)
            {
                if (_hand.Count == 0) { _myTurn = false; return; }
                if (Input.Repeat(Button.Down)) { _sel = (_sel + 1) % _hand.Count; Cursor(); }
                if (Input.Repeat(Button.Up)) { _sel = (_sel + _hand.Count - 1) % _hand.Count; Cursor(); }
                if (Input.Trigger(Button.Action)) { _choosingCell = true; _msg = "Where?"; Decide(); }
                return;
            }
            if (Input.Repeat(Button.Left) && _cell % 3 > 0) { _cell--; Cursor(); }
            if (Input.Repeat(Button.Right) && _cell % 3 < 2) { _cell++; Cursor(); }
            if (Input.Repeat(Button.Up) && _cell > 2) { _cell -= 3; Cursor(); }
            if (Input.Repeat(Button.Down) && _cell < 6) { _cell += 3; Cursor(); }
            if (Input.Trigger(Button.Cancel)) { _choosingCell = false; _msg = "Choose a card."; Cancel(); return; }
            if (Input.Trigger(Button.Action))
            {
                if (_board[_cell] != null) { Buzz(); return; }
                Place(_cell, _hand[_sel], true);
                _hand.RemoveAt(_sel); _sel = 0; _choosingCell = false;
                Decide();
                CheckEnd();
                if (!_over) { _myTurn = false; _msg = _foe + " is thinking..."; }
            }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(64, 96, 64), new Color(24, 40, 24));
            int cw = 76, ch = 84, bx = (g.Width - cw * 3) / 2, by = 16;
            for (int i = 0; i < 9; i++)
            {
                int x = bx + (i % 3) * cw, y = by + (i / 3) * ch;
                g.FillRect(x, y, cw - 4, ch - 4, new Color(40, 64, 40));
                if (_board[i] != null) TriadCards.DrawCard(g, Game, _board[i], x, y, cw - 4, ch - 4, _mine[i]);
                if (_choosingCell && i == _cell) { var c = new Color(248, 232, 64); g.FillRect(x, y, cw - 4, 3, c); g.FillRect(x, y + ch - 7, cw - 4, 3, c); }
            }
            for (int i = 0; i < _hand.Count; i++)
            {
                int y = 16 + i * 58;
                TriadCards.DrawCard(g, Game, _hand[i], 14 + (i == _sel && !_choosingCell ? 10 : 0), y, 64, 54, true);
            }
            for (int i = 0; i < _foeHand.Count; i++) { g.FillRect(g.Width - 78, 16 + i * 58, 64, 54, new Color(160, 64, 64)); g.FillRect(g.Width - 74, 20 + i * 58, 56, 46, new Color(208, 96, 96)); }
            Ui.Box(g, 4, g.Height - 64, g.Width - 8, 60);
            g.DrawText(18, g.Height - 52, "You " + Score(true) + " - " + Score(false) + " " + _foe + ".  " + _msg, Ui.Dark, Ui.DarkShadow);
        }
    }
}

namespace PokeCreator.Runtime.UI
{
    /// <summary>The Triple Triad card shop: buy (a daily selection) or sell cards; also just a list of owned cards.</summary>
    public class TriadShopScene : Scene
    {
        readonly bool _sell, _viewOnly;
        List<string> _list;
        int _index;

        public TriadShopScene(Game g, bool sell, bool viewOnly = false) : base(g)
        {
            Opaque = true; _sell = sell; _viewOnly = viewOnly;
            if (sell) _list = TriadCards.Owned(g).Keys.OrderBy(k => k).ToList();
            else
            {
                var rng = new Random(DateTime.Now.DayOfYear + DateTime.Now.Year * 400);
                var all = g.Project.Species.Where(s => s.Form == 0 && s.BaseStats != null && s.BaseStats.Count == 6).Select(s => s.Id).ToList();
                _list = Enumerable.Range(0, Math.Min(12, all.Count)).Select(_ => all[rng.Next(all.Count)]).Distinct().ToList();
            }
        }

        protected override void Update()
        {
            if (Input.Trigger(Button.Cancel) || _list.Count == 0 && Input.Trigger(Button.Action)) { Cancel(); Close(); return; }
            if (_list.Count == 0) return;
            if (Input.Repeat(Button.Down)) { _index = (_index + 1) % _list.Count; Cursor(); }
            if (Input.Repeat(Button.Up)) { _index = (_index + _list.Count - 1) % _list.Count; Cursor(); }
            if (!Input.Trigger(Button.Action) || _viewOnly) return;
            string sp = _list[_index]; int price = TriadCards.Price(Game, sp);
            if (_sell)
            {
                Game.Message.Show("Sell " + TriadCards.Name(Game, sp) + "'s card for $" + price / 2 + "?", new List<string> { "Yes", "No" }, 1, k =>
                {
                    if (k != 0) return;
                    TriadCards.Give(Game, sp, -1); Game.State.Money += price / 2; Game.Audio.PlaySe("Mart buy item");
                    if (!TriadCards.Owned(Game).ContainsKey(sp)) { _list.Remove(sp); _index = Math.Max(0, Math.Min(_index, _list.Count - 1)); }
                });
            }
            else
            {
                if (Game.State.Money < price) { Buzz(); Game.Message.Show("You don't have enough money."); return; }
                Game.Message.Show("Buy " + TriadCards.Name(Game, sp) + "'s card for $" + price + "?", new List<string> { "Yes", "No" }, 1, k =>
                {
                    if (k != 0) return;
                    Game.State.Money -= price; TriadCards.Give(Game, sp, 1); Game.Audio.PlaySe("Mart buy item");
                });
            }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(64, 96, 64), new Color(24, 40, 24));
            Ui.Panel(g, 8, 8, 280, g.Height - 80, new Color(248, 248, 248), new Color(32, 64, 32));
            int start = Math.Max(0, _index - 8);
            for (int i = start; i < _list.Count && i < start + 9; i++)
            {
                int y = 16 + (i - start) * 30;
                if (i == _index) Ui.Arrow(g, 18, y + 6);
                string label = TriadCards.Name(Game, _list[i]);
                int have; TriadCards.Owned(Game).TryGetValue(_list[i], out have);
                Ui.Text(g, 38, y, label);
                Ui.TextRight(g, 280, y, _viewOnly || _sell ? "×" + have : "$" + TriadCards.Price(Game, _list[i]));
            }
            if (_list.Count > 0) TriadCards.DrawCard(g, Game, _list[_index], 330, 40, 120, 130, true);
            Ui.Box(g, 4, g.Height - 64, g.Width - 8, 60);
            Ui.Text(g, 18, g.Height - 52, _list.Count == 0 ? "No cards." : "Money: $" + Game.State.Money + "   Cards: " + TriadCards.Count(Game));
        }
    }
}
