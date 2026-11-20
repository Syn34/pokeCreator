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
            string kind = m.Type.EndsWith("Rod") ? "Fishing" : m.Type.StartsWith("Water") ? "Water" : m.Type.StartsWith("Cave") ? "Cave" : m.Type.StartsWith("Land") ? "Land" : m.Type;
            if (Roaming.TryEncounter(g, kind)) return true;
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
        /// <summary>Key items used straight from the Bag (besides rods).</summary>
        public static readonly string[] DirectKeyItems = { "BICYCLE", "MACHBIKE", "ACROBIKE", "TOWNMAP", "ESCAPEROPE", "ITEMFINDER", "DOWSINGMACHINE", "DOWSINGMCHN", "SPRAYDUCK", "SQUIRTBOTTLE", "WAILMERPAIL", "SPRINKLOTAD" };

        /// <summary>
        /// Itemfinder / Dowsing Machine (as in Essentials): looks for the nearest event named "HiddenItem" that still has
        /// something to pick up, within 8 tiles across and 6 up or down, and turns the player toward it.
        /// </summary>
        public static void Itemfinder(Game g, string itemName)
        {
            var p = g.Player;
            if (g.Map == null || p == null) return;
            GameEvent best = null; int bestD = int.MaxValue;
            foreach (var ev in g.Map.Events)
            {
                if (ev.Name != "HiddenItem" || ev.Erased || !ev.HasCommands) continue;
                int dx = ev.X - p.X, dy = ev.Y - p.Y;
                if (Math.Abs(dx) > 8 || Math.Abs(dy) > 6) continue;
                int d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = ev; }
            }
            if (best == null) { g.Message.Show("... \\| ... \\| ... \\| ...\\nThere's no response."); return; }
            int ox = best.X - p.X, oy = best.Y - p.Y;
            if (ox == 0 && oy == 0) { g.Message.Show("The " + itemName + "'s indicating something right underfoot!"); return; }
            int dir = Math.Abs(ox) > Math.Abs(oy) ? (ox < 0 ? 4 : 6) : (oy < 0 ? 8 : 2);
            p.Direction = dir;
            g.Message.Show("Huh? The " + itemName + " is responding!\\nThere's an item buried around here!");
        }

        /// <summary>A watering can used from the Bag waters the berry plant the player faces.</summary>
        public static void WaterFacingPlant(Game g)
        {
            var p = g.Player;
            int fx = p.X + (p.Direction == 4 ? -1 : p.Direction == 6 ? 1 : 0), fy = p.Y + (p.Direction == 8 ? -1 : p.Direction == 2 ? 1 : 0);
            var ev = g.Map != null ? g.Map.EventsAt(fx, fy).FirstOrDefault(Berries.IsBerryPlantEvent) : null;
            var plant = ev != null ? Berries.Get(g, g.Map.Id, ev.Id) : null;
            if (plant == null || Berries.Stage(plant) < 0 || Berries.Stage(plant) >= 4) { g.Message.Show("Can't use that here."); return; }
            int st = Berries.Stage(plant);
            plant.Moisture = 100; plant.MoistureAt = DateTime.UtcNow.Ticks;
            if (plant.LastWateredStage != st) { plant.LastWateredStage = st; plant.WateredStages = Math.Min(4, plant.WateredStages + 1); }
            g.Message.Show("\\PN watered the plant.\\nThe plant seemed to be delighted.");
        }

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

        // ------------------------------------------------------------------ Fly, Teleport, Dig, Escape Rope
        static bool Outdoor(Game g) { return g.Map != null && g.Map.Data.Properties != null && g.Map.Data.Properties.Outdoor; }

        /// <summary>Opens the Town Map to choose where to fly. closeMenus closes the party/pause screens first.</summary>
        public static void Fly(Game g, Pokemon pk)
        {
            if (!Outdoor(g)) { g.Message.Show("Can't use that here."); return; }
            if (g.Project.TownMaps.Count == 0) { g.Message.Show("There is no Town Map to fly with."); return; }
            g.PushScene(new UI.TownMapScene(g, true, p =>
            {
                g.CloseAllScenes();
                g.Message.Show(pk.Name(g.Project) + " used Fly!", null, -1, _ =>
                {
                    g.Audio.PlaySe("Fly");
                    StopRiding(g);
                    g.RequestTransfer(p.FlyMap, p.FlyX, p.FlyY, 2, true);
                });
            }));
        }

        public static void Teleport(Game g, Pokemon pk)
        {
            if (!Outdoor(g) || g.State.HealMap <= 0) { g.Message.Show("Can't use that here."); return; }
            g.CloseAllScenes();
            g.Message.Show("Want to return to the healing spot used last in " + MapName(g, g.State.HealMap) + "?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) return;
                g.Message.Show(pk.Name(g.Project) + " used Teleport!", null, -1, _ =>
                {
                    StopRiding(g);
                    g.RequestTransfer(g.State.HealMap, g.State.HealX, g.State.HealY, g.State.HealDir > 0 ? g.State.HealDir : 2, true);
                });
            });
        }

        /// <summary>Dig and the Escape Rope: back to the cave entrance recorded by pbSetEscapePoint.</summary>
        public static bool CanEscape(Game g) { return g.State.EscapePoint != null && g.State.EscapePoint.Count >= 4 && !Outdoor(g); }

        public static void Escape(Game g, string usedText)
        {
            if (!CanEscape(g)) { g.Message.Show("Can't use that here."); return; }
            var e = g.State.EscapePoint;
            g.CloseAllScenes();
            g.Message.Show(usedText, null, -1, _ =>
            {
                StopRiding(g);
                g.State.EscapePoint = null;
                g.RequestTransfer(e[0], e[1], e[2], e[3], true);
            });
        }

        /// <summary>pbSetEscapePoint: one step back from where the player stands, facing away (called before entering a cave).</summary>
        public static void SetEscapePoint(Game g)
        {
            var p = g.Player; int x = p.X, y = p.Y, d = 2;
            switch (p.Direction) { case 2: y--; d = 8; break; case 4: x++; d = 6; break; case 6: x--; d = 4; break; case 8: y++; d = 2; break; }
            g.State.EscapePoint = new List<int> { g.Map.Id, x, y, d };
        }

        static string MapName(Game g, int id)
        {
            var info = g.Project.MapInfos.FirstOrDefault(m => m.Id == id);
            try { var md = g.Project.LoadMap(id); if (md != null && md.Properties != null && !string.IsNullOrEmpty(md.Properties.Name)) return md.Properties.Name; } catch { }
            return info != null ? info.Name : "map " + id;
        }

        public static void SweetScent(Game g, Pokemon pk)
        {
            g.CloseAllScenes();
            g.Message.Show(pk.Name(g.Project) + " used Sweet Scent!", null, -1, _ =>
            {
                bool found = EncounterFrom(g, g.Player.Surfing ? new[] { "Water" } : new[] { "Land", "LandDay", "LandNight", "LandMorning", "Cave" });
                if (!found) g.Message.Show("There appears to be nothing here...");
            });
        }

        // ------------------------------------------------------------------ bicycle
        public static bool CanBike(Game g)
        {
            var pr = g.Map == null ? null : g.Map.Data.Properties;
            return pr != null && (pr.Bicycle || pr.BicycleAlways || pr.Outdoor);
        }

        public static void ToggleBike(Game g)
        {
            var p = g.Player;
            if (p.Cycling)
            {
                if (g.Map != null && g.Map.Data.Properties != null && g.Map.Data.Properties.BicycleAlways) { g.Message.Show("You can't dismount your Bike here."); return; }
                g.CloseAllScenes();
                p.Cycling = false; g.RestoreMapBgm();
                return;
            }
            if (p.Surfing || !CanBike(g)) { g.Message.Show("Can't use that here."); return; }
            g.CloseAllScenes();
            p.Cycling = true;
            string bgm = g.Project.Settings.BicycleBGM;
            if (!string.IsNullOrEmpty(bgm)) g.Audio.PlayBgm(bgm);
        }

        /// <summary>Gets off the bike/water before warping somewhere else.</summary>
        public static void StopRiding(Game g) { g.Player.Cycling = false; g.Player.Surfing = false; g.Player.Diving = false; }

        // ------------------------------------------------------------------ field moves from the party screen
        public static readonly string[] PartyMoves = { "CUT", "FLY", "SURF", "STRENGTH", "ROCKSMASH", "HEADBUTT", "FLASH", "WATERFALL", "DIVE", "TELEPORT", "DIG", "SWEETSCENT" };

        public static List<string> FieldMovesOf(Pokemon pk)
        {
            var list = new List<string>();
            if (pk == null || pk.IsEgg) return list;
            foreach (var m in pk.Moves) if (Array.IndexOf(PartyMoves, m.Id) >= 0 && !list.Contains(m.Id)) list.Add(m.Id);
            return list;
        }

        static GameEvent Facing(Game g, string namePart)
        {
            var p = g.Player; int fx = p.X + (p.Direction == 4 ? -1 : p.Direction == 6 ? 1 : 0), fy = p.Y + (p.Direction == 8 ? -1 : p.Direction == 2 ? 1 : 0);
            return g.Map.EventsAt(fx, fy).FirstOrDefault(e => e.Name != null && e.Name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0 && e.CharacterName.Length > 0);
        }

        /// <summary>Using a field move from the party screen (as Essentials' HiddenMoveHandlers).</summary>
        public static void UseFromParty(Game g, Pokemon pk, string move)
        {
            string name = pk.Name(g.Project);
            switch (move)
            {
                case "FLY": Fly(g, pk); return;
                case "TELEPORT": Teleport(g, pk); return;
                case "DIG": Escape(g, name + " used Dig!"); return;
                case "SWEETSCENT": SweetScent(g, pk); return;
                case "FLASH": Flash(g, pk); return;
                case "DIVE": g.CloseAllScenes(); if (!TryDive(g)) g.Message.Show("Can't use that here."); return;
                case "WATERFALL":
                    {
                        var p = g.Player; int fx = p.X + (p.Direction == 4 ? -1 : p.Direction == 6 ? 1 : 0), fy = p.Y + (p.Direction == 8 ? -1 : p.Direction == 2 ? 1 : 0);
                        if (!p.Surfing || g.Map.TerrainTag(fx, fy) != GameMap.TagWaterfall) { g.Message.Show("Can't use that here."); return; }
                        g.CloseAllScenes();
                        g.Message.Show(name + " used Waterfall!", null, -1, _ => p.ClimbWaterfall(true));
                        return;
                    }
                case "SURF":
                    {
                        var p = g.Player; int fx = p.X + (p.Direction == 4 ? -1 : p.Direction == 6 ? 1 : 0), fy = p.Y + (p.Direction == 8 ? -1 : p.Direction == 2 ? 1 : 0);
                        if (p.Surfing || p.Cycling || !IsWater(g, fx, fy) || !g.Map.SurfPassable(fx, fy, 10 - p.Direction)) { g.Message.Show("Can't use that here."); return; }
                        g.CloseAllScenes();
                        g.Message.Show(name + " used Surf!", null, -1, _ => p.StartSurfing());
                        return;
                    }
                case "CUT":
                    {
                        var e = Facing(g, "cuttree");
                        if (e == null) { g.Message.Show("Can't use that here."); return; }
                        g.CloseAllScenes();
                        g.Message.Show(name + " used Cut!", null, -1, _ => { g.Audio.PlaySe("Cut"); e.Erase(); });
                        return;
                    }
                case "ROCKSMASH":
                    {
                        var e = Facing(g, "smashrock");
                        if (e == null) { g.Message.Show("Can't use that here."); return; }
                        g.CloseAllScenes();
                        g.Message.Show(name + " used Rock Smash!", null, -1, _ => { g.Audio.PlaySe("Rock Smash"); e.Erase(); RockSmashEncounter(g); });
                        return;
                    }
                case "STRENGTH":
                    {
                        if (Facing(g, "strengthboulder") == null || g.StrengthActive) { g.Message.Show("Can't use that here."); return; }
                        g.CloseAllScenes();
                        g.StrengthActive = true;
                        g.Message.Show(name + " used Strength!\n" + name + "'s Strength made it possible to move boulders around.");
                        return;
                    }
                case "HEADBUTT":
                    {
                        if (Facing(g, "headbutttree") == null) { g.Message.Show("Can't use that here."); return; }
                        g.CloseAllScenes();
                        g.Message.Show(name + " did a Headbutt!", null, -1, _ =>
                        {
                            g.Audio.PlaySe("Headbutt");
                            if (!(g.Rng.Next(100) < 50 && EncounterFrom(g, new[] { "HeadbuttHigh", "HeadbuttLow" }))) g.Message.Show("Nope. Nothing...");
                        });
                        return;
                    }
            }
        }

        // ------------------------------------------------------------------ Flash, Waterfall, Dive
        public static bool IsDark(Game g) { return g.Map != null && g.Map.Data.Properties != null && g.Map.Data.Properties.DarkMap; }

        public static void Flash(Game g, Pokemon pk)
        {
            if (!IsDark(g) || g.FlashUsed) { g.Message.Show("Can't use that here."); return; }
            g.CloseAllScenes();
            g.Message.Show(pk.Name(g.Project) + " used Flash!", null, -1, _ => { g.FlashUsed = true; g.Audio.PlaySe("Flash"); });
        }

        /// <summary>Surfing and facing a waterfall: climb it with Waterfall.</summary>
        public static void OfferWaterfall(Game g)
        {
            var pk = WithMove(g, "WATERFALL");
            if (pk == null) { g.Message.Show("A wall of water is crashing down with a mighty roar."); return; }
            g.Message.Show("It's a large waterfall. Would you like to use Waterfall?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) return;
                g.Message.Show(pk.Name(g.Project) + " used Waterfall!", null, -1, _ => g.Player.ClimbWaterfall(true));
            });
        }

        /// <summary>The map this deep water dives to, or 0.</summary>
        static int DiveMap(Game g) { var p = g.Map == null ? null : g.Map.Data.Properties; return p != null ? p.DiveMap : 0; }

        /// <summary>The map above this underwater map (one whose "Dive map ID" points here), or 0.</summary>
        static int SurfaceMap(Game g)
        {
            if (g.Map == null) return 0;
            int here = g.Map.Id;
            foreach (var info in g.Project.MapInfos)
            {
                try { var md = g.Project.LoadMap(info.Id); if (md != null && md.Properties != null && md.Properties.DiveMap == here) return info.Id; } catch { }
            }
            return 0;
        }

        /// <summary>Pressing A while surfing on deep water (dive down) or while underwater (surface).</summary>
        public static bool TryDive(Game g)
        {
            var p = g.Player;
            if (p.Diving)
            {
                int up = SurfaceMap(g);
                if (up <= 0) return false;
                var above = g.Project.LoadMap(up);
                var tmp = new GameMap(g.Project, above, g, false);
                if (!tmp.Valid(p.X, p.Y) || tmp.TerrainTag(p.X, p.Y) != GameMap.TagDeepWater) return false;
                var pk = WithMove(g, "DIVE");
                if (pk == null) { g.Message.Show("Light is filtering down from above. A Pokémon may be able to surface here."); return true; }
                g.Message.Show("Light is filtering down from above. Would you like to use Dive?", new List<string> { "Yes", "No" }, 1, c =>
                {
                    if (c != 0) return;
                    g.Message.Show(pk.Name(g.Project) + " used Dive!", null, -1, _ =>
                    {
                        p.Diving = false; p.Surfing = true;
                        g.RequestTransfer(up, p.X, p.Y, p.Direction, true);
                    });
                });
                return true;
            }
            if (!p.Surfing || g.Map.TerrainTag(p.X, p.Y) != GameMap.TagDeepWater) return false;
            int down = DiveMap(g);
            if (down <= 0) return false;
            var diver = WithMove(g, "DIVE");
            if (diver == null) { g.Message.Show("The sea is deep here. A Pokémon may be able to go underwater."); return true; }
            g.Message.Show("The sea is deep here. Would you like to use Dive?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) return;
                g.Message.Show(diver.Name(g.Project) + " used Dive!", null, -1, _ =>
                {
                    p.Surfing = false; p.Diving = true;
                    g.RequestTransfer(down, p.X, p.Y, p.Direction, true);
                });
            });
            return true;
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

        public static void BerryPlant(Game g, Interpreter ctx) { Berries.Interact(g, ctx); }
    }
}
