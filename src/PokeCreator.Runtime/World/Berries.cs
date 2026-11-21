using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Runtime.Script;
using PokeCreator.Runtime.UI;

namespace PokeCreator.Runtime.World
{
    /// <summary>A berry growing in a patch of soil (an event named BerryPlant, or one whose page calls pbBerryPlant).</summary>
    public class BerryPlantState
    {
        public string Berry { get; set; }
        /// <summary>When it was planted (UTC ticks); growth follows real time, as in Essentials.</summary>
        public long PlantedAt { get; set; }
        /// <summary>Moisture 0-100 and when it was last checked (UTC ticks).</summary>
        public int Moisture { get; set; }
        public long MoistureAt { get; set; }
        /// <summary>Growth stages (1-4) during which the plant was watered: more berries.</summary>
        public int WateredStages { get; set; }
        public int LastWateredStage { get; set; }
        /// <summary>How many times the plant has dropped its berries and grown again.</summary>
        public int Replants { get; set; }
        /// <summary>Mulch put in the soil before planting (GROWTHMULCH, DAMPMULCH, STABLEMULCH, GOOEYMULCH) or null.</summary>
        public string Mulch { get; set; }
    }

    /// <summary>
    /// Berry planting as in Essentials: plant a berry in soft soil, it sprouts, grows taller, flowers and fruits over
    /// several hours of real time; watering it (Wailmer Pail, Sprayduck...) gives more berries. Ripe berries fall after a
    /// while and the plant grows again, up to 9 times.
    /// </summary>
    public static class Berries
    {
        public static readonly string[] WateringCans = { "SPRAYDUCK", "SQUIRTBOTTLE", "WAILMERPAIL", "SPRINKLOTAD" };
        const int MaxReplants = 9;

        /// <summary>Hours per growth stage (Essentials' berry_plants.txt defaults).</summary>
        public static int HoursPerStage(string berry)
        {
            switch (berry)
            {
                case "CHERIBERRY": case "CHESTOBERRY": case "PECHABERRY": case "RAWSTBERRY": case "ASPEARBERRY": return 3;
                case "ORANBERRY": case "PERSIMBERRY": case "LEPPABERRY": return 4;
                case "FIGYBERRY": case "WIKIBERRY": case "MAGOBERRY": case "AGUAVBERRY": case "IAPAPABERRY": return 5;
                case "SITRUSBERRY": return 8;
                case "LUMBERRY": return 12;
                case "RAZZBERRY": case "BLUKBERRY": case "NANABBERRY": case "WEPEARBERRY": case "PINAPBERRY": return 2;
                case "LIECHIBERRY": case "GANLONBERRY": case "SALACBERRY": case "PETAYABERRY": case "APICOTBERRY": case "LANSATBERRY": case "STARFBERRY":
                case "ENIGMABERRY": case "MICLEBERRY": case "CUSTAPBERRY": case "JABOCABERRY": case "ROWAPBERRY": case "KEEBERRY": case "MARANGABERRY": return 24;
            }
            if (berry != null && berry.EndsWith("BERRY") && IsResistBerry(berry)) return 18;
            return 6;
        }

        static bool IsResistBerry(string b)
        {
            string[] r = { "OCCA", "PASSHO", "WACAN", "RINDO", "YACHE", "CHOPLE", "KEBIA", "SHUCA", "COBA", "PAYAPA", "TANGA", "CHARTI", "KASIB", "HABAN", "COLBUR", "BABIRI", "CHILAN", "ROSELI" };
            return r.Any(x => b == x + "BERRY");
        }

        static int MinYield(string berry) { return HoursPerStage(berry) >= 18 ? 1 : 2; }
        static int MaxYield(string berry) { return HoursPerStage(berry) >= 18 ? 5 : 5; }

        static string Key(int mapId, int eventId) { return mapId + "," + eventId; }

        public static BerryPlantState Get(Game g, int mapId, int eventId)
        {
            BerryPlantState s;
            return g.State.BerryPlants.TryGetValue(Key(mapId, eventId), out s) ? s : null;
        }

