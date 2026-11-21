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
        /// <summary>Caught Pokémon with a full party: false = sent to the PC, true = asked which party Pokémon to send instead (Essentials' "Send to Boxes").</summary>
        public bool SendToBoxesManual { get; set; }
        /// <summary>Translation chosen by the player (a file in the project's Languages folder; "" = original).</summary>
        public string Language { get; set; }
        /// <summary>Window size: 0 = the game's default, 1-4 = times the game size, 5 = full screen.</summary>
        public int ScreenSize { get; set; }
        /// <summary>Changed key bindings: button name → SDL scancodes.</summary>
        public Dictionary<string, List<int>> Keys { get; set; }
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
            Input.ResetKeys();
            if (Keys != null) foreach (var kv in Keys) { Button b; if (Enum.TryParse(kv.Key, out b) && kv.Value != null && kv.Value.Count > 0) Input.SetKeys(b, kv.Value.ToArray()); }
            if (ScreenSize > 0 && g.Gfx != null && ScreenSize != _appliedSize) { g.Gfx.SetScreenSize(ScreenSize >= 5 ? 0 : ScreenSize); _appliedSize = ScreenSize; }
        }
        int _appliedSize;

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
                case "GRACIDEA": case "REVEALGLASS": case "PRISONBOTTLE": case "ROTOMCATALOG": case "DNASPLICERS": case "NSOLARIZER": case "NLUNARIZER": case "REINSOFUNITY":
                case "REDNECTAR": case "YELLOWNECTAR": case "PINKNECTAR": case "PURPLENECTAR":
                    g.PushScene(new PartyScene(g, "Use on which Pokémon?", null, (i, close) => { close(); FormItems.Use(g, itemId, g.State.Party[i], refresh); }, null));
                    return;
                case "WHITEFLUTE": case "BLACKFLUTE":
                    {
                        bool white = itemId == "WHITEFLUTE";
                        g.State.Extras.Flute = white ? 1 : -1; g.State.Extras.FluteMap = g.Map != null ? g.Map.Id : 0;
                        g.Message.Show(white ? "Wild Pokémon will be lured." : "Wild Pokémon will be repelled.");
                        return;
                    }
                case "SACREDASH":
                    {
                        var fainted = g.State.Party.Where(x => !x.IsEgg && x.Fainted).ToList();
                        if (fainted.Count == 0) { Say(g, "It won't have any effect.", refresh); return; }
                        g.State.AddItem(itemId, -1);
                        foreach (var x in fainted) { x.HP = x.TotalHP(g.Project); x.Status = ""; }
                        g.Audio.PlaySe("Use item in party");
                        Say(g, string.Join("\n", fainted.Select(x => x.Name(g.Project) + "'s HP was restored.")), refresh);
                        return;
                    }
                case "ESCAPEROPE": FieldMoves.Escape(g, "\\PN used the " + (item != null ? item.Name : "Escape Rope") + "."); return;
                case "POKERADAR": g.CloseAllScenes(); PokeRadar.Use(g); return;
                case "JOYSCENT": case "EXCITESCENT": case "VIVIDSCENT":
                    g.PushScene(new PartyScene(g, "Use on which Pokémon?", null, (i, close) =>
                    {
                        var pk = g.State.Party[i];
                        int amount = itemId == "JOYSCENT" ? 500 : itemId == "EXCITESCENT" ? 1000 : 2000;
                        if (!pk.ShadowPokemon || pk.HeartGauge <= 0) { g.Message.Show("It won't have any effect."); return; }
                        close();
                        g.State.AddItem(itemId, -1);
                        PokeCreator.Pkmn.Shadow.AdjustHeart(pk, -amount);
                        g.Message.Show(pk.Name(g.Project) + "'s heart opened a little!" + (PokeCreator.Pkmn.Shadow.Purifiable(pk) ? "\n" + pk.Name(g.Project) + " can now be purified!" : ""), null, -1, _ => { if (refresh != null) refresh(); });
                    }, null));
                    return;
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
            if (info.NeedsStat)
            {
                var stats = Enumerable.Range(0, 6).Where(s => pk.IV[s] < 31).ToList();
                if (stats.Count == 0) { Say(g, "It won't have any effect.", () => done(false)); return; }
                var names = stats.Select(s => Stat.Names[s]).ToList(); names.Add("Cancel");
                g.Message.Show("Which stat should be trained?", names, names.Count - 1, c =>
                {
                    if (c < 0 || c >= stats.Count) { done(false); return; }
                    Apply(g, pk, item, info, stats[c], done);
                });
                return;
            }
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
                    LearnMove(g, pk, o.TeachMove, learned => { if (learned) pk.ChangeHappiness("machine"); if (learned && info.Consumed) g.State.AddItem(item.Id, -1); done(learned); }));
                return;
            }
            if (info.Consumed) g.State.AddItem(item.Id, -1);
            if (o.LevelsGained > 0) g.Audio.PlayMe("Pkmn level up");
            else if (info.Kind != ItemKind.Evolution) g.Audio.PlaySe("Use item in party");
            Action afterMessage = () => LearnMoves(g, pk, new Queue<string>(o.MovesToLearn), () =>
            {
                if (o.EvolveTo != null) g.PushScene(new EvolutionScene(g, pk, o.EvolveTo, info.Kind != ItemKind.Evolution, () => done(true)));
                else done(true);
            });
            Say(g, o.Message, () =>
            {
                // Rare Candy and the like: the stat increases, as in battle
                if (o.LevelsGained == 1) { var st = new LevelUpStatsScene(g, pk); st.OnClosed = afterMessage; g.PushScene(st); }
                else afterMessage();
            });
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
            if (IsMail(item))
            {
                // Mail: write the letter first; giving it up cancels
                g.AskName("Write a message (" + iname + ")", "", 40, text =>
                {
                    if (string.IsNullOrEmpty(text)) { Say(g, "The Mail was put away.", done); return; }
                    g.State.AddItem(item.Id, -1); pk.Item = item.Id;
                    pk.Mail = new PokemonMail { Item = item.Id, Message = text, Sender = g.State.PlayerName };
                    Say(g, "The " + iname + " was given to " + name + " to hold.", done);
                }, null);
                return;
            }
            g.State.AddItem(item.Id, -1); pk.Item = item.Id;
            FormChangedByItem(g, pk);
            Say(g, name + " is now holding the " + iname + ".", done);
        }

        public static bool IsMail(ItemData item) { return item != null && (item.Pocket == 6 || item.Flags.Contains("Mail") || item.Flags.Contains("IconMail")); }

        /// <summary>Takes held Mail: the letter goes to the PC mailbox (or is erased to put the Mail in the Bag).</summary>
        public static void TakeMail(Game g, Pokemon pk, Action done)
        {
            if (pk.Mail == null) { Take(g, pk, done); return; }
            g.Message.Show("The message will be lost. Is that OK?", new List<string> { "Send to PC mailbox", "Erase it", "Cancel" }, 2, c =>
            {
                if (c == 2 || c < 0) { if (done != null) done(); return; }
                if (c == 0)
                {
                    if (g.State.Extras.Mailbox == null) g.State.Extras.Mailbox = new List<PokemonMail>();
                    if (g.State.Extras.Mailbox.Count >= 10) { Say(g, "The mailbox is full.", done); return; }
                    g.State.Extras.Mailbox.Add(pk.Mail); pk.Mail = null; pk.Item = null;
                    Say(g, "The Mail was sent to your PC.", done);
                    return;
                }
                pk.Mail = null; Take(g, pk, done);
            });
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
            World.GameStats.Inc(Game, "evolution_count");
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
        List<SpeciesData> _list;
        List<SpeciesData> _full;
        readonly Dictionary<string, int> _numbers = new Dictionary<string, int>();
        CommandList _popup;
        string _searchNote;
        int _index, _scroll;
        const int Rows = 8;

        readonly string _title = "Pokédex";

        /// <summary>The Pokédexes the player can open: unlocked regional dexes (with something seen in them), then the National Dex (-1).</summary>
        public static List<int> ViableDexes(Game g)
        {
            var list = new List<int>();
            var unlocked = World.DexState.Unlocked(g);
            for (int i = 0; i < g.Project.RegionalDexes.Count; i++)
                if (unlocked.Contains(i) && g.Project.RegionalDexes[i].Species.Any(sp => g.State.Seen.Contains(sp))) list.Add(i);
            if (unlocked.Contains(-1) || g.Project.RegionalDexes.Count == 0) list.Add(-1);
            return list;
        }

        public static string DexName(Game g, int dex)
        {
            if (dex < 0 || dex >= g.Project.RegionalDexes.Count) return "National Pokédex";
            var d = g.Project.RegionalDexes[dex];
            return string.IsNullOrEmpty(d.Name) ? "Regional Pokédex" : d.Name + (d.Name.IndexOf("dex", StringComparison.OrdinalIgnoreCase) >= 0 ? "" : " Pokédex");
        }

        /// <summary>Opens the Pokédex: with several viable dexes the player picks one first.</summary>
        public static void Open(Game g)
        {
            var dexes = ViableDexes(g);
            if (dexes.Count <= 1) { g.PushScene(new PokedexScene(g, dexes.Count == 1 ? dexes[0] : -1)); return; }
            var names = dexes.Select(d => DexName(g, d)).ToList(); names.Add("Cancel");
            g.Message.Show("Which Pokédex?", names, names.Count - 1, k => { if (k >= 0 && k < dexes.Count) g.PushScene(new PokedexScene(g, dexes[k])); });
        }

        public PokedexScene(Game g) : this(g, -1) { }

        public PokedexScene(Game g, int dex) : base(g)
        {
            Opaque = true;
            if (dex >= 0 && dex < g.Project.RegionalDexes.Count)
            {
                _list = g.Project.RegionalDexes[dex].Species.Select(sp => g.Project.Species.FirstOrDefault(s => s.Id == sp && s.Form == 0)).Where(s => s != null).ToList();
                _title = DexName(g, dex);
            }
            else _list = g.Project.Species.Where(s => s.Form == 0).ToList();
            _full = _list.ToList();
            for (int k = 0; k < _full.Count; k++) _numbers[_full[k].Id] = k + 1;
            // Start on the last species the player has seen
            int last = _list.FindLastIndex(s => g.State.Seen.Contains(s.Id));
            _index = Math.Max(0, last); _scroll = Math.Max(0, _index - Rows / 2);
        }

        protected override void Update()
        {
            if (_popup != null) { SearchMenu(); return; }
            if (Input.Trigger(Button.Menu) || Input.Trigger(Button.Run)) { Decide(); OpenSearch(); return; }
            int n = _list.Count; if (n == 0) { if (Input.Trigger(Button.Cancel)) { if (_searchNote != null) ResetSearch(); else Close(); } return; }
            int old = _index;
            if (Input.Repeat(Button.Down)) _index = Math.Min(n - 1, _index + 1);
            if (Input.Repeat(Button.Up)) _index = Math.Max(0, _index - 1);
            if (Input.Repeat(Button.Right)) _index = Math.Min(n - 1, _index + Rows);
            if (Input.Repeat(Button.Left)) _index = Math.Max(0, _index - Rows);
            if (_index != old) Cursor();
            if (_index < _scroll) _scroll = _index;
            if (_index >= _scroll + Rows) _scroll = _index - Rows + 1;
            if (Input.Trigger(Button.Cancel)) { Cancel(); if (_searchNote != null) ResetSearch(); else Close(); return; }
            if (Input.Trigger(Button.Action))
            {
                if (!Game.State.Seen.Contains(_list[_index].Id)) { Buzz(); return; }
                Decide();
                Game.PushScene(new PokedexEntryScene(Game, _list, _index, i => { _index = i; _scroll = Math.Max(0, Math.Min(_index - Rows / 2, _list.Count - Rows)); }));
            }
        }

        // ---- Search (Menu key): sort the seen Pokémon or show only one type
        int _searchStep; // 0 = main, 1 = sort, 2 = type
        void OpenSearch() { _searchStep = 0; _popup = new CommandList(new[] { "Sort", "Type", "Name starts with", "Show all", "Cancel" }); }

        void SearchMenu()
        {
            int r = _popup.Update(this, Cursor);
            if (r == -2) { Cancel(); _popup = null; return; }
            if (r < 0) return;
            string cmd = _popup.Items[r]; _popup = null;
            if (cmd == "Cancel") return;
            var seen = _full.Where(x => Game.State.Seen.Contains(x.Id)).ToList();
            if (_searchStep == 0)
            {
                if (cmd == "Sort") { _searchStep = 1; _popup = new CommandList(new[] { "Numerical", "A to Z", "Heaviest", "Lightest", "Tallest", "Smallest", "Cancel" }); }
                else if (cmd == "Type") { _searchStep = 2; _popup = new CommandList(Game.Project.Types.Where(t => !t.IsPseudoType).Select(t => t.Name ?? t.Id).Concat(new[] { "Cancel" })); }
                else if (cmd == "Name starts with") Game.AskName("First letters?", "", 10, text => { if (!string.IsNullOrEmpty(text)) Apply(seen.Where(x => (x.Name ?? x.Id).StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToList(), "Starts with " + text); }, null);
                else ResetSearch();
                return;
            }
            if (_searchStep == 1)
            {
                switch (cmd)
                {
                    case "Numerical": ResetSearch(); break;
                    case "A to Z": Apply(seen.OrderBy(x => x.Name ?? x.Id).ToList(), "A to Z"); break;
                    case "Heaviest": Apply(seen.OrderByDescending(x => x.Weight).ToList(), "Heaviest"); break;
                    case "Lightest": Apply(seen.OrderBy(x => x.Weight).ToList(), "Lightest"); break;
                    case "Tallest": Apply(seen.OrderByDescending(x => x.Height).ToList(), "Tallest"); break;
                    case "Smallest": Apply(seen.OrderBy(x => x.Height).ToList(), "Smallest"); break;
                }
                return;
            }
            var type = Game.Project.Types.FirstOrDefault(t => (t.Name ?? t.Id) == cmd);
            if (type != null) Apply(seen.Where(x => x.Types.Contains(type.Id)).ToList(), (type.Name ?? type.Id) + " type");
        }

        void Apply(List<SpeciesData> list, string note) { _list = list; _searchNote = note; _index = 0; _scroll = 0; }
        void ResetSearch() { _list = _full.ToList(); _searchNote = null; _index = 0; _scroll = 0; }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(232, 72, 64), new Color(168, 32, 32));
            Ui.Panel(g, 8, 8, g.Width - 16, 40, new Color(248, 248, 248), new Color(120, 24, 24));
            Ui.Text(g, 22, 12, _title);
            Ui.TextRight(g, g.Width - 24, 12, "Seen " + _list.Count(s => Game.State.Seen.Contains(s.Id)) + "   Owned " + _list.Count(s => Game.State.Caught.Contains(s.Id)));
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
                int num; _numbers.TryGetValue(s.Id, out num);
                Ui.Text(g, 254, y, num.ToString("000"));
                Ui.Text(g, 310, y, seen ? (s.Name ?? s.Id) : "-----");
            }
            Ui.Box(g, 8, 304, 200, 64);
            int cn = 0; if (cur != null) _numbers.TryGetValue(cur.Id, out cn);
            Ui.Text(g, 20, 314, _searchNote ?? (cur == null ? "" : cn.ToString("000") + " " + (Game.State.Seen.Contains(cur.Id) ? cur.Name : "?????")));
            if (_list.Count == 0) Ui.TextCenter(g, 216 + (g.Width - 224) / 2, 180, "No matching Pokémon.");
            if (_popup != null) { int w = _popup.Width(g); _popup.Draw(g, g.Width - w - 8, g.Height - _popup.Height(g) - 8); }
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

        int _page; // 0 = info, 1 = area, 2 = forms
        int _form;
        static readonly string[] PageNames = { "INFO", "AREA", "FORMS" };

        protected override void Update()
        {
            if (Input.Trigger(Button.Cancel)) { Cancel(); Close(); if (_onClose != null) _onClose(_index); return; }
            if (Input.Trigger(Button.Action))
            {
                if (_page == 2 && Forms().Count > 1) { _form = (_form + 1) % Forms().Count; Cursor(); return; }
                Game.Audio.PlayCry(new Pokemon { Species = _list[_index].Id, Form = _page == 2 ? Forms()[_form].Form : 0 }); return;
            }
            if (Input.Trigger(Button.Right) && _page < 2) { _page++; _form = 0; Cursor(); return; }
            if (Input.Trigger(Button.Left) && _page > 0) { _page--; _form = 0; Cursor(); return; }
            int dir = Input.Repeat(Button.Down) ? 1 : Input.Repeat(Button.Up) ? -1 : 0;
            if (dir == 0) return;
            for (int i = _index + dir; i >= 0 && i < _list.Count; i += dir)
                if (Game.State.Seen.Contains(_list[i].Id)) { _index = i; _form = 0; Cursor(); break; }
        }

        List<SpeciesData> Forms()
        {
            string id = _list[_index].Id;
            var l = Game.Project.Species.Where(x => x.Id == id || (x.Form > 0 && x.Id.StartsWith(id + "_"))).OrderBy(x => x.Form).ToList();
            return l.Count > 0 ? l : new List<SpeciesData> { _list[_index] };
        }

        static readonly Dictionary<int, PokeCreator.Maps.MapProperties> _propCache = new Dictionary<int, PokeCreator.Maps.MapProperties>();

        /// <summary>Town map squares (region → x,y) where wild encounters include this species.</summary>
        public static Dictionary<int, HashSet<int>> AreaSquares(Game g, string species)
        {
            var res = new Dictionary<int, HashSet<int>>();
            foreach (var enc in g.Project.Encounters.Where(e => e.Methods.Any(m => m.Slots.Any(sl => sl.Species == species))))
            {
                PokeCreator.Maps.MapProperties props;
                if (!_propCache.TryGetValue(enc.Map, out props))
                {
                    PokeCreator.Maps.MapData md = null; try { md = g.Project.LoadMap(enc.Map); } catch { }
                    props = md != null ? md.Properties : null; _propCache[enc.Map] = props;
                }
                if (props == null || props.MapPosition == null || props.MapPosition.Count < 3) continue;
                int region = props.MapPosition[0], x = props.MapPosition[1], y = props.MapPosition[2], w = 1, h = 1;
                if (!string.IsNullOrEmpty(props.MapSize))
                {
                    var parts = props.MapSize.Split(',');
                    if (int.TryParse(parts[0].Trim(), out w) && w > 0) { string fl = parts.Length > 1 ? parts[1].Trim() : ""; h = Math.Max(1, (int)Math.Ceiling(fl.Length / (double)w)); } else w = 1;
                }
                HashSet<int> set; if (!res.TryGetValue(region, out set)) res[region] = set = new HashSet<int>();
                for (int yy = 0; yy < h; yy++) for (int xx = 0; xx < w; xx++) set.Add((x + xx) * 1000 + y + yy);
            }
            return res;
        }

        void DrawArea(Gfx g, SpeciesData s)
        {
            var squares = AreaSquares(Game, s.Id);
            int regionNo = 0; var pos = TownMapScene.CurrentPosition(Game); if (pos != null) regionNo = pos[0];
            if (!squares.ContainsKey(regionNo) && squares.Count > 0) regionNo = squares.Keys.First();
            var region = Game.Project.TownMaps.FirstOrDefault(r => r.Id == regionNo.ToString()) ?? Game.Project.TownMaps.FirstOrDefault();
            if (region == null) { Ui.TextCenter(g, g.Width / 2, g.Height / 2, "There is no town map."); return; }
            var img = string.IsNullOrEmpty(region.Filename) ? null : g.Load(Game.Project.FindAsset("Graphics/UI/Town Map", System.IO.Path.GetFileNameWithoutExtension(region.Filename)));
            int mw = img != null ? img.Width : 480, mh = img != null ? img.Height : 288;
            mw = Math.Min(mw, g.Width - 32); mh = Math.Min(mh, g.Height - 96);
            int mx = (g.Width - mw) / 2, my = 60;
            if (img != null) g.Draw(img, 0, 0, mw, mh, mx, my); else g.FillRect(mx, my, mw, mh, new Color(96, 168, 96));
            HashSet<int> set;
            bool any = squares.TryGetValue(regionNo, out set) && set.Count > 0;
            if (any)
            {
                byte alpha = (byte)(140 + (int)(80 * Math.Sin(Frame / 10.0)));
                foreach (int k in set) { int sx = mx + (k / 1000) * 16, sy = my + (k % 1000) * 16; g.FillRect(sx, sy, 16, 16, new Color(248, 40, 40, alpha)); g.FillRect(sx, sy, 16, 2, new Color(160, 0, 0)); g.FillRect(sx, sy + 14, 16, 2, new Color(160, 0, 0)); g.FillRect(sx, sy, 2, 16, new Color(160, 0, 0)); g.FillRect(sx + 14, sy, 2, 16, new Color(160, 0, 0)); }
            }
            Ui.Box(g, 8, g.Height - 44, g.Width - 16, 36);
            Ui.Text(g, 20, g.Height - 40, any ? (s.Name ?? s.Id) + "'s area" + (string.IsNullOrEmpty(region.Name) ? "" : " in " + region.Name) : "Area unknown");
        }

        void DrawForms(Gfx g)
        {
            var forms = Forms(); if (_form >= forms.Count) _form = 0;
            var f = forms[_form];
            Ui.Panel(g, g.Width / 2 - 110, 60, 220, 220, new Color(232, 232, 224), new Color(160, 160, 152));
            Ui.PokemonFront(g, Game, new Pokemon { Species = _list[_index].Id, Form = f.Form }, g.Width / 2, 270, 190);
            string fname = !string.IsNullOrEmpty(f.FormName) ? f.FormName : f.Form == 0 ? "Normal Form" : "Form " + f.Form;
            Ui.TextCenter(g, g.Width / 2, 292, fname);
            if (forms.Count > 1) Ui.TextCenter(g, g.Width / 2, 326, (_form + 1) + " / " + forms.Count + "   (Action: next form)");
        }

        public override void Draw(Gfx g)
        {
            var s = _list[_index];
            bool caught = Game.State.Caught.Contains(s.Id);
            if (_page > 0)
            {
                Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(232, 72, 64), new Color(168, 32, 32));
                Ui.Panel(g, 8, 8, g.Width - 16, 44, new Color(248, 248, 240), new Color(120, 24, 24));
                Ui.Text(g, 20, 14, (_index + 1).ToString("000") + "  " + (s.Name ?? s.Id));
                Ui.TextRight(g, g.Width - 20, 14, "< " + PageNames[_page] + " >");
                if (_page == 1) DrawArea(g, s); else DrawForms(g);
                return;
            }
            Ui.TextRight(g, g.Width - 24, g.Height - 40, "INFO >");
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
        readonly string[] Labels;
        List<string> _speech, _menu, _langs;

        public OptionsScene(Game g) : base(g)
        {
            Opaque = true; _o = g.Options;
            _speech = PlayerOptions.Frames(g, "speech"); _speech.Insert(0, "");
            _menu = PlayerOptions.Frames(g, "choice"); _menu.Insert(0, "");
            _langs = PokeCreator.Projects.Localization.Available(g.Project); _langs.Insert(0, "");
            Labels = _langs.Count > 1
                ? new[] { "Text Speed", "Music Volume", "Sound Volume", "Battle Effects", "Battle Style", "Send to Boxes", "Speech Frame", "Menu Frame", "Running Key", "Screen Size", "Language", "Controls", "Close" }
                : new[] { "Text Speed", "Music Volume", "Sound Volume", "Battle Effects", "Battle Style", "Send to Boxes", "Speech Frame", "Menu Frame", "Running Key", "Screen Size", "Controls", "Close" };
        }

        static string Cycle(List<string> list, string cur, int d)
        {
            int i = Math.Max(0, list.IndexOf(cur ?? ""));
            return list[(i + d + list.Count) % list.Count];
        }

        static string FrameLabel(string f) { return string.IsNullOrEmpty(f) ? "Default" : f; }

        string Value(string label)
        {
            switch (label)
            {
                case "Text Speed": return PlayerOptions.TextSpeeds[_o.TextSpeed];
                case "Music Volume": return _o.BgmVolume + "%";
                case "Sound Volume": return _o.SeVolume + "%";
                case "Battle Effects": return _o.BattleEffects ? "On" : "Off";
                case "Battle Style": return _o.BattleStyleSet ? "Set" : "Switch";
                case "Send to Boxes": return _o.SendToBoxesManual ? "Manual" : "Automatic";
                case "Speech Frame": return FrameLabel(_o.SpeechFrame);
                case "Menu Frame": return FrameLabel(_o.MenuFrame);
                case "Running Key": return _o.RunToggle ? "Toggle" : "Hold";
                case "Screen Size": return ScreenLabel();
                case "Language": return string.IsNullOrEmpty(_o.Language) ? "Original" : _o.Language;
            }
            return "";
        }
        string ScreenLabel() { int s = _o.ScreenSize <= 0 ? Math.Max(1, Game.Project.Settings.WindowScale) : _o.ScreenSize; return s >= 5 ? "Full Screen" : "×" + s; }

        protected override void Update()
        {
            if (Input.Repeat(Button.Down)) { _row = (_row + 1) % Labels.Length; Cursor(); }
            if (Input.Repeat(Button.Up)) { _row = (_row + Labels.Length - 1) % Labels.Length; Cursor(); }
            int d = Input.Repeat(Button.Right) ? 1 : Input.Repeat(Button.Left) ? -1 : 0;
            if (d != 0)
            {
                switch (Labels[_row])
                {
                    case "Text Speed": _o.TextSpeed = (_o.TextSpeed + d + 4) % 4; break;
                    case "Music Volume": _o.BgmVolume = Math.Max(0, Math.Min(100, _o.BgmVolume + d * 10)); break;
                    case "Sound Volume": _o.SeVolume = Math.Max(0, Math.Min(100, _o.SeVolume + d * 10)); break;
                    case "Battle Effects": _o.BattleEffects = !_o.BattleEffects; break;
                    case "Battle Style": _o.BattleStyleSet = !_o.BattleStyleSet; break;
                    case "Send to Boxes": _o.SendToBoxesManual = !_o.SendToBoxesManual; break;
                    case "Speech Frame": _o.SpeechFrame = Cycle(_speech, _o.SpeechFrame, d); break;
                    case "Menu Frame": _o.MenuFrame = Cycle(_menu, _o.MenuFrame, d); break;
                    case "Running Key": _o.RunToggle = !_o.RunToggle; break;
                    case "Screen Size": { int cur = _o.ScreenSize <= 0 ? Math.Max(1, Game.Project.Settings.WindowScale) : _o.ScreenSize; _o.ScreenSize = (cur - 1 + d + 5) % 5 + 1; break; }
                    case "Language": _o.Language = Cycle(_langs, _o.Language, d); PokeCreator.Projects.Localization.Apply(Game.Project, _o.Language); break;
                }
                _o.Apply(Game);
                if (_row != Labels.Length - 1) Cursor();
            }
            if (Input.Trigger(Button.Action) && Labels[_row] == "Controls") { Decide(); Game.PushScene(new ControlsScene(Game)); return; }
            if (Input.Trigger(Button.Cancel) || (Input.Trigger(Button.Action) && _row == Labels.Length - 1)) { _o.Save(Game); Cancel(); Close(); }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(96, 136, 200), new Color(40, 64, 128));
            Ui.Panel(g, 8, 8, g.Width - 16, 44, new Color(248, 248, 248), new Color(32, 48, 96));
            Ui.Text(g, 22, 14, "Options");
            Ui.Panel(g, 8, 60, g.Width - 16, 262, new Color(248, 248, 248), new Color(32, 48, 96));
            int rowH = Math.Min(23, 254 / Labels.Length);
            for (int i = 0; i < Labels.Length; i++)
            {
                int y = 64 + i * rowH;
                if (i == _row) Ui.Arrow(g, 22, y + 8);
                Ui.Text(g, 44, y, Labels[i]);
                string v = Value(Labels[i]);
                if (v.Length > 0) Ui.TextRight(g, g.Width - 32, y, "◀ " + v + " ▶");
            }
            Ui.Box(g, 4, g.Height - 56, g.Width - 8, 52);
            Ui.Text(g, 22, g.Height - 44, "◀ ▶ change a setting (kept for all saves).");
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
            Opaque = true; _prompt = g.Message != null ? g.Message.Expand(prompt ?? "") : prompt; _max = Math.Max(1, max); _done = done; _name = initial ?? "";
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

