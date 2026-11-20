using System;
using System.Linq;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// Pokérus: after a battle a party Pokémon may catch it (3 in 65536); an infected one may pass it to the Pokémon
    /// next to it in the party (1 in 3); the infection wears off after a few days (party only, as in the games);
    /// infected and cured Pokémon gain double EVs.
    /// </summary>
    public static class Pokerus
    {
        public static void AfterBattle(Game g)
        {
            var party = g.State.Party;
            if (party.Count == 0) return;
            if (g.Rng.Next(65536) < 3)
            {
                var pk = party[g.Rng.Next(party.Count)];
                if (!pk.IsEgg && pk.Pokerus == 0) pk.GivePokerus(g.Rng);
            }
            for (int i = 0; i < party.Count; i++)
            {
                if (party[i].PokerusStage != 1 || g.Rng.Next(3) != 0) continue;
                int strain = party[i].Pokerus >> 4;
                foreach (int j in new[] { i - 1, i + 1 })
                    if (j >= 0 && j < party.Count && party[j].Pokerus == 0 && !party[j].IsEgg) party[j].GivePokerus(g.Rng, strain);
            }
        }

        /// <summary>Counts the days since the last check (called on steps).</summary>
        public static void DailyCheck(Game g)
        {
            int today = (int)(DateTime.Today.Ticks / TimeSpan.TicksPerDay);
            var ex = g.State.Extras;
            if (ex.PokerusDay == 0) { ex.PokerusDay = today; return; }
            int days = today - ex.PokerusDay;
            if (days <= 0) return;
            ex.PokerusDay = today;
            foreach (var pk in g.State.Party) pk.LowerPokerusCount(days);
        }

        public static bool AnyInfected(Game g) { return g.State.Party.Any(p => p.PokerusStage == 1); }
    }
}
