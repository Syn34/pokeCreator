using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.World
{
    /// <summary>A Pokémon left at the Day Care, with the level it had when it was left.</summary>
    public class DayCareSlot
    {
        public Pokemon Pokemon { get; set; }
        public int StartLevel { get; set; }
    }

    /// <summary>
    /// The Day Care (Essentials' DayCare class): two Pokémon gain 1 EXP per step, make Eggs if compatible,
    /// and Eggs in the party hatch after their hatch steps.
    /// </summary>
    public static class DayCare
    {
        public const int Slots = 2;
        static readonly int[] EggChance = { 0, 20, 50, 70 };

        public static int Count(Game g) { return g.State.DayCare.Count; }

        public static void Deposit(Game g, int partyIndex)
        {
            if (partyIndex < 0 || partyIndex >= g.State.Party.Count || g.State.DayCare.Count >= Slots) return;
            var pk = g.State.Party[partyIndex];
            g.State.Party.RemoveAt(partyIndex);
            pk.Status = ""; // statuses don't stay at the Day Care
            g.State.DayCare.Add(new DayCareSlot { Pokemon = pk, StartLevel = pk.Level });
        }

        public static void Withdraw(Game g, int slot)
        {
            if (slot < 0 || slot >= g.State.DayCare.Count || g.State.Party.Count >= GameState.MaxParty) return;
            var pk = g.State.DayCare[slot].Pokemon;
            g.State.DayCare.RemoveAt(slot);
            pk.Heal(g.Project);
            g.State.Party.Add(pk);
        }

        /// <summary>Withdrawal cost: $100 plus $100 per level gained.</summary>
        public static int Cost(Game g, int slot)
        {
            if (slot < 0 || slot >= g.State.DayCare.Count) return 0;
            var s = g.State.DayCare[slot];
            return (s.Pokemon.Level - s.StartLevel + 1) * 100;
        }

        public static int LevelGain(Game g, int slot)
        {
            if (slot < 0 || slot >= g.State.DayCare.Count) return 0;
            var s = g.State.DayCare[slot];
            return s.Pokemon.Level - s.StartLevel;
        }

        // ------------------------------------------------------------------ breeding
        static List<string> Groups(Game g, Pokemon pk) { var d = pk.Data(g.Project); return d != null ? d.EggGroups : new List<string>(); }

        /// <summary>0 = won't breed, 1 = 20%, 2 = 50%, 3 = 70% per 256 steps.</summary>
        public static int Compatibility(Game g)
        {
            if (g.State.DayCare.Count < 2) return 0;
            var a = g.State.DayCare[0].Pokemon; var b = g.State.DayCare[1].Pokemon;
            var ga = Groups(g, a); var gb = Groups(g, b);
            if (ga.Contains("Undiscovered") || gb.Contains("Undiscovered")) return 0;
            bool dittoA = ga.Contains("Ditto"), dittoB = gb.Contains("Ditto");
            if (dittoA && dittoB) return 0;
            if (!dittoA && !dittoB)
            {
                if (string.IsNullOrEmpty(a.Gender) || string.IsNullOrEmpty(b.Gender) || a.Gender == b.Gender) return 0;
                if (!ga.Intersect(gb).Any()) return 0;
            }
            int ret = 1;
            if (a.Species == b.Species) ret++;
            if (a.OTId != b.OTId || a.OTName != b.OTName) ret++;
            return Math.Min(3, ret);
        }

        public static string CompatibilityText(int c)
        {
            switch (c)
            {
                case 3: return "The two seem to get along very well.";
                case 2: return "The two seem to get along.";
                case 1: return "The two don't really seem to like each other very much.";
                default: return "The two prefer to play with other Pokémon more than with each other.";
            }
        }

        /// <summary>The first stage of a species' evolution line (honouring incense babies and Offspring lists).</summary>
        static string BabySpecies(Game g, string species, Pokemon mother, Pokemon father)
        {
            string cur = species;
            for (int guard = 0; guard < 8; guard++)
            {
                var prev = g.Project.Species.FirstOrDefault(s => s.Form == 0 && s.Evolutions != null && s.Evolutions.Any(e => e.Species == cur));
                if (prev == null) break;
                // Incense babies (Azurill, Wynaut...) only hatch if a parent holds the incense
                if (!string.IsNullOrEmpty(prev.Incense) && mother.Item != prev.Incense && (father == null || father.Item != prev.Incense)) break;
                cur = prev.Id;
            }
            var d = g.Project.Species.FirstOrDefault(s => s.Id == cur && s.Form == 0);
            if (d != null && d.Offspring != null && d.Offspring.Count > 0) cur = d.Offspring[g.Rng.Next(d.Offspring.Count)];
            return cur;
        }

        /// <summary>Makes the Egg from the two Pokémon at the Day Care.</summary>
        public static Pokemon MakeEgg(Game g)
        {
            if (g.State.DayCare.Count < 2) return null;
            var a = g.State.DayCare[0].Pokemon; var b = g.State.DayCare[1].Pokemon;
            bool dittoA = Groups(g, a).Contains("Ditto"), dittoB = Groups(g, b).Contains("Ditto");
            Pokemon mother, father;
            if (dittoA) { mother = b; father = a; }
            else if (dittoB) { mother = a; father = b; }
            else if (a.Gender == "F") { mother = a; father = b; }
            else { mother = b; father = a; }
            string species = BabySpecies(g, mother.Species, mother, father);
            var egg = Pokemon.Create(g.Project, species, 1, g.Rng);
            // Three random IVs from the parents (five with a Destiny Knot)
            int inherit = mother.Item == "DESTINYKNOT" || father.Item == "DESTINYKNOT" ? 5 : 3;
            var stats = Enumerable.Range(0, 6).OrderBy(x => g.Rng.Next()).Take(inherit);
            foreach (int s in stats) egg.IV[s] = (g.Rng.Next(2) == 0 ? mother : father).IV[s];
            // Everstone passes on the nature
            if (mother.Item == "EVERSTONE") egg.Nature = mother.Nature;
            else if (father.Item == "EVERSTONE") egg.Nature = father.Nature;
            // Egg moves the father (or mother) knows
            var data = egg.Data(g.Project);
            if (data != null && data.EggMoves != null)
                foreach (var m in father.Moves.Concat(mother.Moves).Select(m => m.Id).Distinct())
                    if (data.EggMoves.Contains(m) && !egg.Moves.Any(x => x.Id == m))
                    {
                        var md = g.Project.Moves.FirstOrDefault(x => x.Id == m);
                        if (md == null) continue;
                        if (egg.Moves.Count >= 4) egg.Moves.RemoveAt(0);
                        egg.Moves.Add(new PokemonMove { Id = m, PP = md.TotalPP });
                    }
            egg.Ball = mother.Ball ?? "POKEBALL";
            if (egg.Ball == "MASTERBALL" || egg.Ball == "CHERISHBALL") egg.Ball = "POKEBALL";
            // Regional and other inheritable forms come from the mother (the other parent with Ditto)
            if (mother.Form > 0 && g.Project.Species.Any(s => s.Id == egg.Species + "_" + mother.Form)) egg.Form = mother.Form;
            // Abilities: a hidden ability passes on 60% of the time, otherwise the same ability slot 80% of the time
            var momData = mother.Data(g.Project); var eggData = egg.Data(g.Project);
            bool hidden = momData != null && !string.IsNullOrEmpty(mother.Ability) && momData.HiddenAbilities != null && momData.HiddenAbilities.Contains(mother.Ability);
            if (hidden && eggData != null && eggData.HiddenAbilities != null && eggData.HiddenAbilities.Count > 0 && g.Rng.Next(100) < 60) egg.Ability = eggData.HiddenAbilities[g.Rng.Next(eggData.HiddenAbilities.Count)];
            else if (!hidden && g.Rng.Next(100) < 80) egg.AbilityIndex = mother.AbilityIndex;
            EncounterModifiers.ShinyCharm(g, egg);
            egg.IsEgg = true;
            egg.EggSteps = data != null && data.HatchSteps > 1 ? data.HatchSteps : 5120;
            egg.OTName = g.State.PlayerName; egg.OTId = g.State.TrainerId;
            egg.MetMap = g.Map != null ? g.Map.Id : 0; egg.MetLevel = 1;
            return egg;
        }

        public static void CollectEgg(Game g)
        {
            if (!g.State.DayCareEgg || g.State.Party.Count >= GameState.MaxParty) return;
            var egg = MakeEgg(g);
            g.State.DayCareEgg = false;
            if (egg != null) g.State.Party.Add(egg);
        }

        // ------------------------------------------------------------------ every step
        public static void OnStep(Game g)
        {
            var s = g.State;
            // EXP for Pokémon at the Day Care (they learn new moves, forgetting the oldest)
            foreach (var slot in s.DayCare)
            {
                var pk = slot.Pokemon;
                if (pk.Level >= GrowthRates.MaxLevel) continue;
                var d = pk.Data(g.Project);
                string rate = d != null ? d.GrowthRate : "Medium";
                pk.Exp++;
                while (pk.Level < GrowthRates.MaxLevel && pk.Exp >= GrowthRates.ExpForLevel(rate, pk.Level + 1))
                {
                    int exp = pk.Exp;
                    foreach (var m in Growth.LevelUp(g.Project, pk))
                    {
                        if (pk.Moves.Any(x => x.Id == m)) continue;
                        var md = g.Project.Moves.FirstOrDefault(x => x.Id == m);
                        if (md == null) continue;
                        if (pk.Moves.Count >= 4) pk.Moves.RemoveAt(0);
                        pk.Moves.Add(new PokemonMove { Id = m, PP = md.TotalPP });
                    }
                    pk.Exp = exp;
                }
            }
            // Egg chance every 256 steps
            if (s.DayCare.Count == 2 && !s.DayCareEgg)
            {
                s.DayCareEggSteps++;
                if (s.DayCareEggSteps >= 256)
                {
                    s.DayCareEggSteps = 0;
                    int c = Compatibility(g);
                    int chance = g.State.ItemCount("OVALCHARM") > 0 ? new[] { 0, 40, 80, 88 }[c] : EggChance[c];
                    if (c > 0 && g.Rng.Next(100) < chance) s.DayCareEgg = true;
                }
            }
            // Eggs in the party hatch (twice as fast with Flame Body / Magma Armor in the party)
            bool fast = s.Party.Any(p => !p.IsEgg && (p.AbilityId(g.Project) == "FLAMEBODY" || p.AbilityId(g.Project) == "MAGMAARMOR"));
            foreach (var egg in s.Party.Where(p => p.IsEgg).ToList())
            {
                egg.EggSteps -= fast ? 2 : 1;
                if (egg.EggSteps <= 0 && !g.MessageBusy && !g.SceneOpen) { Hatch(g, egg); break; }
            }
        }

        public static void Hatch(Game g, Pokemon egg)
        {
            egg.IsEgg = false; egg.EggSteps = 0; egg.Nickname = null;
            egg.Level = 1; egg.Exp = 0;
            egg.HP = egg.TotalHP(g.Project);
            egg.MetLevel = 1; egg.MetMap = g.Map != null ? g.Map.Id : egg.MetMap;
            egg.Happiness = 120;
            g.State.Register(egg);
            GameStats.Inc(g, "eggs_hatched");
            string name = egg.Name(g.Project);
            g.Message.Show("Oh?", null, -1, _ =>
            {
                g.Audio.PlayMe("Evolution success");
                g.Message.Show(name + " hatched from the Egg!", null, -1, __ =>
                    g.Message.Show("Would you like to nickname the newly hatched " + name + "?", new List<string> { "Yes", "No" }, 1, c =>
                    {
                        if (c == 0) g.AskName(name + "'s nickname?", "", 12, n => egg.Nickname = string.IsNullOrEmpty(n) ? null : n, null);
                    }));
            });
        }
    }
}