namespace PokeCreator.Runtime.UI
{
    /// <summary>Reading a letter: the Mail's picture (Graphics/UI/Mail/ITEM, or a plain card) with its message and sender.</summary>
    public class MailScene : Scene
    {
        readonly PokemonMail _mail;
        public MailScene(Game g, PokemonMail mail) : base(g) { Opaque = true; _mail = mail; }

        protected override void Update()
        {
            if (Input.Trigger(Button.Cancel) || Input.Trigger(Button.Action)) { Cancel(); Close(); }
        }

        public override void Draw(Gfx g)
        {
            var bg = g.Load(Game.Project.FindAsset("Graphics/UI/Mail", (_mail.Item ?? "").ToLowerInvariant())) ?? g.Load(Game.Project.FindAsset("Graphics/UI/Mail", _mail.Item ?? ""));
            if (bg != null) g.Draw(bg, 0, 0, Math.Min(bg.Width, g.Width), Math.Min(bg.Height, g.Height), 0, 0);
            else
            {
                Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(248, 232, 200), new Color(232, 200, 160));
                Ui.Panel(g, 24, 24, g.Width - 48, g.Height - 48, new Color(252, 248, 236), new Color(176, 136, 96));
                for (int y = 96; y < g.Height - 60; y += 36) g.FillRect(48, y + 30, g.Width - 96, 1, new Color(216, 192, 160));
            }
            var it = Game.Project.Items.FirstOrDefault(x => x.Id == _mail.Item);
            Ui.Text(g, 48, 40, it != null ? it.Name : (_mail.Item ?? "Mail"));
            var words = (_mail.Message ?? "").Split(' '); string line = ""; int ly = 96;
            foreach (var w in words)
            {
                string t = line.Length == 0 ? w : line + " " + w;
                if (line.Length > 0 && g.TextWidth(t) > g.Width - 112) { Ui.Text(g, 56, ly, line); ly += 36; line = w; }
                else line = t;
            }
            Ui.Text(g, 56, ly, line);
            if (!string.IsNullOrEmpty(_mail.Sender)) Ui.TextRight(g, g.Width - 56, g.Height - 76, "From " + _mail.Sender);
        }
    }
}

