using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Projects;

namespace PokeCreator.Pkmn
{
    /// <summary>What a Pokémon's surroundings are when checking an evolution.</summary>
    public class EvolutionContext
    {
        public List<Pokemon> Party = new List<Pokemon>();
        public int MapId;
        public DateTime Now = DateTime.Now;
        public bool Raining, Snowing, Sandstorm, Sunny, Surfing, Cycling;
    }

    /// <summary>Levelling up, learning moves and evolving (the parts that don't need a battle).</summary>
    public static class Growth
    {
        public const int HappinessToEvolve = 220;

        public static bool IsDay(DateTime t) { return t.Hour >= 6 && t.Hour < 20; }
        public static bool IsNight(DateTime t) { return !IsDay(t); }
        public static bool IsMorning(DateTime t) { return t.Hour >= 5 && t.Hour < 10; }
        public static bool IsAfternoon(DateTime t) { return t.Hour >= 14 && t.Hour < 17; }
        public static bool IsEvening(DateTime t) { return t.Hour >= 17 && t.Hour < 20; }

        /// <summary>Raises the level by one. Returns the moves learnable at the new level (not yet learned).</summary>
        public static List<string> LevelUp(Project p, Pokemon pk)
        {
            if (pk.Level >= GrowthRates.MaxLevel) return new List<string>();
            int oldMax = pk.TotalHP(p);
            pk.Level++;
            var d = pk.Data(p);
            pk.Exp = Math.Max(pk.Exp, GrowthRates.ExpForLevel(d != null ? d.GrowthRate : "Medium", pk.Level));
            if (!pk.Fainted) pk.HP = Math.Min(pk.TotalHP(p), pk.HP + (pk.TotalHP(p) - oldMax));
            return MovesAtLevel(p, pk, pk.Level);
        }

        /// <summary>Adds experience; returns how many levels were gained (the caller learns moves per level).</summary>
        public static int AddExp(Project p, Pokemon pk, int exp, List<string> movesToLearn)
        {
            var d = pk.Data(p);
            string rate = d != null ? d.GrowthRate : "Medium";
            int maxExp = GrowthRates.ExpForLevel(rate, GrowthRates.MaxLevel);
            pk.Exp = Math.Min(maxExp, pk.Exp + Math.Max(0, exp));
            int levels = 0;
            while (pk.Level < GrowthRates.MaxLevel && pk.Exp >= GrowthRates.ExpForLevel(rate, pk.Level + 1))
            {
                int exp0 = pk.Exp;
                movesToLearn.AddRange(LevelUp(p, pk));
                pk.Exp = exp0;
                levels++;
            }
            return levels;
        }

        /// <summary>Level-up moves at exactly this level that the Pokémon doesn't know.</summary>
        public static List<string> MovesAtLevel(Project p, Pokemon pk, int level)
        {
            var d = pk.Data(p);
            if (d == null) return new List<string>();
            return d.Moves.Where(m => m.Level == level).Select(m => m.Move).Distinct().Where(m => !pk.Moves.Any(k => k.Id == m)).ToList();
        }

        /// <summary>Learns a move if there's a free slot. Returns false if it already knows four (the caller asks what to forget).</summary>
        public static bool TryLearn(Project p, Pokemon pk, string move)
        {
            if (pk.Moves.Any(m => m.Id == move)) return true;
            if (pk.Moves.Count >= 4) return false;
            var md = p.Moves.FirstOrDefault(x => x.Id == move);
            pk.Moves.Add(new PokemonMove(move, md != null ? md.TotalPP : 10));
            return true;
        }

        public static void Replace(Project p, Pokemon pk, int slot, string move)
        {
            var md = p.Moves.FirstOrDefault(x => x.Id == move);
            pk.Moves[slot] = new PokemonMove(move, md != null ? md.TotalPP : 10);
        }

        public static bool CanLearnFromItem(Project p, Pokemon pk, string move)
        {
            var d = pk.Data(p);
            if (d == null || pk.IsEgg) return false;
            return d.TutorMoves.Contains(move) || d.Moves.Any(m => m.Move == move) || d.EggMoves.Contains(move);
        }

        // ------------------------------------------------------------------ evolution
        /// <summary>The species this Pokémon evolves into after levelling up (or null).</summary>
        public static string CheckLevelEvolution(Project p, Pokemon pk, EvolutionContext ctx)
        {
            return Check(p, pk, ctx, null, false);
        }

