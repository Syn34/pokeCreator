using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Json;
using PokeCreator.Projects;

namespace PokeCreator.Pkmn
{
    public static class Stat
    {
        // Same order as SpeciesData.BaseStats (Essentials order)
        public const int HP = 0, Attack = 1, Defense = 2, Speed = 3, SpAtk = 4, SpDef = 5;
        public static readonly string[] Names = { "HP", "Attack", "Defense", "Speed", "Sp. Atk", "Sp. Def" };
        public static readonly string[] Ids = { "HP", "ATTACK", "DEFENSE", "SPEED", "SPECIAL_ATTACK", "SPECIAL_DEFENSE" };
        public static int IndexOf(string id) { int i = Array.IndexOf(Ids, (id ?? "").ToUpperInvariant()); return i; }
    }

    public static class Natures
    {
        public static readonly string[] All = { "HARDY", "LONELY", "BRAVE", "ADAMANT", "NAUGHTY", "BOLD", "DOCILE", "RELAXED", "IMPISH", "LAX", "TIMID", "HASTY", "SERIOUS", "JOLLY", "NAIVE", "MODEST", "MILD", "QUIET", "BASHFUL", "RASH", "CALM", "GENTLE", "SASSY", "CAREFUL", "QUIRKY" };
        // Raised / lowered stat for each nature, in the classic 5x5 grid: Attack, Defense, Speed, Sp.Atk, Sp.Def
        static readonly int[] Grid = { Stat.Attack, Stat.Defense, Stat.Speed, Stat.SpAtk, Stat.SpDef };

        public static int Raised(string nature) { int i = Array.IndexOf(All, (nature ?? "").ToUpperInvariant()); return i < 0 || i / 5 == i % 5 ? -1 : Grid[i / 5]; }
        public static int Lowered(string nature) { int i = Array.IndexOf(All, (nature ?? "").ToUpperInvariant()); return i < 0 || i / 5 == i % 5 ? -1 : Grid[i % 5]; }

        /// <summary>Multiplier x10 (11 = +10%, 9 = -10%).</summary>
        public static int Modifier(string nature, int stat)
        {
            if (Raised(nature) == stat) return 11;
            if (Lowered(nature) == stat) return 9;
            return 10;
        }

        public static string Display(string id) { return string.IsNullOrEmpty(id) ? "" : id.Substring(0, 1) + id.Substring(1).ToLowerInvariant(); }
    }

    public static class GrowthRates
    {
        public const int MaxLevel = 100;

        public static int ExpForLevel(string rate, int n)
        {
            if (n <= 1) return 0;
            if (n > MaxLevel) n = MaxLevel;
            double c = (double)n * n * n;
            double e;
            switch ((rate ?? "Medium").ToLowerInvariant())
            {
                case "erratic":
                    if (n <= 50) e = c * (100 - n) / 50;
                    else if (n <= 68) e = c * (150 - n) / 100;
                    else if (n <= 98) e = c * Math.Floor((1911 - 10 * n) / 3.0) / 500;
                    else e = c * (160 - n) / 100;
                    break;
                case "fluctuating":
                    if (n <= 15) e = c * (Math.Floor((n + 1) / 3.0) + 24) / 50;
                    else if (n <= 36) e = c * (n + 14) / 50;
                    else e = c * (Math.Floor(n / 2.0) + 32) / 50;
                    break;
                case "parabolic": case "mediumslow": e = 1.2 * c - 15.0 * n * n + 100 * n - 140; break;
                case "fast": e = 0.8 * c; break;
                case "slow": e = 1.25 * c; break;
                default: e = c; break; // Medium (Medium Fast)
            }
            return Math.Max(0, (int)Math.Floor(e));
        }

        public static int LevelForExp(string rate, int exp)
        {
            int lv = 1;
            while (lv < MaxLevel && ExpForLevel(rate, lv + 1) <= exp) lv++;
            return lv;
        }
    }

    public class PokemonMove
    {
        public string Id { get; set; }
        public int PP { get; set; }
        public int PPUps { get; set; }
        public PokemonMove() { }
        public PokemonMove(string id, int pp) { Id = id; PP = pp; }
    }