namespace PokeCreator.Runtime.UI
{
    /// <summary>Options → Controls: choose a button, then press the key to use for it (it replaces the first key; Shift+Action adds one).</summary>
    public class ControlsScene : Scene
    {
        static readonly Button[] Buttons = { Button.Up, Button.Down, Button.Left, Button.Right, Button.Action, Button.Cancel, Button.Menu, Button.Run };
        static readonly string[] Names = { "Up", "Down", "Left", "Right", "Action (A)", "Cancel (B)", "Menu", "Run" };
        int _row; int _waiting = -1, _waitFrames;
        public ControlsScene(Game g) : base(g) { Opaque = true; }

        protected override void Update()
        {
            if (_waiting >= 0)
            {
                if (++_waitFrames < 10) { Input.AnyKeyTriggered(); return; } // let go of the key that chose the row
                int sc = Input.AnyKeyTriggered();
                if (sc < 0) return;
                var b = Buttons[_waiting];
                var keys = Input.KeysFor(b).ToList();
                if (keys.Count > 0) keys[0] = sc; else keys.Add(sc);
                Input.SetKeys(b, keys.Distinct().ToArray());
                var o = Game.Options;
                if (o.Keys == null) o.Keys = new Dictionary<string, List<int>>();
                o.Keys[b.ToString()] = Input.KeysFor(b).ToList();
                _waiting = -1; Decide();
                return;
            }
            int n = Buttons.Length + 2; // + Reset, Back
            if (Input.Repeat(Button.Down)) { _row = (_row + 1) % n; Cursor(); }
            if (Input.Repeat(Button.Up)) { _row = (_row + n - 1) % n; Cursor(); }
            if (Input.Trigger(Button.Cancel)) { Cancel(); Game.Options.Save(Game); Close(); return; }
            if (!Input.Trigger(Button.Action)) return;
            Decide();
            if (_row < Buttons.Length) { _waiting = _row; _waitFrames = 0; }
            else if (_row == Buttons.Length) { Game.Options.Keys = null; Input.ResetKeys(); }
            else { Game.Options.Save(Game); Close(); }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(96, 136, 200), new Color(40, 64, 128));
            Ui.Panel(g, 8, 8, g.Width - 16, 44, new Color(248, 248, 248), new Color(32, 48, 96));
            Ui.Text(g, 22, 14, "Controls");
            Ui.Panel(g, 8, 60, g.Width - 16, 270, new Color(248, 248, 248), new Color(32, 48, 96));
            for (int i = 0; i < Buttons.Length + 2; i++)
            {
                int y = 66 + i * 26;
                if (i == _row) Ui.Arrow(g, 22, y + 8);
                if (i < Buttons.Length)
                {
                    Ui.Text(g, 44, y, Names[i]);
                    string keys = _waiting == i ? "Press a key..." : string.Join(", ", Input.KeysFor(Buttons[i]).Take(3).Select(Input.KeyName));
                    Ui.TextRight(g, g.Width - 28, y, keys);
                }
                else Ui.Text(g, 44, y, i == Buttons.Length ? "Reset to defaults" : "Back");
            }
            Ui.Box(g, 4, g.Height - 50, g.Width - 8, 46);
            Ui.Text(g, 22, g.Height - 40, "A: change a key   B: back");
        }
    }
}