        /// <summary>The species this Pokémon evolves into when this item is used on it (or null).</summary>
        public static string CheckItemEvolution(Project p, Pokemon pk, string item, EvolutionContext ctx)
        {
            return Check(p, pk, ctx, item, true);
        }

        /// <summary>The species this Pokémon evolves into after being traded for another (or null). Uses up a TradeItem.</summary>
        public static string CheckTradeEvolution(Project p, Pokemon pk, Pokemon tradedFor)
        {
            var d = pk.Data(p);
            if (d == null || pk.IsEgg || pk.Item == "EVERSTONE") return null;
            foreach (var evo in d.Evolutions)
            {
                if (string.IsNullOrEmpty(evo.Species) || p.Species.All(s => s.Id != evo.Species)) continue;
                switch (evo.Method ?? "")
                {
                    case "Trade": return evo.Species;
                    case "TradeMale": if (pk.Gender == "M") return evo.Species; break;
                    case "TradeFemale": if (pk.Gender == "F") return evo.Species; break;
                    case "TradeItem": if (pk.Item == evo.Parameter) { pk.Item = null; return evo.Species; } break;
                    case "TradeSpecies": if (tradedFor != null && tradedFor.Species == evo.Parameter) return evo.Species; break;
                }
            }
            return null;
        }

        static string Check(Project p, Pokemon pk, EvolutionContext ctx, string item, bool byItem)
        {
            var d = pk.Data(p);
            if (d == null || pk.IsEgg || pk.Item == "EVERSTONE" && !byItem) return null;
            foreach (var evo in d.Evolutions)
            {
                if (string.IsNullOrEmpty(evo.Species) || p.Species.All(s => s.Id != evo.Species)) continue;
                if (Matches(p, pk, evo, ctx, item, byItem)) return evo.Species;
            }
            return null;
        }

        static int ParamInt(Evolution e) { int n; return int.TryParse(e.Parameter, out n) ? n : 0; }

        static bool Matches(Project p, Pokemon pk, Evolution e, EvolutionContext ctx, string item, bool byItem)
        {
            string m = e.Method ?? "";
            var now = ctx.Now;
            if (byItem)
            {
                if (!m.StartsWith("Item") || e.Parameter != item) return false;
                switch (m)
                {
                    case "Item": return true;
                    case "ItemMale": return pk.Gender == "M";
                    case "ItemFemale": return pk.Gender == "F";
                    case "ItemDay": return IsDay(now);
                    case "ItemNight": return IsNight(now);
                    case "ItemHappiness": return pk.Happiness >= HappinessToEvolve;
                    default: return false;
                }
            }
            int lv = ParamInt(e);
            var stats = pk.Stats(p);
            switch (m)
            {
                case "Level": return pk.Level >= lv;
                case "LevelMale": return pk.Level >= lv && pk.Gender == "M";
                case "LevelFemale": return pk.Level >= lv && pk.Gender == "F";
                case "LevelDay": return pk.Level >= lv && IsDay(now);
                case "LevelNight": return pk.Level >= lv && IsNight(now);
                case "LevelMorning": return pk.Level >= lv && IsMorning(now);
                case "LevelAfternoon": return pk.Level >= lv && IsAfternoon(now);
                case "LevelEvening": return pk.Level >= lv && IsEvening(now);
                case "LevelNoWeather": return pk.Level >= lv && !ctx.Raining && !ctx.Snowing && !ctx.Sandstorm && !ctx.Sunny;
                case "LevelSun": return pk.Level >= lv && ctx.Sunny;
                case "LevelRain": return pk.Level >= lv && ctx.Raining;
                case "LevelSnow": return pk.Level >= lv && ctx.Snowing;
                case "LevelSandstorm": return pk.Level >= lv && ctx.Sandstorm;
                case "LevelCycling": return pk.Level >= lv && ctx.Cycling;
                case "LevelSurfing": return pk.Level >= lv && ctx.Surfing;
                case "LevelDarkInParty":
                    return pk.Level >= lv && ctx.Party.Any(o => o != pk && !o.IsEgg && o.Types(p).Contains("DARK"));
                case "AttackGreater": return pk.Level >= lv && stats[Stat.Attack] > stats[Stat.Defense];
                case "AtkDefEqual": return pk.Level >= lv && stats[Stat.Attack] == stats[Stat.Defense];
                case "DefenseGreater": return pk.Level >= lv && stats[Stat.Attack] < stats[Stat.Defense];
                case "Silcoon": return pk.Level >= lv && PersonalValue(pk) % 2 == 0;
                case "Cascoon": return pk.Level >= lv && PersonalValue(pk) % 2 == 1;
                case "Ninjask": return pk.Level >= lv;
                case "Happiness": return pk.Happiness >= HappinessToEvolve;
                case "HappinessMale": return pk.Happiness >= HappinessToEvolve && pk.Gender == "M";
                case "HappinessFemale": return pk.Happiness >= HappinessToEvolve && pk.Gender == "F";
                case "HappinessDay": return pk.Happiness >= HappinessToEvolve && IsDay(now);
                case "HappinessNight": return pk.Happiness >= HappinessToEvolve && IsNight(now);
                case "HappinessMove": return pk.Happiness >= HappinessToEvolve && pk.Moves.Any(x => x.Id == e.Parameter);
                case "HappinessMoveType": return pk.Happiness >= HappinessToEvolve && pk.Moves.Any(x => MoveType(p, x.Id) == e.Parameter);
                case "HappinessHoldItem": return pk.Happiness >= HappinessToEvolve && pk.Item == e.Parameter;
                case "MaxHappiness": return pk.Happiness >= 255;
                case "HoldItem": return pk.Item == e.Parameter;
                case "HoldItemMale": return pk.Item == e.Parameter && pk.Gender == "M";
                case "HoldItemFemale": return pk.Item == e.Parameter && pk.Gender == "F";
                case "DayHoldItem": return pk.Item == e.Parameter && IsDay(now);
                case "NightHoldItem": return pk.Item == e.Parameter && IsNight(now);
                case "HoldItemHappiness": return pk.Item == e.Parameter && pk.Happiness >= HappinessToEvolve;
                case "HasMove": return pk.Moves.Any(x => x.Id == e.Parameter);
                case "HasMoveType": return pk.Moves.Any(x => MoveType(p, x.Id) == e.Parameter);
                case "HasInParty": return ctx.Party.Any(o => o != pk && !o.IsEgg && o.Species == e.Parameter);
                case "Location": return ctx.MapId == lv;
                default: return false; // trades, battle-only and event methods are handled elsewhere
            }
        }

