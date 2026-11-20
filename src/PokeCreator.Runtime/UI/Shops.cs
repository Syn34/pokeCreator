using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Battles;
using PokeCreator.Data;
using PokeCreator.Pkmn;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime.UI
{
    // =====================================================================================
    /// <summary>Poké Mart: Buy (with quantity), Sell (from the Bag at half price), Quit.</summary>
    public class MartScene : Scene
    {
        readonly List<string> _stock;
        readonly bool _cantSell;
        enum Mode { Welcome, Buy, Quantity, Busy }
        Mode _mode = Mode.Welcome;
        int _index, _scroll, _qty = 1;
        ItemData _item;
        bool _selling;
        const int Rows = 7;

        public MartScene(Game g, IEnumerable<string> stock, bool cantSell) : base(g)
        {
            _stock = stock.Where(id => g.Project.Items.Any(i => i.Id == id)).Distinct().ToList();
            _cantSell = cantSell;
        }

        ItemData Item(string id) { return Game.Project.Items.FirstOrDefault(i => i.Id == id); }
        static int SellPrice(ItemData it) { return it.SellPrice > 0 ? it.SellPrice : it.Price / 2; }
        int Have(string id) { int n; return Game.State.Bag.TryGetValue(id, out n) ? n : 0; }

        void Welcome(string text)
        {
            _mode = Mode.Busy; Opaque = false;
            var options = _cantSell ? new List<string> { "Buy", "Quit" } : new List<string> { "Buy", "Sell", "Quit" };
            Game.Message.Show(text, options, options.Count - 1, c =>
            {
                string pick = options[c];
                if (pick == "Buy") { _mode = Mode.Buy; Opaque = true; _index = 0; _scroll = 0; }
                else if (pick == "Sell") Sell();
                else Game.Message.Show("Please come again!", null, -1, _ => Close());
            });
        }

        void Sell()
        {
            _mode = Mode.Busy;
            Game.PushScene(new BagScene(Game, id =>
            {
                var it = Item(id);
                if (it == null || it.Price <= 0 || it.Pocket == 8) { Game.Message.Show("Oh, no. I can't buy that.", null, -1, _ => Sell()); return; }
                _item = it; _selling = true; _qty = 1; _mode = Mode.Quantity;
            }, it => true, () => Welcome("Is there anything else I can do for you?")));
        }

        protected override void Update()
        {
            if (_mode == Mode.Welcome) { Welcome("Welcome! How may I help you?"); return; }
            if (_mode == Mode.Buy) BuyList();
            else if (_mode == Mode.Quantity) Quantity();
        }

        void BuyList()
        {
            int n = _stock.Count + 1; // + Cancel
            if (Input.Repeat(Button.Down)) { _index = (_index + 1) % n; Cursor(); }
            if (Input.Repeat(Button.Up)) { _index = (_index + n - 1) % n; Cursor(); }
            if (_index < _scroll) _scroll = _index;
            if (_index >= _scroll + Rows) _scroll = _index - Rows + 1;
            if (Input.Trigger(Button.Cancel) || (Input.Trigger(Button.Action) && _index == _stock.Count)) { Cancel(); Welcome("Is there anything else I can do for you?"); return; }
            if (!Input.Trigger(Button.Action)) return;
            var it = Item(_stock[_index]);
            if (Game.State.Money < it.Price) { Buzz(); _mode = Mode.Busy; Game.Message.Show("You don't have enough money.", null, -1, _ => _mode = Mode.Buy); return; }
            Decide();
            _item = it; _selling = false; _qty = 1;
            if (it.Pocket == 8 || it.FieldUse == "HM") { Confirm(); return; } // key items: just one
            _mode = Mode.Quantity;
        }

        int MaxQty()
        {
            if (_selling) return Math.Max(1, Have(_item.Id));
            int byMoney = _item.Price > 0 ? Game.State.Money / _item.Price : 99;
            return Math.Max(1, Math.Min(99, Math.Min(byMoney, 999 - Have(_item.Id))));
        }

        void Quantity()
        {
            int max = MaxQty();
            if (Input.Repeat(Button.Up)) { _qty = _qty >= max ? 1 : _qty + 1; Cursor(); }
            if (Input.Repeat(Button.Down)) { _qty = _qty <= 1 ? max : _qty - 1; Cursor(); }
            if (Input.Repeat(Button.Right)) { _qty = Math.Min(max, _qty + 10); Cursor(); }
            if (Input.Repeat(Button.Left)) { _qty = Math.Max(1, _qty - 10); Cursor(); }
            if (Input.Trigger(Button.Cancel)) { Cancel(); if (_selling) Sell(); else _mode = Mode.Buy; return; }
            if (Input.Trigger(Button.Action)) { Decide(); Confirm(); }
        }

        void Confirm()
        {
            _mode = Mode.Busy;
            string name = _qty > 1 && !string.IsNullOrEmpty(_item.NamePlural) ? _item.NamePlural : (_item.Name ?? _item.Id);
            if (_selling)
            {
                int total = SellPrice(_item) * _qty;
                Game.Message.Show("I can pay $" + total + ". Would that be OK?", new List<string> { "Yes", "No" }, 1, c =>
                {
                    if (c == 0)
                    {
                        Game.State.Money += total; Game.State.AddItem(_item.Id, -_qty);
                        Game.Audio.PlaySe("Mart buy item");
                        Game.Message.Show("Turned over the " + name + " and received $" + total + ".", null, -1, _ => Sell());
                    }
                    else Sell();
                });
                return;
            }
            int cost = _item.Price * _qty;
            Game.Message.Show(name + ", and you want " + _qty + ". That will be $" + cost + ". OK?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) { _mode = Mode.Buy; return; }
                if (Game.State.Money < cost) { Game.Message.Show("You don't have enough money.", null, -1, _ => _mode = Mode.Buy); return; }
                Game.State.Money -= cost; Game.State.AddItem(_item.Id, _qty);
                Game.Audio.PlaySe("Mart buy item");
                bool bonus = _item.Id == "POKEBALL" && _qty >= 10 && Item("PREMIERBALL") != null;
                if (bonus) Game.State.AddItem("PREMIERBALL", 1);
                Game.Message.Show("Here you are! Thank you!" + (bonus ? "\\nI'll throw in a Premier Ball, too." : ""), null, -1, _ => _mode = Mode.Buy);
            });
        }

        public override void Draw(Gfx g)
        {
            if (_mode == Mode.Welcome || (_mode == Mode.Busy && !Opaque && !_selling)) return;
            if (_selling)
            {
                if (_mode != Mode.Quantity) return;
                DrawQuantity(g, SellPrice(_item));
                return;
            }
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(120, 184, 248), new Color(56, 104, 192));
            Ui.Panel(g, 8, 8, 176, 60, new Color(248, 248, 248), new Color(40, 64, 120));
            Ui.Text(g, 20, 10, "Money");
            Ui.TextRight(g, 172, 36, "$" + Game.State.Money);
            Ui.Panel(g, 8, 76, 176, 200, new Color(248, 248, 248), new Color(40, 64, 120));
            if (_index < _stock.Count)
            {
                Ui.ItemIcon(g, Game, _stock[_index], 72, 120);
                Ui.TextCenter(g, 96, 200, "In Bag: " + Have(_stock[_index]));
            }
            Ui.Panel(g, 192, 8, g.Width - 200, 268, new Color(248, 248, 248), new Color(40, 64, 120));
            for (int row = 0; row < Rows; row++)
            {
                int i = _scroll + row; if (i > _stock.Count) break;
                int y = 18 + row * 36;
                if (i == _index) Ui.Arrow(g, 202, y + 8);
                if (i == _stock.Count) { Ui.Text(g, 222, y, "Cancel"); continue; }
                var it = Item(_stock[i]);
                Ui.Text(g, 222, y, it.Name ?? it.Id);
                Ui.TextRight(g, g.Width - 24, y, "$" + it.Price);
            }
            Ui.Box(g, 4, 284, g.Width - 8, 96);
            string desc = _index < _stock.Count ? Item(_stock[_index]).Description ?? "" : "Quit shopping.";
            DrawWrapped(g, desc, 22, 298, g.Width - 48);
            if (_mode == Mode.Quantity) DrawQuantity(g, _item.Price);
        }

        void DrawQuantity(Gfx g, int unit)
        {
            int w = 230, h = 64, x = g.Width - w - 8, y = 284 - h - 6;
            Ui.Panel(g, x, y, w, h, new Color(248, 248, 248), new Color(40, 64, 120));
            Ui.Text(g, x + 16, y + 6, "×" + _qty.ToString("00"));
            Ui.TextRight(g, x + w - 16, y + 6, "$" + unit * _qty);
            g.DrawText(x + 16, y + 34, "▲▼ 1   ◀▶ 10", new Color(120, 120, 136));
            if (_selling) { Ui.Box(g, 4, 284, g.Width - 8, 96); Ui.Text(g, 22, 298, "How many " + (_item.NamePlural ?? _item.Name) + " would you like to sell?"); }
        }

        static void DrawWrapped(Gfx g, string text, int x, int y, int width)
        {
            var words = (text ?? "").Split(' '); string line = ""; int ly = y;
            foreach (var w in words)
            {
                string t = line.Length == 0 ? w : line + " " + w;
                if (line.Length > 0 && g.TextWidth(t) > width) { Ui.Text(g, x, ly, line); ly += 32; line = w; if (ly > y + 50) return; }
                else line = t;
            }
            Ui.Text(g, x, ly, line);
        }
    }

    // =====================================================================================
    /// <summary>
    /// Pokémon Storage: boxes of 30. Withdraw, deposit, see the summary, release.
    /// </summary>
    public class StorageScene : Scene
    {
        public const int BoxSize = 30;
        readonly bool _deposit;
        int _box, _cursor;
        CommandList _popup;
        List<Pokemon> Store { get { return Game.State.Storage; } }
        int Boxes { get { return Math.Max(1, (Store.Count + BoxSize) / BoxSize); } }

        public StorageScene(Game g, bool deposit) : base(g) { Opaque = true; _deposit = deposit; }

        protected override void Update()
        {
            if (_deposit) { DepositFlow(); return; }
            if (_popup != null)
            {
                int r = _popup.Update(this, Cursor);
                if (r == -2 || (r >= 0 && _popup.Items[r] == "Cancel")) { Cancel(); _popup = null; return; }
                if (r < 0) return;
                string cmd = _popup.Items[r]; _popup = null;
                int idx = _box * BoxSize + _cursor;
                var pk = Store[idx];
                if (cmd == "Withdraw")
                {
                    if (Game.State.Party.Count >= GameState.MaxParty) { Game.Message.Show("Your party is full!"); return; }
                    Store.RemoveAt(idx); Game.State.Party.Add(pk);
                    Game.Message.Show(pk.Name(Game.Project) + " was taken out of the box.");
                    ClampCursor();
                }
                else if (cmd == "Summary")
                {
                    // The summary screen works on the party list, so show it from a temporary one
                    Game.PushScene(new SummaryScene(Game, 0, new List<Pokemon> { pk }));
                }
                else if (cmd == "Release")
                {
                    Game.Message.Show("Release " + pk.Name(Game.Project) + "?", new List<string> { "Yes", "No" }, 1, c =>
                    {
                        if (c != 0) return;
                        Store.Remove(pk); ClampCursor();
                        Game.Message.Show(pk.Name(Game.Project) + " was released.\\nBye-bye, " + pk.Name(Game.Project) + "!");
                    });
                }
                return;
            }
            int inBox = Math.Max(0, Math.Min(BoxSize, Store.Count - _box * BoxSize));
            if (Input.Repeat(Button.Right)) { if (_cursor % 6 == 5) { _box = (_box + 1) % Boxes; } else _cursor++; Cursor(); }
            if (Input.Repeat(Button.Left)) { if (_cursor % 6 == 0) { _box = (_box + Boxes - 1) % Boxes; } else _cursor--; Cursor(); }
            if (Input.Repeat(Button.Down)) { _cursor = (_cursor + 6) % BoxSize; Cursor(); }
            if (Input.Repeat(Button.Up)) { _cursor = (_cursor + BoxSize - 6) % BoxSize; Cursor(); }
            if (Input.Trigger(Button.Cancel)) { Cancel(); Close(); return; }
            if (Input.Trigger(Button.Action))
            {
                if (_cursor >= inBox) { Buzz(); return; }
                Decide();
                _popup = new CommandList(new[] { "Withdraw", "Summary", "Release", "Cancel" });
            }
        }

        void ClampCursor() { int inBox = Store.Count - _box * BoxSize; if (inBox <= 0 && _box > 0) { _box--; } }

        bool _depositOpen;
        void DepositFlow()
        {
            if (_depositOpen) return;
            _depositOpen = true;
            Game.PushScene(new PartyScene(Game, "Deposit which Pokémon?", null, (i, close) =>
            {
                var party = Game.State.Party;
                var pk = party[i];
                if (party.Count(p => Battle.Able(p)) <= 1 && Battle.Able(pk)) { Game.Message.Show("That's your last Pokémon that can battle!"); return; }
                party.RemoveAt(i); Store.Add(pk);
                Game.Audio.PlaySe("PC access");
                Game.Message.Show(pk.Name(Game.Project) + " was stored in Box " + ((Store.Count - 1) / BoxSize + 1) + ".");
                if (party.Count == 0) close();
            }, () => Close()));
        }

        public override void Draw(Gfx g)
        {
            if (_deposit) return;
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(152, 208, 168), new Color(64, 136, 104));
            Ui.Panel(g, 196, 8, g.Width - 204, 40, new Color(248, 248, 248), new Color(32, 80, 64));
            Ui.TextCenter(g, 196 + (g.Width - 204) / 2, 12, "◀ Box " + (_box + 1) + " ▶");
            Ui.Panel(g, 196, 56, g.Width - 204, 260, new Color(208, 236, 216), new Color(32, 80, 64));
            for (int i = 0; i < BoxSize; i++)
            {
                int idx = _box * BoxSize + i;
                int x = 206 + (i % 6) * 50, y = 64 + (i / 6) * 50;
                if (i == _cursor) g.FillRect(x - 2, y - 2, 52, 52, new Color(248, 216, 120));
                if (idx < Store.Count) Ui.PokemonIcon(g, Game, Store[idx], x - 7, y - 10, i == _cursor ? Frame * 2 : 0);
            }
            // Selected Pokémon
            Ui.Panel(g, 8, 8, 180, 308, new Color(248, 248, 248), new Color(32, 80, 64));
            int sel = _box * BoxSize + _cursor;
            if (sel < Store.Count)
            {
                var pk = Store[sel];
                Ui.PokemonFront(g, Game, pk, 98, 180, 160);
                Ui.Text(g, 20, 190, pk.Name(Game.Project));
                Ui.Text(g, 20, 222, "Lv. " + pk.Level);
                Ui.GenderSymbol(g, 150, 190, pk.Gender);
                if (!string.IsNullOrEmpty(pk.Item)) { var it = Game.Project.Items.FirstOrDefault(x => x.Id == pk.Item); Ui.Text(g, 20, 254, it != null ? it.Name : pk.Item); }
            }
            Ui.Box(g, 4, 324, g.Width - 8, 56);
            Ui.Text(g, 22, 336, Store.Count == 0 ? "The boxes are empty." : Store.Count + " Pokémon stored. A: options   B: close");
            if (_popup != null) { int w = _popup.Width(g); _popup.Draw(g, g.Width - w - 8, 324 - _popup.Height(g) - 4); }
        }
    }

    /// <summary>The PC menus: Someone's PC (Pokémon storage), the player's PC (item storage), Log off.</summary>
    public static class PcMenu
    {
        public static void Open(Game g, bool pokeCenter)
        {
            g.Audio.PlaySe("PC open");
            if (!pokeCenter) { PlayerPc(g, null); return; }
            g.Message.Show("\\PN booted up the PC.", null, -1, _ => ShowMain(g));
        }

        static void ShowMain(Game g)
        {
            var options = new List<string> { "Someone's PC", g.State.PlayerName + "'s PC", "Log off" };
            g.Message.Show("Which PC should be accessed?", options, 2, c =>
            {
                if (c == 0) StoragePc(g);
                else if (c == 1) PlayerPc(g, () => ShowMain(g));
                else g.Audio.PlaySe("PC close");
            });
        }

        static void StoragePc(Game g)
        {
            g.Audio.PlaySe("PC access");
            var options = new List<string> { "Withdraw Pokémon", "Deposit Pokémon", "See ya!" };
            g.Message.Show("The Pokémon Storage System was opened.", options, 2, c =>
            {
                if (c == 0) g.PushScene(new ReturnTo(g, new StorageScene(g, false), () => StoragePc(g)));
                else if (c == 1) g.PushScene(new ReturnTo(g, new StorageScene(g, true), () => StoragePc(g)));
                else ShowMain(g);
            });
        }

        static void PlayerPc(Game g, Action back)
        {
            g.Audio.PlaySe("PC access");
            var options = new List<string> { "Withdraw Item", "Deposit Item", "Toss Item", "Log off" };
            g.Message.Show("What do you want to do?", options, 3, c =>
            {
                Action again = () => PlayerPc(g, back);
                if (c == 0) WithdrawItem(g, again);
                else if (c == 1) g.PushScene(new BagScene(g, id =>
                {
                    var it = g.Project.Items.FirstOrDefault(i => i.Id == id);
                    if (it != null && it.Pocket == 8) { g.Message.Show("That's too important to store.", null, -1, _ => again()); return; }
                    int n; g.State.Bag.TryGetValue(id, out n);
                    g.State.AddItem(id, -n); AddPcItem(g, id, n);
                    g.Message.Show("Stored " + n + " " + (it != null ? (n > 1 && it.NamePlural != null ? it.NamePlural : it.Name) : id) + " in the PC.", null, -1, _ => again());
                }, x => x.Pocket != 8, again));
                else if (c == 2) TossItem(g, again);
                else if (back != null) back(); else g.Audio.PlaySe("PC close");
            });
        }

        static void AddPcItem(Game g, string id, int n)
        {
            int have; g.State.PcItems.TryGetValue(id, out have);
            if (have + n <= 0) g.State.PcItems.Remove(id); else g.State.PcItems[id] = have + n;
        }

        static void WithdrawItem(Game g, Action again)
        {
            var ids = g.State.PcItems.Keys.ToList();
            if (ids.Count == 0) { g.Message.Show("There are no items.", null, -1, _ => again()); return; }
            var names = ids.Select(id => { var it = g.Project.Items.FirstOrDefault(i => i.Id == id); return (it != null ? it.Name : id) + " ×" + g.State.PcItems[id]; }).ToList();
            names.Add("Cancel");
            g.Message.Show("Withdraw which item?", names.Take(6).Concat(names.Count > 6 ? new[] { "Cancel" } : new string[0]).Distinct().ToList(), Math.Min(6, names.Count - 1), c =>
            {
                if (c < 0 || c >= ids.Count || c >= 6) { again(); return; }
                string id = ids[c]; int n = g.State.PcItems[id];
                AddPcItem(g, id, -n); g.State.AddItem(id, n);
                g.Message.Show("Withdrew " + names[c] + ".", null, -1, _ => again());
            });
        }

        static void TossItem(Game g, Action again)
        {
            var ids = g.State.PcItems.Keys.Take(6).ToList();
            if (ids.Count == 0) { g.Message.Show("There are no items.", null, -1, _ => again()); return; }
            var names = ids.Select(id => { var it = g.Project.Items.FirstOrDefault(i => i.Id == id); return it != null ? it.Name : id; }).ToList();
            names.Add("Cancel");
            g.Message.Show("Toss which item?", names, names.Count - 1, c =>
            {
                if (c < 0 || c >= ids.Count) { again(); return; }
                g.State.PcItems.Remove(ids[c]);
                g.Message.Show("Threw away the " + names[c] + ".", null, -1, _ => again());
            });
        }
    }

    /// <summary>Runs an action once the wrapped scene closes (used to return to a menu).</summary>
    public class ReturnTo : Scene
    {
        readonly Scene _inner;
        readonly Action _after;
        bool _pushed;
        public ReturnTo(Game g, Scene inner, Action after) : base(g) { _inner = inner; _after = after; }
        protected override void Update()
        {
            if (!_pushed) { _pushed = true; Game.PushScene(_inner); return; }
            if (_inner.Closed) { Close(); if (_after != null) _after(); }
        }
        public override void Draw(Gfx g) { }
    }
}