namespace PokeCreator.Runtime.UI
{
    /// <summary>
    /// Items that change a Pokémon's form (as Essentials): Gracidea (Shaymin), Reveal Glass (the forces of nature),
    /// Prison Bottle (Hoopa), nectars (Oricorio), Rotom Catalog, and fusing with DNA Splicers / N-Solarizer /
    /// N-Lunarizer / Reins of Unity (used again to separate them).
    /// </summary>
    public static class FormItems
    {
        static void Changed(Game g, Pokemon pk, Action done)
        {
            g.Audio.PlaySe("Use item in party");
            g.Message.Show(pk.Name(g.Project) + " changed its form!", null, -1, _ => { if (done != null) done(); });
        }

        static void NoEffect(Game g, Action done) { g.Message.Show("It won't have any effect.", null, -1, _ => { if (done != null) done(); }); }

        public static void Use(Game g, string item, Pokemon pk, Action done)
        {
            var p = g.Project;
            if (pk.IsEgg) { NoEffect(g, done); return; }
            switch (item)
            {
                case "GRACIDEA":
                    {
                        int h = DateTime.Now.Hour;
                        if (pk.Species != "SHAYMIN" || pk.Form != 0 || h >= 20 || h < 5 || (pk.Status ?? "").ToUpperInvariant() == "FROZEN" || !FormRules.HasForm(p, "SHAYMIN", 1)) { NoEffect(g, done); return; }
                        pk.Form = 1; Changed(g, pk, done); return;
                    }
                case "REVEALGLASS":
                    if (Array.IndexOf(new[] { "TORNADUS", "THUNDURUS", "LANDORUS", "ENAMORUS" }, pk.Species) < 0 || !FormRules.HasForm(p, pk.Species, 1)) { NoEffect(g, done); return; }
                    pk.Form = pk.Form == 0 ? 1 : 0; Changed(g, pk, done); return;
                case "PRISONBOTTLE":
                    if (pk.Species != "HOOPA" || !FormRules.HasForm(p, "HOOPA", 1)) { NoEffect(g, done); return; }
                    pk.Form = pk.Form == 0 ? 1 : 0; Changed(g, pk, done); return;
                case "REDNECTAR": case "YELLOWNECTAR": case "PINKNECTAR": case "PURPLENECTAR":
                    {
                        int f = item == "REDNECTAR" ? 0 : item == "YELLOWNECTAR" ? 1 : item == "PINKNECTAR" ? 2 : 3;
                        if (pk.Species != "ORICORIO" || pk.Form == f || !FormRules.HasForm(p, "ORICORIO", f)) { NoEffect(g, done); return; }
                        pk.Form = f; g.State.AddItem(item, -1); Changed(g, pk, done); return;
                    }
                case "ROTOMCATALOG":
                    {
                        if (pk.Species != "ROTOM") { NoEffect(g, done); return; }
                        string[] names = { "Light bulb", "Microwave oven", "Washing machine", "Refrigerator", "Electric fan", "Lawn mower" };
                        string[] moves = { "THUNDERSHOCK", "OVERHEAT", "HYDROPUMP", "BLIZZARD", "AIRSLASH", "LEAFSTORM" };
                        var opts = names.Where((n, i) => FormRules.HasForm(p, "ROTOM", i)).ToList(); opts.Add("Cancel");
                        g.Message.Show("Which appliance would you like to order?", opts, opts.Count - 1, k =>
                        {
                            if (k < 0 || k >= opts.Count - 1) { if (done != null) done(); return; }
                            int f = Array.IndexOf(names, opts[k]);
                            if (f == pk.Form) { NoEffect(g, done); return; }
                            // The old appliance's move is replaced by the new one's
                            string oldMove = pk.Form > 0 ? moves[pk.Form] : null, newMove = f > 0 ? moves[f] : null;
                            int at = oldMove != null ? pk.Moves.FindIndex(m => m.Id == oldMove) : -1;
                            if (at >= 0) pk.Moves.RemoveAt(at);
                            pk.Form = f;
                            var md = newMove != null ? p.Moves.FirstOrDefault(m => m.Id == newMove) : null;
                            if (md != null && !pk.Moves.Any(m => m.Id == md.Id))
                            {
                                if (pk.Moves.Count >= 4) pk.Moves[at >= 0 && at < pk.Moves.Count ? at : 0] = new PokemonMove { Id = md.Id, PP = md.TotalPP };
                                else pk.Moves.Insert(at >= 0 ? Math.Min(at, pk.Moves.Count) : pk.Moves.Count, new PokemonMove { Id = md.Id, PP = md.TotalPP });
                            }
                            if (pk.Moves.Count == 0) { var ts = p.Moves.FirstOrDefault(m => m.Id == "THUNDERSHOCK"); if (ts != null) pk.Moves.Add(new PokemonMove { Id = ts.Id, PP = ts.TotalPP }); }
                            Changed(g, pk, done);
                        });
                        return;
                    }
                case "DNASPLICERS": Fuse(g, pk, "KYUREM", new[] { "RESHIRAM", "ZEKROM" }, done); return;
                case "NSOLARIZER": Fuse(g, pk, "NECROZMA", new[] { "SOLGALEO" }, done); return;
                case "NLUNARIZER": Fuse(g, pk, "NECROZMA", new[] { null, "LUNALA" }, done); return;
                case "REINSOFUNITY": Fuse(g, pk, "CALYREX", new[] { "GLASTRIER", "SPECTRIER" }, done); return;
            }
            NoEffect(g, done);
        }

