using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Pkmn;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime.World
{
    /// <summary>A team entered into the Hall of Fame.</summary>
    public class HallOfFameEntry
    {
        public string Date { get; set; }
        public List<Pokemon> Team { get; set; }
        public HallOfFameEntry() { Team = new List<Pokemon>(); }
    }
}

namespace PokeCreator.Runtime.UI
{
    /// <summary>
    /// pbHallOfFameEntry: each party Pokémon is shown in turn, then the whole team with "Welcome to the Hall of Fame!".
    /// The team is recorded and the game is saved. With viewOnly (pbHallOfFamePC) it shows the past entries instead.
    /// </summary>
    public class HallOfFameScene : Scene
    {
        readonly bool _view;
        readonly List<Pokemon> _team;
        int _shown;         // Pokémon revealed so far
        int _timer;
        bool _finished;
        int _entry;         // view mode: which entry

        public HallOfFameScene(Game g, bool viewOnly = false) : base(g)
        {
            Opaque = true; _view = viewOnly;
            if (viewOnly)
            {
                _entry = Math.Max(0, g.State.HallOfFame.Count - 1);
                _team = g.State.HallOfFame.Count > 0 ? g.State.HallOfFame[_entry].Team : new List<Pokemon>();
                _shown = _team.Count; _finished = true;
                return;
            }
            _team = g.State.Party.Where(p => !p.IsEgg).Select(Copy).ToList();
            string bgm = g.Project.FindAsset("Audio/BGM", "Hall of Fame") != null ? "Hall of Fame" : null;
            if (bgm != null) g.Audio.PlayBgm(bgm);
        }

        static Pokemon Copy(Pokemon p)
        {
            return (Pokemon)PokeCreator.Json.Json.ConvertTo(PokeCreator.Json.Json.Parse(PokeCreator.Json.Json.Write(p)), typeof(Pokemon));
        }

        protected override void Update()
        {
            if (_view)
            {
                var hof = Game.State.HallOfFame;
                if (hof.Count > 1 && (Input.Trigger(Button.Left) || Input.Trigger(Button.Right)))
                {
                    _entry = (_entry + (Input.Trigger(Button.Left) ? hof.Count - 1 : 1)) % hof.Count;
                    _team.Clear(); _team.AddRange(hof[_entry].Team); _shown = _team.Count; Cursor();
                }
                if (Input.Trigger(Button.Action) || Input.Trigger(Button.Cancel)) { Cancel(); Close(); }
                return;
            }
            if (_finished) return;
            _timer++;
            if (_shown < _team.Count)
            {
                if (_timer >= 100 || (Input.Trigger(Button.Action) && _timer > 10))
                {
                    _shown++; _timer = 0;
                    if (_shown < _team.Count && _team[_shown] != null) Game.Audio.PlayCry(_team[_shown]);
                }
                if (_shown == 0 && _timer == 1 && _team.Count > 0) Game.Audio.PlayCry(_team[0]);
                return;
            }
            // Whole team: welcome message, record, save
            _finished = true;
            Game.Message.Show("Welcome to the Hall of Fame!", null, -1, _ =>
            {
                World.GameStats.SetTimeTo(Game, "hall_of_fame"); World.GameStats.Inc(Game, "hall_of_fame_entry_count");
                Game.State.HallOfFame.Add(new HallOfFameEntry { Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Team = _team });
                try { SaveSystem.Save(Game); } catch (Exception ex) { Console.WriteLine("Hall of Fame save failed: " + ex.Message); }
                Close();
            });
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(40, 32, 72), new Color(8, 8, 24));
            // Sparkles
            var rng = new Random(7);
            for (int i = 0; i < 40; i++)
            {
                int x = rng.Next(g.Width), y = rng.Next(g.Height);
                if ((Frame / 8 + i) % 5 != 0) g.FillRect(x, y, 2, 2, new Color(248, 232, 160, 200));
            }
            if (_team.Count == 0) { Ui.LightText(g, 24, g.Height / 2, _view ? "No Hall of Fame records yet." : ""); return; }
            if (!_finished && _shown < _team.Count)
            {
                // One Pokémon in the middle, sliding in
                var pk = _team[_shown];
                int slide = Math.Max(0, 20 - _timer) * 16;
                Ui.PokemonFront(g, Game, pk, g.Width / 2 + slide, g.Height / 2 + 64, 192);
                DrawInfo(g, pk, g.Height - 100);
                return;
            }
            // The team in two rows
            int cols = Math.Min(3, _team.Count);
            for (int i = 0; i < _team.Count; i++)
            {
                int row = i / 3, col = i % 3, inRow = Math.Min(3, _team.Count - row * 3);
                int cx = g.Width / 2 + (col - (inRow - 1) / 2.0 > 0 ? 1 : -1) * 0 + (int)((col - (inRow - 1) / 2.0) * 150);
                Ui.PokemonFront(g, Game, _team[i], cx, 150 + row * 140, 128);
            }
            string title = _view ? "Hall of Fame  " + (_entry + 1) + "/" + Game.State.HallOfFame.Count + "  " + Game.State.HallOfFame[_entry].Date : Game.State.PlayerName;
            Ui.LightText(g, 20, 8, title);
            if (_view) Ui.LightTextRight(g, g.Width - 20, g.Height - 34, Game.State.HallOfFame.Count > 1 ? "Left/Right: other entries" : "");
        }

