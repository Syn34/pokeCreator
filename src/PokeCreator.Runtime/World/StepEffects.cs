using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// Essentials' per-step effects on the party: friendship from walking (every 128 steps) and, if the game turns it on,
    /// poison damage in the field (every 4 steps).
    /// </summary>
    public static class StepEffects
    {
        /// <summary>Returns true if a message was shown (the step's other checks should wait).</summary>
        public static bool OnStep(Game g)
        {
            var party = g.State.Party;
            if (g.State.Steps % 128 == 0)
                foreach (var pk in party)
                {
                    if (pk.IsEgg || pk.Fainted) continue;
                    pk.ChangeHappiness("walking");
                }
            // Soot Sack: walking through ashy grass collects soot ($player.soot)
            if (g.Map != null && g.Player != null && g.Map.TerrainTag(g.Player.X, g.Player.Y) == GameMap.TagSootGrass && g.State.Bag.ContainsKey("SOOTSACK"))
                GameStats.Inc(g, "soot");
            var s = g.Project.Settings;
            if (!s.PoisonInField || g.State.Steps % 4 != 0) return false;
            var poisoned = party.Where(pk => !pk.IsEgg && !pk.Fainted && (pk.Status == "POISON" || pk.Status == "TOXIC")).ToList();
            if (poisoned.Count == 0) return false;
            g.Flash(8);
            var lines = new List<string>();
            foreach (var pk in poisoned)
            {
                if (pk.HP > 1 || s.PoisonFaintInField) pk.HP = Math.Max(0, pk.HP - 1);
                string n = pk.Name(g.Project);
                if (pk.HP <= 0) { pk.Status = ""; lines.Add(n + " fainted!"); }
                else if (pk.HP == 1 && !s.PoisonFaintInField) { pk.Status = ""; lines.Add(n + " survived the poisoning.\\nThe poison faded away!"); }
            }
            if (lines.Count == 0) return false;
            Action next = null;
            int i = 0;
            next = () =>
            {
                if (i < lines.Count) { g.Message.Show(lines[i++], null, -1, _ => next()); return; }
                if (!g.PartyCanBattle)
                    g.Message.Show("\\PN has no usable Pokémon!\\n\\PN blacked out!", null, -1, _ => g.Whiteout());
            };
            next();
            return true;
        }

        /// <summary>Repel ran out: offers another one from the Bag (as Essentials).</summary>
        public static void RepelWoreOff(Game g)
        {
            var repels = new[] { "REPEL", "SUPERREPEL", "MAXREPEL" }.Where(id => g.State.Bag.ContainsKey(id) && g.State.Bag[id] > 0).ToList();
            if (repels.Count == 0) { g.Message.Show("The repelling effect wore off!"); return; }
            g.Message.Show("The repelling effect wore off! Would you like to use another one?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) return;
                Action<string> use = id =>
                {
                    var item = g.Project.Items.FirstOrDefault(x => x.Id == id);
                    var info = PokeCreator.Pkmn.ItemEffects.Info(g.Project, item);
                    g.State.AddItem(id, -1);
                    g.State.RepelSteps = info.Amount > 0 ? info.Amount : 100;
                    g.Audio.PlaySe("Repel");
                    g.Message.Show("\\PN used the " + (item != null && item.Name != null ? item.Name : id) + ".");
                };
                if (repels.Count == 1) { use(repels[0]); return; }
                var names = repels.Select(id => { var it = g.Project.Items.FirstOrDefault(x => x.Id == id); return it != null && it.Name != null ? it.Name : id; }).ToList();
                names.Add("Cancel");
                g.Message.Show("Which one?", names, names.Count - 1, k => { if (k >= 0 && k < repels.Count) use(repels[k]); });
            });
        }
    }
}