        /// <summary>partners[i] gives form i + 1 (Kyurem: White with Reshiram, Black with Zekrom).</summary>
        static void Fuse(Game g, Pokemon pk, string baseSpecies, string[] partners, Action done)
        {
            var p = g.Project;
            if (pk.Species != baseSpecies) { NoEffect(g, done); return; }
            if (pk.Fused != null)
            {
                if (g.State.Party.Count >= GameState.MaxParty) { g.Message.Show("You have no room to separate the Pokémon.", null, -1, _ => { if (done != null) done(); }); return; }
                var partner = pk.Fused; pk.Fused = null; pk.Form = 0;
                g.State.Party.Add(partner);
                Changed(g, pk, done); return;
            }
            g.PushScene(new PartyScene(g, "Fuse with which Pokémon?", null, (i, close) =>
            {
                var other = g.State.Party[i];
                int idx = Array.IndexOf(partners, other.Species);
                if (other == pk || idx < 0 || other.IsEgg || other.Fainted || !FormRules.HasForm(p, baseSpecies, idx + 1)) { g.Message.Show("It can't be fused with that Pokémon."); return; }
                close();
                g.State.Party.Remove(other); pk.Fused = other; pk.Form = idx + 1;
                Changed(g, pk, done);
            }, () => { if (done != null) done(); }));
        }
    }
}

