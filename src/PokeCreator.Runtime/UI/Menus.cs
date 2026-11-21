using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime.UI
{
    // =====================================================================================
    /// <summary>Title screen: title image, "press a button", then Continue / New Game / Quit.</summary>
    public class TitleScene : Scene
    {
        CommandList _menu;
        SaveSummary _save;
        bool _fading; int _fade; int _choice;

        public TitleScene(Game g) : base(g)
        {
            Opaque = true;
            _save = SaveSystem.Peek(g.Project);
            if (!string.IsNullOrEmpty(g.Project.Settings.TitleBGM)) g.Audio.PlayBgm(g.Project.Settings.TitleBGM);
        }

        protected override void Update()
        {
            if (_fading)
            {
                _fade += 16;
                if (_fade >= 255) { Close(); Game.FinishTitle(_choice == 0 && _save != null); }
                return;
            }
            if (_menu == null)
            {
                if (Frame > 10 && (Input.Trigger(Button.Action) || Input.Trigger(Button.Cancel)))
                {
                    Decide();
                    var items = new List<string>();
                    if (_save != null) items.Add("Continue");
                    items.Add("New Game");
                    if (Game.Project.MysteryGifts.Any(m => !string.IsNullOrEmpty(m.Code))) items.Add("Mystery Gift");
                    items.Add("Quit");
                    _menu = new CommandList(items);
                }
                return;
            }
            int r = _menu.Update(this, Cursor);
            if (r == -2) { Cancel(); _menu = null; return; }
            if (r < 0) return;
            string cmd = _menu.Items[r];
            Decide();
            if (cmd == "Quit") { Game.Quit(); return; }
            if (cmd == "Mystery Gift")
            {
                Game.PushScene(new NameEntryScene(Game, "Enter the Mystery Gift code.", "", 16, code =>
                {
                    if (string.IsNullOrEmpty(code)) return;
                    var gift = MysteryGift.Redeem(Game.Project, code);
                    Game.Message.Show(gift == null ? "That code isn't valid." : "You unlocked a Mystery Gift: " + (string.IsNullOrEmpty(gift.Name) ? gift.Id : gift.Name) + "! Pick it up from the delivery person.");
                }));
                return;
            }
            _choice = cmd == "Continue" ? 0 : 1;
            _fading = true;
            Game.Audio.FadeOutBgm(1);
        }

        public override void Draw(Gfx g)
        {
            var title = g.Load(Game.Project.FindAsset("Graphics/Titles", "title"));
            if (title != null) g.Draw(title, 0, 0, title.Width, title.Height, 0, 0, g.Width, g.Height);
            else
            {
                Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(32, 64, 160), new Color(8, 16, 48));
                string t = Game.Project.Settings.Title ?? "PokeCreator";
                g.DrawText(g.Width / 2 - g.TextWidth(t) / 2, g.Height / 3, t, Ui.Light, Ui.LightShadow);
            }
            if (_menu == null)
            {
                if ((Frame / 30) % 2 == 0)
                {
                    var start = g.Load(Game.Project.FindAsset("Graphics/Titles", "start"));
                    if (start != null) g.Draw(start, 0, 0, start.Width, start.Height, g.Width / 2 - start.Width / 2, g.Height - 80);
                    else { string p = "Press Enter"; g.DrawText(g.Width / 2 - g.TextWidth(p) / 2, g.Height - 80, p, Ui.Light, Ui.LightShadow); }
                }
            }
            else
            {
                int w = Math.Max(_menu.Width(g), 200), h = _menu.Height(g);
                _menu.Draw(g, g.Width / 2 - w / 2, g.Height - h - 24);
                if (_save != null && _menu.Items[_menu.Index] == "Continue")
                {
                    // Essentials' load panel: name, badges, Pokédex, time; the party; where the game was saved
                    int bx = 16, by = 8, bw = g.Width - 32, bh = 132;
                    Ui.Box(g, bx, by, bw, bh);
                    Ui.Text(g, bx + 20, by + 12, _save.PlayerName);
                    Ui.TextRight(g, bx + bw - 20, by + 12, Ui.PlayTime(_save.PlayFrames));
                    Ui.Text(g, bx + 20, by + 42, "Badges");
                    Ui.TextRight(g, bx + bw / 2 - 12, by + 42, _save.Badges.ToString());
                    if (_save.HasPokedex)
                    {
                        Ui.Text(g, bx + bw / 2 + 12, by + 42, "Pokédex");
                        Ui.TextRight(g, bx + bw - 20, by + 42, _save.Caught.ToString());
                    }
                    for (int i = 0; i < _save.Party.Count && i < 6; i++)
                        Ui.PokemonIcon(g, Game, _save.Party[i], bx + 16 + i * ((bw - 32) / 6), by + 68, Frame);
                    Ui.Box(g, bx, by + bh + 4, bw, 56);
                    Ui.Text(g, bx + 20, by + bh + 16, _save.Location ?? "");
                }
            }
            if (_fade > 0) g.FillRect(0, 0, g.Width, g.Height, new Color(0, 0, 0, (byte)Math.Min(255, _fade)));
        }
    }

    // =====================================================================================
    /// <summary>Pause menu (Esc / X): Pokémon, Bag, trainer card, Save, Quit.</summary>
    public class PauseMenu : Scene
    {
        CommandList _list;
        readonly List<string> _ids = new List<string>();

        public PauseMenu(Game g) : base(g)
        {
            var labels = new List<string>();
            if (g.State.HasPokedex) { _ids.Add("dex"); labels.Add("Pokédex"); }
            if (g.State.Party.Count > 0) { _ids.Add("party"); labels.Add("Pokémon"); }
            _ids.Add("bag"); labels.Add("Bag");
            if (g.State.Flags.Contains("has_pokegear")) { _ids.Add("pokegear"); labels.Add("Pokégear"); }
            _ids.Add("card"); labels.Add(g.State.PlayerName);
            if (!g.State.Flags.Contains("save_disabled")) { _ids.Add("save"); labels.Add("Save"); }
            _ids.Add("options"); labels.Add("Options");
            _ids.Add("title"); labels.Add("Quit to Title");
            _ids.Add("exit"); labels.Add("Exit");
            _list = new CommandList(labels);
            g.Audio.PlaySe("GUI menu open");
        }

        protected override void Update()
        {
            int r = _list.Update(this, Cursor);
            if (r == -2) { Game.Audio.PlaySe("GUI menu close"); Close(); return; }
            if (r < 0) return;
            Decide();
            switch (_ids[r])
            {
                case "dex": PokedexScene.Open(Game); break;
                case "options": Game.PushScene(new OptionsScene(Game)); break;
                case "party": Game.PushScene(new PartyScene(Game)); break;
                case "bag": Game.PushScene(new BagScene(Game)); break;
                case "pokegear": Game.PushScene(new PokegearScene(Game)); break;
                case "card": Game.PushScene(new TrainerCardScene(Game)); break;
                case "save":
                    Game.Message.Show("Would you like to save the game?", new List<string> { "Yes", "No" }, 1, c =>
                    {
                        if (c != 0) return;
                        try
                        {
                            SaveSystem.Save(Game);
                            Game.Audio.PlayMe("GUI save game");
                            Game.Message.Show("\\PN saved the game.");
                        }
                        catch (Exception ex) { Game.Message.Show("Saving failed: " + ex.Message); }
                    });
                    break;
                case "title":
                    Game.Message.Show("Return to the title screen? Unsaved progress will be lost.", new List<string> { "Yes", "No" }, 1, c =>
                    {
                        if (c == 0) { Close(); Game.ReturnToTitle(); }
                    });
                    break;
                case "exit": Close(); break;
            }
        }

        public override void Draw(Gfx g)
        {
            int w = _list.Width(g) + 20;
            _list.Draw(g, g.Width - w - 6, 6);
            if (Safari.InSafari(Game))
            {
                var s = Safari.State(Game);
                string steps = Game.Project.Settings.SafariSteps > 0 ? "Steps: " + s.Steps + "/" + Game.Project.Settings.SafariSteps : "Steps: -";
                string balls = "Balls: " + s.Balls;
                int bw = Math.Max(g.TextWidth(steps), g.TextWidth(balls)) + 28;
                MessageWindow.DrawBox(g, 6, 6, bw, 76);
                Ui.Text(g, 20, 18, steps);
                Ui.Text(g, 20, 46, balls);
            }
        }
    }

    // =====================================================================================
    /// <summary>Party screen: six panels, Summary / Switch.</summary>
    public class PartyScene : Scene
    {
        int _index, _switchFrom = -1, _moveItemFrom = -1;
        CommandList _popup;
        readonly Dictionary<string, string> _fieldMoves = new Dictionary<string, string>();
        string _help = "Choose a Pokémon.";
        List<Pokemon> Party { get { return Game.State.Party; } }
        // Choose mode (using or giving an item): pick a Pokémon and hand it to the caller
        readonly Action<int, Action> _onChosen;
        readonly Action _onCancel;
        readonly Func<Pokemon, string> _label;

        public PartyScene(Game g) : base(g) { Opaque = true; }

        /// <summary>Choose a Pokémon. onChosen gets its index and an action that closes the party screen.</summary>
        public PartyScene(Game g, string help, Func<Pokemon, string> label, Action<int, Action> onChosen, Action onCancel) : base(g)
        {
            Opaque = true; _help = help; _label = label; _onChosen = onChosen; _onCancel = onCancel;
        }

        protected override void Update()
        {
            if (_popup != null)
            {
                int r = _popup.Update(this, Cursor);
                if (r == -2 || (r >= 0 && _popup.Items[r] == "Cancel")) { Cancel(); _popup = null; return; }
                if (r < 0) return;
                Decide();
                string cmd = _popup.Items[r];
                _popup = null;
                var pk = Party[_index];
                if (cmd == "Summary") Game.PushScene(new SummaryScene(Game, _index));
                else if (_fieldMoves.ContainsKey(cmd)) FieldMoves.UseFromParty(Game, pk, _fieldMoves[cmd]);
                else if (cmd == "Switch") { _switchFrom = _index; _help = "Move to where?"; }
                else if (cmd == "Item") _popup = new CommandList(string.IsNullOrEmpty(pk.Item) ? new[] { "Give", "Cancel" } : new[] { "Give", "Take", "Move", "Cancel" });
                else if (cmd == "Move") { _moveItemFrom = _index; _help = "Give " + ItemName(pk.Item) + " to which Pokémon?"; }
                else if (cmd == "Mail") _popup = new CommandList(new[] { "Read", "Take", "Cancel" });
                else if (cmd == "Read") { if (pk.Mail != null) Game.PushScene(new MailScene(Game, pk.Mail)); }
                else if (cmd == "Give") Game.PushScene(new BagScene(Game, id => ItemFlow.Give(Game, pk, id, null)));
                else if (cmd == "Take") { if (pk.Mail != null) ItemFlow.TakeMail(Game, pk, null); else ItemFlow.Take(Game, pk, null); }
                return;
            }
            int last = Party.Count; // index of the Cancel button
            if (Input.Repeat(Button.Down)) { _index = _index == last ? 0 : (_index + 2 <= last - 1 ? _index + 2 : last); Cursor(); }
            if (Input.Repeat(Button.Up)) { _index = _index == last ? Math.Max(0, last - 1) : (_index - 2 >= 0 ? _index - 2 : last); Cursor(); }
            if (Input.Repeat(Button.Right) && _index < Party.Count - 1 && _index % 2 == 0) { _index++; Cursor(); }
            if (Input.Repeat(Button.Left) && _index < Party.Count && _index % 2 == 1) { _index--; Cursor(); }
            if (Input.Trigger(Button.Cancel))
            {
                Cancel();
                if (_switchFrom >= 0) { _switchFrom = -1; _help = "Choose a Pokémon."; }
                else if (_moveItemFrom >= 0) { _moveItemFrom = -1; _help = "Choose a Pokémon."; }
                else { Close(); if (_onCancel != null) _onCancel(); }
                return;
            }
            if (Input.Trigger(Button.Action))
            {
                if (_index == Party.Count) { Cancel(); Close(); if (_onCancel != null) _onCancel(); return; }
                Decide();
                if (_onChosen != null) { _onChosen(_index, () => Close()); return; }
                if (_switchFrom >= 0)
                {
                    var t = Party[_switchFrom]; Party[_switchFrom] = Party[_index]; Party[_index] = t;
                    _switchFrom = -1; _help = "Choose a Pokémon.";
                    return;
                }
                if (_moveItemFrom >= 0) { MoveItemTo(_index); return; }
                // Summary, the Pokémon's field moves (Cut, Fly, Surf...), Switch, Item
                var cmds = new List<string> { "Summary" };
                _fieldMoves.Clear();
                foreach (var m in FieldMoves.FieldMovesOf(Party[_index]))
                {
                    string label = PokeCreator.Pkmn.ItemEffects.MoveName(Game.Project, m);
                    _fieldMoves[label] = m; cmds.Add(label);
                }
                cmds.Add("Switch");
                if (!Party[_index].IsEgg) cmds.Add(Party[_index].Mail != null ? "Mail" : "Item");
                cmds.Add("Cancel");
                _popup = new CommandList(cmds);
            }
        }

        string ItemName(string id) { var it = Game.Project.Items.FirstOrDefault(i => i.Id == id); return it != null && !string.IsNullOrEmpty(it.Name) ? it.Name : id; }

        /// <summary>Party → Item → Move: hands the held item to another Pokémon (swapping if it holds one).</summary>
        void MoveItemTo(int to)
        {
            var from = Party[_moveItemFrom]; var target = Party[to];
            _moveItemFrom = -1; _help = "Choose a Pokémon.";
            if (target == from) return;
            if (target.IsEgg) { Game.Message.Show("Eggs can't hold items."); return; }
            string item = from.Item, other = target.Item;
            Action swap = () =>
            {
                target.Item = item; from.Item = other;
                PokeCreator.Pkmn.FormRules.UpdateItemForm(Game.Project, target); PokeCreator.Pkmn.FormRules.UpdateItemForm(Game.Project, from);
                Game.Message.Show(string.IsNullOrEmpty(other)
                    ? target.Name(Game.Project) + " was given the " + ItemName(item) + " to hold."
                    : target.Name(Game.Project) + " was given the " + ItemName(item) + " to hold, and " + from.Name(Game.Project) + " took the " + ItemName(other) + ".");
            };
            if (string.IsNullOrEmpty(other)) { swap(); return; }
            Game.Message.Show(target.Name(Game.Project) + " is already holding the " + ItemName(other) + ". Would you like to switch the two items?", new List<string> { "Yes", "No" }, 1, c => { if (c == 0) swap(); });
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(56, 120, 104), new Color(24, 72, 64));
            for (int i = 0; i < 6; i++)
            {
                int x = i % 2 == 0 ? 6 : g.Width / 2 + 2, y = 14 + (i / 2) * 96 + (i % 2) * 16;
                int w = g.Width / 2 - 8, h = 84;
                if (i >= Party.Count) { Ui.Panel(g, x, y, w, h, new Color(40, 88, 80), new Color(32, 64, 56)); continue; }
                var pk = Party[i];
                bool sel = i == _index, swap = i == _switchFrom || i == _moveItemFrom;
                var fill = pk.Fainted ? new Color(200, 120, 112) : sel ? new Color(248, 224, 120) : new Color(120, 200, 248);
                if (swap) fill = new Color(248, 160, 200);
                Ui.Panel(g, x, y, w, h, fill, sel ? new Color(248, 64, 48) : new Color(40, 56, 80));
                Ui.PokemonIcon(g, Game, pk, x - 4, y - 8, sel ? Frame * 2 : Frame);
                Ui.Text(g, x + 64, y + 6, pk.Name(Game.Project));
                Ui.GenderSymbol(g, x + w - 30, y + 6, pk.Gender);
                if (!string.IsNullOrEmpty(pk.Item)) Ui.ItemIcon(g, Game, pk.Item, x + 40, y + 40);
                if (pk.IsEgg) continue;
                int max = pk.TotalHP(Game.Project);
                Ui.Text(g, x + 20, y + 46, "Lv." + pk.Level);
                string lbl = _label != null ? _label(pk) : null;
                if (lbl != null) { Ui.TextRight(g, x + w - 16, y + 46, lbl); continue; }
                string st = Ui.StatusAbbrev(pk.Status);
                if (pk.Fainted) st = "FNT";
                if (st != "") g.DrawText(x + 20, y + 46, st, new Color(248, 64, 48), Ui.DarkShadow);
                Ui.HpBar(g, x + 104, y + 44, w - 124, pk.HP, max);
                Ui.TextRight(g, x + w - 16, y + 52, pk.HP + "/" + max);
            }
            // Cancel button and help box
            int cw = 120, cx = g.Width - cw - 8, cy = g.Height - 52;
            Ui.Panel(g, cx, cy, cw, 44, _index == Party.Count ? new Color(248, 224, 120) : new Color(220, 220, 228), new Color(40, 56, 80));
            Ui.TextCenter(g, cx + cw / 2, cy + 6, "Cancel");
            Ui.Box(g, 6, g.Height - 56, g.Width - cw - 24, 52);
            Ui.Text(g, 24, g.Height - 46, _help);
            if (_popup != null) { int w = _popup.Width(g); _popup.Draw(g, g.Width - w - 8, g.Height - _popup.Height(g) - 60); }
        }
    }

    // =====================================================================================
    /// <summary>Summary: sprite + Info / Stats / Moves pages. Left/Right change page, Up/Down change Pokémon.</summary>
    public class SummaryScene : Scene
    {
        int _index, _page;
        static readonly string[] Pages = { "INFO", "MEMO", "STATS", "MOVES", "RIBBONS" };
        readonly List<Pokemon> _list;
        public SummaryScene(Game g, int index) : base(g) { Opaque = true; _index = index; }
        /// <summary>Summary of Pokémon from another list (e.g. a PC box).</summary>
        public SummaryScene(Game g, int index, List<Pokemon> list) : base(g) { Opaque = true; _index = index; _list = list; }
        List<Pokemon> Party { get { return _list ?? Game.State.Party; } }

        protected override void Update()
        {
            var party = Party;
            if (_moveSel >= 0) { MoveSelect(); return; }
            if (_page == 3 && Input.Trigger(Button.Action) && party.Count > 0 && !party[Math.Min(_index, party.Count - 1)].IsEgg) { Decide(); _moveSel = 0; _swapFrom = -1; return; }
            if (Input.Trigger(Button.Cancel)) { Cancel(); Close(); return; }
            if (Input.Repeat(Button.Right)) { _page = (_page + 1) % Pages.Length; Game.Audio.PlaySe("GUI summary change page"); }
            if (Input.Repeat(Button.Left)) { _page = (_page + Pages.Length - 1) % Pages.Length; Game.Audio.PlaySe("GUI summary change page"); }
            if (Input.Repeat(Button.Down) && party.Count > 1) { _index = (_index + 1) % party.Count; Cursor(); }
            if (Input.Repeat(Button.Up) && party.Count > 1) { _index = (_index + party.Count - 1) % party.Count; Cursor(); }
        }

        public override void Draw(Gfx g)
        {
            var p = Game.Project;
            var pk = Party[Math.Min(_index, Party.Count - 1)];
            var d = pk.Data(p);
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(248, 248, 240), new Color(216, 224, 232));
            // Header with page tabs
            g.FillRect(0, 0, g.Width, 40, new Color(232, 88, 64));
            Ui.Panel(g, 236, 6, 160, 30, new Color(248, 248, 248), new Color(160, 48, 32));
            g.DrawText(316 - g.TextWidth(Pages[_page]) / 2, 5, Pages[_page], Ui.Dark, Ui.DarkShadow);
            g.DrawText(212, 5, "◀", Ui.Light, new Color(160, 48, 32));
            g.DrawText(404, 5, "▶", Ui.Light, new Color(160, 48, 32));
            for (int i = 0; i < Pages.Length; i++) g.FillRect(436 + i * 14, 16, 10, 10, i == _page ? new Color(248, 248, 248) : new Color(176, 64, 48));
            Ui.LightText(g, 12, 5, "SUMMARY");

            // Left column: sprite and name
            Ui.Panel(g, 8, 48, 184, 328, new Color(248, 248, 248), new Color(152, 160, 176));
            Ui.Text(g, 20, 54, pk.Name(p));
            Ui.GenderSymbol(g, 160, 54, pk.Gender);
            Ui.Text(g, 20, 86, "Lv. " + pk.Level);
            if (pk.Shiny) g.DrawText(120, 86, "Shiny", new Color(248, 64, 48), Ui.DarkShadow);
            if (pk.ShadowPokemon)
            {
                g.DrawText(20, 146, "Shadow", new Color(136, 64, 200), Ui.DarkShadow);
                int stage = PokeCreator.Pkmn.Shadow.Stage(pk);
                for (int s = 0; s < 5; s++) g.FillRect(110 + s * 14, 152, 12, 14, s < stage ? new Color(136, 64, 200) : new Color(208, 208, 216));
            }
            if (pk.PokerusStage == 1) { g.FillRect(18, 116, 66, 28, new Color(168, 64, 200)); g.DrawText(24, 116, "PKRS", new Color(248, 248, 248), new Color(96, 32, 120)); }
            else if (pk.PokerusStage == 2) g.DrawText(20, 115, "PKRS (cured)", new Color(168, 64, 200), Ui.DarkShadow);
            Ui.PokemonFront(g, Game, pk, 100, 290, 168);
            int ty = 300;
            for (int i = 0; i < pk.Types(p).Count; i++) Ui.TypeBadge(g, Game, 14 + i * (Ui.BadgeWidth + 4), ty, pk.Types(p)[i]);
            if (!string.IsNullOrEmpty(pk.Item))
            {
                var it = p.Items.FirstOrDefault(x => x.Id == pk.Item);
                Ui.Text(g, 20, 336, it != null ? it.Name : pk.Item);
            }
            else Ui.Text(g, 20, 336, "No item");

            int rx = 204, rw = g.Width - rx - 8;
            Ui.Panel(g, rx, 48, rw, 328, new Color(248, 248, 248), new Color(152, 160, 176));
            switch (_page)
            {
                case 0: DrawInfo(g, pk, d, rx, rw); break;
                case 1: DrawMemo(g, pk, rx, rw); break;
                case 2: DrawStats(g, pk, rx, rw); break;
                case 3: DrawMoves(g, pk, rx, rw); break;
                case 4: DrawRibbons(g, pk, rx, rw); break;
            }
        }

        int _moveSel = -1, _swapFrom = -1;

        /// <summary>Moves page: look at a move's details; choose two to swap their order.</summary>
        void MoveSelect()
        {
            var pk = Party[Math.Min(_index, Party.Count - 1)];
            int n = pk.Moves.Count;
            if (Input.Repeat(Button.Down) && n > 0) { _moveSel = (_moveSel + 1) % n; Cursor(); }
            if (Input.Repeat(Button.Up) && n > 0) { _moveSel = (_moveSel + n - 1) % n; Cursor(); }
            if (Input.Trigger(Button.Cancel)) { Cancel(); if (_swapFrom >= 0) _swapFrom = -1; else _moveSel = -1; return; }
            if (Input.Trigger(Button.Action) && n > 1)
            {
                Decide();
                if (_swapFrom < 0) { _swapFrom = _moveSel; return; }
                if (_swapFrom != _moveSel) { var t = pk.Moves[_swapFrom]; pk.Moves[_swapFrom] = pk.Moves[_moveSel]; pk.Moves[_moveSel] = t; }
                _swapFrom = -1;
            }
        }

        void DrawMoveDetails(Gfx g, PokeCreator.Data.MoveData md)
        {
            Ui.Panel(g, 8, 48, 184, 328, new Color(248, 248, 248), new Color(152, 160, 176));
            if (md == null) return;
            Ui.TypeBadge(g, Game, 18, 56, md.Type);
            Ui.Text(g, 18, 92, md.Category);
            g.DrawText(18, 126, "Power", new Color(232, 88, 64), new Color(248, 192, 176)); Ui.TextRight(g, 180, 126, md.Power > 1 ? md.Power.ToString() : "---");
            g.DrawText(18, 158, "Accuracy", new Color(232, 88, 64), new Color(248, 192, 176)); Ui.TextRight(g, 180, 158, md.Accuracy > 0 ? md.Accuracy + "%" : "---");
            int y = 196;
            foreach (var l in Wrap(g, md.Description ?? "", 164)) { if (y > 344) break; Ui.Text(g, 18, y, l); y += 28; }
        }

        void Row(Gfx g, int rx, int rw, int y, string label, string value)
        {
            g.DrawText(rx + 14, y, label, new Color(232, 88, 64), new Color(248, 192, 176));
            Ui.Text(g, rx + 130, y, value);
        }

        void DrawInfo(Gfx g, Pokemon pk, SpeciesData d, int rx, int rw)
        {
            var p = Game.Project;
            int dex = p.Species.Where(s => s.Form == 0).ToList().FindIndex(s => s.Species == pk.Species) + 1;
            Row(g, rx, rw, 58, "Dex No.", dex > 0 ? dex.ToString("000") : "???");
            Row(g, rx, rw, 92, "Species", d != null ? d.Name : pk.Species);
            Row(g, rx, rw, 126, "OT", string.IsNullOrEmpty(pk.OTName) ? "-" : pk.OTName);
            Row(g, rx, rw, 160, "ID No.", pk.OTId.ToString("00000"));
            Row(g, rx, rw, 194, "Nature", Natures.Display(pk.Nature));
            var ab = p.Abilities.FirstOrDefault(a => a.Id == pk.AbilityId(p));
            Row(g, rx, rw, 228, "Ability", ab != null ? ab.Name : (pk.AbilityId(p) ?? "-"));
            if (ab != null && !string.IsNullOrEmpty(ab.Description))
            {
                var lines = Wrap(g, ab.Description, rw - 28);
                if (lines.Count > 2)
                {
                    // Too long for two lines: end the second one with "…"
                    string l2 = lines[1];
                    while (l2.Length > 0 && g.TextWidth(l2 + "…") > rw - 28) l2 = l2.Substring(0, l2.Length - 1);
                    lines[1] = l2.TrimEnd() + "…";
                }
                for (int i = 0; i < Math.Min(2, lines.Count); i++) Ui.Text(g, rx + 14, 260 + i * 29, lines[i]);
            }
            int need = pk.ExpForNextLevel(p) - pk.Exp, from = pk.ExpForThisLevel(p), to = pk.ExpForNextLevel(p);
            Ui.Text(g, rx + 14, 326, "Exp. to next Lv.");
            Ui.TextRight(g, rx + rw - 14, 326, Math.Max(0, need).ToString());
            Ui.ExpBar(g, rx + 14, 360, rw - 28, to > from ? (double)(pk.Exp - from) / (to - from) : 1);
        }

        static readonly string[][] Characteristics =
        {
            new[] { "Loves to eat.", "Takes plenty of siestas.", "Nods off a lot.", "Scatters things often.", "Likes to relax." },
            new[] { "Proud of its power.", "Likes to thrash about.", "A little quick tempered.", "Likes to fight.", "Quick tempered." },
            new[] { "Sturdy body.", "Capable of taking hits.", "Highly persistent.", "Good endurance.", "Good perseverance." },
            new[] { "Highly curious.", "Mischievous.", "Thoroughly cunning.", "Often lost in thought.", "Very finicky." },
            new[] { "Strong willed.", "Somewhat vain.", "Strongly defiant.", "Hates to lose.", "Somewhat stubborn." },
            new[] { "Likes to run.", "Alert to sounds.", "Impetuous and silly.", "Somewhat of a clown.", "Quick to flee." },
        };

        /// <summary>The characteristic from the highest IV (as the games).</summary>
        public static string Characteristic(Pokemon pk)
        {
            if (pk.IV == null || pk.IV.Count < 6) return "";
            int best = 0;
            int[] order = { Stat.HP, Stat.Attack, Stat.Defense, Stat.Speed, Stat.SpAtk, Stat.SpDef };
            foreach (int s in order) if (pk.IV[s] > pk.IV[best]) best = s;
            int row = best == Stat.HP ? 0 : best == Stat.Attack ? 1 : best == Stat.Defense ? 2 : best == Stat.SpAtk ? 3 : best == Stat.SpDef ? 4 : 5;
            return Characteristics[row][pk.IV[best] % 5];
        }

        void DrawMemo(Gfx g, Pokemon pk, int rx, int rw)
        {
            var p = Game.Project;
            if (pk.IsEgg) { Ui.Text(g, rx + 14, 60, "An odd Pokémon Egg."); return; }
            var lines = new List<string>();
            lines.Add(Natures.Display(pk.Nature) + " nature.");
            var info = pk.MetMap > 0 ? p.GetMapInfo(pk.MetMap) : null;
            string where = info != null ? Game.Message.Expand(info.Name) : null;
            bool foreign = !string.IsNullOrEmpty(pk.OTName) && (pk.OTName != Game.State.PlayerName || pk.OTId != Game.State.TrainerId);
            if (foreign) lines.Add("Obtained in a trade" + (where != null ? " (met at " + where + ")." : "."));
            else if (pk.MetLevel <= 1 && where != null) lines.Add("Hatched at " + where + ".");
            else lines.Add("Met at Lv. " + Math.Max(1, pk.MetLevel) + (where != null ? " at " + where + "." : "."));
            lines.Add(Characteristic(pk));
            if (pk.Pokerus > 0) lines.Add(pk.PokerusStage == 1 ? "Infected with Pokérus." : "Cured of Pokérus.");
            int y = 60;
            foreach (var l in lines) foreach (var w in Wrap(g, l, rw - 28)) { Ui.Text(g, rx + 14, y, w); y += 30; }
            y += 10;
            g.DrawText(rx + 14, y, "Happiness", new Color(232, 88, 64), new Color(248, 192, 176));
            for (int i = 0; i < 5; i++) g.FillRect(rx + 150 + i * 22, y + 8, 18, 14, pk.Happiness >= (i + 1) * 51 ? new Color(232, 88, 120) : new Color(216, 216, 224));
        }

        void DrawRibbons(Gfx g, Pokemon pk, int rx, int rw)
        {
            if (pk.Ribbons == null || pk.Ribbons.Count == 0) { Ui.Text(g, rx + 14, 60, "No ribbons."); return; }
            int y = 60;
            foreach (var r in pk.Ribbons.Take(10))
            {
                var icon = g.Load(Game.Project.FindAsset("Graphics/UI/Summary", "ribbon_" + r.ToLowerInvariant()));
                if (icon != null) g.Draw(icon, 0, 0, Math.Min(28, icon.Width), Math.Min(28, icon.Height), rx + 12, y);
                else g.FillRect(rx + 16, y + 6, 18, 18, new Color(232, 184, 40));
                Ui.Text(g, rx + 46, y, new Script.RibbonInfo(r).Name);
                y += 30;
            }
            if (pk.Ribbons.Count > 10) Ui.Text(g, rx + 14, y, "+" + (pk.Ribbons.Count - 10) + " more");
        }

        void DrawStats(Gfx g, Pokemon pk, int rx, int rw)
        {
            var stats = pk.Stats(Game.Project);
            int max = stats[Stat.HP];
            g.DrawText(rx + 14, 58, "HP", new Color(232, 88, 64), new Color(248, 192, 176));
            Ui.TextRight(g, rx + rw - 14, 58, pk.HP + "/" + max);
            Ui.HpBar(g, rx + 130, 98, rw - 150, pk.HP, max);
            int[] order = { Stat.Attack, Stat.Defense, Stat.SpAtk, Stat.SpDef, Stat.Speed };
            for (int i = 0; i < order.Length; i++)
            {
                int s = order[i], y = 120 + i * 36;
                var labelColor = Natures.Raised(pk.Nature) == s ? new Color(232, 40, 40) : Natures.Lowered(pk.Nature) == s ? new Color(40, 96, 232) : new Color(120, 120, 128);
                g.DrawText(rx + 14, y, Stat.Names[s] + (Natures.Raised(pk.Nature) == s ? " +" : Natures.Lowered(pk.Nature) == s ? " -" : ""), labelColor, Ui.DarkShadow);
                Ui.TextRight(g, rx + rw - 14, y, stats[s].ToString());
            }
            Ui.Text(g, rx + 14, 310, "Nature: " + Natures.Display(pk.Nature));
            Ui.Text(g, rx + 14, 340, "Happiness: " + pk.Happiness);
        }

        void DrawMoves(Gfx g, Pokemon pk, int rx, int rw)
        {
            var p = Game.Project;
            for (int i = 0; i < 4; i++)
            {
                int y = 60 + i * 78;
                Color fill = i == _swapFrom ? new Color(200, 232, 248) : i == _moveSel ? new Color(248, 232, 168) : new Color(240, 240, 248);
                Ui.Panel(g, rx + 8, y, rw - 16, 70, fill, new Color(184, 192, 208));
                if (i >= pk.Moves.Count) { Ui.Text(g, rx + 24, y + 18, "-"); continue; }
                var m = pk.Moves[i];
                var md = p.Moves.FirstOrDefault(x => x.Id == m.Id);
                if (md != null) Ui.TypeBadge(g, Game, rx + 16, y + 6, md.Type);
                Ui.Text(g, rx + 110, y + 4, md != null ? md.Name : m.Id);
                int total = md != null ? Pokemon.TotalPP(md, m.PPUps) : m.PP;
                Ui.TextRight(g, rx + rw - 22, y + 36, "PP " + m.PP + "/" + total);
                if (md != null) Ui.Text(g, rx + 22, y + 36, md.Category);
                if (md != null && md.Power > 1) Ui.Text(g, rx + 126, y + 36, md.Power.ToString());
            }
            if (_moveSel >= 0 && _moveSel < pk.Moves.Count) DrawMoveDetails(g, p.Moves.FirstOrDefault(x => x.Id == pk.Moves[_moveSel].Id));
        }

        static List<string> Wrap(Gfx g, string text, int width)
        {
            var lines = new List<string>(); string line = "";
            foreach (var w in text.Split(' '))
            {
                string t = line.Length == 0 ? w : line + " " + w;
                if (line.Length > 0 && g.TextWidth(t) > width) { lines.Add(line); line = w; } else line = t;
            }
            if (line.Length > 0) lines.Add(line);
            return lines;
        }
    }

    // =====================================================================================
    /// <summary>Bag: pockets (Left/Right), item list, icon and description.</summary>
    public class BagScene : Scene
    {
        public static readonly string[] PocketNames = { "Items", "Medicine", "Poké Balls", "TMs & HMs", "Berries", "Mail", "Battle Items", "Key Items" };
        int _pocket, _index, _scroll;
        CommandList _popup;
        readonly Action<string> _choose; // choose mode: picking an item to give or use in battle
        readonly Func<ItemData, bool> _filter;
        readonly Action _onCancel;

        public BagScene(Game g) : base(g) { Opaque = true; }

        /// <summary>Pick an item (e.g. to give to a Pokémon); the bag closes and onChosen gets the item ID.</summary>
        public BagScene(Game g, Action<string> onChosen) : base(g) { Opaque = true; _choose = onChosen; }

        /// <summary>Choose mode showing only some items (e.g. those usable in battle).</summary>
        public BagScene(Game g, Action<string> onChosen, Func<ItemData, bool> filter, Action onCancel) : base(g)
        {
            Opaque = true; _choose = onChosen; _filter = filter; _onCancel = onCancel;
            // Open on the first pocket that has something to pick
            for (int pk = 0; pk < 8; pk++) { _pocket = pk; if (Items().Count > 0) return; }
            _pocket = 0;
        }

        List<KeyValuePair<ItemData, int>> Items()
        {
            var list = new List<KeyValuePair<ItemData, int>>();
            foreach (var kv in Game.State.Bag)
            {
                var it = Game.Project.Items.FirstOrDefault(i => i.Id == kv.Key) ?? new ItemData { Id = kv.Key, Name = kv.Key, Pocket = 1 };
                if (Math.Max(1, Math.Min(8, it.Pocket)) == _pocket + 1 && (_filter == null || _filter(it))) list.Add(new KeyValuePair<ItemData, int>(it, kv.Value));
            }
            return list;
        }

        protected override void Update()
        {
            var items = Items();
            if (_popup != null)
            {
                int r = _popup.Update(this, Cursor);
                if (r == -2 || (r >= 0 && _popup.Items[r] == "Cancel")) { Cancel(); _popup = null; return; }
                if (r < 0) return;
                string cmd = _popup.Items[r]; _popup = null;
                var it = items[_index].Key;
                if (cmd == "Toss")
                {
                    int have = Game.State.ItemCount(it.Id);
                    Action<int> toss = qty =>
                    {
                        qty = Math.Max(0, Math.Min(have, qty)); if (qty == 0) return;
                        Game.Message.Show("Throw away " + qty + " " + (qty > 1 ? (it.NamePlural ?? it.Name) : it.Name) + "?", new List<string> { "Yes", "No" }, 1, c =>
                        {
                            if (c == 0) { Game.State.AddItem(it.Id, -qty); ClampIndex(); Game.Message.Show("Threw away " + qty + " " + (qty > 1 ? (it.NamePlural ?? it.Name) : it.Name) + "."); }
                        });
                    };
                    if (have <= 1) toss(1); else Game.AskNumber("Toss how many?", 1, have >= 100 ? 3 : 2, toss);
                }
                else if (cmd == "Register" || cmd == "Deregister")
                {
                    var reg = Game.State.Extras.Registered ?? (Game.State.Extras.Registered = new List<string>());
                    if (cmd == "Register") { if (!reg.Contains(it.Id)) reg.Add(it.Id); Game.Message.Show(it.Name + " was registered. Press F5 to use it."); }
                    else reg.Remove(it.Id);
                }
                else if (cmd == "Sort")
                {
                    // Sort this pocket by name (the Bag keeps its order)
                    var mine = Items().Select(kv => kv.Key.Id).ToList();
                    var others = Game.State.Bag.Where(kv => !mine.Contains(kv.Key)).ToList();
                    var sorted = Items().OrderBy(kv => kv.Key.Name ?? kv.Key.Id).Select(kv => new KeyValuePair<string, int>(kv.Key.Id, kv.Value)).ToList();
                    Game.State.Bag.Clear();
                    foreach (var kv in others.Concat(sorted)) Game.State.Bag[kv.Key] = kv.Value;
                }
                else if (cmd == "Use") ItemFlow.UseFromBag(Game, it.Id, ClampIndex);
                else if (cmd == "Give")
                    Game.PushScene(new PartyScene(Game, "Give to which Pokémon?", null, (i, close) => { close(); ItemFlow.Give(Game, Game.State.Party[i], it.Id, ClampIndex); }, null));
                return;
            }
            if (Input.Repeat(Button.Right)) { _pocket = (_pocket + 1) % 8; _index = 0; _scroll = 0; Cursor(); }
            if (Input.Repeat(Button.Left)) { _pocket = (_pocket + 7) % 8; _index = 0; _scroll = 0; Cursor(); }
            int n = items.Count + 1; // + Close bag
            if (Input.Repeat(Button.Down)) { _index = (_index + 1) % n; Cursor(); }
            if (Input.Repeat(Button.Up)) { _index = (_index + n - 1) % n; Cursor(); }
            if (_index < _scroll) _scroll = _index;
            if (_index >= _scroll + 7) _scroll = _index - 6;
            if (Input.Trigger(Button.Cancel)) { Cancel(); Close(); if (_onCancel != null) _onCancel(); return; }
            if (Input.Trigger(Button.Action))
            {
                if (_index >= items.Count) { Cancel(); Close(); if (_onCancel != null) _onCancel(); return; }
                Decide();
                var it = items[_index].Key;
                if (_choose != null)
                {
                    if (it.Pocket == 8 && _filter == null) { Game.Message.Show("The " + (it.Name ?? it.Id) + " can't be held."); return; }
                    Close(); _choose(it.Id); return;
                }
                var cmds = new List<string>();
                if (ItemEffects.Info(Game.Project, it).Kind != ItemKind.None || Array.IndexOf(FieldMoves.Rods, it.Id) >= 0 || Array.IndexOf(FieldMoves.DirectKeyItems, it.Id) >= 0) cmds.Add("Use");
                if (it.Pocket != 8 && Game.State.Party.Any(pk => !pk.IsEgg)) cmds.Add("Give");
                if (it.Pocket != 8) cmds.Add("Toss");
                if (it.Pocket == 8 && (Array.IndexOf(FieldMoves.Rods, it.Id) >= 0 || Array.IndexOf(FieldMoves.DirectKeyItems, it.Id) >= 0 || ItemEffects.Info(Game.Project, it).Kind != ItemKind.None))
                    cmds.Add(Game.State.Extras.Registered != null && Game.State.Extras.Registered.Contains(it.Id) ? "Deregister" : "Register");
                if (items.Count > 1) cmds.Add("Sort");
                cmds.Add("Cancel");
                _popup = new CommandList(cmds);
            }
        }

        /// <summary>Keeps the cursor on the list after an item runs out.</summary>
        void ClampIndex() { int n = Items().Count; if (_index > n) _index = n; if (_scroll > _index) _scroll = _index; }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(248, 200, 120), new Color(216, 136, 64));
            // Bag panel on the left
            Ui.Panel(g, 8, 8, 176, 268, new Color(248, 232, 200), new Color(160, 96, 48));
            string pname = PocketNames[_pocket];
            Ui.TextCenter(g, 96, 20, pname);
            for (int i = 0; i < 8; i++) g.FillRect(24 + i * 18, 60, 12, 12, i == _pocket ? new Color(232, 64, 48) : new Color(200, 168, 128));
            Ui.Text(g, 22, 84, "◀ ▶ to switch");
            var items = Items();
            if (_index < items.Count) Ui.ItemIcon(g, Game, items[_index].Key.Id, 72, 140);

            // Item list
            Ui.Panel(g, 192, 8, g.Width - 200, 268, new Color(248, 248, 248), new Color(160, 96, 48));
            int lh = 36;
            for (int row = 0; row < 7; row++)
            {
                int i = _scroll + row;
                if (i > items.Count) break;
                int y = 18 + row * lh;
                if (i == _index) Ui.Arrow(g, 202, y + 8);
                if (i == items.Count) { Ui.Text(g, 222, y, "Close bag"); continue; }
                var it = items[i];
                Ui.Text(g, 222, y, it.Key.Name ?? it.Key.Id);
                if (it.Key.Pocket != 8) Ui.TextRight(g, g.Width - 24, y, "x" + it.Value);
            }
            if (items.Count == 0) Ui.Text(g, 222, 18 + lh, "(empty)");

            // Description
            Ui.Box(g, 4, 284, g.Width - 8, 96);
            string desc = _index < items.Count ? items[_index].Key.Description ?? "" : "Close the bag.";
            var words = desc.Split(' '); string line = ""; int ly = 298;
            foreach (var w in words)
            {
                string t = line.Length == 0 ? w : line + " " + w;
                if (line.Length > 0 && g.TextWidth(t) > g.Width - 48) { Ui.Text(g, 22, ly, line); ly += 32; line = w; if (ly > 340) break; }
                else line = t;
            }
            if (ly <= 340) Ui.Text(g, 22, ly, line);
            if (_popup != null) { int w = _popup.Width(g); _popup.Draw(g, g.Width - w - 8, 284 - _popup.Height(g) - 4); }
        }
    }

    // =====================================================================================
    public class TrainerCardScene : Scene
    {
        public TrainerCardScene(Game g) : base(g) { Opaque = true; g.Audio.PlaySe("GUI trainer card open"); }

        protected override void Update()
        {
            if (Input.Trigger(Button.Action)) { _back = !_back; Game.Audio.PlaySe("GUI trainer card flip"); return; }
            if (Input.Trigger(Button.Cancel)) { Cancel(); Close(); }
        }
        bool _back;

        void DrawBack(Gfx g)
        {
            Ui.Text(g, 56, 44, "PLAY RECORD");
            int y = 92;
            foreach (var line in World.GameStats.CardLines)
            {
                Ui.Text(g, 56, y, line[0]);
                object v = World.GameStats.Get(Game, line[1]);
                Ui.TextRight(g, g.Width - 56, y, line[1] == "money_spent_at_marts" ? "$" + Convert.ToInt32(v).ToString("N0") : Convert.ToInt32(v).ToString("N0"));
                y += 32;
            }
            int hof = World.GameStats.Int(Game, "time_to_hall_of_fame");
            if (hof > 0) { Ui.Text(g, 56, y, "Hall of Fame"); Ui.TextRight(g, g.Width - 56, y, Ui.PlayTime(hof * 60L)); }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(96, 160, 232), new Color(40, 88, 176));
            Ui.Panel(g, 24, 24, g.Width - 48, g.Height - 48, new Color(248, 240, 216), new Color(176, 136, 72));
            var s = Game.State;
            if (_back) { DrawBack(g); return; }
            Ui.Text(g, 56, 44, "TRAINER CARD");
            Ui.TextRight(g, g.Width - 56, 44, "IDNo. " + s.TrainerId.ToString("00000"));
            Ui.Text(g, 56, 104, "Name");    Ui.TextRight(g, 300, 104, s.PlayerName);
            Ui.Text(g, 56, 144, "Money");   Ui.TextRight(g, 300, 144, "$" + s.Money.ToString("N0"));
            if (s.HasPokedex) { Ui.Text(g, 56, 184, "Pokédex"); Ui.TextRight(g, 300, 184, s.Caught.Count + " / " + s.Seen.Count); }
            else { Ui.Text(g, 56, 184, "Pokémon"); Ui.TextRight(g, 300, 184, (s.Party.Count + s.Storage.Count).ToString()); }
            Ui.Text(g, 56, 224, "Time");    Ui.TextRight(g, 300, 224, Ui.PlayTime(s.PlayFrames));
            var look = Game.Player.Look;
            var tex = look != null ? g.Load(Game.Project.FindAsset("Graphics/Trainers", look.TrainerType)) : null;
            if (tex != null) g.Draw(tex, 0, 0, tex.Width, tex.Height, g.Width - 48 - tex.Width - 16, 90);
            // Badges: Graphics/UI/Trainer Card/icon_badges (32×32 each, one row per region), or drawn ones
            var badges = g.Load(Game.Project.FindAsset("Graphics/UI/Trainer Card", "icon_badges"));
            int bx = (g.Width - 8 * 48) / 2 + 8, by = 280;
            for (int i = 0; i < 8; i++)
            {
                int x = bx + i * 48;
                if (!s.Badges.Contains(i)) { g.FillRect(x + 6, by + 6, 20, 20, new Color(208, 200, 176)); continue; }
                if (badges != null && badges.Width >= (i + 1) * 32) g.Draw(badges, i * 32, 0, 32, 32, x, by);
                else
                {
                    var c = new[] { new Color(160, 160, 168), new Color(72, 136, 232), new Color(248, 200, 48), new Color(96, 192, 96), new Color(232, 96, 168), new Color(216, 168, 72), new Color(232, 72, 48), new Color(120, 96, 200) }[i];
                    g.FillRect(x + 4, by + 4, 24, 24, new Color(64, 56, 48)); g.FillRect(x + 6, by + 6, 20, 20, c);
                    g.FillRect(x + 9, by + 9, 6, 6, new Color(255, 255, 255, 160));
                }
            }
        }
    }
}
