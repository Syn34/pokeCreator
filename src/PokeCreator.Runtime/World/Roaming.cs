using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.World
{
    /// <summary>Where a roaming Pokémon is and how it's doing (HP and status carry over between meetings).</summary>
    public class RoamerState
    {
        public int Map { get; set; }
        public Pokemon Pokemon { get; set; }
        /// <summary>Caught or defeated: it no longer roams.</summary>
        public bool Done { get; set; }
    }

    /// <summary>Roaming Pokémon (Essentials' ROAMING_SPECIES): each moves to another map whenever the player changes map.</summary>
    public static class Roaming
    {
        static List<RoamerState> States(Game g)
        {
            var list = g.State.Extras.Roamers;
            if (list == null) list = g.State.Extras.Roamers = new List<RoamerState>();
            while (list.Count < g.Project.Settings.Roamers.Count) list.Add(new RoamerState());
            return list;
        }

        static List<int> MapsFor(Game g, RoamerDef def)
        {
            var maps = new List<int>();
            if (!string.IsNullOrEmpty(def.Maps))
                foreach (var part in def.Maps.Split(',', ';', ' ')) { int id; if (int.TryParse(part.Trim(), out id) && id > 0) maps.Add(id); }
            if (maps.Count == 0) maps = g.Project.Encounters.Select(e => e.Map).Distinct().ToList();
            return maps;
        }

        static bool Active(Game g, RoamerDef def, RoamerState st)
        {
            return !st.Done && !string.IsNullOrEmpty(def.Species) && (def.Switch <= 0 || g.State.GetSwitch(def.Switch));
        }

        /// <summary>The player entered another map: every roamer moves to a random map (not the one it was on), and may be met again.</summary>
        public static void OnMapChanged(Game g)
        {
            var defs = g.Project.Settings.Roamers;
            if (defs == null || defs.Count == 0 || g.State == null) return;
            var states = States(g);
            g.State.Extras.RoamedAlready = false;
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i]; var st = states[i];
                if (!Active(g, def, st)) continue;
                var maps = MapsFor(g, def);
                if (maps.Count == 0) continue;
                var choices = maps.Count > 1 ? maps.Where(m => m != st.Map).ToList() : maps;
                st.Map = choices[g.Rng.Next(choices.Count)];
            }
        }

        static bool MethodAllowed(string allowed, string method)
        {
            switch (allowed ?? "Any")
            {
                case "Land": return method == "Land" || method == "Cave";
                case "Surfing": return method == "Water";
                case "Fishing": return method == "Fishing";
                case "Water": return method == "Water" || method == "Fishing";
                default: return true;
            }
        }

        /// <summary>A wild encounter of this method is about to happen: 1 in 4 times a roamer on this map takes its place. True = a battle started.</summary>
        public static bool TryEncounter(Game g, string method)
        {
            var defs = g.Project.Settings.Roamers;
            if (defs == null || defs.Count == 0 || g.Map == null || g.State.Extras.RoamedAlready || g.State.Extras.Partner != null) return false;
            var states = States(g);
            var here = Enumerable.Range(0, defs.Count).Where(i => Active(g, defs[i], states[i]) && states[i].Map == g.Map.Id && MethodAllowed(defs[i].Encounter, method)).ToList();
            if (here.Count == 0 || g.Rng.Next(100) >= 25) return false;
            int idx = here[g.Rng.Next(here.Count)];
            var def = defs[idx]; var st = states[idx];
            if (st.Pokemon == null || st.Pokemon.Species != def.Species)
                st.Pokemon = Pokemon.Create(g.Project, def.Species, Math.Max(1, def.Level), g.Rng);
            if (st.Pokemon.Data(g.Project) == null || !g.PartyCanBattle) return false;
            g.State.Extras.RoamedAlready = true;
            g.StartRoamerBattle(st.Pokemon, def, o => { if (o == PokeCreator.Battles.BattleOutcome.Win || o == PokeCreator.Battles.BattleOutcome.Caught) st.Done = true; });
            return true;
        }

        /// <summary>pbResetAllRoamers: roamers that were defeated (not caught) roam again, at full health.</summary>
        public static void ResetAll(Game g)
        {
            var states = States(g);
            foreach (var st in states)
            {
                if (st.Pokemon != null && g.State.Party.Concat(g.State.Storage).Contains(st.Pokemon)) continue;
                st.Done = false; st.Pokemon = null;
            }
        }
    }
}