namespace PokeCreator.Runtime.UI
{
    /// <summary>"Oh! A bite!": press Action within about a second to hook the Pokémon.</summary>
    public class HookScene : Scene
    {
        readonly Action<bool> _done;
        int _t;
        const int Window = 50;
        public HookScene(Game g, Action<bool> done) : base(g)
        {
            Opaque = false; _done = done;
            if (g.Player != null) g.Exclaim(g.Player, Window);
        }

        protected override void Update()
        {
            _t++;
            if (_t > 4 && Input.Trigger(Button.Action)) { Close(); _done(true); return; }
            if (_t > Window) { Close(); _done(false); }
        }

        public override void Draw(Gfx g)
        {
            string s = "Oh! A bite!";
            int w = g.TextWidth(s) + 40, h = 56, x = (g.Width - w) / 2, y = g.Height - h - 24;
            Ui.Box(g, x, y, w, h);
            Ui.Text(g, x + 20, y + 12, s);
        }
    }
}

namespace PokeCreator.Runtime.UI
{
    /// <summary>
    /// The trade animation: the player's Pokémon goes into its ball and flies off, the other trainer's ball comes
    /// down and opens. Calls back after the last message (trade evolutions run from there).
    /// </summary>
    public class TradeScene : Scene
    {
        readonly Pokemon _sent, _got;
        readonly string _partner;
        readonly Action _done;
        int _phase, _t;

