using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Projects;

namespace PokeCreator.Pkmn
{
    /// <summary>What using an item outside battle does.</summary>
    public enum ItemKind { None, HealHP, FullRestore, CureStatus, Revive, RestorePP, PPUp, RareCandy, ExpCandy, EVBoost, Mint, Repel, Evolution, TeachMove, AbilityCapsule, AbilityPatch, BottleCap, GoldBottleCap }

    public class ItemInfo
    {
        public ItemKind Kind;
        public int Amount;          // HP / PP / EXP / EV / steps
        public bool Fraction;       // Amount is a percentage of max HP
        public string Cures;        // status it cures, "ALL" for every status
        public bool AllMoves;       // Elixirs restore every move
        public int Stat = -1;       // vitamins
        public string Nature;       // mints
        public string Move;         // TMs / HMs
        public bool Consumed = true;
        public bool NeedsPokemon { get { return Kind != ItemKind.Repel && Kind != ItemKind.None; } }
        public bool NeedsMove { get { return (Kind == ItemKind.RestorePP && !AllMoves) || Kind == ItemKind.PPUp; } }
        /// <summary>Bottle Cap: the player chooses which stat to train (passed as the move slot).</summary>
        public bool NeedsStat { get { return Kind == ItemKind.BottleCap; } }
    }

    /// <summary>The result of using an item on a Pokémon.</summary>
    public class ItemOutcome
    {
        public bool Used;
        public string Message;                  // "Bulbasaur's HP was restored by 20 points."
        public int LevelsGained;
        public List<string> MovesToLearn = new List<string>();
        public string EvolveTo;
        public string TeachMove;
    }

    /// <summary>
    /// Out-of-battle item effects for the standard Essentials items (Potions, status heals, Revives, Ethers,
    /// Rare Candy, Exp. Candy, vitamins, feathers, mints, Repels, evolution items, TMs).
    /// </summary>
    public static class ItemEffects
    {
        static readonly Dictionary<string, int> HpItems = new Dictionary<string, int>
        {
            { "POTION", 20 }, { "SUPERPOTION", 60 }, { "HYPERPOTION", 120 }, { "MAXPOTION", 9999 }, { "FRESHWATER", 30 }, { "SODAPOP", 50 },
            { "LEMONADE", 70 }, { "MOOMOOMILK", 100 }, { "ENERGYPOWDER", 60 }, { "ENERGYROOT", 120 }, { "BERRYJUICE", 20 }, { "ORANBERRY", 10 },
            { "RAGECANDYBAR", 20 }, { "SWEETHEART", 20 },
        };
        static readonly Dictionary<string, string> StatusItems = new Dictionary<string, string>
        {
            { "ANTIDOTE", "POISON" }, { "PECHABERRY", "POISON" }, { "BURNHEAL", "BURN" }, { "RAWSTBERRY", "BURN" }, { "ICEHEAL", "FROZEN" },
            { "ASPEARBERRY", "FROZEN" }, { "AWAKENING", "SLEEP" }, { "CHESTOBERRY", "SLEEP" }, { "BLUEFLUTE", "SLEEP" }, { "PARALYZEHEAL", "PARALYSIS" },
            { "CHERIBERRY", "PARALYSIS" }, { "FULLHEAL", "ALL" }, { "HEALPOWDER", "ALL" }, { "LAVACOOKIE", "ALL" }, { "OLDGATEAU", "ALL" },
            { "CASTELIACONE", "ALL" }, { "LUMIOSEGALETTE", "ALL" }, { "SHALOURSABLE", "ALL" }, { "BIGMALASADA", "ALL" }, { "PEWTERCRUNCHIES", "ALL" },
            { "LUMBERRY", "ALL" },
        };
        static readonly Dictionary<string, int> ExpCandies = new Dictionary<string, int>
        { { "EXPCANDYXS", 100 }, { "EXPCANDYS", 800 }, { "EXPCANDYM", 3000 }, { "EXPCANDYL", 10000 }, { "EXPCANDYXL", 30000 } };
        static readonly Dictionary<string, int> Vitamins = new Dictionary<string, int>
        { { "HPUP", Stat.HP }, { "PROTEIN", Stat.Attack }, { "IRON", Stat.Defense }, { "CALCIUM", Stat.SpAtk }, { "ZINC", Stat.SpDef }, { "CARBOS", Stat.Speed } };
        static readonly Dictionary<string, int> Feathers = new Dictionary<string, int>
        { { "HEALTHFEATHER", Stat.HP }, { "MUSCLEFEATHER", Stat.Attack }, { "RESISTFEATHER", Stat.Defense }, { "GENIUSFEATHER", Stat.SpAtk }, { "CLEVERFEATHER", Stat.SpDef }, { "SWIFTFEATHER", Stat.Speed } };
        static readonly Dictionary<string, int> Repels = new Dictionary<string, int> { { "REPEL", 100 }, { "SUPERREPEL", 200 }, { "MAXREPEL", 250 } };

