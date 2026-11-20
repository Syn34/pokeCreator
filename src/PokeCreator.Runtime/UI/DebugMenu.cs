using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Pkmn;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime.UI
{
    /// <summary>A long list to pick from (maps, species, items, switches): Up/Down, Left/Right = page, Action picks, Cancel backs out.</summary>
    public class PickListScene : Scene
    {
        readonly string _title;
        readonly Func<List<string>> _items;
        readonly Action<int, PickListScene> _onPick;
        readonly Action<int, int, PickListScene> _onAdjust;
        int _index, _scroll;
        const int Rows = 8;
        List<string> _cache;

        /// <param name="onAdjust">Optional: Left/Right change the highlighted entry instead of paging (index, -1/+1).</param>
        public PickListScene(Game g, string title, Func<List<string>> items, Action<int, PickListScene> onPick, Action<int, int, PickListScene> onAdjust = null) : base(g)
        {
            Opaque = true; _title = title; _items = items; _onPick = onPick; _onAdjust = onAdjust; _cache = items();
        }

        public void Refresh() { _cache = _items(); if (_index >= _cache.Count) _index = Math.Max(0, _cache.Count - 1); }

        protected override void Update()
        {
            int n = _cache.Count;
            if (Input.Trigger(Button.Cancel)) { Cancel(); Close(); return; }
            if (n == 0) return;
            int old = _index;
            if (Input.Repeat(Button.Down)) _index = (_index + 1) % n;
            if (Input.Repeat(Button.Up)) _index = (_index + n - 1) % n;
            if (_onAdjust != null)
            {
                if (Input.Repeat(Button.Right)) { _onAdjust(_index, Input.Press(Button.Run) ? 10 : 1, this); Refresh(); }
                if (Input.Repeat(Button.Left)) { _onAdjust(_index, Input.Press(Button.Run) ? -10 : -1, this); Refresh(); }
            }
            else
            {
                if (Input.Repeat(Button.Right)) _index = Math.Min(n - 1, _index + Rows);
                if (Input.Repeat(Button.Left)) _index = Math.Max(0, _index - Rows);
            }
            if (_index != old) Cursor();
            if (_index < _scroll) _scroll = _index;
            if (_index >= _scroll + Rows) _scroll = _index - Rows + 1;
            if (Input.Trigger(Button.Action)) { Decide(); _onPick(_index, this); Refresh(); }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(72, 72, 88), new Color(32, 32, 44));
            Ui.Panel(g, 8, 8, g.Width - 16, 40, new Color(248, 248, 248), new Color(40, 40, 56));
            Ui.Text(g, 20, 12, _title);
            if (_cache.Count > 0) Ui.TextRight(g, g.Width - 20, 12, (_index + 1) + "/" + _cache.Count);
            Ui.Panel(g, 8, 54, g.Width - 16, Rows * 34 + 12, new Color(248, 248, 248), new Color(40, 40, 56));
            for (int r = 0; r < Rows; r++)
            {
                int i = _scroll + r; if (i >= _cache.Count) break;
                int y = 60 + r * 34;
                if (i == _index) g.FillRect(14, y, g.Width - 28, 32, new Color(248, 216, 120));
                Ui.Text(g, 22, y, _cache[i]);
            }
            Ui.LightText(g, 12, g.Height - 30, _onAdjust != null ? "Left/Right change (Shift ×10)  A: choose  B: back" : "Left/Right: page  A: choose  B: back");
        }
    }

    /// <summary>
    /// The debug menu (F9 during a playtest, as Essentials): warp, switches, variables, heal, add Pokémon or items,
    /// money, battles, Pokédex, flags, hatch eggs; hold Ctrl to walk through walls.
    /// </summary>
    public static class DebugMenu
    {
        public static void Open(Game g)
        {
            var cmds = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("Warp to map", () => Warp(g)),
                new KeyValuePair<string, Action>("Switches", () => Switches(g)),
                new KeyValuePair<string, Action>("Variables", () => Variables(g)),
                new KeyValuePair<string, Action>("Heal party", () => { foreach (var p in g.State.Party) p.Heal(g.Project); g.Message.Show("Your party was healed."); }),
                new KeyValuePair<string, Action>("Add Pokémon", () => AddPokemon(g)),
                new KeyValuePair<string, Action>("Add item", () => AddItem(g)),
                new KeyValuePair<string, Action>("Fill Bag (99 of every item)", () => { foreach (var it in g.Project.Items) if (g.State.ItemCount(it.Id) < 99) g.State.AddItem(it.Id, 99 - g.State.ItemCount(it.Id)); g.Message.Show("The Bag was filled."); }),
                new KeyValuePair<string, Action>("Money / Coins / BP", () => Money(g)),
                new KeyValuePair<string, Action>("Wild battle", () => WildBattle(g)),
                new KeyValuePair<string, Action>("Trainer battle", () => TrainerBattle(g)),
                new KeyValuePair<string, Action>("Fill Pokédex", () => { foreach (var s in g.Project.Species.Where(x => x.Form == 0)) { g.State.Seen.Add(s.Id); g.State.Caught.Add(s.Id); } g.State.Flags.Add("has_pokedex"); g.Message.Show("Every species was registered."); }),
                new KeyValuePair<string, Action>("Toggle Pokédex / Pokégear / Running Shoes", () => Toggles(g)),
                new KeyValuePair<string, Action>("Toggle wild encounters", () => { g.Encounters.Enabled = !g.Encounters.Enabled; g.Message.Show("Wild encounters " + (g.Encounters.Enabled ? "on." : "off.")); }),
                new KeyValuePair<string, Action>("Hatch eggs now", () => { int n = 0; foreach (var p in g.State.Party.Where(x => x.IsEgg).ToList()) { DayCare.Hatch(g, p); n++; } if (n == 0) g.Message.Show("There are no Eggs in the party."); }),
                new KeyValuePair<string, Action>("Open PC", () => PcMenu.Open(g, true)),
                new KeyValuePair<string, Action>("Refresh map", () => { g.Map.RefreshEvents(); g.Message.Show("The map's events were refreshed."); }),
            };
            g.PushScene(new PickListScene(g, "Debug", () => cmds.Select(c => c.Key).ToList(), (i, s) => cmds[i].Value()));
        }

        static void Warp(Game g)
        {
            var maps = g.Project.MapInfos.OrderBy(m => m.Id).ToList();
            g.PushScene(new PickListScene(g, "Warp to map", () => maps.Select(m => m.Id.ToString("000") + "  " + m.Name).ToList(), (i, s) =>
            {
                var data = g.Project.LoadMap(maps[i].Id);
                if (data == null) return;
                // Land on the first walkable tile near the middle
                int cx = data.Width / 2, cy = data.Height / 2, bx = cx, by = cy;
                var tmp = new GameMap(g.Project, data, g, false);
                for (int r = 0; r < Math.Max(data.Width, data.Height); r++)
                {
                    bool found = false;
                    for (int y = cy - r; y <= cy + r && !found; y++)
                        for (int x = cx - r; x <= cx + r && !found; x++)
                            if (data.Valid(x, y) && tmp.TilePassable(x, y, 2, null) && tmp.TilePassable(x, y, 8, null)) { bx = x; by = y; found = true; }
                    if (found) break;
                }
                g.CloseAllScenes();
                g.RequestTransfer(maps[i].Id, bx, by, 2, true);
            }));
        }

        static void Switches(Game g)
        {
            int n = Math.Max(100, g.Project.System.Switches.Count);
            Func<int, string> name = i => i < g.Project.System.Switches.Count ? g.Project.System.Switches[i] : "";
            g.PushScene(new PickListScene(g, "Switches", () => Enumerable.Range(1, n).Select(i => i.ToString("0000") + " " + (g.State.GetSwitch(i) ? "[ON] " : "[off] ") + name(i - 1)).ToList(),
                (i, s) => { g.State.SetSwitch(i + 1, !g.State.GetSwitch(i + 1)); g.Map.RefreshEvents(); }));
        }

        static void Variables(Game g)
        {
            int n = Math.Max(100, g.Project.System.Variables.Count);
            Func<int, string> name = i => i < g.Project.System.Variables.Count ? g.Project.System.Variables[i] : "";
            g.PushScene(new PickListScene(g, "Variables", () => Enumerable.Range(1, n).Select(i => i.ToString("0000") + " = " + g.State.VarText(i) + "  " + name(i - 1)).ToList(),
                (i, s) => g.AskNumber("Value of variable " + (i + 1) + "?", g.State.GetVar(i + 1), 8, v => { g.State.SetVar(i + 1, v); g.Map.RefreshEvents(); }),
                (i, d, s) => { g.State.SetVar(i + 1, g.State.GetVar(i + 1) + d); g.Map.RefreshEvents(); }));
        }

        static void AddPokemon(Game g)
        {
            var sp = g.Project.Species.Where(s => s.Form == 0).ToList();
            g.PushScene(new PickListScene(g, "Add Pokémon", () => sp.Select((s, i) => (i + 1).ToString("000") + "  " + (s.Name ?? s.Id)).ToList(), (i, s) =>
                g.AskNumber("Level?", 5, 3, lv =>
                {
                    var pk = Pokemon.Create(g.Project, sp[i].Id, Math.Max(1, Math.Min(GrowthRates.MaxLevel, lv)), g.Rng);
                    pk.OTName = g.State.PlayerName; pk.OTId = g.State.TrainerId; pk.MetMap = g.Map.Id; pk.MetLevel = pk.Level;
                    g.State.Register(pk);
                    if (g.State.Party.Count < GameState.MaxParty) g.State.Party.Add(pk); else g.State.Storage.Add(pk);
                    g.Message.Show(pk.Name(g.Project) + " (Lv. " + pk.Level + ") was added.");
                })));
        }

        static void AddItem(Game g)
        {
            var items = g.Project.Items.OrderBy(x => x.Pocket).ThenBy(x => x.Name).ToList();
            g.PushScene(new PickListScene(g, "Add item", () => items.Select(x => (x.Name ?? x.Id) + "  ×" + g.State.ItemCount(x.Id)).ToList(),
                (i, s) => g.AskNumber("How many?", 1, 3, n => g.State.AddItem(items[i].Id, Math.Max(-g.State.ItemCount(items[i].Id), n))),
                (i, d, s) => g.State.AddItem(items[i].Id, Math.Max(-g.State.ItemCount(items[i].Id), d))));
        }

        static void Money(Game g)
        {
            var labels = new Func<List<string>>(() => new List<string> { "Money: $" + g.State.Money, "Coins: " + g.State.Coins, "Battle Points: " + g.State.BattlePoints });
            g.PushScene(new PickListScene(g, "Money", labels, (i, s) =>
            {
                if (i == 0) g.AskNumber("Money?", g.State.Money, 7, v => g.State.Money = Math.Max(0, v));
                else if (i == 1) g.AskNumber("Coins?", g.State.Coins, 5, v => g.State.Coins = Math.Max(0, v));
                else g.AskNumber("Battle Points?", g.State.BattlePoints, 5, v => g.State.BattlePoints = Math.Max(0, v));
            }, (i, d, s) =>
            {
                if (i == 0) g.State.Money = Math.Max(0, g.State.Money + d * 1000);
                else if (i == 1) g.State.Coins = Math.Max(0, g.State.Coins + d * 10);
                else g.State.BattlePoints = Math.Max(0, g.State.BattlePoints + d);
            }));
        }

        static void WildBattle(Game g)
        {
            var sp = g.Project.Species.Where(s => s.Form == 0).ToList();
            g.PushScene(new PickListScene(g, "Wild battle", () => sp.Select((s, i) => (i + 1).ToString("000") + "  " + (s.Name ?? s.Id)).ToList(), (i, s) =>
                g.AskNumber("Level?", 5, 3, lv => { g.CloseAllScenes(); g.StartWildBattle(sp[i].Id, Math.Max(1, Math.Min(GrowthRates.MaxLevel, lv)), null); })));
        }

        static void TrainerBattle(Game g)
        {
            var trs = g.Project.Trainers.ToList();
            g.PushScene(new PickListScene(g, "Trainer battle", () => trs.Select(t => t.TrainerType + " " + t.Name + (t.Version > 0 ? " (" + t.Version + ")" : "")).ToList(), (i, s) =>
            {
                g.CloseAllScenes();
                g.StartTrainerBattle(trs[i].TrainerType, trs[i].Name, trs[i].Version, true, null);
            }));
        }

        static void Toggles(Game g)
        {
            var flags = new[] { "has_pokedex", "has_pokegear", "has_running_shoes" };
            var names = new[] { "Pokédex", "Pokégear", "Running Shoes" };
            g.PushScene(new PickListScene(g, "Toggles", () => flags.Select((f, i) => (g.State.Flags.Contains(f) ? "[ON]  " : "[off] ") + names[i]).ToList(),
                (i, s) => { if (!g.State.Flags.Remove(flags[i])) g.State.Flags.Add(flags[i]); }));
        }
    }
}
