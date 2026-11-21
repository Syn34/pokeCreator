using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Projects;

namespace PokeCreator.Pkmn
{
    /// <summary>
    /// Which form a Pokémon has (Essentials' MultipleForms): random or seasonal forms when it's created (Unown letters,
    /// Deerling seasons, Flabébé flowers, Tatsugiri, Squawkabilly...), forms set by a held item (Arceus' Plates, Silvally's
    /// Memories, Genesect's Drives, Giratina's Griseous Orb, Zacian / Zamazenta's Rusted items, Ogerpon's masks), and the
    /// Gen 9 forms the built-in data is missing.
    /// </summary>
    public static class FormRules
    {
        static readonly string[] LowKeyNatures = { "LONELY", "BOLD", "RELAXED", "TIMID", "SERIOUS", "MODEST", "MILD", "QUIET", "BASHFUL", "CALM", "GENTLE", "CAREFUL" };

        static bool HasForm(Project p, string species, int form) { return form == 0 || p.Species.Any(s => s.Id == species + "_" + form); }
        static int FormCount(Project p, string species) { int n = 1; while (p.Species.Any(s => s.Id == species + "_" + n)) n++; return n; }

        /// <summary>The form a newly created Pokémon gets (before its held item is considered).</summary>
        /// <summary>Essentials' season: it changes every month (January spring, February summer, March autumn, April winter, May spring...).</summary>
        public static int Season() { return (DateTime.Now.Month - 1) % 4; }

        /// <summary>
        /// Forms that follow the clock and calendar (as Essentials): Deerling and Sawsbuck always show the current season,
        /// Shaymin's Sky Forme goes back to Land Forme at night. Returns true if anything changed.
        /// </summary>
        public static bool UpdateTimeForms(Project p, IEnumerable<Pokemon> mons)
        {
            bool changed = false;
            int season = Season(), hour = DateTime.Now.Hour;
            foreach (var pk in mons)
            {
                if (pk == null || pk.IsEgg) continue;
                int f = pk.Form;
                if ((pk.Species == "DEERLING" || pk.Species == "SAWSBUCK") && HasForm(p, pk.Species, season)) f = season;
                if (pk.Species == "SHAYMIN" && pk.Form == 1 && (hour >= 20 || hour < 5)) f = 0;
                if (f != pk.Form) { pk.Form = f; changed = true; }
            }
            return changed;
        }

        /// <summary>Burmy's cloak after a battle it took part in: grass → Plant, sand / caves / rocks → Sandy, buildings → Trash.</summary>
        public static void BurmyCloak(Project p, Pokemon pk, string environment)
        {
            if (pk == null || pk.Species != "BURMY") return;
            string e = (environment ?? "").ToLowerInvariant();
            int f = e.Contains("grass") || e.Contains("forest") || e == "" ? 0 : e.Contains("sand") || e.Contains("cave") || e.Contains("rock") || e.Contains("mountain") ? 1 : 2;
            if (HasForm(p, "BURMY", f)) pk.Form = f;
        }

        public static int FormOnCreation(Project p, Pokemon pk, Random rng)
        {
            switch (pk.Species)
            {
                case "UNOWN": case "FLABEBE": case "FLOETTE": case "FLORGES": case "VIVILLON": case "TATSUGIRI": case "SQUAWKABILLY":
                    {
                        int n = FormCount(p, pk.Species);
                        // Floette's Eternal Flower (form 5) isn't found in the wild
                        if (pk.Species == "FLOETTE" || pk.Species == "FLABEBE" || pk.Species == "FLORGES") n = Math.Min(n, 5);
                        return rng.Next(n);
                    }
                case "DEERLING": case "SAWSBUCK":
                    {
                        int season = Season();
                        return HasForm(p, pk.Species, season) ? season : 0;
                    }
                case "TOXTRICITY": return LowKeyNatures.Contains(pk.Nature) && HasForm(p, pk.Species, 1) ? 1 : 0;
                case "MAUSHOLD": case "DUDUNSPARCE": return rng.Next(100) == 0 && HasForm(p, pk.Species, 1) ? 1 : 0;
            }
            return pk.Form;
        }

