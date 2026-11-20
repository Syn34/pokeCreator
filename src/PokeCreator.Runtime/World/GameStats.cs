using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// Play statistics (Essentials' $stats): counters scripts read and change ($stats.poke_center_count += 1),
    /// plus ones the engine keeps itself — steps walked, cycled and surfed, battles, catches, eggs hatched,
    /// evolutions, money spent, play time and sessions. Shown on the Trainer Card's back and readable by scripts.
    /// </summary>
    public static class GameStats
    {
        static Dictionary<string, int> D(Game g) { if (g.State.Extras.Stats == null) g.State.Extras.Stats = new Dictionary<string, int>(); return g.State.Extras.Stats; }

        public static void Inc(Game g, string name, int by = 1) { if (g == null || g.State == null) return; var d = D(g); int v; d.TryGetValue(name, out v); d[name] = v + by; }
        public static void Set(Game g, string name, int value) { D(g)[name] = value; }

        public static object Get(Game g, string name)
        {
            switch (name)
            {
                case "play_time": return (int)(g.State.PlayFrames / 60);
                case "distance_moved": return Int(g, "distance_walked") + Int(g, "distance_cycled") + Int(g, "distance_surfed") + Int(g, "distance_slid");
                case "battle_count": return Int(g, "wild_battles_won") + Int(g, "wild_battles_lost") + Int(g, "wild_battles_fled") + Int(g, "trainer_battles_won") + Int(g, "trainer_battles_lost");
                case "wild_battles": return Int(g, "wild_battles_won") + Int(g, "wild_battles_lost") + Int(g, "wild_battles_fled");
                case "trainer_battles": return Int(g, "trainer_battles_won") + Int(g, "trainer_battles_lost");
                case "caught_pokemon_count": return Int(g, "caught_pokemon_count");
            }
            if (name.StartsWith("time_to_") || name.StartsWith("set_time_to_")) return Int(g, name.Replace("set_", ""));
            return Int(g, name);
        }
        public static int Int(Game g, string name) { int v; return D(g).TryGetValue(name, out v) ? v : 0; }

        /// <summary>$stats.set_time_to_badge(n) / set_time_to_hall_of_fame: the play time when it first happened.</summary>
        public static void SetTimeTo(Game g, string what)
        {
            string key = "time_to_" + what;
            if (Int(g, key) == 0) Set(g, key, Math.Max(1, (int)(g.State.PlayFrames / 60)));
        }

        public static void OnStep(Game g)
        {
            var pl = g.Player;
            if (pl == null) return;
            if (pl.Surfing) Inc(g, "distance_surfed");
            else if (pl.Cycling) Inc(g, "distance_cycled");
            else Inc(g, "distance_walked");
        }

        public static void OnBattleEnd(Game g, bool wild, PokeCreator.Battles.BattleOutcome o)
        {
            string k = wild ? "wild_battles_" : "trainer_battles_";
            switch (o)
            {
                case PokeCreator.Battles.BattleOutcome.Win: Inc(g, k + "won"); break;
                case PokeCreator.Battles.BattleOutcome.Loss: Inc(g, k + "lost"); break;
                case PokeCreator.Battles.BattleOutcome.Caught: Inc(g, "wild_battles_won"); Inc(g, "caught_pokemon_count"); break;
                default: if (wild) Inc(g, "wild_battles_fled"); break;
            }
        }

        public static readonly string[][] CardLines =
        {
            new[] { "Steps taken", "distance_moved" }, new[] { "Battles", "battle_count" },
            new[] { "Trainers beaten", "trainer_battles_won" }, new[] { "Pokémon caught", "caught_pokemon_count" },
            new[] { "Eggs hatched", "eggs_hatched" }, new[] { "Evolutions", "evolution_count" },
            new[] { "Pokémon Center visits", "poke_center_count" }, new[] { "Money spent", "money_spent_at_marts" },
        };
    }
}
