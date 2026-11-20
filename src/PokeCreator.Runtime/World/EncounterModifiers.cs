using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// What the lead Pokémon and items do to wild encounters (as Essentials): Stench / White Smoke / Quick Feet,
    /// Cleanse Tag and Pure Incense make them rarer, Arena Trap / Illuminate / No Guard / Swarm more common, Sand Veil
    /// and Snow Cloak in their weather, White / Black Flute; Hustle / Pressure / Vital Spirit find stronger Pokémon,
    /// Intimidate / Keen Eye scare off weak ones, Static / Magnet Pull / Flash Fire / Harvest / Lightning Rod / Storm Drain
    /// attract a type, Synchronize passes on its nature and Cute Charm makes the opposite gender likelier.
    /// </summary>
    public static class EncounterModifiers
    {
        static Pokemon Lead(Game g) { return g.State.Party.FirstOrDefault(p => !p.IsEgg); }
        static string Ability(Game g, Pokemon pk) { return pk == null ? "" : (pk.AbilityId(g.Project) ?? ""); }

        public static double RateFactor(Game g)
        {
            var lead = Lead(g);
            double f = 1;
            if (lead != null)
            {
                if (lead.Item == "CLEANSETAG" || lead.Item == "PUREINCENSE") f *= 2.0 / 3;
                string ab = Ability(g, lead);
                string w = g.Weather != null && g.Weather.Type > 0 && g.Weather.Type < WeatherSystem.Names.Length ? WeatherSystem.Names[g.Weather.Type] : "";
                switch (ab)
                {
                    case "STENCH": case "WHITESMOKE": case "QUICKFEET": case "INFILTRATOR": f *= 0.5; break;
                    case "SNOWCLOAK": if (w == "Snow" || w == "Blizzard") f *= 0.5; break;
                    case "SANDVEIL": if (w == "Sandstorm") f *= 0.5; break;
                    case "ARENATRAP": case "ILLUMINATE": case "NOGUARD": f *= 2; break;
                    case "SWARM": f *= 1.5; break;
                }
            }
            int flute = g.State.Extras.FluteMap == (g.Map != null ? g.Map.Id : -1) ? g.State.Extras.Flute : 0;
            if (flute > 0) f *= 1.5; else if (flute < 0) f *= 0.5;
            return f;
        }

        static readonly Dictionary<string, string> TypeAttract = new Dictionary<string, string>
        {
            { "STATIC", "ELECTRIC" }, { "LIGHTNINGROD", "ELECTRIC" }, { "MAGNETPULL", "STEEL" }, { "FLASHFIRE", "FIRE" }, { "HARVEST", "GRASS" }, { "STORMDRAIN", "WATER" },
        };

        /// <summary>Static, Magnet Pull...: half the time the slot is chosen among Pokémon of the attracted type.</summary>
        public static EncounterSlot PickSlot(Game g, EncounterMethod m, EncounterSlot normal)
        {
            string type;
            if (!TypeAttract.TryGetValue(Ability(g, Lead(g)), out type) || g.Rng.Next(2) != 0) return normal;
            var typed = m.Slots.Where(s => { var d = g.Project.Species.FirstOrDefault(x => x.Id == s.Species); return d != null && d.Types.Contains(type); }).ToList();
            return typed.Count > 0 ? typed[g.Rng.Next(typed.Count)] : normal;
        }

        /// <summary>Hustle / Pressure / Vital Spirit: half the time the highest level of the slot.</summary>
        public static int AdjustLevel(Game g, EncounterSlot s, int level)
        {
            string ab = Ability(g, Lead(g));
            if ((ab == "HUSTLE" || ab == "PRESSURE" || ab == "VITALSPIRIT") && g.Rng.Next(2) == 0) return Math.Max(level, s.MaxLevel);
            return level;
        }

        /// <summary>Intimidate / Keen Eye: half of the encounters with Pokémon 5+ levels weaker than the lead don't happen.</summary>
        public static bool Scared(Game g, int level)
        {
            var lead = Lead(g); string ab = Ability(g, lead);
            return lead != null && !lead.Fainted && (ab == "INTIMIDATE" || ab == "KEENEYE") && level <= lead.Level - 5 && g.Rng.Next(2) == 0;
        }

        /// <summary>Synchronize and Cute Charm on the wild Pokémon just made.</summary>
        public static void ApplyToWild(Game g, Pokemon wild)
        {
            var lead = Lead(g); string ab = Ability(g, lead);
            if (lead == null) return;
            if (ab == "SYNCHRONIZE" && g.Rng.Next(2) == 0) wild.Nature = lead.Nature;
            if (ab == "CUTECHARM" && (lead.Gender == "M" || lead.Gender == "F") && (wild.Gender == "M" || wild.Gender == "F") && g.Rng.Next(3) < 2)
                wild.Gender = lead.Gender == "M" ? "F" : "M";
        }
    }
}
