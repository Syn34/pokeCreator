using System;
using PokeCreator.Battles;

namespace PokeCreator.Runtime.Script
{
    /// <summary>
    /// Hooks for project scripts and plugins: subscribe in a [ScriptInit] method, e.g.
    /// <c>GameEvents.Step += g => { ... };</c>
    /// </summary>
    public static class GameEvents
    {
        /// <summary>The player finished a step on the map.</summary>
        public static event Action<Game> Step;
        /// <summary>The player arrived on another map (warp or connection).</summary>
        public static event Action<Game> MapChanged;
        /// <summary>A battle ended (any kind) with this outcome.</summary>
        public static event Action<Game, BattleOutcome> BattleEnded;
        /// <summary>Every frame while the player is on the map.</summary>
        public static event Action<Game> Frame;
        /// <summary>A new game started or a save was loaded.</summary>
        public static event Action<Game> GameStarted;
        /// <summary>A wild Pokémon was just made for a battle (change its moves, shininess, form...).</summary>
        public static event Action<Game, PokeCreator.Pkmn.Pokemon> WildPokemonCreated;
        /// <summary>A battle is about to start (set battle fields, weather, rules).</summary>
        public static event Action<Game, PokeCreator.Battles.Battle> BattleStarting;
        /// <summary>The game was saved.</summary>
        public static event Action<Game> GameSaved;

        static void Safe(Action a, string what)
        {
            try { a(); } catch (Exception e) { Console.WriteLine("[scripts] " + what + " handler failed: " + e.Message); }
        }

        internal static void RaiseStep(Game g) { var h = Step; if (h != null) Safe(() => h(g), "Step"); }
        internal static void RaiseMapChanged(Game g) { var h = MapChanged; if (h != null) Safe(() => h(g), "MapChanged"); }
        internal static void RaiseBattleEnded(Game g, BattleOutcome o) { var h = BattleEnded; if (h != null) Safe(() => h(g, o), "BattleEnded"); }
        internal static void RaiseFrame(Game g) { var h = Frame; if (h != null) Safe(() => h(g), "Frame"); }
        internal static void RaiseGameStarted(Game g) { var h = GameStarted; if (h != null) Safe(() => h(g), "GameStarted"); }
        internal static void RaiseWildPokemonCreated(Game g, PokeCreator.Pkmn.Pokemon pk) { var h = WildPokemonCreated; if (h != null) Safe(() => h(g, pk), "WildPokemonCreated"); }
        internal static void RaiseBattleStarting(Game g, PokeCreator.Battles.Battle b) { var h = BattleStarting; if (h != null) Safe(() => h(g, b), "BattleStarting"); }
        internal static void RaiseGameSaved(Game g) { var h = GameSaved; if (h != null) Safe(() => h(g), "GameSaved"); }
    }
}