        public const int MaxEV = 252, MaxTotalEV = 510;

        /// <summary>What the item does outside battle (Kind None if it can't be used there).</summary>
        public static ItemInfo Info(Project p, ItemData item)
        {
            var info = new ItemInfo();
            if (item == null) return info;
            string id = item.Id;
            int n; string s;
            if (HpItems.TryGetValue(id, out n)) { info.Kind = ItemKind.HealHP; info.Amount = n; }
            else if (id == "SITRUSBERRY") { info.Kind = ItemKind.HealHP; info.Amount = 25; info.Fraction = true; }
            else if (id == "FULLRESTORE") info.Kind = ItemKind.FullRestore;
            else if (StatusItems.TryGetValue(id, out s)) { info.Kind = ItemKind.CureStatus; info.Cures = s; }
            else if (id == "REVIVE") { info.Kind = ItemKind.Revive; info.Amount = 50; }
            else if (id == "MAXREVIVE" || id == "REVIVALHERB") { info.Kind = ItemKind.Revive; info.Amount = 100; }
            else if (id == "ETHER" || id == "LEPPABERRY") { info.Kind = ItemKind.RestorePP; info.Amount = 10; }
            else if (id == "MAXETHER") { info.Kind = ItemKind.RestorePP; info.Amount = 999; }
            else if (id == "ELIXIR") { info.Kind = ItemKind.RestorePP; info.Amount = 10; info.AllMoves = true; }
            else if (id == "MAXELIXIR") { info.Kind = ItemKind.RestorePP; info.Amount = 999; info.AllMoves = true; }
            else if (id == "PPUP") { info.Kind = ItemKind.PPUp; info.Amount = 1; }
            else if (id == "PPMAX") { info.Kind = ItemKind.PPUp; info.Amount = 3; }
            else if (id == "RARECANDY") info.Kind = ItemKind.RareCandy;
            else if (id == "ABILITYCAPSULE") info.Kind = ItemKind.AbilityCapsule;
            else if (id == "ABILITYPATCH") info.Kind = ItemKind.AbilityPatch;
            else if (id == "BOTTLECAP") info.Kind = ItemKind.BottleCap;
            else if (id == "GOLDBOTTLECAP") info.Kind = ItemKind.GoldBottleCap;
            else if (ExpCandies.TryGetValue(id, out n)) { info.Kind = ItemKind.ExpCandy; info.Amount = n; }
            else if (Vitamins.TryGetValue(id, out n)) { info.Kind = ItemKind.EVBoost; info.Stat = n; info.Amount = 10; }
            else if (Feathers.TryGetValue(id, out n)) { info.Kind = ItemKind.EVBoost; info.Stat = n; info.Amount = 1; }
            else if (Repels.TryGetValue(id, out n)) { info.Kind = ItemKind.Repel; info.Amount = n; }
            else if (id.EndsWith("MINT") && Natures.All.Contains(id.Substring(0, id.Length - 4))) { info.Kind = ItemKind.Mint; info.Nature = id.Substring(0, id.Length - 4); }
            else if ((item.FieldUse == "TM" || item.FieldUse == "HM" || item.FieldUse == "TR") && !string.IsNullOrEmpty(item.Move))
            { info.Kind = ItemKind.TeachMove; info.Move = item.Move; info.Consumed = item.FieldUse == "TR"; }
            else if (p.Species.Any(sp => sp.Evolutions.Any(e => e.Method != null && e.Method.StartsWith("Item") && e.Parameter == id))) info.Kind = ItemKind.Evolution;
            if (item.Consumable == false) info.Consumed = false;
            return info;
        }

