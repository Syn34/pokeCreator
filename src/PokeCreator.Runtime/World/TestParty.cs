using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;
using PokeCreator.Projects;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// A strong party for playtesting trainers (Debug menu, or Studio's "Playtests start with a test party").
    /// Six well-known strong Pokémon at the chosen level with perfect IVs and their best attacking moves.
    /// </summary>
    public static class TestParty
    {
        static readonly string[] Preferred = { "MEWTWO", "RAYQUAZA", "GARCHOMP", "METAGROSS", "TYRANITAR", "DRAGONITE", "SALAMENCE", "LUCARIO", "GYARADOS", "CHARIZARD", "BLASTOISE", "VENUSAUR", "MACHAMP", "ALAKAZAM", "GENGAR", "SNORLAX" };

        /// <summary>Replaces the party (the old one goes to the PC) and adds healing items. Returns how many Pokémon were given.</summary>
        public static int Give(Game g, int level)
        {
            var p = g.Project;
            int max = p.Settings.MaxLevel > 0 ? p.Settings.MaxLevel : 100;
            level = Math.Max(1, Math.Min(Math.Min(max, GrowthRates.MaxLevel), level));
            var picks = Preferred.Select(id => p.Species.FirstOrDefault(s => s.Id == id && s.Form == 0)).Where(s => s != null).ToList();
            if (picks.Count < 6)
                picks.AddRange(p.Species.Where(s => s.Form == 0 && !picks.Contains(s) && s.BaseStats != null && s.BaseStats.Count >= 6)
                                        .OrderByDescending(s => s.BaseStats.Sum()).Take(6 - picks.Count));
            if (picks.Count == 0) return 0;

            // the current party goes to the PC so nothing is lost
            foreach (var old in g.State.Party.ToList()) if (g.State.Storage.TryAdd(old) >= 0) g.State.Party.Remove(old);
            g.State.Party.Clear();

            string leftovers = p.Items.Any(i => i.Id == "LEFTOVERS") ? "LEFTOVERS" : null;
            foreach (var s in picks.Take(6))
            {
                var pk = Pokemon.Create(p, s.Id, level, g.Rng);
                pk.OTName = g.State.PlayerName;
                pk.IV = Enumerable.Repeat(31, 6).ToList();
                var moves = BestMoves(p, pk.Data(p) ?? s, level);
                if (moves.Count > 0) pk.Moves = moves.Select(m => new PokemonMove(m.Id, m.TotalPP > 0 ? m.TotalPP : 10)).ToList();
                if (leftovers != null) pk.Item = leftovers;
                pk.Heal(p);
                pk.HP = pk.TotalHP(p);
                g.State.Party.Add(pk);
            }
            foreach (var kv in new[] { new KeyValuePair<string, int>("FULLRESTORE", 20), new KeyValuePair<string, int>("MAXREVIVE", 10), new KeyValuePair<string, int>("MAXELIXIR", 10) })
                if (p.Items.Any(i => i.Id == kv.Key) && g.State.ItemCount(kv.Key) < kv.Value) g.State.AddItem(kv.Key, kv.Value - g.State.ItemCount(kv.Key));
            return g.State.Party.Count;
        }

        /// <summary>Its four best attacking moves (by power, accuracy and same-type bonus), preferring different types.</summary>
        static List<MoveData> BestMoves(Project p, SpeciesData d, int level)
        {
            var ids = new HashSet<string>(d.Moves.Where(m => m.Level <= level).Select(m => m.Move).Concat(d.TutorMoves ?? new List<string>()));
            bool physical = d.BaseStats != null && d.BaseStats.Count >= 5 && d.BaseStats[1] >= d.BaseStats[4];
            var types = d.Types ?? new List<string>();
            var scored = p.Moves.Where(m => ids.Contains(m.Id) && m.Power > 0 && m.Category != "Status" && !Awkward(m))
                .Select(m => new
                {
                    M = m,
                    Score = (m.Power == 1 ? 60 : m.Power) * (m.Accuracy <= 0 ? 100 : m.Accuracy) / 100.0
                            * (types.Contains(m.Type) ? 1.5 : 1.0) * ((m.Category == "Physical") == physical ? 1.2 : 0.8)
                }).ToList();
            var chosen = new List<MoveData>();
            while (chosen.Count < 4 && scored.Count > 0)
            {
                var best = scored.OrderByDescending(x => x.Score * (chosen.Any(c => c.Type == x.M.Type) ? 0.5 : 1.0)).First();
                chosen.Add(best.M);
                scored.Remove(best);
            }
            return chosen;
        }

        /// <summary>Moves that make a poor test (recharge turns, fainting the user, charging first, fixed damage).</summary>
        static bool Awkward(MoveData m)
        {
            string f = m.FunctionCode ?? "";
            return f.Contains("Recharge") || f.Contains("SkipNextTurn") || f.Contains("UserFaints") || f.Contains("TwoTurn") || f.Contains("TwoTurnsLater")
                || f.Contains("FixedDamage") || f.Contains("OHKO") || f.StartsWith("Fails") || f.Contains("IfTargetAsleep") || f.Contains("MultiTurnAttack")
                || f.Contains("RemoveTerrain") || f.Contains("LowerUserSpAtk2") || f.Contains("UserLosesHalf") || f.Contains("Counter") || f.Contains("Recoil50");
        }
    }
}
