using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// Mystery Gifts (Database → Mystery Gifts): a gift with no code is waiting for every player; one with a code is
    /// unlocked by typing it on the title screen. pbReceiveMysteryGift hands waiting gifts over (the delivery person).
    /// Redeemed codes are kept next to the save file until the game is loaded.
    /// </summary>
    public static class MysteryGift
    {
        static string CodesFile(PokeCreator.Projects.Project p) { return Path.Combine(SaveSystem.Folder(p), "MysteryGift.json"); }

        /// <summary>Codes typed on the title screen that the save hasn't picked up yet.</summary>
        public static List<string> RedeemedCodes(PokeCreator.Projects.Project p)
        {
            try { var f = CodesFile(p); return File.Exists(f) ? Json.Json.Deserialize<List<string>>(File.ReadAllText(f)) ?? new List<string>() : new List<string>(); }
            catch { return new List<string>(); }
        }

        /// <summary>Title screen: a code was entered. Returns the gift it unlocks, or null.</summary>
        public static MysteryGiftData Redeem(PokeCreator.Projects.Project p, string code)
        {
            code = (code ?? "").Trim();
            var gift = p.MysteryGifts.FirstOrDefault(m => !string.IsNullOrEmpty(m.Code) && string.Equals(m.Code.Trim(), code, StringComparison.OrdinalIgnoreCase));
            if (gift == null) return null;
            var codes = RedeemedCodes(p);
            if (!codes.Contains(gift.Id)) codes.Add(gift.Id);
            Directory.CreateDirectory(SaveSystem.Folder(p));
            File.WriteAllText(CodesFile(p), Json.Json.Write(codes, false));
            return gift;
        }

        /// <summary>Gifts this save can receive now: code-free ones and redeemed ones, not yet received.</summary>
        public static List<MysteryGiftData> Waiting(Game g)
        {
            var ex = g.State.Extras;
            if (ex.GiftsReceived == null) ex.GiftsReceived = new List<string>();
            var codes = RedeemedCodes(g.Project);
            return g.Project.MysteryGifts.Where(m => !ex.GiftsReceived.Contains(m.Id) && (string.IsNullOrEmpty(m.Code) || codes.Contains(m.Id))).ToList();
        }

        public static Pokemon MakePokemon(Game g, MysteryGiftData m)
        {
            var pk = Pokemon.Create(g.Project, m.Species, Math.Max(1, m.Level), g.Rng);
            if (m.Form > 0) pk.Form = m.Form;
            if (m.Shiny) pk.Shiny = true;
            if (!string.IsNullOrEmpty(m.Nature)) pk.Nature = m.Nature.ToUpperInvariant();
            if (!string.IsNullOrEmpty(m.Ability)) pk.Ability = m.Ability;
            if (!string.IsNullOrEmpty(m.HeldItem)) pk.Item = m.HeldItem;
            if (m.Moves != null && m.Moves.Count > 0)
            {
                pk.Moves.Clear();
                foreach (var id in m.Moves.Take(4))
                {
                    var md = g.Project.Moves.FirstOrDefault(x => x.Id == id);
                    if (md != null) pk.Moves.Add(new PokemonMove { Id = id, PP = md.TotalPP });
                }
            }
            pk.OTName = string.IsNullOrEmpty(m.OTName) ? g.State.PlayerName : m.OTName;
            pk.OTId = string.IsNullOrEmpty(m.OTName) ? g.State.TrainerId : g.Rng.Next(65536);
            if (!string.IsNullOrEmpty(m.Ball)) pk.Ball = m.Ball;
            pk.Heal(g.Project);
            return pk;
        }

        /// <summary>pbReceiveMysteryGift: hands over the next waiting gift (or the one with this ID). False if there's none / no room.</summary>
        public static bool Receive(Game g, string id)
        {
            var waiting = Waiting(g);
            var m = string.IsNullOrEmpty(id) ? waiting.FirstOrDefault() : waiting.FirstOrDefault(x => x.Id == id);
            if (m == null) { g.Message.Show("There are no gifts for you right now."); return false; }
            if (m.Kind == "Item")
            {
                if (string.IsNullOrEmpty(m.Item)) return false;
                g.State.AddItem(m.Item, Math.Max(1, m.Quantity));
                var it = g.Project.Items.FirstOrDefault(i => i.Id == m.Item);
                g.State.Extras.GiftsReceived.Add(m.Id);
                g.Audio.PlayMe("Item get");
                g.Message.Show("\\PN received " + (m.Quantity > 1 ? m.Quantity + " " : "") + (it != null ? it.Name : m.Item) + "!");
                return true;
            }
            var pk = MakePokemon(g, m);
            if (pk.Data(g.Project) == null) return false;
            if (g.State.Party.Count >= GameState.MaxParty && g.State.Storage.Count >= 30 * 40) { g.Message.Show("There's no room for the Pokémon!"); return false; }
            g.State.Register(pk);
            g.State.Extras.GiftsReceived.Add(m.Id);
            g.Audio.PlayMe("Pkmn get");
            if (g.State.Party.Count < GameState.MaxParty) { g.State.Party.Add(pk); g.Message.Show("\\PN received " + pk.Name(g.Project) + "!"); }
            else { g.State.Storage.Add(pk); g.Message.Show("\\PN received " + pk.Name(g.Project) + "! It was sent to the PC."); }
            return true;
        }
    }
}