        public TradeScene(Game g, Pokemon sent, Pokemon got, string partner, Action done) : base(g)
        {
            Opaque = true; _sent = sent; _got = got; _partner = partner ?? "Stranger"; _done = done;
        }

        protected override void Update()
        {
            var p = Game.Project;
            switch (_phase)
            {
                case 0:
                    _phase = 1; _t = 0;
                    Game.Audio.PlayBgm("Evolution");
                    Game.Audio.PlayCry(_sent);
                    Game.Message.Show("For " + _partner + "'s " + _got.Name(p) + ", \\PN sent over " + _sent.Name(p) + ".", null, -1, _ => { _phase = 2; _t = 0; Game.Audio.PlaySe("Battle recall"); });
                    return;
                case 2:
                    if (++_t >= 110) { _phase = 3; _t = 0; }
                    return;
                case 3:
                    _t++;
                    if (_t == 60) Game.Audio.PlaySe("Battle throw");
                    if (_t == 110) Game.Audio.PlaySe("Battle recall");
                    if (_t >= 150)
                    {
                        _phase = 4;
                        Game.Audio.StopBgm();
                        Game.Audio.PlayMe("Evolution success");
                        Game.Audio.PlayCry(_got);
                        Game.Message.Show(_partner + " sent over " + _got.Name(p) + ".", null, -1, _ =>
                            Game.Message.Show("Take good care of " + _got.Name(p) + "!", null, -1, __ =>
                            {
                                Close();
                                Game.RestoreMapBgm();
                                if (_done != null) _done();
                            }));
                    }
                    return;
            }
        }