        /// <summary>The form a held item gives, or -1 if the item doesn't decide this species' form.</summary>
        public static int ItemForm(Project p, Pokemon pk)
        {
            string item = pk.Item ?? "";
            switch (pk.Species)
            {
                case "ARCEUS":
                    {
                        string type = PlateType(item) ?? "NORMAL";
                        return FormByTypeName(p, pk.Species, type);
                    }
                case "SILVALLY":
                    {
                        string type = item.EndsWith("MEMORY") ? item.Substring(0, item.Length - 6) : "NORMAL";
                        return FormByTypeName(p, pk.Species, type);
                    }
                case "GENESECT":
                    switch (item) { case "SHOCKDRIVE": return 1; case "BURNDRIVE": return 2; case "CHILLDRIVE": return 3; case "DOUSEDRIVE": return 4; default: return 0; }
                case "GIRATINA": return item == "GRISEOUSORB" || item == "GRISEOUSCORE" ? 1 : 0;
                case "DIALGA": return item == "ADAMANTCRYSTAL" && HasForm(p, "DIALGA", 1) ? 1 : 0;
                case "PALKIA": return item == "LUSTROUSGLOBE" && HasForm(p, "PALKIA", 1) ? 1 : 0;
                case "ZACIAN": return item == "RUSTEDSWORD" ? 1 : 0;
                case "ZAMAZENTA": return item == "RUSTEDSHIELD" ? 1 : 0;
                case "OGERPON":
                    switch (item) { case "WELLSPRINGMASK": return 1; case "HEARTHFLAMEMASK": return 2; case "CORNERSTONEMASK": return 3; default: return 0; }
            }
            return -1;
        }

        static int FormByTypeName(Project p, string species, string type)
        {
            var td = p.Types.FirstOrDefault(t => t.Id == type);
            string tn = td != null && td.Name != null ? td.Name : type;
            for (int f = 0; ; f++)
            {
                var s = p.Species.FirstOrDefault(x => x.Id == (f == 0 ? species : species + "_" + f));
                if (s == null) return 0;
                if (s.Types != null && s.Types.Count > 0 && s.Types[0] == type) return f;
                if (!string.IsNullOrEmpty(s.FormName) && s.FormName.IndexOf(tn, StringComparison.OrdinalIgnoreCase) >= 0) return f;
                if (f > 30) return 0;
            }
        }

        static string PlateType(string item)
        {
            switch (item)
            {
                case "FLAMEPLATE": return "FIRE"; case "SPLASHPLATE": return "WATER"; case "ZAPPLATE": return "ELECTRIC"; case "MEADOWPLATE": return "GRASS";
                case "ICICLEPLATE": return "ICE"; case "FISTPLATE": return "FIGHTING"; case "TOXICPLATE": return "POISON"; case "EARTHPLATE": return "GROUND";
                case "SKYPLATE": return "FLYING"; case "MINDPLATE": return "PSYCHIC"; case "INSECTPLATE": return "BUG"; case "STONEPLATE": return "ROCK";
                case "SPOOKYPLATE": return "GHOST"; case "DRACOPLATE": return "DRAGON"; case "DREADPLATE": return "DARK"; case "IRONPLATE": return "STEEL";
                case "PIXIEPLATE": return "FAIRY";
            }
            return null;
        }

        /// <summary>Call after a Pokémon's held item changes: Arceus, Genesect, Giratina... change form to match.</summary>
        public static void UpdateItemForm(Project p, Pokemon pk)
        {
            if (pk == null || pk.IsEgg) return;
            int f = ItemForm(p, pk);
            if (f >= 0 && f != pk.Form && HasForm(p, pk.Species, f))
            {
                int oldMax = pk.TotalHP(p);
                pk.Form = f;
                int newMax = pk.TotalHP(p);
                if (newMax != oldMax) pk.HP = Math.Max(pk.HP > 0 ? 1 : 0, Math.Min(newMax, pk.HP + newMax - oldMax));
            }
        }

        // ------------------------------------------------------------------ Gen 9 forms
        class FormDef { public string Species; public string[] Names; public Action<SpeciesData, int> Apply; }

