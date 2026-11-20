using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Battles;
using PokeCreator.Pkmn;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime.UI
{
    /// <summary>
    /// The Pokémon Storage System (as Essentials): boxes of 30 with names and wallpapers, the party beside them,
    /// picking Pokémon up and putting them down anywhere (swapping), withdraw / store, summary, held items, marks and release.
    /// Organize mode does everything; Withdraw starts on the boxes, Deposit starts on the party.
    /// </summary>
    public class StorageScene : Scene
    {
        public enum Mode { Organize, Withdraw, Deposit }
        readonly Mode _mode;
        // Cursor: -1 = box name, 0..29 = slots, 30 = "Party Pokémon", 31 = "Close Box"; in the party panel 0..5, 6 = back
        int _cursor, _party = -1;
        bool PartyOpen { get { return _party >= 0; } }
        Pokemon _held; int _heldFromBox = -1, _heldFromSlot = -1; bool _heldFromParty;
        CommandList _popup; Action<string> _popupDone;
        const int Cell = 48, GridX = 208, GridY = 64;
        static readonly Color[] Papers =
        {
            new Color(120, 184, 232), new Color(120, 200, 120), new Color(232, 168, 96), new Color(200, 136, 216),
            new Color(232, 120, 120), new Color(232, 216, 120), new Color(144, 144, 160), new Color(96, 200, 192),
            new Color(168, 120, 88), new Color(248, 168, 200), new Color(104, 120, 208), new Color(176, 216, 88),
            new Color(208, 208, 216), new Color(80, 80, 96), new Color(232, 136, 64), new Color(152, 104, 200),
        };

        PokemonStorage S { get { return Game.State.Storage; } }
        StorageBox Box { get { return S.Boxes[S.Current]; } }
        List<Pokemon> Party { get { return Game.State.Party; } }

        public StorageScene(Game g, Mode mode) : base(g)
        {
            Opaque = true; _mode = mode;
            S.Ensure(PokemonStorage.DefaultBoxes);
            if (mode == Mode.Deposit) _party = 0;
        }

        Pokemon Selected
        {
            get
            {
                if (PartyOpen) return _party < Party.Count ? Party[_party] : null;
                return _cursor >= 0 && _cursor < PokemonStorage.BoxSize ? Box.Slots[_cursor] : null;
            }
        }

        void Popup(IEnumerable<string> items, Action<string> done) { _popup = new CommandList(items); _popupDone = done; }

        protected override void Update()
        {
            if (_popup != null)
            {
                int r = _popup.Update(this, Cursor);
                if (r == -2) { Cancel(); _popup = null; return; }
                if (r < 0) return;
                var done = _popupDone; string cmd = _popup.Items[r]; _popup = null;
                if (cmd != "Cancel") done(cmd); else Cancel();
                return;
            }
            if (PartyOpen) { PartyInput(); return; }
            BoxInput();
        }

        void BoxInput()
        {
            int old = _cursor;
            if (Input.Repeat(Button.Left))
            {
                if (_cursor == -1) ChangeBox(-1);
                else if (_cursor >= 30) _cursor = _cursor == 31 ? 30 : 31;
                else if (_cursor % 6 == 0) { ChangeBox(-1); _cursor += 5; }
                else _cursor--;
            }
            if (Input.Repeat(Button.Right))
            {
                if (_cursor == -1) ChangeBox(1);
                else if (_cursor >= 30) _cursor = _cursor == 30 ? 31 : 30;
                else if (_cursor % 6 == 5) { ChangeBox(1); _cursor -= 5; }
                else _cursor++;
            }
            if (Input.Repeat(Button.Up)) { if (_cursor == -1) _cursor = 30; else if (_cursor >= 30) _cursor = _cursor == 30 ? 25 : 28; else if (_cursor < 6) _cursor = -1; else _cursor -= 6; }
            if (Input.Repeat(Button.Down)) { if (_cursor == -1) _cursor = 2; else if (_cursor >= 30) _cursor = -1; else if (_cursor >= 24) _cursor = _cursor % 6 < 3 ? 30 : 31; else _cursor += 6; }
            if (_cursor != old) Cursor();
            if (Input.Trigger(Button.Cancel))
            {
                if (_held != null) { Buzz(); Game.Message.Show("You're holding a Pokémon!"); return; }
                Cancel(); Close(); return;
            }
            if (!Input.Trigger(Button.Action)) return;
            if (_cursor == -1) { Decide(); BoxMenu(); return; }
            if (_cursor == 30) { Decide(); _party = 0; return; }
            if (_cursor == 31) { if (_held != null) { Buzz(); return; } Decide(); Close(); return; }
            SlotAction(false);
        }

        void PartyInput()
        {
            int old = _party;
            if (Input.Repeat(Button.Up)) _party = _party == 0 ? 6 : _party - 1;
            if (Input.Repeat(Button.Down)) _party = _party == 6 ? 0 : _party + 1;
            if (Input.Repeat(Button.Left) && _party > 0 && _party < 6 && _party % 2 == 1) _party--;
            if (Input.Repeat(Button.Right) && _party < 5 && _party % 2 == 0) _party++;
            if (_party != old) Cursor();
            if (Input.Trigger(Button.Cancel) || (Input.Trigger(Button.Action) && _party == 6))
            {
                if (_mode == Mode.Deposit && _held == null) { Cancel(); Close(); return; }
                Cancel(); _party = -1; _cursor = 30; return;
            }
            if (Input.Trigger(Button.Action)) SlotAction(true);
        }

        void ChangeBox(int d) { S.Current = (S.Current + d + S.Boxes.Count) % S.Boxes.Count; }

        int AbleInParty(Pokemon except) { return Party.Count(p => p != except && Battle.Able(p)); }

        void SlotAction(bool inParty)
        {
            var pk = Selected;
            if (_held != null) { Place(inParty); return; }
            if (pk == null) { Buzz(); return; }
            Decide();
            var items = new List<string>();
            items.Add("Move");
            if (_mode != Mode.Organize) items[0] = inParty ? "Store" : "Withdraw";
            else items.Add(inParty ? "Store" : "Withdraw");
            items.Add("Summary"); items.Add("Item"); items.Add("Mark"); items.Add("Release"); items.Add("Cancel");
            Popup(items, cmd =>
            {
                switch (cmd)
                {
                    case "Move": PickUp(inParty); break;
                    case "Withdraw": Withdraw(); break;
                    case "Store": Store(); break;
                    case "Summary": Game.PushScene(new SummaryScene(Game, 0, new List<Pokemon> { pk })); break;
                    case "Item": ItemMenu(pk); break;
                    case "Mark": MarkMenu(pk); break;
                    case "Release": Release(pk, inParty); break;
                }
            });
        }

        void PickUp(bool inParty)
        {
            var pk = Selected;
            if (inParty && Battle.Able(pk) && AbleInParty(pk) == 0) { Game.Message.Show("That's your last Pokémon!"); return; }
            _held = pk; _heldFromParty = inParty; _heldFromBox = S.Current; _heldFromSlot = inParty ? _party : _cursor;
            if (inParty) Party.RemoveAt(_party); else Box.Slots[_cursor] = null;
            Game.Audio.PlaySe("GUI storage pick up");
        }

        void Place(bool inParty)
        {
            var target = Selected;
            if (inParty)
            {
                if (_party >= Party.Count) { if (Party.Count >= GameState.MaxParty) { Buzz(); return; } Party.Add(_held); _held = null; }
                else { Party[_party] = _held; _held = target; }
            }
            else { Box.Slots[_cursor] = _held; _held = target; }
            Game.Audio.PlaySe("GUI storage put down");
        }

        void Withdraw()
        {
            if (Party.Count >= GameState.MaxParty) { Game.Message.Show("Your party is full!"); return; }
            var pk = Box.Slots[_cursor]; Box.Slots[_cursor] = null; Party.Add(pk);
            Game.Message.Show(pk.Name(Game.Project) + " was taken out of the box.");
        }

        void Store()
        {
            var pk = Party[_party];
            if (Battle.Able(pk) && AbleInParty(pk) == 0) { Game.Message.Show("That's your last Pokémon!"); return; }
            int at = Box.Slots.IndexOf(null);
            int boxNo = S.Current;
            if (at < 0) { boxNo = S.TryAdd(pk); if (boxNo < 0) { Game.Message.Show("The boxes are full."); return; } }
            else Box.Slots[at] = pk;
            Party.RemoveAt(_party);
            if (_party >= Party.Count) _party = Math.Max(0, Party.Count - 1);
            Game.Audio.PlaySe("PC access");
            Game.Message.Show(pk.Name(Game.Project) + " was stored in " + S.Boxes[boxNo].Name + ".");
        }

        void Release(Pokemon pk, bool inParty)
        {
            if (pk.IsEgg) { Game.Message.Show("You can't release an Egg."); return; }
            if (inParty && Battle.Able(pk) && AbleInParty(pk) == 0) { Game.Message.Show("That's your last Pokémon!"); return; }
            Game.Message.Show("Release this Pokémon?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) return;
                if (inParty) Party.Remove(pk); else S.Remove(pk);
                if (PartyOpen && _party >= Party.Count) _party = Math.Max(0, Party.Count - 1);
                string n = pk.Name(Game.Project);
                Game.Message.Show(n + " was released.\\nBye-bye, " + n + "!");
            });
        }

        void ItemMenu(Pokemon pk)
        {
            if (pk.IsEgg) { Game.Message.Show("Eggs can't hold items."); return; }
            if (!string.IsNullOrEmpty(pk.Item))
            {
                var it = Game.Project.Items.FirstOrDefault(x => x.Id == pk.Item);
                string name = it != null ? it.Name : pk.Item;
                Game.Message.Show(pk.Name(Game.Project) + " is holding " + name + ".", new List<string> { "Take", "Swap", "Cancel" }, 2, c =>
                {
                    if (c == 0) { Game.State.AddItem(pk.Item, 1); Game.Message.Show("Took the " + name + " from " + pk.Name(Game.Project) + "."); pk.Item = null; FormRules.UpdateItemForm(Game.Project, pk); }
                    else if (c == 1) GiveItem(pk);
                });
                return;
            }
            GiveItem(pk);
        }

        void GiveItem(Pokemon pk)
        {
            Game.PushScene(new BagScene(Game, id =>
            {
                if (!string.IsNullOrEmpty(pk.Item)) Game.State.AddItem(pk.Item, 1);
                Game.State.AddItem(id, -1); pk.Item = id; FormRules.UpdateItemForm(Game.Project, pk);
                var it = Game.Project.Items.FirstOrDefault(x => x.Id == id);
                Game.Message.Show(pk.Name(Game.Project) + " is now holding " + (it != null ? it.Name : id) + ".");
            }));
        }

        static readonly string[] MarkSymbols = { "Circle", "Triangle", "Square", "Heart", "Star", "Diamond" };
        static readonly Color[] MarkColors = { new Color(232, 64, 64), new Color(64, 160, 232), new Color(72, 184, 72), new Color(232, 96, 176), new Color(232, 184, 40), new Color(152, 96, 216) };
        void MarkMenu(Pokemon pk)
        {
            var items = MarkSymbols.Select((m, i) => (((pk.Markings >> i) & 1) != 0 ? "[x] " : "[  ] ") + m).ToList();
            items.Add("OK");
            Popup(items, cmd =>
            {
                int i = items.IndexOf(cmd);
                if (i >= 0 && i < MarkSymbols.Length) { pk.Markings ^= 1 << i; MarkMenu(pk); }
            });
        }

        void BoxMenu()
        {
            Popup(new[] { "Jump", "Wallpaper", "Name", "Cancel" }, cmd =>
            {
                if (cmd == "Jump")
                {
                    var names = S.Boxes.Select((b, i) => b.Name + " (" + b.Count + "/30)").ToList();
                    Popup(names.Concat(new[] { "Cancel" }), pick => { int i = names.IndexOf(pick); if (i >= 0) S.Current = i; });
                }
                else if (cmd == "Wallpaper")
                {
                    var names = Enumerable.Range(1, Papers.Length).Select(i => "Wallpaper " + i).ToList();
                    Popup(names.Concat(new[] { "Cancel" }), pick => { int i = names.IndexOf(pick); if (i >= 0) Box.Wallpaper = i; });
                }
                else if (cmd == "Name") Game.AskName("Box name?", Box.Name, 12, n => { if (!string.IsNullOrEmpty(n)) Box.Name = n; }, null);
            });
        }

        // ------------------------------------------------------------------ drawing
        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(152, 208, 168), new Color(64, 136, 104));
            // Box: name header, wallpaper (Graphics/UI/Storage/box_N or a colour), the 30 places
            int bw = 6 * Cell + 8, bh = 5 * Cell + 8;
            var paper = g.Load(Game.Project.FindAsset("Graphics/UI/Storage", "box_" + Box.Wallpaper));
            if (paper != null) g.Draw(paper, 0, 0, Math.Min(paper.Width, bw), Math.Min(paper.Height, bh), GridX - 4, GridY - 4);
            else Ui.Panel(g, GridX - 4, GridY - 4, bw, bh, Papers[Box.Wallpaper % Papers.Length], new Color(32, 80, 64));
            Ui.Panel(g, GridX - 4, 12, bw, 40, _cursor == -1 && !PartyOpen ? new Color(248, 232, 168) : new Color(248, 248, 248), new Color(32, 80, 64));
            Ui.TextCenter(g, GridX - 4 + bw / 2, 16, "◀ " + Box.Name + " ▶");
            for (int i = 0; i < PokemonStorage.BoxSize; i++)
            {
                int x = GridX + (i % 6) * Cell, y = GridY + (i / 6) * Cell;
                bool cur = !PartyOpen && i == _cursor;
                if (cur) g.FillRect(x, y, Cell, Cell, new Color(255, 255, 255, 110));
                var pk = Box.Slots[i];
                if (pk != null) Ui.PokemonIcon(g, Game, pk, x - 8, y - 12, cur ? Frame * 2 : 0);
            }
            // Buttons under the box
            Ui.Panel(g, GridX - 4, GridY + bh + 2, bw / 2 - 4, 34, !PartyOpen && _cursor == 30 ? new Color(248, 232, 168) : new Color(248, 248, 248), new Color(32, 80, 64));
            Ui.TextCenter(g, GridX - 4 + bw / 4 - 2, GridY + bh + 4, "Party");
            Ui.Panel(g, GridX + bw / 2, GridY + bh + 2, bw / 2 - 4, 34, !PartyOpen && _cursor == 31 ? new Color(248, 232, 168) : new Color(248, 248, 248), new Color(32, 80, 64));
            Ui.TextCenter(g, GridX + bw * 3 / 4 - 2, GridY + bh + 4, "Close Box");

            // Left: the selected (or held) Pokémon
            Ui.Panel(g, 8, 12, 188, 296, new Color(248, 248, 248), new Color(32, 80, 64));
            var show = _held ?? Selected;
            if (show != null)
            {
                Ui.PokemonFront(g, Game, show, 102, 170, 150);
                Ui.Text(g, 18, 176, show.Name(Game.Project));
                if (!show.IsEgg)
                {
                    Ui.GenderSymbol(g, 166, 176, show.Gender);
                    Ui.Text(g, 18, 206, "Lv. " + show.Level);
                    var d = show.Data(Game.Project);
                    if (d != null) for (int t = 0; t < d.Types.Count && t < 2; t++) Ui.TypeBadge(g, Game, 18 + t * (Ui.BadgeWidth + 4), 238, d.Types[t]);
                    if (!string.IsNullOrEmpty(show.Item)) { var it = Game.Project.Items.FirstOrDefault(x => x.Id == show.Item); Ui.Text(g, 18, 270, it != null ? it.Name : show.Item); }
                    for (int i = 0; i < 6; i++) g.FillRect(110 + i * 13, 214, 10, 10, ((show.Markings >> i) & 1) != 0 ? MarkColors[i] : new Color(208, 208, 208));
                }
            }

            // Party panel
            if (PartyOpen)
            {
                Ui.Panel(g, 8, 12, 188, 344, new Color(232, 96, 88), new Color(120, 32, 32));
                for (int i = 0; i < 6; i++)
                {
                    int x = 20 + (i % 2) * 86, y = 24 + (i / 2) * 92 + (i % 2) * 20;
                    g.FillRect(x, y, 76, 76, i == _party ? new Color(248, 232, 168) : new Color(248, 200, 192));
                    if (i < Party.Count) Ui.PokemonIcon(g, Game, Party[i], x + 6, y + 2, i == _party ? Frame * 2 : 0);
                }
                g.FillRect(20, 318, 162, 30, _party == 6 ? new Color(248, 232, 168) : new Color(248, 200, 192));
                Ui.TextCenter(g, 101, 318, "Back");
            }
            // Held Pokémon follows the cursor
            if (_held != null)
            {
                int hx, hy;
                if (PartyOpen) { hx = 20 + (_party % 2) * 86 + 20; hy = 24 + (Math.Min(_party, 5) / 2) * 92 - 18; }
                else if (_cursor >= 0 && _cursor < 30) { hx = GridX + (_cursor % 6) * Cell; hy = GridY + (_cursor / 6) * Cell - 30; }
                else { hx = GridX + (_cursor == 31 ? bw / 2 : 0) + 40; hy = GridY + bh - 30; }
                Ui.PokemonIcon(g, Game, _held, hx - 8, hy - 12 + (Frame / 8) % 2 * 2, Frame * 2);
            }
            if (_popup != null) { int w = _popup.Width(g); _popup.Draw(g, g.Width - w - 8, g.Height - _popup.Height(g) - 8); }
        }
    }
}