    /// <summary>An individual Pokémon (in the party, a PC box, or a trainer's team). Saved as JSON.</summary>
    public class Pokemon
    {
        public string Species { get; set; }
        public int Form { get; set; }
        public string Nickname { get; set; }
        public int Level { get; set; }
        public int Exp { get; set; }
        public string Nature { get; set; }
        [JsonCompact] public List<int> IV { get; set; }
        [JsonCompact] public List<int> EV { get; set; }
        public int AbilityIndex { get; set; }
        /// <summary>Explicit ability (overrides AbilityIndex), e.g. a hidden ability.</summary>
        public string Ability { get; set; }
        /// <summary>"M", "F" or "" (genderless).</summary>
        public string Gender { get; set; }
        public bool Shiny { get; set; }
        public string Item { get; set; }
        public List<PokemonMove> Moves { get; set; }
        public int HP { get; set; }
        /// <summary>"" or BURN, POISON, TOXIC, PARALYSIS, SLEEP, FROZEN.</summary>
        public string Status { get; set; }
        public int Happiness { get; set; }
        public string Ball { get; set; }
        public string OTName { get; set; }
        public int OTId { get; set; }
        public int MetMap { get; set; }
        public int MetLevel { get; set; }
        public bool IsEgg { get; set; }
        /// <summary>Steps left before an Egg hatches.</summary>
        public int EggSteps { get; set; }

        public Pokemon()
        {
            IV = new List<int> { 0, 0, 0, 0, 0, 0 }; EV = new List<int> { 0, 0, 0, 0, 0, 0 };
            Moves = new List<PokemonMove>(); Level = 1; Nature = "HARDY"; Gender = ""; Status = ""; Ball = "POKEBALL"; Happiness = 70;
        }

        // ------------------------------------------------------------------ data lookups
        public SpeciesData Data(Project p)
        {
            string id = Form > 0 ? Species + "_" + Form : Species;
            return p.Species.FirstOrDefault(s => s.Id == id) ?? p.Species.FirstOrDefault(s => s.Id == Species);
        }

        public string Name(Project p)
        {
            if (IsEgg) return "Egg";
            if (!string.IsNullOrEmpty(Nickname)) return Nickname;
            var d = Data(p);
            return d != null && !string.IsNullOrEmpty(d.Name) ? d.Name : Species;
        }

        public string AbilityId(Project p)
        {
            if (!string.IsNullOrEmpty(Ability)) return Ability;
            var d = Data(p);
            if (d == null || d.Abilities.Count == 0) return null;
            return d.Abilities[Math.Min(AbilityIndex, d.Abilities.Count - 1)];
        }

        public List<string> Types(Project p) { var d = Data(p); return d == null ? new List<string>() : d.Types; }

        // ------------------------------------------------------------------ stats
        public int[] Stats(Project p)
        {
            var d = Data(p);
            var result = new int[6];
            for (int s = 0; s < 6; s++)
            {
                int b = d != null && s < d.BaseStats.Count ? d.BaseStats[s] : 50;
                int iv = s < IV.Count ? IV[s] : 0, ev = s < EV.Count ? EV[s] : 0;
                int core = (2 * b + iv + ev / 4) * Level / 100;
                if (s == Stat.HP) result[s] = d != null && b == 1 ? 1 : core + Level + 10;   // Shedinja-style 1 HP
                else result[s] = (core + 5) * Natures.Modifier(Nature, s) / 10;
            }
            return result;
        }

        public int TotalHP(Project p) { return Stats(p)[Stat.HP]; }
        public bool Fainted { get { return HP <= 0 && !IsEgg; } }

        public void Heal(Project p)
        {
            HP = TotalHP(p);
            Status = "";
            foreach (var m in Moves)
            {
                var md = p.Moves.FirstOrDefault(x => x.Id == m.Id);
                if (md != null) m.PP = TotalPP(md, m.PPUps);
            }
        }

        public static int TotalPP(MoveData md, int ups) { return md.TotalPP + md.TotalPP * ups / 5; }

        public int ExpForNextLevel(Project p) { var d = Data(p); return GrowthRates.ExpForLevel(d != null ? d.GrowthRate : "Medium", Level + 1); }
        public int ExpForThisLevel(Project p) { var d = Data(p); return GrowthRates.ExpForLevel(d != null ? d.GrowthRate : "Medium", Level); }