        static readonly FormDef[] Gen9 =
        {
            new FormDef { Species = "TATSUGIRI", Names = new[] { "Curly Form", "Droopy Form", "Stretchy Form" } },
            new FormDef { Species = "SQUAWKABILLY", Names = new[] { "Green Plumage", "Blue Plumage", "Yellow Plumage", "White Plumage" },
                          Apply = (s, f) => { if (f >= 2) { s.Abilities = new List<string> { "INTIMIDATE", "HUSTLE" }; s.HiddenAbilities = new List<string> { "SHEERFORCE" }; } } },
            new FormDef { Species = "MAUSHOLD", Names = new[] { "Family of Four", "Family of Three" } },
            new FormDef { Species = "DUDUNSPARCE", Names = new[] { "Two-Segment Form", "Three-Segment Form" } },
            new FormDef { Species = "GIMMIGHOUL", Names = new[] { "Chest Form", "Roaming Form" } },
            new FormDef { Species = "POLTCHAGEIST", Names = new[] { "Counterfeit Form", "Artisan Form" } },
            new FormDef { Species = "SINISTCHA", Names = new[] { "Unremarkable Form", "Masterpiece Form" } },
            new FormDef { Species = "PALAFIN", Names = new[] { "Zero Form", "Hero Form" },
                          Apply = (s, f) => { if (f == 1) s.BaseStats = new List<int> { 100, 160, 97, 100, 106, 87 }; } },
            new FormDef { Species = "OGERPON", Names = new[] { "Teal Mask", "Wellspring Mask", "Hearthflame Mask", "Cornerstone Mask" },
                          Apply = (s, f) =>
                          {
                              string[] t = { null, "WATER", "FIRE", "ROCK" }; string[] ab = { "DEFIANT", "WATERABSORB", "MOLDBREAKER", "STURDY" };
                              if (f > 0) s.Types = new List<string> { "GRASS", t[f] };
                              s.Abilities = new List<string> { ab[f] }; s.HiddenAbilities = new List<string>();
                          } },
        };

        static readonly string[][] Gen9Items =
        {
            new[] { "WELLSPRINGMASK", "Wellspring Mask", "An item to be held by Ogerpon. This carved wooden mask is adorned with crystals." },
            new[] { "HEARTHFLAMEMASK", "Hearthflame Mask", "An item to be held by Ogerpon. This carved wooden mask is adorned with crystals." },
            new[] { "CORNERSTONEMASK", "Cornerstone Mask", "An item to be held by Ogerpon. This carved wooden mask is adorned with crystals." },
        };

        /// <summary>Adds the Gen 9 forms (and Ogerpon's masks) a project's data doesn't have yet. Returns how many were added.</summary>
        public static int EnsureGen9Forms(Project p)
        {
            int added = 0;
            foreach (var def in Gen9)
            {
                var baseSp = p.Species.FirstOrDefault(s => s.Id == def.Species);
                if (baseSp == null) continue;
                if (string.IsNullOrEmpty(baseSp.FormName)) baseSp.FormName = def.Names[0];
                if (def.Apply != null && def.Species == "OGERPON") def.Apply(baseSp, 0);
                for (int f = 1; f < def.Names.Length; f++)
                {
                    string id = def.Species + "_" + f;
                    if (p.Species.Any(s => s.Id == id)) continue;
                    var copy = Json.Json.Deserialize<SpeciesData>(Json.Json.Write(baseSp, false));
                    copy.Id = id; copy.Form = f; copy.FormName = def.Names[f];
                    copy.Evolutions = new List<Evolution>(baseSp.Evolutions ?? new List<Evolution>());
                    if (def.Apply != null) def.Apply(copy, f);
                    int at = p.Species.IndexOf(baseSp) + f;
                    p.Species.Insert(Math.Min(at, p.Species.Count), copy);
                    added++;
                }
            }
            foreach (var it in Gen9Items)
            {
                if (p.Items.Any(x => x.Id == it[0])) continue;
                p.Items.Add(new ItemData { Id = it[0], Name = it[1], NamePlural = it[1] + "s", Pocket = 1, Price = 0, Description = it[2] });
                added++;
            }
            return added;
        }
    }
}
