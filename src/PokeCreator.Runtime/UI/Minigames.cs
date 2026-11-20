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