        /// <summary>Moves known at this level: the last four distinct level-up moves.</summary>
        public void ResetMoves(Project p)
        {
            var d = Data(p);
            Moves.Clear();
            if (d == null) return;
            var known = new List<string>();
            foreach (var lm in d.Moves.Where(m => m.Level <= Level))
            {
                known.Remove(lm.Move);
                known.Add(lm.Move);
            }
            foreach (var id in known.Skip(Math.Max(0, known.Count - 4)))
            {
                var md = p.Moves.FirstOrDefault(x => x.Id == id);
                Moves.Add(new PokemonMove(id, md != null ? md.TotalPP : 10));
            }
        }

        // ------------------------------------------------------------------ creation
        static int FemaleChance(string ratio)
        {
            switch (ratio)
            {
                case "AlwaysMale": return 0;
                case "FemaleOneEighth": return 32;
                case "Female25Percent": return 64;
                case "Female75Percent": return 192;
                case "FemaleSevenEighths": return 224;
                case "AlwaysFemale": return 256;
                case "Genderless": return -1;
                default: return 128;
            }
        }

        /// <summary>Creates a wild-style Pokémon: random IVs, nature, gender, ability; level-up moves; full HP.</summary>
        public static Pokemon Create(Project p, string species, int level, Random rng)
        {
            var pk = new Pokemon { Species = (species ?? "").ToUpperInvariant(), Level = Math.Max(1, Math.Min(GrowthRates.MaxLevel, level)) };
            int us = pk.Species.IndexOf('_');
            int form;
            if (us > 0 && int.TryParse(pk.Species.Substring(us + 1), out form)) { pk.Form = form; pk.Species = pk.Species.Substring(0, us); }
            var d = pk.Data(p);
            for (int s = 0; s < 6; s++) pk.IV[s] = rng.Next(32);
            pk.Nature = Natures.All[rng.Next(Natures.All.Length)];
            int fc = FemaleChance(d != null ? d.GenderRatio : "Female50Percent");
            pk.Gender = fc < 0 ? "" : rng.Next(256) < fc ? "F" : "M";
            pk.AbilityIndex = d != null && d.Abilities.Count > 1 ? rng.Next(d.Abilities.Count) : 0;
            pk.Shiny = rng.Next(4096) == 0;
            pk.Happiness = d != null ? d.Happiness : 70;
            pk.Exp = GrowthRates.ExpForLevel(d != null ? d.GrowthRate : "Medium", pk.Level);
            pk.MetLevel = pk.Level;
            pk.ResetMoves(p);
            pk.HP = pk.TotalHP(p);
            return pk;
        }

        /// <summary>Creates a trainer's Pokémon from trainers.json, applying its fixed settings.</summary>
        public static Pokemon FromTrainer(Project p, TrainerPokemon tp, Random rng)
        {
            var pk = Create(p, tp.Species, tp.Level, rng);
            pk.Form = tp.Form;
            if (!string.IsNullOrEmpty(tp.Name)) pk.Nickname = tp.Name;
            if (!string.IsNullOrEmpty(tp.Nature)) pk.Nature = tp.Nature.ToUpperInvariant();
            if (!string.IsNullOrEmpty(tp.Gender)) pk.Gender = tp.Gender.StartsWith("f", StringComparison.OrdinalIgnoreCase) ? "F" : "M";
            if (!string.IsNullOrEmpty(tp.Ability)) pk.Ability = tp.Ability;
            if (tp.AbilityIndex.HasValue) pk.AbilityIndex = tp.AbilityIndex.Value;
            if (!string.IsNullOrEmpty(tp.Item)) pk.Item = tp.Item;
            if (tp.IV.Count == 6) pk.IV = new List<int>(tp.IV);
            if (tp.EV.Count == 6) pk.EV = new List<int>(tp.EV);
            if (tp.Happiness.HasValue) pk.Happiness = tp.Happiness.Value;
            pk.Shiny = tp.Shiny || tp.SuperShiny;
            if (!string.IsNullOrEmpty(tp.Ball)) pk.Ball = tp.Ball;
            if (tp.Moves.Count > 0)
            {
                pk.Moves.Clear();
                foreach (var id in tp.Moves.Take(4))
                {
                    var md = p.Moves.FirstOrDefault(x => x.Id == id);
                    pk.Moves.Add(new PokemonMove(id, md != null ? md.TotalPP : 10));
                }
            }
            pk.HP = pk.TotalHP(p);
            return pk;
        }
    }
}