        void DrawInfo(Gfx g, Pokemon pk, int y)
        {
            MessageWindow.DrawBox(g, 16, y, g.Width - 32, 84);
            Ui.Text(g, 40, y + 14, pk.Name(Game.Project) + (pk.Gender == "M" ? " ♂" : pk.Gender == "F" ? " ♀" : ""));
            Ui.TextRight(g, g.Width - 40, y + 14, "Lv. " + pk.Level);
            Ui.Text(g, 40, y + 44, "OT/ " + (pk.OTName ?? Game.State.PlayerName));
            Ui.TextRight(g, g.Width - 40, y + 44, "IDNo. " + (pk.OTId % 100000).ToString("00000"));
        }
    }

    /// <summary>Scrolling credits ($scene = Scene_Credits.new), from Game Settings > Credits.</summary>
    public class CreditsScene : Scene
    {
        readonly List<string> _lines;
        double _y;
        readonly string _oldBgm;

        public CreditsScene(Game g) : base(g)
        {
            Opaque = true;
            string text = g.Project.Settings.Credits;
            if (string.IsNullOrWhiteSpace(text))
                text = (g.Project.Settings.Title ?? "") + "\n\n\nThank you for playing!\n\n\nMade with PokeCreator\n\n\nPokémon is © Nintendo, Creatures Inc.\nand GAME FREAK inc.";
            _lines = text.Replace("\r", "").Split('\n').ToList();
            _y = g.Gfx.Height + 16;
            _oldBgm = g.Audio.CurrentBgm;
            if (!string.IsNullOrEmpty(g.Project.Settings.CreditsBGM)) g.Audio.PlayBgm(g.Project.Settings.CreditsBGM);
        }

        protected override void Update()
        {
            _y -= Input.Press(Button.Action) ? 4 : 0.75;
            int total = _lines.Count * 32;
            if (_y + total < -16 || Input.Trigger(Button.Cancel))
            {
                Close();
                Game.PlayMapMusic();
            }
        }

        public override void Draw(Gfx g)
        {
            g.FillRect(0, 0, g.Width, g.Height, new Color(0, 0, 0));
            for (int i = 0; i < _lines.Count; i++)
            {
                int y = (int)_y + i * 32;
                if (y < -32 || y > g.Height) continue;
                string s = Game.Message.Expand(_lines[i]);
                g.DrawText((g.Width - g.TextWidth(s)) / 2, y, s, new Color(248, 248, 248), new Color(72, 72, 80));
            }
        }
    }
}