        /// <summary>0 = just planted, 1 sprouted, 2 taller, 3 flowering, 4 berries. Handles berries dropping and regrowing.</summary>
        public static int Stage(BerryPlantState p)
        {
            if (p == null) return -1;
            double hours = (DateTime.UtcNow.Ticks - p.PlantedAt) / (double)TimeSpan.TicksPerHour;
            // Growth Mulch: grows faster; Damp Mulch: slower
            double hps = HoursPerStage(p.Berry) * (p.Mulch == "GROWTHMULCH" ? 0.75 : p.Mulch == "DAMPMULCH" ? 1.5 : 1);
            // Ripe berries stay for 4 stages' time (longer with Stable Mulch), then drop and the plant starts again
            double cycle = hps * 4 + hps * 4 * (p.Mulch == "STABLEMULCH" ? 1.5 : 1);
            int maxReplants = p.Mulch == "GOOEYMULCH" ? MaxReplants + 5 : MaxReplants;
            while (hours >= cycle && p.Replants < maxReplants)
            {
                p.PlantedAt += (long)(cycle * TimeSpan.TicksPerHour);
                p.Replants++; p.WateredStages = 0; p.LastWateredStage = -1;
                hours -= cycle;
            }
            if (hours >= cycle) return -2; // withered away
            return Math.Min(4, (int)(hours / hps));
        }

        public static int Moisture(BerryPlantState p)
        {
            double hours = (DateTime.UtcNow.Ticks - p.MoistureAt) / (double)TimeSpan.TicksPerHour;
            // dries 15% an hour; twice as fast with Growth Mulch, half as fast with Damp Mulch
            double rate = p.Mulch == "GROWTHMULCH" ? 30 : p.Mulch == "DAMPMULCH" ? 7.5 : 15;
            return Math.Max(0, p.Moisture - (int)(hours * rate));
        }

        public static int Yield(BerryPlantState p)
        {
            int lo = MinYield(p.Berry), hi = MaxYield(p.Berry);
            return Math.Max(lo, Math.Min(hi, lo + ((hi - lo) * p.WateredStages + 3) / 4));
        }

        static string BerryName(Game g, string id, int qty = 1)
        {
            var it = g.Project.Items.FirstOrDefault(i => i.Id == id);
            if (it == null) return id;
            return qty > 1 && !string.IsNullOrEmpty(it.NamePlural) ? it.NamePlural : it.Name;
        }

        /// <summary>The sprite to show for a berry plant event, or null to keep its page's sprite (no plant).</summary>
        public static bool Graphic(Game g, int mapId, int eventId, out string charset, out int direction)
        {
            charset = null; direction = 2;
            var p = Get(g, mapId, eventId);
            if (p == null) return false;
            int st = Stage(p);
            if (st == -2) { g.State.BerryPlants.Remove(Key(mapId, eventId)); return false; }
            string tree = "berrytree_" + p.Berry;
            bool hasTree = g.Project.FindAsset("Graphics/Characters", tree) != null;
            if (st == 0)
            {
                charset = g.Project.FindAsset("Graphics/Characters", "berrytreeplanted") != null ? "berrytreeplanted" : hasTree ? tree : "";
                direction = 2;
            }
            else { charset = hasTree ? tree : ""; direction = st == 1 ? 2 : st == 2 ? 4 : st == 3 ? 6 : 8; }
            return true;
        }

        /// <summary>pbBerryPlant: talk to the soil or the plant growing in it.</summary>
        public static void Interact(Game g, Interpreter ctx)
        {
            if (ctx == null) return;
            int mapId = ctx.MapId, eventId = ctx.EventId;
            var p = Get(g, mapId, eventId);
            int st = Stage(p);
            if (p == null || st == -2)
            {
                if (st == -2) g.State.BerryPlants.Remove(Key(mapId, eventId));
                AskPlant(g, mapId, eventId);
                return;
            }
            string name = BerryName(g, p.Berry);
            if (st == 4)
            {
                int qty = Yield(p);
                string plural = BerryName(g, p.Berry, qty);
                g.Message.Show("There " + (qty > 1 ? "are " + qty + " " : "is 1 ") + plural + "!\\nWant to pick " + (qty > 1 ? "them" : "it") + "?", new List<string> { "Yes", "No" }, 1, c =>
                {
                    if (c != 0) return;
                    g.State.AddItem(p.Berry, qty);
                    g.State.BerryPlants.Remove(Key(mapId, eventId));
                    g.Audio.PlayMe("Berry get");
                    g.Message.Show("\\PN picked the " + plural + ".", null, -1, _ => { g.Message.Show("The soil returned to its soft and loamy state."); Refresh(g); });
                });
                return;
            }
            string text = st == 0 ? "A " + name + " was planted here." : st == 1 ? "The " + name + " plant has sprouted." : st == 2 ? "The " + name + " plant is growing bigger." : "This " + name + " plant is in bloom!";
            int moist = Moisture(p);
            text += moist >= 50 ? "" : moist > 0 ? "\\nThe soil is a little dry." : "\\nThe soil is dry.";
            var can = WateringCans.FirstOrDefault(i => g.State.ItemCount(i) > 0);
            if (can == null || moist >= 100) { g.Message.Show(text); return; }
            g.Message.Show(text, null, -1, _ =>
                g.Message.Show("Want to sprinkle some water with the " + BerryName(g, can) + "?", new List<string> { "Yes", "No" }, 1, c =>
                {
                    if (c != 0) return;
                    p.Moisture = 100; p.MoistureAt = DateTime.UtcNow.Ticks;
                    if (p.LastWateredStage != st) { p.LastWateredStage = st; p.WateredStages = Math.Min(4, p.WateredStages + 1); }
                    g.Message.Show("\\PN watered the plant.\\nThe plant seemed to be delighted.");
                }));
        }

