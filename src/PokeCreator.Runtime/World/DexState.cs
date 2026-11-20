using System.Collections.Generic;
using System.Linq;

namespace PokeCreator.Runtime.World
{
    /// <summary>Which Pokédexes are unlocked ($player.pokedex.unlock / lock, pbUnlockDex).</summary>
    public static class DexState
    {
        public static List<int> Unlocked(Game g)
        {
            var x = g.State.Extras.UnlockedDexes;
            if (x != null) return x;
            return g.Project.RegionalDexes.Count > 0 ? new List<int> { 0 } : new List<int> { -1 };
        }

        static List<int> Own(Game g)
        {
            if (g.State.Extras.UnlockedDexes == null) g.State.Extras.UnlockedDexes = Unlocked(g).ToList();
            return g.State.Extras.UnlockedDexes;
        }

        /// <summary>dex: a regional dex number, or -1 / anything past the last one for the National Dex.</summary>
        public static void Unlock(Game g, int dex) { if (dex < 0 || dex >= g.Project.RegionalDexes.Count) dex = -1; var l = Own(g); if (!l.Contains(dex)) l.Add(dex); }
        public static void Lock(Game g, int dex) { if (dex < 0 || dex >= g.Project.RegionalDexes.Count) dex = -1; Own(g).Remove(dex); }
        public static bool IsUnlocked(Game g, int dex) { if (dex < 0 || dex >= g.Project.RegionalDexes.Count) dex = -1; return Unlocked(g).Contains(dex); }

        /// <summary>The species' number in a regional dex (1-based), 0 if it isn't in it; -1 = National order.</summary>
        public static int RegionalNumber(Game g, int dex, string species)
        {
            if (dex < 0 || dex >= g.Project.RegionalDexes.Count)
            {
                var all = g.Project.Species.Where(s => s.Form == 0).ToList();
                int i = all.FindIndex(s => s.Id == species); return i + 1;
            }
            return g.Project.RegionalDexes[dex].Species.IndexOf(species) + 1;
        }
    }
}
