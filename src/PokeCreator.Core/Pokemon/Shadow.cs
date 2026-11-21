using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;

namespace PokeCreator.Pkmn
{
    /// <summary>
    /// Shadow Pokémon (as in Colosseum / XD and Essentials): a heart gauge that falls as the Pokémon walks with the
    /// player, battles and smells scents; Shadow moves (strong against every non-Shadow Pokémon); EXP saved up until
    /// it is purified (Relic Stone) once the gauge is empty.
    /// </summary>
    public static class Shadow
    {
        public const int HeartGaugeSize = 4000;

        static readonly string[][] Moves =
        {
            // id, name, category, power, accuracy, pp, target, function code, effect chance, description
            new[] { "SHADOWRUSH", "Shadow Rush", "Physical", "55", "100", "10", "NearOther", "None", "0", "An attack that damages the target with a shadowy tackle." },
            new[] { "SHADOWBLITZ", "Shadow Blitz", "Physical", "40", "100", "10", "NearOther", "None", "0", "A quick shadowy blitz." },
            new[] { "SHADOWBREAK", "Shadow Break", "Physical", "75", "100", "10", "NearOther", "None", "0", "A shadowy attack that strikes with force." },
            new[] { "SHADOWEND", "Shadow End", "Physical", "120", "60", "10", "NearOther", "RecoilHalfOfDamageDealt", "0", "A reckless shadowy attack that also hurts the user." },
            new[] { "SHADOWWAVE", "Shadow Wave", "Special", "50", "100", "10", "AllNearFoes", "None", "0", "A shadowy wave that hits all opponents." },
            new[] { "SHADOWRAVE", "Shadow Rave", "Special", "70", "100", "10", "AllNearFoes", "None", "0", "A shadowy rave that hits all opponents." },
            new[] { "SHADOWSTORM", "Shadow Storm", "Special", "95", "100", "10", "AllNearFoes", "None", "0", "A shadowy storm that hits all opponents." },
            new[] { "SHADOWFIRE", "Shadow Fire", "Special", "75", "100", "10", "NearOther", "BurnTarget", "10", "Shadowy flames that may burn the target." },
            new[] { "SHADOWBOLT", "Shadow Bolt", "Special", "75", "100", "10", "NearOther", "ParalyzeTarget", "10", "A shadowy bolt that may paralyze the target." },
            new[] { "SHADOWCHILL", "Shadow Chill", "Special", "75", "100", "10", "NearOther", "FreezeTarget", "10", "A shadowy chill that may freeze the target." },
            new[] { "SHADOWBLAST", "Shadow Blast", "Special", "80", "100", "10", "NearOther", "None", "0", "A shadowy blast with a high critical-hit ratio." },
            new[] { "SHADOWDOWN", "Shadow Down", "Status", "0", "100", "10", "AllNearFoes", "LowerTargetDefense2", "0", "Sharply lowers the opponents' Defense." },
            new[] { "SHADOWMIST", "Shadow Mist", "Status", "0", "100", "10", "AllNearFoes", "LowerTargetEvasion2", "0", "Sharply lowers the opponents' evasiveness." },
            new[] { "SHADOWPANIC", "Shadow Panic", "Status", "0", "60", "10", "AllNearFoes", "ConfuseTarget", "0", "Confuses the opponents." },
            new[] { "SHADOWHOLD", "Shadow Hold", "Status", "0", "80", "10", "AllNearFoes", "TrapTargetInBattle", "0", "The opponents can't escape." },
        };

        /// <summary>Adds the Shadow type and Shadow moves to a project that lacks them.</summary>
        public static void EnsureData(PokeCreator.Projects.Project p)
        {
            if (!p.Types.Any(t => t.Id == "SHADOW")) p.Types.Add(new TypeData { Id = "SHADOW", Name = "Shadow" });
            foreach (var m in Moves)
            {
                if (p.Moves.Any(x => x.Id == m[0])) continue;
                var md = new MoveData { Id = m[0], Name = m[1], Type = "SHADOW", Category = m[2], Power = int.Parse(m[3]), Accuracy = int.Parse(m[4]), TotalPP = int.Parse(m[5]), Target = m[6], FunctionCode = m[7], EffectChance = int.Parse(m[8]), Description = m[9] };
                md.Flags.Add("CanProtect");
                if (m[2] == "Physical") md.Flags.Add("Contact");
                if (m[0] == "SHADOWBLAST") md.Flags.Add("HighCriticalHitRate");
                p.Moves.Add(md);
            }
        }

