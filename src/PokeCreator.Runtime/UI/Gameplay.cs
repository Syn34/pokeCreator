using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime.UI
{
    // =====================================================================================
    /// <summary>Player options (text speed, volumes), kept next to the save file.</summary>
    public class PlayerOptions
    {
        public static readonly string[] TextSpeeds = { "Slow", "Normal", "Fast", "Instant" };
        public int TextSpeed { get; set; }
        public int BgmVolume { get; set; }
        public int SeVolume { get; set; }
        /// <summary>Message / menu frames chosen by the player ("" = the game's default).</summary>
        public string SpeechFrame { get; set; }
        public string MenuFrame { get; set; }
        /// <summary>Running key: false = hold to run, true = press to switch running on/off.</summary>
        public bool RunToggle { get; set; }
        /// <summary>Battle style: false = Switch (asked to change Pokémon when a trainer sends out the next one), true = Set.</summary>
        public bool BattleStyleSet { get; set; }
        /// <summary>Battle animations off (Essentials' "Battle Effects" option). Stored inverted so older options files keep them on.</summary>
        public bool BattleEffectsOff { get; set; }
        [Json.JsonIgnore] public bool BattleEffects { get { return !BattleEffectsOff; } set { BattleEffectsOff = !value; } }
        public PlayerOptions() { TextSpeed = 1; BgmVolume = 100; SeVolume = 100; SpeechFrame = ""; MenuFrame = ""; }

        static string FilePath(Game g) { return Path.Combine(SaveSystem.Folder(g.Project), "options.json"); }

        public static PlayerOptions Load(Game g)
        {
            try { if (File.Exists(FilePath(g))) return Json.Json.FromFile<PlayerOptions>(FilePath(g)); } catch { }
            return new PlayerOptions();
        }

        public void Save(Game g)
        {
            try { Directory.CreateDirectory(SaveSystem.Folder(g.Project)); Json.Json.ToFile(FilePath(g), this); } catch { }
        }

        public void Apply(Game g)
        {
            g.Message.CharsPerFrame = new[] { 1, 2, 4, 999 }[Math.Max(0, Math.Min(3, TextSpeed))];
            g.Audio.BgmVolume = Math.Max(0, Math.Min(100, BgmVolume));
            g.Audio.SeVolume = Math.Max(0, Math.Min(100, SeVolume));
            g.Audio.ApplyVolume();
            g.LoadFrames();
        }

        /// <summary>Frame images in Graphics/Windowskins whose names start with prefix ("speech", "choice"), in natural order.</summary>
        public static List<string> Frames(Game g, string prefix)
        {
            var list = new List<string>();
            foreach (var dir in new[] { Path.Combine(g.Project.GraphicsDir, "Windowskins") })
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.png"))
                    {
                        string n = Path.GetFileNameWithoutExtension(f);
                        if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !list.Contains(n)) list.Add(n);
                    }
            list.Sort((a, b) =>
            {
                var ma = System.Text.RegularExpressions.Regex.Match(a, @"^(.*?)(\d+)$"); var mb = System.Text.RegularExpressions.Regex.Match(b, @"^(.*?)(\d+)$");
                if (ma.Success && mb.Success && ma.Groups[1].Value == mb.Groups[1].Value) return int.Parse(ma.Groups[2].Value).CompareTo(int.Parse(mb.Groups[2].Value));
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }
    }

    // =====================================================================================
    /// <summary>Using and giving items, learning moves: chains of messages and screens.</summary>
    public static class ItemFlow
    {
        static void Say(Game g, string text, Action then)
        {
            if (string.IsNullOrEmpty(text)) { if (then != null) then(); return; }
            g.Message.Show(text, null, -1, _ => { if (then != null) then(); });
        }

        public static EvolutionContext Context(Game g)
        {
            return new EvolutionContext { Party = g.State.Party, MapId = g.Map != null ? g.Map.Id : 0, Now = DateTime.Now };
        }

        static ItemData Item(Game g, string id) { return g.Project.Items.FirstOrDefault(i => i.Id == id) ?? new ItemData { Id = id, Name = id }; }

        /// <summary>Bag > Use.</summary>
        public static void UseFromBag(Game g, string itemId, Action refresh)
        {
            var item = Item(g, itemId);
            switch (itemId)
            {
                case "BICYCLE": case "MACHBIKE": case "ACROBIKE": FieldMoves.ToggleBike(g); return;
                case "TOWNMAP": g.PushScene(new TownMapScene(g, false, null)); return;
                case "ESCAPEROPE": FieldMoves.Escape(g, "\\PN used the " + (item != null ? item.Name : "Escape Rope") + "."); return;
                case "ITEMFINDER": case "DOWSINGMACHINE": case "DOWSINGMCHN":
                    g.CloseAllScenes(); FieldMoves.Itemfinder(g, item.Name); return;
            }
            if (Array.IndexOf(Berries.WateringCans, itemId) >= 0) { g.CloseAllScenes(); FieldMoves.WaterFacingPlant(g); return; }
            if (Array.IndexOf(FieldMoves.Rods, itemId) >= 0)
            {
                // Rods: close the menus and fish
                g.CloseAllScenes();
                FieldMoves.Fish(g, itemId);
                return;
            }
            var info = ItemEffects.Info(g.Project, item);
            if (info.Kind == ItemKind.None) { Say(g, "Can't use that here.", refresh); return; }
            if (info.Kind == ItemKind.Repel)
            {
                if (g.State.RepelSteps > 0) { Say(g, "But a repellent's effect still lingers from earlier.", refresh); return; }
                g.State.AddItem(item.Id, -1);
                g.State.RepelSteps = info.Amount;
                g.Audio.PlaySe("Repel");
                Say(g, "\\PN used the " + (item.Name ?? item.Id) + ".", refresh);
                return;
            }
            if (g.State.Party.Count == 0) { Say(g, "There are no Pokémon.", refresh); return; }
            var ctx = Context(g);
            string help = info.Kind == ItemKind.TeachMove ? "Teach " + ItemEffects.MoveName(g.Project, info.Move) + " to which Pokémon?" : "Use on which Pokémon?";
            Func<Pokemon, string> label = null;
            if (info.Kind == ItemKind.Evolution || info.Kind == ItemKind.TeachMove)
                label = pk => ItemEffects.Works(g.Project, pk, item, info, ctx) ? "ABLE" : "NOT ABLE";
            g.PushScene(new PartyScene(g, help, label, (index, close) =>
            {
                UseOn(g, g.State.Party[index], item, info, used =>
                {
                    // Back to the Bag after using one; stay to pick another Pokémon if it did nothing
                    if (used || !g.State.Bag.ContainsKey(item.Id)) { close(); if (refresh != null) refresh(); }
                });
            }, refresh));
        }

        /// <summary>Uses an item on one Pokémon; calls done(true) if the item was used.</summary>
        public static void UseOn(Game g, Pokemon pk, ItemData item, ItemInfo info, Action<bool> done)
        {
            if (info.NeedsMove)
            {
                ChooseMove(g, pk, info.Kind == ItemKind.PPUp ? "Boost PP of which move?" : "Restore which move?", slot =>
                {
                    if (slot < 0) { done(false); return; }
                    Apply(g, pk, item, info, slot, done);
                });
            }
            else Apply(g, pk, item, info, -1, done);
        }

        static void Apply(Game g, Pokemon pk, ItemData item, ItemInfo info, int slot, Action<bool> done)
        {
            var ctx = Context(g);
            var o = ItemEffects.Use(g.Project, pk, item, info, slot, ctx);
            if (!o.Used) { Say(g, o.Message, () => done(false)); return; }
            if (o.TeachMove != null)
            {
                // TMs/TRs: only spent if the move is actually learned
                Say(g, "\\PN booted up the " + (item.Name ?? item.Id) + ".\\nIt contained " + ItemEffects.MoveName(g.Project, o.TeachMove) + ".", () =>
                    LearnMove(g, pk, o.TeachMove, learned => { if (learned && info.Consumed) g.State.AddItem(item.Id, -1); done(learned); }));
                return;
            }
            if (info.Consumed) g.State.AddItem(item.Id, -1);
            if (o.LevelsGained > 0) g.Audio.PlayMe("Pkmn level up");
            else if (info.Kind != ItemKind.Evolution) g.Audio.PlaySe("Use item in party");
            Say(g, o.Message, () => LearnMoves(g, pk, new Queue<string>(o.MovesToLearn), () =>
            {
                if (o.EvolveTo != null) g.PushScene(new EvolutionScene(g, pk, o.EvolveTo, info.Kind != ItemKind.Evolution, () => done(true)));
                else done(true);
            }));
        }

        /// <summary>Learns moves one after another, asking what to forget when it knows four.</summary>
        public static void LearnMoves(Game g, Pokemon pk, Queue<string> moves, Action done)
        {
            if (moves.Count == 0) { done(); return; }
            string m = moves.Dequeue();
            LearnMove(g, pk, m, _ => LearnMoves(g, pk, moves, done));
        }

        public static void LearnMove(Game g, Pokemon pk, string move, Action<bool> done)
        {
            var p = g.Project;
            string name = pk.Name(p), mname = ItemEffects.MoveName(p, move);
            if (Growth.TryLearn(p, pk, move))
            {
                g.Audio.PlayMe("Pkmn move learnt");
                Say(g, name + " learned " + mname + "!", () => done(true));
                return;
            }
            g.Message.Show(name + " wants to learn " + mname + ", but it already knows four moves.\\nForget a move to learn " + mname + "?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) { Say(g, name + " did not learn " + mname + ".", () => done(false)); return; }
                ChooseMove(g, pk, "Which move should be forgotten?", slot =>
                {
                    if (slot < 0) { Say(g, name + " did not learn " + mname + ".", () => done(false)); return; }
                    string old = ItemEffects.MoveName(p, pk.Moves[slot].Id);
                    Growth.Replace(p, pk, slot, move);
                    g.Audio.PlayMe("Pkmn move learnt");
                    Say(g, "1, 2, and... ... ... Poof!\\n" + name + " forgot how to use " + old + ".\\nAnd... " + name + " learned " + mname + "!", () => done(true));
                });
            });
        }

        /// <summary>Picks one of the Pokémon's moves (index), or -1 if cancelled.</summary>
        public static void ChooseMove(Game g, Pokemon pk, string prompt, Action<int> chosen)
        {
            var p = g.Project;
            var names = pk.Moves.Select(m =>
            {
                var md = p.Moves.FirstOrDefault(x => x.Id == m.Id);
                return ItemEffects.MoveName(p, m.Id) + "  " + m.PP + "/" + (md != null ? Pokemon.TotalPP(md, m.PPUps) : m.PP);
            }).ToList();
            names.Add("Cancel");
            g.Message.Show(prompt, names, names.Count - 1, c => chosen(c >= 0 && c < pk.Moves.Count ? c : -1));
        }

        /// <summary>Party > Item > Give, or Bag > Give.</summary>
        public static void Give(Game g, Pokemon pk, string itemId, Action done)
        {
            var item = Item(g, itemId);
            string name = pk.Name(g.Project), iname = item.Name ?? item.Id;
            if (pk.IsEgg) { Say(g, "Eggs can't hold items.", done); return; }
            if (item.Pocket == 8) { Say(g, "The " + iname + " can't be held.", done); return; }
            if (!string.IsNullOrEmpty(pk.Item))
            {
                string oldName = Item(g, pk.Item).Name ?? pk.Item;
                g.Message.Show(name + " is already holding the " + oldName + ".\\nWould you like to switch the two items?", new List<string> { "Yes", "No" }, 1, c =>
                {
                    if (c != 0) { done(); return; }
                    g.State.AddItem(pk.Item, 1); g.State.AddItem(item.Id, -1); pk.Item = item.Id;
                    FormChangedByItem(g, pk);
                    Say(g, "The " + oldName + " was taken and replaced with the " + iname + ".", done);
                });
                return;
            }
            g.State.AddItem(item.Id, -1); pk.Item = item.Id;
            FormChangedByItem(g, pk);
            Say(g, name + " is now holding the " + iname + ".", done);
        }

        public static void Take(Game g, Pokemon pk, Action done)
        {
            if (string.IsNullOrEmpty(pk.Item)) { Say(g, pk.Name(g.Project) + " isn't holding anything.", done); return; }
            string iname = Item(g, pk.Item).Name ?? pk.Item;
            g.State.AddItem(pk.Item, 1); pk.Item = null;
            FormChangedByItem(g, pk);
            Say(g, "Received the " + iname + " from " + pk.Name(g.Project) + ".", done);
        }

        /// <summary>Arceus, Genesect, Giratina, Ogerpon... take the form their held item gives.</summary>
        static void FormChangedByItem(Game g, Pokemon pk)
        {
            int before = pk.Form;
            FormRules.UpdateItemForm(g.Project, pk);
            if (pk.Form != before) g.Message.Show(pk.Name(g.Project) + " transformed!");
        }
    }

    // =====================================================================================
    /// <summary>"What? Bulbasaur is evolving!" The old and new sprites swap faster and faster, then it evolves.</summary>
    public class EvolutionScene : Scene
    {
        readonly Pokemon _pk;
        readonly Pokemon _newLook;
        readonly string _newSpecies;
        readonly bool _canCancel;
        readonly Action _done;
        int _phase, _t; // 0 intro, 1 animating, 2 finished/cancelled
        const int Length = 300;

        public EvolutionScene(Game g, Pokemon pk, string newSpecies, bool canCancel, Action done) : base(g)
        {
            Opaque = true; _pk = pk; _newSpecies = newSpecies; _canCancel = canCancel; _done = done;
            _newLook = new Pokemon { Species = newSpecies, Form = pk.Form, Shiny = pk.Shiny, Gender = pk.Gender };
            if (_newLook.Data(g.Project) == null || g.Project.Species.All(s => s.Id != newSpecies + "_" + pk.Form)) _newLook.Form = 0;
        }

        protected override void Update()
        {
            if (_phase == 0)
            {
                _phase = 1;
                Game.Audio.PlayBgm("Evolution");
                Game.Message.Show("What?\\n" + _pk.Name(Game.Project) + " is evolving!");
                return;
            }
            if (_phase != 1) return;
            _t++;
            if (_canCancel && Input.Trigger(Button.Cancel))
            {
                _phase = 2;
                Game.Audio.StopBgm(); Game.RestoreMapBgm();
                Game.Message.Show("Huh?\\n" + _pk.Name(Game.Project) + " stopped evolving!", null, -1, _ => Finish());
                return;
            }
            if (_t < Length) return;
            _phase = 2;
            var p = Game.Project;
            string oldName = _pk.Name(p), oldSpecies = _pk.Species;
            string oldSpeciesName = _pk.Data(p) != null ? _pk.Data(p).Name : _pk.Species;
            var moves = Growth.Evolve(p, _pk, _newSpecies);
            Game.State.Register(_pk);
            Game.Audio.StopBgm();
            Game.Audio.PlayMe("Evolution success");
            string newName = _pk.Data(p) != null ? _pk.Data(p).Name : _newSpecies;
            Game.Message.Show("Congratulations! Your " + oldName + " evolved into " + newName + "!", null, -1, _ =>
                ItemFlow.LearnMoves(Game, _pk, new Queue<string>(moves), () =>
                {
                    // Nincada also leaves a Shedinja behind if there's room and a Poké Ball
                    if (Growth.MakesShedinja(p, oldSpecies, _newSpecies) && Game.State.Party.Count < GameState.MaxParty && Game.State.Bag.ContainsKey("POKEBALL"))
                    {
                        string sh = Growth.ShedinjaSpecies(p, oldSpecies);
                        if (sh != null)
                        {
                            var s = Pokemon.Create(p, sh, _pk.Level, Game.Rng);
                            s.OTName = _pk.OTName; s.OTId = _pk.OTId; s.Nature = _pk.Nature; s.IV = new List<int>(_pk.IV);
                            Game.State.Party.Add(s); Game.State.Register(s); Game.State.AddItem("POKEBALL", -1);
                        }
                    }
                    Game.RestoreMapBgm();
                    Finish();
                }));
        }

        void Finish() { Close(); if (_done != null) _done(); }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(40, 56, 96), new Color(8, 16, 40));
            // Light rays
            for (int i = 0; i < 12; i++)
            {
                int w = 6 + (i * 37 + Frame) % 40;
                g.FillRect((i * 53 + Frame * 2) % g.Width, 0, 2, g.Height, new Color(255, 255, 255, (byte)(20 + w)));
            }
            bool showNew;
            if (_phase == 1 && _t > 30)
            {
                // Swap faster and faster
                double progress = Math.Min(1.0, (_t - 30) / (double)(Length - 60));
                int period = Math.Max(2, (int)(40 - 36 * progress));
                showNew = (_t / period) % 2 == 1;
            }
            else showNew = _phase == 2 && _pk.Species == _newSpecies;
            Ui.PokemonFront(g, Game, showNew ? _newLook : _pk, g.Width / 2, g.Height / 2 + 60, 192);
            if (_phase == 1 && _t > Length - 30) g.FillRect(0, 0, g.Width, g.Height, new Color(255, 255, 255, (byte)Math.Min(255, (_t - (Length - 30)) * 9)));
        }
    }

    // =====================================================================================
    /// <summary>Pokédex: every species in order, seen and caught counts; Action opens an entry.</summary>
    public class PokedexScene : Scene
    {
        readonly List<SpeciesData> _list;
        int _index, _scroll;
        const int Rows = 8;

        public PokedexScene(Game g) : base(g)
        {
            Opaque = true;
            _list = g.Project.Species.Where(s => s.Form == 0).ToList();
            // Start on the last species the player has seen
            int last = _list.FindLastIndex(s => g.State.Seen.Contains(s.Id));
            _index = Math.Max(0, last); _scroll = Math.Max(0, _index - Rows / 2);
        }

        protected override void Update()
        {
            int n = _list.Count; if (n == 0) { if (Input.Trigger(Button.Cancel)) Close(); return; }
            int old = _index;
            if (Input.Repeat(Button.Down)) _index = Math.Min(n - 1, _index + 1);
            if (Input.Repeat(Button.Up)) _index = Math.Max(0, _index - 1);
            if (Input.Repeat(Button.Right)) _index = Math.Min(n - 1, _index + Rows);
            if (Input.Repeat(Button.Left)) _index = Math.Max(0, _index - Rows);
            if (_index != old) Cursor();
            if (_index < _scroll) _scroll = _index;
            if (_index >= _scroll + Rows) _scroll = _index - Rows + 1;
            if (Input.Trigger(Button.Cancel)) { Cancel(); Close(); return; }
            if (Input.Trigger(Button.Action))
            {
                if (!Game.State.Seen.Contains(_list[_index].Id)) { Buzz(); return; }
                Decide();
                Game.PushScene(new PokedexEntryScene(Game, _list, _index, i => { _index = i; _scroll = Math.Max(0, Math.Min(_index - Rows / 2, _list.Count - Rows)); }));
            }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(232, 72, 64), new Color(168, 32, 32));
            Ui.Panel(g, 8, 8, g.Width - 16, 40, new Color(248, 248, 248), new Color(120, 24, 24));
            Ui.Text(g, 22, 12, "Pokédex");
            Ui.TextRight(g, g.Width - 24, 12, "Seen " + Game.State.Seen.Count + "   Owned " + Game.State.Caught.Count);
            // Sprite of the highlighted species on the left
            Ui.Panel(g, 8, 56, 200, 240, new Color(248, 248, 248), new Color(120, 24, 24));
            var cur = _list.Count > 0 ? _list[_index] : null;
            if (cur != null && Game.State.Seen.Contains(cur.Id)) Ui.PokemonFront(g, Game, new Pokemon { Species = cur.Id }, 108, 256, 160);
            else Ui.TextCenter(g, 108, 160, "?");
            // List
            Ui.Panel(g, 216, 56, g.Width - 224, 312, new Color(248, 248, 248), new Color(120, 24, 24));
            int lh = 37;
            for (int row = 0; row < Rows; row++)
            {
                int i = _scroll + row; if (i >= _list.Count) break;
                var s = _list[i]; int y = 64 + row * lh;
                if (i == _index) g.FillRect(222, y - 2, g.Width - 236, lh - 2, new Color(248, 216, 120));
                bool seen = Game.State.Seen.Contains(s.Id), caught = Game.State.Caught.Contains(s.Id);
                if (caught) { g.FillRect(232, y + 10, 14, 14, new Color(232, 56, 48)); g.FillRect(232, y + 16, 14, 2, new Color(40, 40, 48)); g.FillRect(237, y + 14, 4, 6, new Color(248, 248, 248)); }
                Ui.Text(g, 254, y, (i + 1).ToString("000"));
                Ui.Text(g, 310, y, seen ? (s.Name ?? s.Id) : "-----");
            }
            Ui.Box(g, 8, 304, 200, 64);
            Ui.Text(g, 20, 314, cur == null ? "" : (_index + 1).ToString("000") + " " + (Game.State.Seen.Contains(cur.Id) ? cur.Name : "?????"));
        }
    }

    /// <summary>A Pokédex entry: sprite, category, types, height, weight and description (details only once caught).</summary>
    public class PokedexEntryScene : Scene
    {
        readonly List<SpeciesData> _list;
        int _index;
        readonly Action<int> _onClose;

        public PokedexEntryScene(Game g, List<SpeciesData> list, int index, Action<int> onClose) : base(g)
        {
            Opaque = true; _list = list; _index = index; _onClose = onClose;
        }

        protected override void Update()
        {
            if (Input.Trigger(Button.Cancel) || Input.Trigger(Button.Action)) { Cancel(); Close(); if (_onClose != null) _onClose(_index); return; }
            int dir = Input.Repeat(Button.Down) ? 1 : Input.Repeat(Button.Up) ? -1 : 0;
            if (dir == 0) return;
            for (int i = _index + dir; i >= 0 && i < _list.Count; i += dir)
                if (Game.State.Seen.Contains(_list[i].Id)) { _index = i; Cursor(); break; }
        }

        public override void Draw(Gfx g)
        {
            var s = _list[_index];
            bool caught = Game.State.Caught.Contains(s.Id);
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(232, 72, 64), new Color(168, 32, 32));
            Ui.Panel(g, 8, 8, g.Width - 16, g.Height - 16, new Color(248, 248, 240), new Color(120, 24, 24));
            Ui.Panel(g, 20, 20, 200, 200, new Color(232, 232, 224), new Color(160, 160, 152));
            Ui.PokemonFront(g, Game, new Pokemon { Species = s.Id }, 120, 210, 176);
            Ui.Text(g, 236, 24, (_index + 1).ToString("000") + "  " + (s.Name ?? s.Id));
            Ui.Text(g, 236, 58, caught && !string.IsNullOrEmpty(s.Category) ? s.Category + " Pokémon" : "????? Pokémon");
            if (caught)
            {
                for (int i = 0; i < s.Types.Count; i++) Ui.TypeBadge(g, Game, 236 + i * (Ui.BadgeWidth + 8), 96, s.Types[i]);
                Ui.Text(g, 236, 140, "Height");
                Ui.TextRight(g, g.Width - 32, 140, s.Height.ToString("0.0") + " m");
                Ui.Text(g, 236, 176, "Weight");
                Ui.TextRight(g, g.Width - 32, 176, s.Weight.ToString("0.0") + " kg");
            }
            else
            {
                Ui.Text(g, 236, 140, "Height");  Ui.TextRight(g, g.Width - 32, 140, "??? m");
                Ui.Text(g, 236, 176, "Weight");  Ui.TextRight(g, g.Width - 32, 176, "??? kg");
            }
            string text = caught ? (s.Pokedex ?? "") : "Catch this Pokémon to learn more about it.";
            var words = text.Split(' '); string line = ""; int ly = 236;
            foreach (var w in words)
            {
                string t = line.Length == 0 ? w : line + " " + w;
                if (line.Length > 0 && g.TextWidth(t) > g.Width - 64) { Ui.Text(g, 28, ly, line); ly += 30; line = w; if (ly > 330) break; }
                else line = t;
            }
            if (ly <= 330) Ui.Text(g, 28, ly, line);
        }
    }

    // =====================================================================================
    /// <summary>Options: text speed and volumes. Left/Right change a setting.</summary>
    public class OptionsScene : Scene
    {
        readonly PlayerOptions _o;
        int _row;
        static readonly string[] Labels = { "Text Speed", "Music Volume", "Sound Volume", "Battle Effects", "Battle Style", "Speech Frame", "Menu Frame", "Running Key", "Close" };
        List<string> _speech, _menu;

        public OptionsScene(Game g) : base(g)
        {
            Opaque = true; _o = g.Options;
            _speech = PlayerOptions.Frames(g, "speech"); _speech.Insert(0, "");
            _menu = PlayerOptions.Frames(g, "choice"); _menu.Insert(0, "");
        }

        static string Cycle(List<string> list, string cur, int d)
        {
            int i = Math.Max(0, list.IndexOf(cur ?? ""));
            return list[(i + d + list.Count) % list.Count];
        }

        static string FrameLabel(string f) { return string.IsNullOrEmpty(f) ? "Default" : f; }

        protected override void Update()
        {
            if (Input.Repeat(Button.Down)) { _row = (_row + 1) % Labels.Length; Cursor(); }
            if (Input.Repeat(Button.Up)) { _row = (_row + Labels.Length - 1) % Labels.Length; Cursor(); }
            int d = Input.Repeat(Button.Right) ? 1 : Input.Repeat(Button.Left) ? -1 : 0;
            if (d != 0)
            {
                if (_row == 0) _o.TextSpeed = (_o.TextSpeed + d + 4) % 4;
                else if (_row == 1) _o.BgmVolume = Math.Max(0, Math.Min(100, _o.BgmVolume + d * 10));
                else if (_row == 2) _o.SeVolume = Math.Max(0, Math.Min(100, _o.SeVolume + d * 10));
                else if (_row == 3) _o.BattleEffects = !_o.BattleEffects;
                else if (_row == 4) _o.BattleStyleSet = !_o.BattleStyleSet;
                else if (_row == 5) _o.SpeechFrame = Cycle(_speech, _o.SpeechFrame, d);
                else if (_row == 6) _o.MenuFrame = Cycle(_menu, _o.MenuFrame, d);
                else if (_row == 7) _o.RunToggle = !_o.RunToggle;
                _o.Apply(Game);
                if (_row != Labels.Length - 1) Cursor();
            }
            if (Input.Trigger(Button.Cancel) || (Input.Trigger(Button.Action) && _row == Labels.Length - 1)) { _o.Save(Game); Cancel(); Close(); }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(96, 136, 200), new Color(40, 64, 128));
            Ui.Panel(g, 8, 8, g.Width - 16, 44, new Color(248, 248, 248), new Color(32, 48, 96));
            Ui.Text(g, 22, 14, "Options");
            Ui.Panel(g, 8, 60, g.Width - 16, 234, new Color(248, 248, 248), new Color(32, 48, 96));
            string[] values = { "◀ " + PlayerOptions.TextSpeeds[_o.TextSpeed] + " ▶", "◀ " + _o.BgmVolume + "% ▶", "◀ " + _o.SeVolume + "% ▶",
                                "◀ " + (_o.BattleEffects ? "On" : "Off") + " ▶", "◀ " + (_o.BattleStyleSet ? "Set" : "Switch") + " ▶", "◀ " + FrameLabel(_o.SpeechFrame) + " ▶", "◀ " + FrameLabel(_o.MenuFrame) + " ▶", "◀ " + (_o.RunToggle ? "Toggle" : "Hold") + " ▶", "" };
            for (int i = 0; i < Labels.Length; i++)
            {
                int y = 66 + i * 25;
                if (i == _row) Ui.Arrow(g, 22, y + 8);
                Ui.Text(g, 44, y, Labels[i]);
                Ui.TextRight(g, g.Width - 32, y, values[i]);
            }
            Ui.Box(g, 4, g.Height - 84, g.Width - 8, 80);
            Ui.Text(g, 22, g.Height - 72, "◀ ▶ change a setting.");
            Ui.Text(g, 22, g.Height - 42, "Settings are kept for every save.");
        }
    }

    // =====================================================================================
    /// <summary>Name entry: a grid of letters. Action types, Cancel deletes, OK (or the Menu button) finishes.</summary>
    public class NameEntryScene : Scene
    {
        static readonly string[][] Pages =
        {
            new[] { "ABCDEFGHIJ", "KLMNOPQRST", "UVWXYZ .,'", "abcdefghij", "klmnopqrst", "uvwxyz -!?" },
            new[] { "0123456789", "&/:;()\"#%+", "éèêàâôûçñü", "ÉÈÀÔÇÑÜ*=~", "♂♀…·          ", "          " },
        };
        readonly string _prompt;
        readonly int _max;
        readonly Action<string> _done;
        string _name;
        int _page, _row, _col; // _row == 6: the bottom buttons (col 0 Page, 1 Back, 2 OK)

        public NameEntryScene(Game g, string prompt, string initial, int max, Action<string> done) : base(g)
        {
            Opaque = true; _prompt = prompt; _max = Math.Max(1, max); _done = done; _name = initial ?? "";
            if (_name.Length > _max) _name = _name.Substring(0, _max);
        }

        string Row(int r) { return Pages[_page][r].PadRight(10).Substring(0, 10); }

        protected override void Update()
        {
            if (Input.Repeat(Button.Down)) { _row = (_row + 1) % 7; if (_row == 6) _col = Math.Min(2, _col / 4); Cursor(); }
            if (Input.Repeat(Button.Up)) { _row = (_row + 6) % 7; if (_row == 6) _col = Math.Min(2, _col / 4); Cursor(); }
            int cols = _row == 6 ? 3 : 10;
            if (Input.Repeat(Button.Right)) { _col = (_col + 1) % cols; Cursor(); }
            if (Input.Repeat(Button.Left)) { _col = (_col + cols - 1) % cols; Cursor(); }
            if (_col >= cols) _col = cols - 1;
            if (Input.Trigger(Button.Cancel))
            {
                if (_name.Length > 0) { _name = _name.Substring(0, _name.Length - 1); Cancel(); }
                else Buzz();
                return;
            }
            if (Input.Trigger(Button.Menu)) { Finish(); return; }
            if (!Input.Trigger(Button.Action)) return;
            if (_row == 6)
            {
                if (_col == 0) { _page = (_page + 1) % Pages.Length; Decide(); }
                else if (_col == 1) { if (_name.Length > 0) { _name = _name.Substring(0, _name.Length - 1); Cancel(); } else Buzz(); }
                else Finish();
                return;
            }
            char ch = Row(_row)[_col];
            if (ch == ' ' && Pages[_page][_row].Length <= _col) { Buzz(); return; }
            if (_name.Length >= _max) { Buzz(); return; }
            _name += ch; Decide();
            if (_name.Length >= _max) { _row = 6; _col = 2; }
        }

        void Finish()
        {
            // Empty keeps the current name (a Pokémon keeps its species name)
            Decide(); Close();
            if (_done != null) _done(_name.Trim());
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(120, 184, 232), new Color(56, 112, 184));
            Ui.Panel(g, 8, 8, g.Width - 16, 88, new Color(248, 248, 248), new Color(40, 72, 128));
            Ui.Text(g, 24, 14, _prompt);
            int cw = 24, x0 = 24;
            for (int i = 0; i < _max; i++)
            {
                int x = x0 + i * cw;
                g.FillRect(x, 84, cw - 6, 3, i == _name.Length ? new Color(232, 64, 48) : new Color(120, 120, 136));
                if (i < _name.Length) Ui.Text(g, x + 2, 50, _name[i].ToString());
            }
            Ui.Panel(g, 8, 104, g.Width - 16, 236, new Color(248, 248, 248), new Color(40, 72, 128));
            int gx = 40, gy = 112, sx = (g.Width - 80) / 10, sy = 36;
            for (int r = 0; r < 6; r++)
            {
                string row = Row(r);
                for (int c = 0; c < 10; c++)
                {
                    int x = gx + c * sx, y = gy + r * sy;
                    if (r == _row && c == _col) g.FillRect(x - 6, y, sx - 4, sy - 4, new Color(248, 216, 120));
                    Ui.Text(g, x, y, row[c].ToString());
                }
            }
            string[] buttons = { "Page", "Back", "OK" };
            for (int i = 0; i < 3; i++)
            {
                int bw = 120, bx = 24 + i * (bw + 24), by = g.Height - 40;
                Ui.Panel(g, bx, by, bw, 34, _row == 6 && _col == i ? new Color(248, 216, 120) : new Color(220, 228, 240), new Color(40, 72, 128));
                Ui.TextCenter(g, bx + bw / 2, by + 1, buttons[i]);
            }
        }
    }

    /// <summary>Input Number (RMXP command 103): digits changed with Up/Down, Left/Right to move between them.</summary>
    public class NumberInputScene : Scene
    {
        readonly int[] _digits;
        int _pos;
        readonly Action<int> _done;

        public NumberInputScene(Game g, int digits, int start, Action<int> done) : base(g)
        {
            _digits = new int[digits];
            start = Math.Max(0, start);
            for (int i = digits - 1; i >= 0; i--) { _digits[i] = start % 10; start /= 10; }
            _done = done;
        }

        protected override void Update()
        {
            if (Input.Repeat(Button.Up)) { _digits[_pos] = (_digits[_pos] + 1) % 10; Cursor(); }
            if (Input.Repeat(Button.Down)) { _digits[_pos] = (_digits[_pos] + 9) % 10; Cursor(); }
            if (Input.Repeat(Button.Right) && _pos < _digits.Length - 1) { _pos++; Cursor(); }
            if (Input.Repeat(Button.Left) && _pos > 0) { _pos--; Cursor(); }
            if (Input.Trigger(Button.Action))
            {
                Decide();
                int n = 0; foreach (var d in _digits) n = n * 10 + d;
                Close();
                if (_done != null) _done(n);
            }
        }

        public override void Draw(Gfx g)
        {
            int cw = 28, w = _digits.Length * cw + 40, h = 60;
            int x = g.Width - w - 4, y = g.Height - 96 - 4 - h - 2;
            MessageWindow.DrawBox(g, x, y, w, h);
            for (int i = 0; i < _digits.Length; i++)
            {
                int dx = x + 20 + i * cw;
                if (i == _pos) g.FillRect(dx - 2, y + 14, cw - 4, 32, new Color(248, 232, 160));
                Ui.Text(g, dx + 4, y + 16, _digits[i].ToString());
            }
        }
    }
}