        /// <summary>
        /// Uses the item on a Pokémon. moveSlot is the chosen move for Ethers / PP Ups.
        /// Level-ups, move learning and evolution are returned for the caller to show.
        /// </summary>
        public static ItemOutcome Use(Project p, Pokemon pk, ItemData item, ItemInfo info, int moveSlot, EvolutionContext ctx)
        {
            var o = new ItemOutcome();
            string name = pk.Name(p);
            if (pk.IsEgg) { o.Message = "It won't have any effect."; return o; }
            int max = pk.TotalHP(p);
            switch (info.Kind)
            {
                case ItemKind.HealHP:
                    {
                        if (pk.Fainted || pk.HP >= max) break;
                        int amount = info.Fraction ? Math.Max(1, max * info.Amount / 100) : info.Amount;
                        int before = pk.HP; pk.HP = Math.Min(max, pk.HP + amount);
                        o.Used = true; o.Message = pk.HP >= max && amount >= 999 ? name + "'s HP was restored." : name + "'s HP was restored by " + (pk.HP - before) + " points.";
                        break;
                    }
                case ItemKind.FullRestore:
                    {
                        if (pk.Fainted || (pk.HP >= max && string.IsNullOrEmpty(pk.Status))) break;
                        pk.HP = max; pk.Status = "";
                        o.Used = true; o.Message = name + "'s HP was restored.";
                        break;
                    }
                case ItemKind.CureStatus:
                    {
                        string st = (pk.Status ?? "").ToUpperInvariant();
                        if (pk.Fainted || st.Length == 0) break;
                        bool cures = info.Cures == "ALL" || st == info.Cures || (info.Cures == "POISON" && st == "TOXIC");
                        if (!cures) break;
                        pk.Status = "";
                        o.Used = true; o.Message = name + CureText(st);
                        break;
                    }
                case ItemKind.Revive:
                    {
                        if (!pk.Fainted) break;
                        pk.HP = Math.Max(1, max * info.Amount / 100); pk.Status = "";
                        o.Used = true; o.Message = name + "'s HP was restored.";
                        break;
                    }
                case ItemKind.RestorePP:
                    {
                        bool any = false;
                        for (int i = 0; i < pk.Moves.Count; i++)
                        {
                            if (!info.AllMoves && i != moveSlot) continue;
                            var m = pk.Moves[i];
                            var md = p.Moves.FirstOrDefault(x => x.Id == m.Id);
                            int total = md != null ? Pokemon.TotalPP(md, m.PPUps) : m.PP;
                            if (m.PP >= total) continue;
                            m.PP = Math.Min(total, m.PP + info.Amount); any = true;
                        }
                        if (!any) break;
                        o.Used = true; o.Message = info.AllMoves ? "PP was restored." : MoveName(p, pk.Moves[moveSlot].Id) + "'s PP was restored.";
                        break;
                    }
                case ItemKind.PPUp:
                    {
                        if (moveSlot < 0 || moveSlot >= pk.Moves.Count) break;
                        var m = pk.Moves[moveSlot];
                        var md = p.Moves.FirstOrDefault(x => x.Id == m.Id);
                        if (m.PPUps >= 3 || md == null || md.TotalPP < 5) break;
                        int oldTotal = Pokemon.TotalPP(md, m.PPUps);
                        m.PPUps = Math.Min(3, m.PPUps + info.Amount);
                        m.PP += Pokemon.TotalPP(md, m.PPUps) - oldTotal;
                        o.Used = true; o.Message = MoveName(p, m.Id) + "'s PP increased.";
                        break;
                    }
                case ItemKind.RareCandy:
                    {
                        if (pk.Level >= GrowthRates.MaxLevel) break;
                        o.MovesToLearn.AddRange(Growth.LevelUp(p, pk));
                        o.LevelsGained = 1; o.Used = true;
                        o.Message = name + " grew to Lv. " + pk.Level + "!";
                        o.EvolveTo = Growth.CheckLevelEvolution(p, pk, ctx);
                        break;
                    }
                case ItemKind.ExpCandy:
                    {
                        if (pk.Level >= GrowthRates.MaxLevel) break;
                        o.LevelsGained = Growth.AddExp(p, pk, info.Amount, o.MovesToLearn);
                        o.Used = true;
                        o.Message = name + " gained " + info.Amount + " Exp. Points!" + (o.LevelsGained > 0 ? "\\n" + name + " grew to Lv. " + pk.Level + "!" : "");
                        if (o.LevelsGained > 0) o.EvolveTo = Growth.CheckLevelEvolution(p, pk, ctx);
                        break;
                    }
                case ItemKind.EVBoost:
                    {
                        int s = info.Stat, total = pk.EV.Sum();
                        int add = Math.Min(info.Amount, Math.Min(MaxEV - pk.EV[s], MaxTotalEV - total));
                        if (add <= 0) break;
                        pk.EV[s] += add;
                        if (s == Stat.HP && !pk.Fainted) pk.HP = Math.Min(pk.TotalHP(p), pk.HP + (pk.TotalHP(p) - max));
                        pk.Happiness = Math.Min(255, pk.Happiness + (pk.Happiness < 100 ? 5 : pk.Happiness < 200 ? 3 : 2));
                        o.Used = true; o.Message = name + "'s base " + Stat.Names[s] + " increased.";
                        break;
                    }
                case ItemKind.Mint:
                    {
                        if (pk.Nature == info.Nature) break;
                        int hpDiff = max - pk.HP;
                        pk.Nature = info.Nature;
                        if (!pk.Fainted) pk.HP = Math.Max(1, pk.TotalHP(p) - hpDiff);
                        o.Used = true; o.Message = name + "'s stats may have changed due to the effects of the " + (item.Name ?? item.Id) + "!";
                        break;
                    }
                case ItemKind.AbilityCapsule:
                    {
                        // Swaps between the two regular abilities (not a hidden one)
                        var d = pk.Data(p);
                        if (d == null || d.Abilities.Count < 2 || !string.IsNullOrEmpty(pk.Ability) || d.Abilities[0] == d.Abilities[1]) break;
                        pk.AbilityIndex = pk.AbilityIndex == 0 ? 1 : 0;
                        o.Used = true; o.Message = name + "'s Ability changed! Its Ability is now " + AbilityName(p, pk.AbilityId(p)) + "!";
                        break;
                    }
                case ItemKind.AbilityPatch:
                    {
                        // Regular → hidden ability; a hidden ability goes back to the first regular one
                        var d = pk.Data(p);
                        if (d == null || d.HiddenAbilities.Count == 0) break;
                        if (!string.IsNullOrEmpty(pk.Ability) && d.HiddenAbilities.Contains(pk.Ability)) { pk.Ability = null; pk.AbilityIndex = 0; }
                        else if (d.HiddenAbilities[0] == pk.AbilityId(p)) break;
                        else pk.Ability = d.HiddenAbilities[0];
                        o.Used = true; o.Message = name + "'s Ability changed! Its Ability is now " + AbilityName(p, pk.AbilityId(p)) + "!";
                        break;
                    }
                case ItemKind.BottleCap:
                case ItemKind.GoldBottleCap:
                    {
                        // Hyper Training: IVs become 31 (from Game Settings' level, max level by default)
                        int need = p.Settings.HyperTrainingLevel > 0 ? p.Settings.HyperTrainingLevel : GrowthRates.MaxLevel;
                        if (pk.Level < need) break;
                        var stats = info.Kind == ItemKind.GoldBottleCap ? Enumerable.Range(0, 6).ToList() : new List<int> { moveSlot };
                        stats = stats.Where(s => s >= 0 && s < 6 && pk.IV[s] < 31).ToList();
                        if (stats.Count == 0) break;
                        int hpDiff = max - pk.HP;
                        foreach (int s in stats) pk.IV[s] = 31;
                        if (!pk.Fainted) pk.HP = Math.Max(1, pk.TotalHP(p) - hpDiff);
                        o.Used = true; o.Message = info.Kind == ItemKind.GoldBottleCap ? name + " became stronger through Hyper Training!" : name + "'s " + Stat.Names[stats[0]] + " became stronger through Hyper Training!";
                        break;
                    }
                case ItemKind.Evolution:
                    {
                        string to = Growth.CheckItemEvolution(p, pk, item.Id, ctx);
                        if (to == null) break;
                        o.Used = true; o.EvolveTo = to;
                        break;
                    }
                case ItemKind.TeachMove:
                    {
                        if (pk.Moves.Any(m => m.Id == info.Move)) { o.Message = name + " already knows " + MoveName(p, info.Move) + "."; return o; }
                        if (!Growth.CanLearnFromItem(p, pk, info.Move)) { o.Message = name + " can't learn " + MoveName(p, info.Move) + "."; return o; }
                        o.Used = true; o.TeachMove = info.Move;
                        break;
                    }
            }
            if (!o.Used && o.Message == null) o.Message = "It won't have any effect.";
            return o;
        }