        /// <summary>Turns a Pokémon into a Shadow Pokémon: full heart gauge and Shadow Rush in its first move slot.</summary>
        public static void Make(PokeCreator.Projects.Project p, Pokemon pk)
        {
            if (pk.ShadowPokemon) return;
            EnsureData(p);
            pk.ShadowPokemon = true;
            var d = pk.Data(p) ?? p.Species.FirstOrDefault(s => s.Id == pk.Species);
            pk.HeartGaugeMax = d != null && d.ShadowGaugeSize > 0 ? d.ShadowGaugeSize : 0;
            pk.HeartGauge = GaugeSize(pk);
            pk.SavedExp = 0;
            // Species with Shadow moves set (PBS shadow_pokemon.txt): they take the first move slots
            var list = d != null && d.ShadowMoves != null ? d.ShadowMoves.Where(id => p.Moves.Any(m => m.Id == id)).Take(4).ToList() : new List<string>();
            if (list.Count > 0)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var smd = p.Moves.First(m => m.Id == list[i]);
                    var spm = new PokemonMove { Id = smd.Id, PP = Math.Max(1, smd.TotalPP) };
                    if (i < pk.Moves.Count) pk.Moves[i] = spm; else pk.Moves.Add(spm);
                }
                return;
            }
            var rush = p.Moves.First(m => m.Id == "SHADOWRUSH");
            var pm = new PokemonMove { Id = rush.Id, PP = Math.Max(1, rush.TotalPP) };
            if (pk.Moves.Count == 0) pk.Moves.Add(pm);
            else if (!pk.Moves.Any(m => m.Id.StartsWith("SHADOW") && p.Moves.Any(x => x.Id == m.Id && x.Type == "SHADOW"))) pk.Moves[0] = pm;
        }

        /// <summary>Heart gauge stage 0-5 (5 = completely closed).</summary>
        public static int GaugeSize(Pokemon pk) { return pk.HeartGaugeMax > 0 ? pk.HeartGaugeMax : HeartGaugeSize; }
        public static int Stage(Pokemon pk) { return !pk.ShadowPokemon || pk.HeartGauge <= 0 ? 0 : Math.Min(5, (pk.HeartGauge - 1) * 5 / GaugeSize(pk) + 1); }

        public static bool Purifiable(Pokemon pk) { return pk.ShadowPokemon && pk.HeartGauge <= 0 && !pk.IsEgg; }

        /// <summary>Opens (negative) the Pokémon's heart. Returns true if the gauge changed.</summary>
        public static bool AdjustHeart(Pokemon pk, int amount)
        {
            if (!pk.ShadowPokemon) return false;
            int before = pk.HeartGauge;
            pk.HeartGauge = Math.Max(0, Math.Min(GaugeSize(pk), pk.HeartGauge + amount));
            return pk.HeartGauge != before;
        }

        /// <summary>Purification: no longer Shadow; it gets the EXP it saved up and its Shadow moves become normal moves.</summary>
        public static void Purify(PokeCreator.Projects.Project p, Pokemon pk)
        {
            if (!pk.ShadowPokemon) return;
            pk.ShadowPokemon = false; pk.HeartGauge = 0;
            var shadowIds = p.Moves.Where(m => m.Type == "SHADOW").Select(m => m.Id).ToList();
            pk.Moves.RemoveAll(m => shadowIds.Contains(m.Id));
            var d = pk.Data(p);
            if (d != null && pk.Moves.Count < 4)
                foreach (var lm in d.Moves.Where(x => x.Level <= pk.Level).OrderByDescending(x => x.Level))
                {
                    if (pk.Moves.Count >= 4) break;
                    if (pk.Moves.Any(m => m.Id == lm.Move)) continue;
                    var md = p.Moves.FirstOrDefault(x => x.Id == lm.Move);
                    if (md != null) pk.Moves.Add(new PokemonMove { Id = md.Id, PP = md.TotalPP });
                }
            if (pk.SavedExp > 0 && d != null)
            {
                pk.Exp = Math.Min(GrowthRates.ExpForLevel(d.GrowthRate, GrowthRates.MaxLevel), pk.Exp + pk.SavedExp);
                while (pk.Level < GrowthRates.MaxLevel && pk.Exp >= GrowthRates.ExpForLevel(d.GrowthRate, pk.Level + 1)) { int e = pk.Exp; Growth.LevelUp(p, pk); pk.Exp = e; }
            }
            pk.SavedExp = 0;
        }
    }
}
