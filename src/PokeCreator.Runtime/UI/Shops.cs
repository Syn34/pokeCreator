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

        /// <summary>Battle Point shop (pbBattlePointShop): prices in BP, nothing to sell.</summary>
        public bool BattlePoints;
        int Funds { get { return BattlePoints ? Game.State.BattlePoints : Game.State.Money; } set { if (BattlePoints) Game.State.BattlePoints = value; else Game.State.Money = value; } }
        /// <summary>setPrice(item, buy, sell) from the event that opened this shop (Essentials).</summary>
        public static Dictionary<string, int[]> PriceOverrides = new Dictionary<string, int[]>();
        int Cost(ItemData it) { int[] o; if (PriceOverrides.TryGetValue(it.Id, out o) && o[0] >= 0) return o[0]; return BattlePoints ? (it.BPPrice > 0 ? it.BPPrice : Math.Max(1, it.Price / 500)) : it.Price; }
        string Fmt(int v) { return BattlePoints ? v + " BP" : "$" + v; }

        public MartScene(Game g, IEnumerable<string> stock, bool cantSell) : base(g)
        {
            _stock = stock.Where(id => g.Project.Items.Any(i => i.Id == id)).Distinct().ToList();
            _cantSell = cantSell;
        }

        ItemData Item(string id) { return Game.Project.Items.FirstOrDefault(i => i.Id == id); }
        static int SellPrice(ItemData it) { int[] o; if (PriceOverrides.TryGetValue(it.Id, out o) && o[1] >= 0) return o[1]; return it.SellPrice > 0 ? it.SellPrice : it.Price / 2; }
        int Have(string id) { int n; return Game.State.Bag.TryGetValue(id, out n) ? n : 0; }

        void Welcome(string text)
        {
            _mode = Mode.Busy; Opaque = false;
            var options = _cantSell || BattlePoints ? new List<string> { "Buy", "Quit" } : new List<string> { "Buy", "Sell", "Quit" };
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
            if (Funds < Cost(it)) { Buzz(); _mode = Mode.Busy; Game.Message.Show(BattlePoints ? "You don't have enough BP." : "You don't have enough money.", null, -1, _ => _mode = Mode.Buy); return; }
            Decide();
            _item = it; _selling = false; _qty = 1;
            if (it.Pocket == 8 || it.FieldUse == "HM") { Confirm(); return; } // key items: just one
            _mode = Mode.Quantity;
        }

        int MaxQty()
        {
            if (_selling) return Math.Max(1, Have(_item.Id));
            int byMoney = Cost(_item) > 0 ? Funds / Cost(_item) : 99;
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
            int cost = Cost(_item) * _qty;
            Game.Message.Show(name + ", and you want " + _qty + ". That will be " + Fmt(cost) + ". OK?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) { _mode = Mode.Buy; return; }
                if (Funds < cost) { Game.Message.Show(BattlePoints ? "You don't have enough BP." : "You don't have enough money.", null, -1, _ => _mode = Mode.Buy); return; }
                Funds -= cost; Game.State.AddItem(_item.Id, _qty);
                if (!BattlePoints) { World.GameStats.Inc(Game, "money_spent_at_marts", cost); World.GameStats.Inc(Game, "mart_items_bought", _qty); }
                Game.Audio.PlaySe("Mart buy item");
                // Gen 8+: a Premier Ball for every 10 Poké Balls of any kind
                int premier = !BattlePoints && _item.Pocket == 3 && _item.Id != "PREMIERBALL" && Item("PREMIERBALL") != null ? _qty / 10 : 0;
                if (premier > 0) Game.State.AddItem("PREMIERBALL", premier);
                Game.Message.Show("Here you are! Thank you!" + (premier == 1 ? "\\nI'll throw in a Premier Ball, too." : premier > 1 ? "\\nI'll throw in some Premier Balls, too." : ""), null, -1, _ => _mode = Mode.Buy);
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
            Ui.Text(g, 20, 10, BattlePoints ? "Battle Points" : "Money");
            Ui.TextRight(g, 172, 36, Fmt(Funds));
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
                Ui.TextRight(g, g.Width - 24, y, Fmt(Cost(it)));
            }
            Ui.Box(g, 4, 284, g.Width - 8, 96);
            string desc = _index < _stock.Count ? Item(_stock[_index]).Description ?? "" : "Quit shopping.";
            DrawWrapped(g, desc, 22, 298, g.Width - 48);
            if (_mode == Mode.Quantity) DrawQuantity(g, Cost(_item));
        }

        void DrawQuantity(Gfx g, int unit)
        {
            int w = 230, h = 64, x = g.Width - w - 8, y = 284 - h - 6;
            Ui.Panel(g, x, y, w, h, new Color(248, 248, 248), new Color(40, 64, 120));
            Ui.Text(g, x + 16, y + 6, "×" + _qty.ToString("00"));
            Ui.TextRight(g, x + w - 16, y + 6, _selling ? "$" + unit * _qty : Fmt(unit * _qty));
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
            bool chamber = g.State.Flags.Contains("seen_purify_chamber");
            var options = new List<string> { "Someone's PC", g.State.PlayerName + "'s PC" };
            if (chamber) options.Add("Purify Chamber");
            bool hof = g.State.HallOfFame.Count > 0;
            if (hof) options.Add("Hall of Fame");
            options.Add("Log off");
            g.Message.Show("Which PC should be accessed?", options, options.Count - 1, c =>
            {
                string pick = c >= 0 && c < options.Count ? options[c] : "Log off";
                if (c == 0) StoragePc(g);
                else if (c == 1) PlayerPc(g, () => ShowMain(g));
                else if (pick == "Purify Chamber") PurifyChamber(g, () => ShowMain(g));
                else if (pick == "Hall of Fame")
                {
                    var s = new HallOfFameScene(g, true);
                    s.OnClosed = () => ShowMain(g);
                    g.PushScene(s);
                }
                else g.Audio.PlaySe("PC close");
            });
        }

        /// <summary>
        /// The Purify Chamber (simplified from Essentials): up to 9 Shadow Pokémon kept here open their hearts twice as
        /// fast as walking with them; one whose heart is fully open is purified when taken out.
        /// </summary>
        public static void PurifyChamber(Game g, Action back)
        {
            var ch = g.State.Extras.PurifyChamber ?? (g.State.Extras.PurifyChamber = new List<Pokemon>());
            var opts = new List<string> { "Put in a Shadow Pokémon", "Take out a Pokémon", "Back" };
            string status = ch.Count == 0 ? "The chamber is empty." : string.Join(", ", ch.Select(p => p.Name(g.Project) + " (" + (PokeCreator.Pkmn.Shadow.Purifiable(p) ? "ready" : "heart " + PokeCreator.Pkmn.Shadow.Stage(p) + "/5") + ")"));
            g.Message.Show(status, opts, 2, c =>
            {
                Action again = () => PurifyChamber(g, back);
                if (c == 0)
                {
                    if (ch.Count >= 9) { g.Message.Show("The chamber is full.", null, -1, _ => again()); return; }
                    g.PushScene(new PartyScene(g, "Put in which Pokémon?", null, (i, close) =>
                    {
                        var pk = g.State.Party[i];
                        if (!pk.ShadowPokemon) { g.Message.Show("Only Shadow Pokémon can go in the chamber."); return; }
                        if (g.State.Party.Count(p => !p.IsEgg && !p.Fainted && p != pk) == 0) { g.Message.Show("That's your last Pokémon!"); return; }
                        close(); g.State.Party.Remove(pk); ch.Add(pk);
                        g.Message.Show(pk.Name(g.Project) + " was placed in the Purify Chamber.", null, -1, _ => again());
                    }, again));
                }
                else if (c == 1)
                {
                    if (ch.Count == 0) { again(); return; }
                    var names = ch.Select(p => p.Name(g.Project)).ToList(); names.Add("Cancel");
                    g.Message.Show("Take out which Pokémon?", names, names.Count - 1, k =>
                    {
                        if (k < 0 || k >= ch.Count) { again(); return; }
                        if (g.State.Party.Count >= GameState.MaxParty) { g.Message.Show("Your party is full!", null, -1, _ => again()); return; }
                        var pk = ch[k]; ch.RemoveAt(k); g.State.Party.Add(pk);
                        if (PokeCreator.Pkmn.Shadow.Purifiable(pk))
                        {
                            PokeCreator.Pkmn.Shadow.Purify(g.Project, pk);
                            g.Audio.PlayMe("Evolution success");
                            g.Message.Show(pk.Name(g.Project) + " opened the door to its heart!\n" + pk.Name(g.Project) + " became a normal Pokémon again!", null, -1, _ => again());
                        }
                        else g.Message.Show(pk.Name(g.Project) + " was taken out of the chamber.", null, -1, _ => again());
                    });
                }
                else back();
            });
        }

        static void StoragePc(Game g)
        {
            g.Audio.PlaySe("PC access");
            var options = new List<string> { "Organize Boxes", "Withdraw Pokémon", "Deposit Pokémon", "See ya!" };
            g.Message.Show("The Pokémon Storage System was opened.", options, 3, c =>
            {
                if (c >= 0 && c <= 2) g.PushScene(new ReturnTo(g, new StorageScene(g, (StorageScene.Mode)c), () => StoragePc(g)));
                else ShowMain(g);
            });
        }

        /// <summary>The PC mailbox: read letters, move the Mail back to the Bag (the message is lost) or give it to a Pokémon.</summary>
        public static void Mailbox(Game g, Action back)
        {
            var box = g.State.Extras.Mailbox;
            if (box == null || box.Count == 0) { back(); return; }
            var names = box.Select(m => { var it = g.Project.Items.FirstOrDefault(i => i.Id == m.Item); return (it != null ? it.Name : m.Item) + " (" + m.Sender + ")"; }).ToList();
            names.Add("Cancel");
            g.Message.Show("Which Mail?", names, names.Count - 1, k =>
            {
                if (k < 0 || k >= box.Count) { back(); return; }
                var mail = box[k];
                g.Message.Show("What do you want to do with it?", new List<string> { "Read", "Move to Bag", "Give", "Cancel" }, 3, a =>
                {
                    Action again = () => Mailbox(g, back);
                    if (a == 0) { var sc = new MailScene(g, mail); sc.OnClosed = again; g.PushScene(sc); }
                    else if (a == 1)
                    {
                        g.Message.Show("The message will be lost. Is that OK?", new List<string> { "Yes", "No" }, 1, y =>
                        {
                            if (y == 0) { box.Remove(mail); g.State.AddItem(mail.Item, 1); g.Message.Show("The Mail was returned to the Bag with its message erased.", null, -1, _ => again()); }
                            else again();
                        });
                    }
                    else if (a == 2)
                    {
                        g.PushScene(new PartyScene(g, "Give to which Pokémon?", null, (i, close) =>
                        {
                            var pk = g.State.Party[i];
                            if (pk.IsEgg) { g.Message.Show("Eggs can't hold items."); return; }
                            if (!string.IsNullOrEmpty(pk.Item)) { g.Message.Show(pk.Name(g.Project) + " is already holding an item."); return; }
                            close(); box.Remove(mail); pk.Item = mail.Item; pk.Mail = mail;
                            g.Message.Show("The Mail was given to " + pk.Name(g.Project) + ".", null, -1, _ => again());
                        }, again));
                    }
                    else again();
                });
            });
        }

        public static void PlayerPc(Game g, Action back)
        {
            g.Audio.PlaySe("PC access");
            bool mail = g.State.Extras.Mailbox != null && g.State.Extras.Mailbox.Count > 0;
            var options = new List<string> { "Withdraw Item", "Deposit Item", "Toss Item" };
            if (mail) options.Add("Mailbox");
            options.Add("Log off");
            g.Message.Show("What do you want to do?", options, options.Count - 1, c =>
            {
                Action again = () => PlayerPc(g, back);
                if (mail && c == 3) { Mailbox(g, again); return; }
                if (mail && c == 4) c = 3;
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