        /// <summary>Whether the item would do anything for this Pokémon (used to show "ABLE" / "NOT ABLE" in the party).</summary>
        public static bool Works(Project p, Pokemon pk, ItemData item, ItemInfo info, EvolutionContext ctx)
        {
            if (pk.IsEgg) return false;
            if (info.Kind == ItemKind.Evolution) return Growth.CheckItemEvolution(p, pk, item.Id, ctx) != null;
            if (info.Kind == ItemKind.TeachMove) return !pk.Moves.Any(m => m.Id == info.Move) && Growth.CanLearnFromItem(p, pk, info.Move);
            return true;
        }

        static string CureText(string st)
        {
            switch (st)
            {
                case "POISON": case "TOXIC": return " was cured of its poisoning.";
                case "BURN": return "'s burn was healed.";
                case "FROZEN": return " was thawed out.";
                case "SLEEP": return " woke up.";
                case "PARALYSIS": return " was cured of paralysis.";
                default: return " became healthy.";
            }
        }

        public static string AbilityName(Project p, string id) { var a = p.Abilities.FirstOrDefault(x => x.Id == id); return a != null && !string.IsNullOrEmpty(a.Name) ? a.Name : id; }
        public static string MoveName(Project p, string id) { var md = p.Moves.FirstOrDefault(x => x.Id == id); return md != null && !string.IsNullOrEmpty(md.Name) ? md.Name : id; }
    }
}