        public static readonly string[] Mulches = { "GROWTHMULCH", "DAMPMULCH", "STABLEMULCH", "GOOEYMULCH" };

        static void AskPlant(Game g, int mapId, int eventId)
        {
            bool anyBerry = g.State.Bag.Any(kv => kv.Value > 0 && IsBerry(g, kv.Key));
            if (!anyBerry) { g.Message.Show("It's soft, loamy soil."); return; }
            // Mulch first (as Essentials), if the player has any
            if (Mulches.Any(m => g.State.ItemCount(m) > 0))
            {
                g.Message.Show("It's soft, loamy soil.\\nWant to fertilize the soil?", new List<string> { "Yes", "No" }, 1, c =>
                {
                    if (c != 0) { AskBerry(g, mapId, eventId, null); return; }
                    g.PushScene(new BagScene(g, mulch =>
                    {
                        if (mulch == null || !Mulches.Contains(mulch) || g.State.ItemCount(mulch) <= 0) { AskBerry(g, mapId, eventId, null); return; }
                        g.Message.Show("The " + BerryName(g, mulch) + " was scattered on the soil.", null, -1, _ => AskBerry(g, mapId, eventId, mulch));
                    }, it => Mulches.Contains(it.Id), null));
                });
                return;
            }
            AskBerry(g, mapId, eventId, null);
        }

        static void AskBerry(Game g, int mapId, int eventId, string mulch)
        {
            g.Message.Show(mulch == null ? "It's soft, loamy soil.\\nWant to plant a Berry?" : "Want to plant a Berry?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) return;
                g.PushScene(new BagScene(g, berry =>
                {
                    if (berry == null || !IsBerry(g, berry) || g.State.ItemCount(berry) <= 0) return;
                    g.State.RemoveItem(berry, 1);
                    if (mulch != null) g.State.RemoveItem(mulch, 1);
                    long now = DateTime.UtcNow.Ticks;
                    g.State.BerryPlants[Key(mapId, eventId)] = new BerryPlantState { Berry = berry, PlantedAt = now, Moisture = 100, MoistureAt = now, LastWateredStage = -1, Mulch = mulch };
                    Refresh(g);
                    g.Message.Show("\\PN planted a " + BerryName(g, berry) + " in the soft, loamy soil.");
                }, it => IsBerry(g, it.Id), null));
            });
        }

        public static bool IsBerry(Game g, string id)
        {
            var it = g.Project.Items.FirstOrDefault(i => i.Id == id);
            if (it == null) return false;
            return it.Pocket == 5 || (id.EndsWith("BERRY") && it.Pocket != 8);
        }

        /// <summary>Re-applies berry plant sprites on the current map (after planting, picking, or as plants grow).</summary>
        public static void Refresh(Game g)
        {
            if (g.Map == null) return;
            foreach (var ev in g.Map.Events) ApplyGraphic(g, ev);
        }

        public static bool IsBerryPlantEvent(GameEvent ev)
        {
            if (ev.Name == "BerryPlant") return true;
            return ev.Data.Pages.Any(pg => pg.List != null && pg.List.Any(c => c.Code == 355 && c.Params.Count > 0 && (c.Params[0] as string ?? "").Trim() == "pbBerryPlant"));
        }

        public static void ApplyGraphic(Game g, GameEvent ev)
        {
            if (!IsBerryPlantEvent(ev)) return;
            string cs; int dir;
            if (!Graphic(g, ev.Owner.Id, ev.Id, out cs, out dir))
            {
                // No plant: the page's own sprite (picked trees show their soil page)
                if (ev.Page != null && ev.BerryOverride) { ev.BerryOverride = false; ev.CharacterName = ev.Page.Graphic.CharacterName ?? ""; ev.Direction = ev.Page.Graphic.Direction == 0 ? 2 : ev.Page.Graphic.Direction; }
                return;
            }
            ev.BerryOverride = true;
            ev.CharacterName = cs; ev.Direction = dir; ev.TileId = 0;
        }
    }
}