        static string MoveType(Project p, string move) { var md = p.Moves.FirstOrDefault(x => x.Id == move); return md != null ? md.Type : null; }

        /// <summary>A stable stand-in for the hidden personality value (decides Silcoon/Cascoon).</summary>
        static int PersonalValue(Pokemon pk) { int v = pk.OTId * 31 + (pk.Nature ?? "").GetHashCode(); foreach (int iv in pk.IV) v = v * 7 + iv; return Math.Abs(v); }

        /// <summary>
        /// Turns the Pokémon into another species. Keeps nickname, level, damage and form when the new species has it.
        /// Returns the moves it can learn on evolving (level 0 "evolution" moves and moves at its current level).
        /// </summary>
        public static List<string> Evolve(Project p, Pokemon pk, string newSpecies)
        {
            int oldMax = pk.TotalHP(p), damage = oldMax - pk.HP;
            string oldSpecies = pk.Species;
            pk.Species = newSpecies;
            if (pk.Form > 0 && !p.Species.Any(s => s.Id == newSpecies + "_" + pk.Form)) pk.Form = 0;
            if (pk.Data(p) == null) pk.Form = 0;
            pk.HP = Math.Max(pk.Fainted ? 0 : 1, pk.TotalHP(p) - damage);
            if (pk.HP > pk.TotalHP(p)) pk.HP = pk.TotalHP(p);
            var d = pk.Data(p);
            var moves = new List<string>();
            if (d != null)
                foreach (var lm in d.Moves.Where(m => m.Level == 0 || m.Level == pk.Level))
                    if (!moves.Contains(lm.Move) && !pk.Moves.Any(k => k.Id == lm.Move)) moves.Add(lm.Move);
            return moves;
        }

        /// <summary>Ninjask's evolution also leaves a Shedinja if there's room and a Poké Ball (the caller pays the ball).</summary>
        public static bool MakesShedinja(Project p, string oldSpecies, string newSpecies)
        {
            var d = p.Species.FirstOrDefault(s => s.Id == oldSpecies && s.Form == 0);
            return d != null && d.Evolutions.Any(e => e.Method == "Ninjask" && e.Species == newSpecies) && d.Evolutions.Any(e => e.Method == "Shedinja");
        }

        public static string ShedinjaSpecies(Project p, string oldSpecies)
        {
            var d = p.Species.FirstOrDefault(s => s.Id == oldSpecies && s.Form == 0);
            var e = d == null ? null : d.Evolutions.FirstOrDefault(x => x.Method == "Shedinja");
            return e == null ? null : e.Species;
        }
    }
}
