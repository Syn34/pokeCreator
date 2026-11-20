using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Pkmn;
using PokeCreator.Runtime.Script;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// Field moves and overworld actions: Cut, Rock Smash, Strength, Headbutt, Surf, fishing and berry plants.
    /// Calls used in a Conditional Branch (pbCut, pbRockSmash...) ask Yes/No first: the event waits for the
    /// answer and then re-checks the condition (see Game.PromptPending).
    /// </summary>
    public static class FieldMoves
    {
        public static Pokemon WithMove(Game g, string move)
        {
            return g.State.Party.FirstOrDefault(pk => !pk.IsEgg && pk.Moves.Any(m => m.Id == move));
        }

        static string MoveName(Game g, string id) { return ItemEffects.MoveName(g.Project, id); }

        /// <summary>
        /// "Would you like to use X?" for a field move. Returns the answer when the event re-checks; until then false.
        /// </summary>
        public static object Ask(Game g, Interpreter ctx, string move, string withoutMove, string question, Action onYes = null)
        {
            object answer;
            if (g.TakePrompt(ctx, out answer)) return answer;
            var pk = WithMove(g, move);
            if (pk == null) { g.Message.Show(withoutMove); return false; }
            g.PromptPending = true;
            g.Message.Show(question, new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) { g.SetPrompt(ctx, false); return; }
                g.Message.Show(pk.Name(g.Project) + " used " + MoveName(g, move) + "!", null, -1, _ =>
                {
                    if (onYes != null) onYes();
                    g.SetPrompt(ctx, true);
                });
            });
            return false;
        }

        public static object Cut(Game g, Interpreter ctx)
        {
            return Ask(g, ctx, "CUT", "This tree looks like it can be cut down.", "This tree looks like it can be cut down!\\nWould you like to cut it?",
                () => g.Audio.PlaySe("Cut"));
        }

        public static object RockSmash(Game g, Interpreter ctx)
        {
            return Ask(g, ctx, "ROCKSMASH", "It's a rugged rock, but a Pokémon may be able to smash it.", "This rock seems breakable with a hidden move.\\nWould you like to use Rock Smash?",
                () => g.Audio.PlaySe("Rock Smash"));
        }

        /// <summary>Removes the event that called it (a cut tree or smashed rock) until the map is entered again.</summary>
        public static void SmashThisEvent(Game g, Interpreter ctx)
        {
            if (ctx == null || g.Map == null) return;
            var e = g.Map.Events.FirstOrDefault(x => x.Id == ctx.EventId);
            if (e != null) e.Erase();
        }

        /// <summary>A wild Pokémon may jump out of a smashed rock (25%, from the map's RockSmash encounters).</summary>
        public static void RockSmashEncounter(Game g)
        {
            if (g.Rng.Next(100) >= 25) return;
            EncounterFrom(g, new[] { "RockSmash" });
        }

        /// <summary>Starts a wild battle from one of the map's encounter tables, if it has one. Returns true if it did.</summary>
        public static bool EncounterFrom(Game g, string[] methods)
        {
            if (g.Map == null) return false;
            var table = g.Project.Encounters.FirstOrDefault(en => en.Map == g.Map.Id && en.Version == 0);
            if (table == null) return false;
            var m = table.Methods.FirstOrDefault(mm => methods.Contains(mm.Type) && mm.Slots.Count > 0);
            if (m == null) return false;
            int total = m.Slots.Sum(s => s.Chance), roll = g.Rng.Next(Math.Max(1, total));
            var slot = m.Slots[0];
            foreach (var s in m.Slots) { if (roll < s.Chance) { slot = s; break; } roll -= s.Chance; }
            int level = g.Rng.Next(slot.MinLevel, Math.Max(slot.MinLevel, slot.MaxLevel) + 1);
            g.StartWildBattle(slot.Species, level, null);
            return true;
        }

        public static void Headbutt(Game g)
        {
            var pk = WithMove(g, "HEADBUTT");
            if (pk == null) { g.Message.Show("A Pokémon could be in this tree. Maybe a Pokémon could shake it."); return; }
            g.Message.Show("A Pokémon could be in this tree. Would you like to use Headbutt?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) return;
                g.Message.Show(pk.Name(g.Project) + " did a Headbutt!", null, -1, _ =>
                {
                    g.Audio.PlaySe("Headbutt");
                    bool found = g.Rng.Next(100) < 50 && EncounterFrom(g, new[] { "HeadbuttHigh", "HeadbuttLow" });
                    if (!found) g.Message.Show("Nope. Nothing...");
                });
            });
        }

        // ------------------------------------------------------------------ Strength
        public static void Strength(Game g, Action after)
        {
            if (g.StrengthActive) { g.Message.Show("Strength made it possible to move boulders around.", null, -1, _ => { if (after != null) after(); }); return; }
            var pk = WithMove(g, "STRENGTH");
            if (pk == null) { g.Message.Show("It's a big boulder, but a Pokémon may be able to push it aside."); return; }
            g.Message.Show("It's a big boulder, but a Pokémon may be able to push it aside.\\nWould you like to use Strength?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) return;
                g.StrengthActive = true;
                g.Message.Show(pk.Name(g.Project) + " used Strength!\\n" + pk.Name(g.Project) + "'s Strength made it possible to move boulders around.");
            });
        }

        /// <summary>Pushes the calling boulder one tile the way the player faces (after Strength).</summary>
        public static void PushThisBoulder(Game g, Interpreter ctx)
        {
            if (ctx == null || g.Map == null) return;
            var e = g.Map.Events.FirstOrDefault(x => x.Id == ctx.EventId);
            if (e == null) return;
            if (!g.StrengthActive) return; // Strength is offered by pressing A at the boulder (Player.CheckActionTrigger)
            int d = g.Player.Direction;
            if (!e.Passable(e.X, e.Y, d)) { g.Audio.PlaySe("Player bump"); return; }
            var route = new PokeCreator.Maps.MoveRoute { Repeat = false };
            route.List.Add(new PokeCreator.Maps.EventCommand(d == 2 ? 1 : d == 4 ? 2 : d == 6 ? 3 : 4, 0));
            route.List.Add(new PokeCreator.Maps.EventCommand(0, 0));
            e.ForceMoveRoute(route);
            g.Audio.PlaySe("Strength push");
        }

        /// <summary>Essentials offers Strength when the player presses A at an event named "StrengthBoulder".</summary>
        public static bool IsBoulder(GameEvent e)
        {
            return e != null && e.Name != null && e.Name.IndexOf("strengthboulder", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------ Surf
        public static bool IsWater(Game g, int x, int y)
        {
            return g.Map != null && g.Map.Valid(x, y) && Array.IndexOf(GameMap.WaterTags, g.Map.TerrainTag(x, y)) >= 0 && g.Map.TerrainTag(x, y) != 8; // not up a waterfall
        }

        /// <summary>Pressing A at the water's edge.</summary>
        public static void OfferSurf(Game g)
        {
            var pk = WithMove(g, "SURF");
            if (pk == null) { g.Message.Show("The water is a deep blue color..."); return; }
            g.Message.Show("The water is a deep blue color...\\nWould you like to use Surf?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) return;
                g.Message.Show(pk.Name(g.Project) + " used Surf!", null, -1, _ => g.Player.StartSurfing());
            });
        }

        // ------------------------------------------------------------------ fishing
        public static readonly string[] Rods = { "OLDROD", "GOODROD", "SUPERROD" };

        public static void Fish(Game g, string rod)
        {
            var p = g.Player;
            int fx = p.X + (p.Direction == 4 ? -1 : p.Direction == 6 ? 1 : 0), fy = p.Y + (p.Direction == 8 ? -1 : p.Direction == 2 ? 1 : 0);
            if (!IsWater(g, fx, fy)) { g.Message.Show("Can't use that here."); return; }
            string method = rod == "OLDROD" ? "OldRod" : rod == "GOODROD" ? "GoodRod" : "SuperRod";
            int bite = rod == "OLDROD" ? 50 : rod == "GOODROD" ? 70 : 85;
            g.Message.Show(". . . . . . . . .", null, -1, _ =>
            {
                bool hasTable = g.Project.Encounters.Any(en => en.Map == g.Map.Id && en.Methods.Any(m => m.Type == method && m.Slots.Count > 0));
                if (!hasTable || g.Rng.Next(100) >= bite) { g.Message.Show("Not even a nibble..."); return; }
                g.Audio.PlaySe("Exclaim");
                g.Message.Show("Oh! A bite!", null, -1, __ => EncounterFrom(g, new[] { method }));
            });
        }

        // ------------------------------------------------------------------ berries
        public static void PickBerry(Game g, Interpreter ctx, string berry, int qty)
        {
            if (ctx == null) return;
            var it = g.Project.Items.FirstOrDefault(i => i.Id == berry);
            string name = it == null ? berry : (qty > 1 && it.NamePlural != null ? it.NamePlural : it.Name);
            g.Message.Show("There " + (qty > 1 ? "are " + qty + " " : "is 1 ") + name + "!\\nWant to pick " + (qty > 1 ? "them" : "it") + "?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) return;
                g.State.AddItem(berry, qty);
                // As in Essentials: self switch A shows the plant's empty-soil page
                g.State.SetSelf(ctx.MapId, ctx.EventId, "A", true);
                if (g.Map != null) g.Map.RefreshEvents();
                g.Audio.PlayMe("Berry get");
                g.Message.Show("\\PN picked the " + name + ".");
            });
        }

        public static void BerryPlant(Game g, Interpreter ctx)
        {
            g.Message.Show("It's soft, loamy soil.");
        }
    }
}
