using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.World
{
    public class RadarPatch
    {
        public int X, Y;
        /// <summary>0 = rustling, 1 = rustling vigorously (keeps a chain more often), 2 = shiny sparkle.</summary>
        public int Kind;
    }

    /// <summary>
    /// The Poké Radar: in grass with wild Pokémon it makes up to four patches of grass shake. Walking into one starts a
    /// battle; beating or catching the same species again and again builds a chain that raises the shiny chance.
    /// Stepping off the patches, leaving the map or running ends the chain. It needs 50 steps to recharge.
    /// </summary>
    public static class PokeRadar
    {
        public const int ChargeSteps = 50;
        static List<RadarPatch> _patches = new List<RadarPatch>();
        static int _mapId;
        public static string ChainSpecies;
        public static int ChainLevel, Chain;
        static int _lastUseStep = -ChargeSteps;

        public static bool Active { get { return _patches.Count > 0; } }

        static EncounterMethod Table(Game g, bool radarOnly)
        {
            var table = g.EncounterTable(g.Map.Id);
            if (table == null) return null;
            var radar = table.Methods.FirstOrDefault(m => m.Type == "PokeRadar" && m.Slots.Count > 0);
            var land = table.Methods.FirstOrDefault(m => m.Type == "Land" && m.Slots.Count > 0);
            if (radarOnly) return radar;
            if (radar == null) return land;
            if (land == null) return radar;
            var merged = new EncounterMethod { Type = "Land", Density = land.Density };
            merged.Slots.AddRange(land.Slots); merged.Slots.AddRange(radar.Slots);
            return merged;
        }

        static bool IsGrass(Game g, int x, int y)
        {
            int tag = g.Map.TerrainTag(x, y);
            return tag == GameMap.TagGrass || tag == GameMap.TagTallGrass;
        }

        /// <summary>Using the Poké Radar from the Bag.</summary>
        public static void Use(Game g)
        {
            if (g.Map == null || g.Player.Surfing || Table(g, false) == null || !IsGrass(g, g.Player.X, g.Player.Y))
            { g.Message.Show("The Poké Radar can't be used here."); return; }
            if (!Active && g.State.Steps - _lastUseStep < ChargeSteps)
            { g.Message.Show("The battery has run dry!\\nWalk " + (ChargeSteps - (g.State.Steps - _lastUseStep)) + " more steps to charge it."); return; }
            _lastUseStep = g.State.Steps;
            Chain = 0; ChainSpecies = null;
            if (!Shake(g)) g.Message.Show("The grassy patch remained quiet...");
        }

        /// <summary>Picks a shaking patch in each ring of 2-4 tiles around the player.</summary>
        static bool Shake(Game g)
        {
            _patches.Clear(); _mapId = g.Map.Id;
            for (int ring = 1; ring <= 4; ring++)
            {
                var spots = new List<int[]>();
                for (int dx = -ring; dx <= ring; dx++)
                    for (int dy = -ring; dy <= ring; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                        int x = g.Player.X + dx, y = g.Player.Y + dy;
                        if (g.Map.Valid(x, y) && IsGrass(g, x, y) && !g.Map.EventsAt(x, y).Any()) spots.Add(new[] { x, y });
                    }
                if (spots.Count == 0 || g.Rng.Next(100) < 25) continue;
                var s = spots[g.Rng.Next(spots.Count)];
                // Shiny chance grows with the chain (as Essentials: 65536 / (8200 - chain * 200), at most 1 in 200)
                int shinyOdds = Math.Max(200, 8200 - Math.Min(40, Chain) * 200);
                int kind = g.Rng.Next(shinyOdds) < 1 ? 2 : g.Rng.Next(100) < 40 ? 1 : 0;
                _patches.Add(new RadarPatch { X = s[0], Y = s[1], Kind = kind });
            }
            return _patches.Count > 0;
        }

        public static void Clear() { _patches.Clear(); Chain = 0; ChainSpecies = null; }

        /// <summary>A step: into a shaking patch starts a battle (true); anywhere else ends the radar.</summary>
        public static bool OnStep(Game g)
        {
            if (!Active) return false;
            if (g.Map == null || g.Map.Id != _mapId) { Clear(); return false; }
            var p = _patches.FirstOrDefault(q => q.X == g.Player.X && q.Y == g.Player.Y);
            if (p == null) { Clear(); return false; }
            int ring = Math.Max(1, _patches.IndexOf(p) + 1);
            var table = Table(g, false);
            string species; int level;
            // Continuing a chain: the same species, more likely in vigorous / farther patches
            bool keep = ChainSpecies != null && g.Rng.Next(100) < 50 + ring * 8 + (p.Kind > 0 ? 25 : 0);
            if (keep) { species = ChainSpecies; level = ChainLevel; }
            else
            {
                int total = table.Slots.Sum(s => s.Chance), roll = g.Rng.Next(Math.Max(1, total));
                var slot = table.Slots[0];
                foreach (var s in table.Slots) { if (roll < s.Chance) { slot = s; break; } roll -= s.Chance; }
                species = slot.Species; level = g.Rng.Next(slot.MinLevel, Math.Max(slot.MinLevel, slot.MaxLevel) + 1);
            }
            _patches.Clear();
            var wild = PokeCreator.Pkmn.Pokemon.Create(g.Project, species, level, g.Rng);
            if (p.Kind == 2) wild.Shiny = true;
            if (wild.Data(g.Project) == null || !g.PartyCanBattle) { Clear(); return false; }
            string chainOf = ChainSpecies;
            g.StartWildPokemonBattle(wild, o =>
            {
                bool won = o == PokeCreator.Battles.BattleOutcome.Win || o == PokeCreator.Battles.BattleOutcome.Caught;
                if (won && (chainOf == null || chainOf == species)) { ChainSpecies = species; ChainLevel = level; Chain++; }
                else { Chain = 0; ChainSpecies = null; }
                if (won && !Shake(g)) Clear();
                else if (!won) Clear();
            });
            return true;
        }

        /// <summary>Rustling grass marks over the shaking patches.</summary>
        public static void Draw(Game g, Gfx gfx, int displayX, int displayY, int frame)
        {
            if (!Active || g.Map == null || g.Map.Id != _mapId) return;
            foreach (var p in _patches)
            {
                int sx = p.X * 32 - displayX, sy = p.Y * 32 - displayY;
                int wob = (frame / (p.Kind == 1 ? 3 : 6)) % 2 == 0 ? -3 : 3;
                var edge = new Color(24, 64, 24);
                var col = p.Kind == 2 ? new Color(255, 236, 110) : p.Kind == 1 ? new Color(176, 240, 120) : new Color(136, 216, 96);
                foreach (var b in new[] { new[] { 5 + wob, 6, 16 }, new[] { 13, 2, 20 }, new[] { 21 - wob, 6, 16 } })
                {
                    gfx.FillRect(sx + b[0] - 1, sy + b[1] - 1, 8, b[2] + 2, edge);
                    gfx.FillRect(sx + b[0], sy + b[1], 6, b[2], col);
                }
                if (p.Kind == 2 && (frame / 4) % 2 == 0) { gfx.FillRect(sx + 4, sy + 20, 4, 4, new Color(255, 255, 255)); gfx.FillRect(sx + 24, sy + 18, 4, 4, new Color(255, 255, 255)); }
            }
        }
    }
}