        public override void Draw(Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(96, 152, 216), new Color(200, 232, 248));
            // Moving stripes
            for (int i = 0; i < 8; i++)
                g.FillRect(0, (i * 64 + Frame) % (g.Height + 64) - 32, g.Width, 6, new Color(255, 255, 255, 40));
            int cx = g.Width / 2, bottom = g.Height / 2 + 72, ballY = bottom - 70;
            switch (_phase)
            {
                case 0:
                case 1:
                    Ui.PokemonFront(g, Game, _sent, cx, bottom, 192);
                    break;
                case 2:
                    if (_t < 40)
                    {
                        // Shrinking into the ball in a white glow
                        int size = 192 - _t * 192 / 40;
                        if (size > 8) Ui.PokemonFront(g, Game, _sent, cx, bottom - (192 - size) / 3, size);
                        g.FillRect(0, 0, g.Width, g.Height, new Color(255, 255, 255, (byte)(_t * 4)));
                    }
                    else
                    {
                        int y = ballY - (_t - 40) * (_t - 40) / 12;
                        Ui.ItemIcon(g, Game, _sent.Ball ?? "POKEBALL", cx - 24, y - 24);
                    }
                    break;
                case 3:
                    if (_t >= 60 && _t < 110)
                    {
                        int k = 110 - _t;
                        Ui.ItemIcon(g, Game, _got.Ball ?? "POKEBALL", cx - 24, ballY - k * k / 12 - 24);
                    }
                    else if (_t >= 110)
                    {
                        int size = Math.Min(192, (_t - 110) * 192 / 30);
                        if (size > 8) Ui.PokemonFront(g, Game, _got, cx, bottom - (192 - size) / 3, size);
                        int a = Math.Max(0, 200 - (_t - 110) * 8);
                        if (a > 0) g.FillRect(0, 0, g.Width, g.Height, new Color(255, 255, 255, (byte)a));
                    }
                    break;
                default:
                    Ui.PokemonFront(g, Game, _got, cx, bottom, 192);
                    break;
            }
        }
    }
}
