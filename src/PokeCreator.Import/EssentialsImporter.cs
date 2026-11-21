using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using PokeCreator.Data;
using PokeCreator.Json;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Ruby;

namespace PokeCreator.Import
{
    /// <summary>
    /// Converts a Pokémon Essentials (v20/v21) project folder into a PokeCreator project.
    /// Reads PBS/*.txt for game data and Data/*.rxdata for maps, tilesets and switch names,
    /// then copies Graphics/, Audio/ and Fonts/.
    /// </summary>
    public class EssentialsImporter
    {
        public Action<string> Log = s => { };
        public bool CopyAssets = true;
        /// <summary>Built-in database used to add what Essentials is missing (Gen 9, Hisuian forms...). Null = don't.</summary>
        public string BaseDataFolder = BaseData.DefaultFolder;
        public readonly List<string> Warnings = new List<string>();

        string _src;
        Project _p;

        public Project Import(string essentialsRoot, string targetRoot)
        {
            _src = Path.GetFullPath(essentialsRoot);
            if (!Directory.Exists(Path.Combine(_src, "PBS")) && !File.Exists(Path.Combine(_src, "Data", "MapInfos.rxdata")))
                throw new DirectoryNotFoundException("This folder does not look like an Essentials project (no PBS folder or Data/MapInfos.rxdata): " + _src);

            _p = new Project();
            _p.SetRoot(targetRoot);
            Directory.CreateDirectory(_p.Root);
            _p.Info.Name = ReadGameTitle() ?? Path.GetFileName(_src);
            _p.Info.ImportedFrom = "Pokémon Essentials: " + _src;
            _p.Settings.Title = _p.Info.Name;
            _p.Settings.RunningShoesRequired = true; // Essentials games give the Running Shoes in an event
            _p.Settings.PlayerChosenInIntro = true;  // and pick the player character in the intro (pbChangePlayer)

            Step("types", ImportTypes);
            Step("abilities", ImportAbilities);
            Step("moves", ImportMoves);
            Step("items", ImportItems);
            Step("pokemon", ImportSpecies);
            Step("pokemon_forms", ImportForms);
            Step("pokemon_metrics", ImportMetrics);
            Step("trainer_types", ImportTrainerTypes);
            Step("trainers", ImportTrainers);
            Step("encounters", ImportEncounters);
            Step("metadata", ImportMetadata);
            Step("town_map", ImportTownMap);
            Step("regional_dexes", ImportDexes);
            Step("map_connections", ImportConnections);
            Step("berry_plants", ImportBerryPlants);
            Step("ribbons", ImportRibbons);
            Step("shadow_pokemon", ImportShadow);

            ImportSystem();
            ImportCommonEvents();
            ImportTilesets();
            ImportMaps();
            ImportAnimations();
            ImportMapAnimations();

            if (BaseDataFolder != null)
            {
                Log("Adding Pokémon and data Essentials doesn't include (from PokeCreator's built-in database)...");
                Log("  " + BaseData.MergeInto(_p, BaseDataFolder));
            }

            Log("Saving project data...");
            _p.Save();
            Directory.CreateDirectory(_p.ScriptsDir);

            if (CopyAssets)
            {
                foreach (var dir in new[] { "Graphics", "Audio", "Fonts" })
                {
                    string from = Path.Combine(_src, dir);
                    if (!Directory.Exists(from)) continue;
                    Log("Copying " + dir + "...");
                    CopyDirectory(from, Path.Combine(_p.Root, dir));
                }
            }
            Log("Import finished: " + _p.Species.Count + " Pokémon, " + _p.Moves.Count + " moves, " + _p.Items.Count + " items, " + _p.MapInfos.Count + " maps. " + Warnings.Count + " warnings.");
            return _p;
        }

        /// <summary>The PBS files ImportPbsInto can read, in the order they're read.</summary>
        public static readonly string[] PbsGroups = { "types", "abilities", "moves", "items", "pokemon", "trainer_types", "trainers", "encounters", "town_map", "regional_dexes", "map_connections", "berry_plants", "ribbons", "shadow_pokemon" };

        /// <summary>
        /// Reads chosen PBS files into an existing project (Database → Import PBS Files): records with the same ID are
        /// replaced, new ones added, and everything else (maps, PokeCreator-only records) is kept. Encounters are matched
        /// by map and version; the town map, regional dexes and map connections are replaced whole.
        /// </summary>
        public string ImportPbsInto(Project target, string folder, ICollection<string> groups)
        {
            folder = Path.GetFullPath(folder);
            _src = Path.GetFileName(folder).Equals("PBS", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(folder) : folder;
            if (!Directory.Exists(Path.Combine(_src, "PBS"))) throw new DirectoryNotFoundException("No PBS folder in " + folder);
            _p = new Project();
            // Records that other files point at (species for forms, trainer types for trainers) come from the target
            Func<string, bool> want = g => groups.Contains(g);
            if (!want("pokemon")) _p.Species.AddRange(target.Species);
            if (!want("trainer_types")) _p.TrainerTypes.AddRange(target.TrainerTypes);
            if (!want("types")) _p.Types.AddRange(target.Types);
            if (!want("items")) _p.Items.AddRange(target.Items);   // berry_plants.txt changes the target's items in place
            if (want("types")) Step("types", ImportTypes);
            if (want("abilities")) Step("abilities", ImportAbilities);
            if (want("moves")) Step("moves", ImportMoves);
            if (want("items")) Step("items", ImportItems);
            if (want("pokemon")) { Step("pokemon", ImportSpecies); Step("pokemon_forms", ImportForms); Step("pokemon_metrics", ImportMetrics); }
            if (want("trainer_types")) Step("trainer_types", ImportTrainerTypes);
            if (want("trainers")) Step("trainers", ImportTrainers);
            if (want("encounters")) Step("encounters", ImportEncounters);
            if (want("town_map")) Step("town_map", ImportTownMap);
            if (want("regional_dexes")) Step("regional_dexes", ImportDexes);
            if (want("map_connections")) Step("map_connections", ImportConnections);
            if (want("berry_plants")) Step("berry_plants", ImportBerryPlants);
            if (want("ribbons")) Step("ribbons", ImportRibbons);
            if (want("shadow_pokemon")) Step("shadow_pokemon", ImportShadow);
            var report = new List<string>();
            if (want("berry_plants")) report.Add("berry plants");
            if (want("shadow_pokemon")) report.Add("Shadow Pokémon");
            if (want("ribbons")) report.Add("ribbons " + MergeRecords(target.Ribbons, _p.Ribbons, x => x.Id));
            if (want("types")) report.Add("types " + MergeRecords(target.Types, _p.Types, x => x.Id));
            if (want("abilities")) report.Add("abilities " + MergeRecords(target.Abilities, _p.Abilities, x => x.Id));
            if (want("moves")) report.Add("moves " + MergeRecords(target.Moves, _p.Moves, x => x.Id));
            if (want("items")) report.Add("items " + MergeRecords(target.Items, _p.Items, x => x.Id));
            if (want("pokemon")) report.Add("Pokémon " + MergeRecords(target.Species, _p.Species, x => x.Id));
            if (want("trainer_types")) report.Add("trainer types " + MergeRecords(target.TrainerTypes, _p.TrainerTypes, x => x.Id));
            if (want("trainers")) report.Add("trainers " + MergeRecords(target.Trainers, _p.Trainers, x => x.TrainerType + "|" + x.Name + "|" + x.Version));
            if (want("encounters")) report.Add("encounter tables " + MergeRecords(target.Encounters, _p.Encounters, x => x.Map + "|" + x.Version));
            if (want("town_map") && _p.TownMaps.Count > 0) { target.TownMaps.Clear(); target.TownMaps.AddRange(_p.TownMaps); report.Add("town map regions " + _p.TownMaps.Count); }
            if (want("regional_dexes") && _p.RegionalDexes.Count > 0) { target.RegionalDexes.Clear(); target.RegionalDexes.AddRange(_p.RegionalDexes); report.Add("regional dexes " + _p.RegionalDexes.Count); }
            if (want("map_connections")) { target.Connections.Clear(); target.Connections.AddRange(_p.Connections); report.Add("map connections " + _p.Connections.Count); }
            return string.Join(", ", report) + (Warnings.Count > 0 ? "\n" + Warnings.Count + " warning(s):\n" + string.Join("\n", Warnings.Take(20)) : "");
        }

        /// <summary>Replaces records with the same key and adds new ones; returns "N updated, M added".</summary>
        static string MergeRecords<T>(List<T> dst, List<T> src, Func<T, string> key)
        {
            int upd = 0, add = 0;
            var index = new Dictionary<string, int>();
            for (int i = 0; i < dst.Count; i++) { string k = key(dst[i]); if (k != null && !index.ContainsKey(k)) index[k] = i; }
            foreach (var r in src)
            {
                string k = key(r); int at;
                if (k != null && index.TryGetValue(k, out at)) { dst[at] = r; upd++; }
                else { dst.Add(r); if (k != null) index[k] = dst.Count - 1; add++; }
            }
            return upd + " updated, " + add + " added";
        }

        void Step(string pbsName, Action<List<PbsSection>, List<PbsLine>> action)
        {
            string f = Path.Combine(_src, "PBS", pbsName + ".txt");
            if (!File.Exists(f)) { Log("  (no " + pbsName + ".txt, skipped)"); return; }
            Log("Reading PBS/" + pbsName + ".txt");
            List<PbsLine> loose;
            var sections = PbsFile.Read(f, out loose);
            try { action(sections, loose); }
            catch (Exception ex) { Warn(pbsName + ".txt: " + ex.Message); }
        }

        void Warn(string msg) { Warnings.Add(msg); Log("  WARNING: " + msg); }

        string ReadGameTitle()
        {
            string ini = Path.Combine(_src, "Game.ini");
            if (!File.Exists(ini)) return null;
            foreach (var l in File.ReadAllLines(ini))
                if (l.StartsWith("Title=", StringComparison.OrdinalIgnoreCase)) return l.Substring(6).Trim();
            return null;
        }

        // ------------------------------------------------------------------ generic key -> property mapping
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Assigns PBS "Key = Value" lines to same-named properties. Returns keys it did not understand.</summary>
        List<string> Apply(object target, IEnumerable<PbsLine> lines, params string[] skip)
        {
            var unknown = new List<string>();
            var t = target.GetType();
            foreach (var l in lines)
            {
                if (l.Key == null || l.Indented || Array.IndexOf(skip, l.Key) >= 0) continue;
                var prop = t.GetProperty(l.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null || !prop.CanWrite) { unknown.Add(l.Key); continue; }
                try { prop.SetValue(target, ConvertValue(l.Value, prop.PropertyType), null); }
                catch (Exception ex) { Warn("line " + l.LineNumber + " (" + l.Key + "): " + ex.Message); }
            }
            return unknown;
        }

        static object ConvertValue(string v, Type t)
        {
            var nt = Nullable.GetUnderlyingType(t);
            if (nt != null) t = nt;
            if (t == typeof(string)) return v;
            if (t == typeof(int)) return int.Parse(v.Trim(), Inv);
            if (t == typeof(double)) return double.Parse(v.Trim(), Inv);
            if (t == typeof(bool)) { var s = v.Trim().ToLowerInvariant(); return s == "true" || s == "yes" || s == "y" || s == "1"; }
            if (t == typeof(List<string>)) return PbsFile.Csv(v);
            if (t == typeof(List<int>)) return PbsFile.Csv(v).Where(x => x.Length > 0).Select(x => int.Parse(x, Inv)).ToList();
            throw new NotSupportedException("can't convert to " + t.Name);
        }

        static void WarnUnknown(EssentialsImporter imp, string file, string header, List<string> unknown)
        {
            foreach (var k in unknown) imp.Warn(file + " [" + header + "]: unknown key " + k);
        }

        // ------------------------------------------------------------------ PBS importers
        void ImportTypes(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var d = new TypeData { Id = s.Header };
                WarnUnknown(this, "types", s.Header, Apply(d, s.Lines));
                _p.Types.Add(d);
            }
        }

        void ImportAbilities(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var d = new AbilityData { Id = s.Header };
                WarnUnknown(this, "abilities", s.Header, Apply(d, s.Lines));
                _p.Abilities.Add(d);
            }
        }

        void ImportMoves(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var d = new MoveData { Id = s.Header };
                WarnUnknown(this, "moves", s.Header, Apply(d, s.Lines));
                _p.Moves.Add(d);
            }
        }

        void ImportItems(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var d = new ItemData { Id = s.Header };
                WarnUnknown(this, "items", s.Header, Apply(d, s.Lines));
                _p.Items.Add(d);
            }
        }

        void ApplySpeciesSpecial(SpeciesData d, PbsSection s, string file)
        {
            var unknown = Apply(d, s.Lines, "EVs", "Moves", "Evolutions", "MegaStone", "MegaMove", "MegaMessage", "FormName", "PokedexForm");
            WarnUnknown(this, file, s.Header, unknown);
            foreach (var l in s.Lines)
            {
                if (l.Key == null) continue;
                var parts = PbsFile.Csv(l.Value);
                switch (l.Key)
                {
                    case "EVs":
                        d.EVs = new List<StatValue>();
                        for (int i = 0; i + 1 < parts.Count; i += 2) d.EVs.Add(new StatValue { Stat = parts[i], Value = int.Parse(parts[i + 1], Inv) });
                        break;
                    case "Moves":
                        d.Moves = new List<LevelMove>();
                        for (int i = 0; i + 1 < parts.Count; i += 2) d.Moves.Add(new LevelMove { Level = int.Parse(parts[i], Inv), Move = parts[i + 1] });
                        break;
                    case "Evolutions":
                        d.Evolutions = new List<Evolution>();
                        for (int i = 0; i + 1 < parts.Count; i += 3)
                            d.Evolutions.Add(new Evolution { Species = parts[i], Method = parts[i + 1], Parameter = i + 2 < parts.Count ? parts[i + 2] : "" });
                        break;
                    case "MegaStone": d.MegaStone = l.Value; break;
                    case "MegaMove": d.MegaMove = l.Value; break;
                    case "MegaMessage": d.MegaMessage = int.Parse(l.Value, Inv); break;
                    case "FormName": d.FormName = l.Value; break;
                    case "PokedexForm": d.PokedexForm = l.Value; break;
                }
            }
        }

        void ImportSpecies(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var parts = PbsFile.Csv(s.Header);
                var d = new SpeciesData { Id = parts[0], Species = parts[0], Form = parts.Count > 1 ? int.Parse(parts[1], Inv) : 0 };
                if (d.Form > 0) d.Id = d.Species + "_" + d.Form;
                ApplySpeciesSpecial(d, s, "pokemon");
                _p.Species.Add(d);
            }
        }

        void ImportForms(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var parts = PbsFile.Csv(s.Header);
                if (parts.Count < 2) { Warn("pokemon_forms [" + s.Header + "]: missing form number"); continue; }
                var baseSp = _p.Species.FirstOrDefault(x => x.Id == parts[0]);
                if (baseSp == null) { Warn("pokemon_forms [" + s.Header + "]: unknown species " + parts[0]); continue; }
                // A form starts as a copy of its base species, then the section's keys override.
                var d = Json.Json.Deserialize<SpeciesData>(Json.Json.Write(baseSp, false));
                d.Form = int.Parse(parts[1], Inv);
                d.Id = d.Species + "_" + d.Form;
                d.FormName = null; d.MegaStone = null; d.MegaMove = null; d.MegaMessage = 0; d.PokedexForm = null;
                ApplySpeciesSpecial(d, s, "pokemon_forms");
                int idx = _p.Species.IndexOf(baseSp);
                int insert = idx + 1;
                while (insert < _p.Species.Count && _p.Species[insert].Species == d.Species) insert++;
                _p.Species.Insert(insert, d);
            }
        }

        void ImportMetrics(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var parts = PbsFile.Csv(s.Header);
                string id = parts.Count > 1 && parts[1] != "0" ? parts[0] + "_" + parts[1] : parts[0];
                var d = _p.Species.FirstOrDefault(x => x.Id == id);
                if (d == null) { Warn("pokemon_metrics [" + s.Header + "]: unknown species"); continue; }
                WarnUnknown(this, "pokemon_metrics", s.Header, Apply(d, s.Lines));
            }
        }

        void ImportTrainerTypes(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var d = new TrainerTypeData { Id = s.Header };
                WarnUnknown(this, "trainer_types", s.Header, Apply(d, s.Lines));
                _p.TrainerTypes.Add(d);
            }
        }

        void ImportTrainers(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var h = PbsFile.Csv(s.Header);
                var d = new TrainerData { TrainerType = h[0], Name = h.Count > 1 ? h[1] : "", Version = h.Count > 2 ? int.Parse(h[2], Inv) : 0 };
                d.Id = TrainerData.MakeId(d.TrainerType, d.Name, d.Version);
                TrainerPokemon cur = null;
                foreach (var l in s.Lines)
                {
                    if (l.Key == null) continue;
                    if (l.Key == "Pokemon")
                    {
                        var p = PbsFile.Csv(l.Value);
                        cur = new TrainerPokemon { Species = p[0], Level = p.Count > 1 ? int.Parse(p[1], Inv) : 5 };
                        d.Pokemon.Add(cur);
                        continue;
                    }
                    if (cur != null && (l.Indented || IsTrainerPokemonKey(l.Key)))
                    {
                        ApplyTrainerPokemonKey(cur, l, s.Header);
                        continue;
                    }
                    switch (l.Key)
                    {
                        case "Items": d.Items = PbsFile.Csv(l.Value); break;
                        case "LoseText": d.LoseText = l.Value; break;
                        default: Warn("trainers [" + s.Header + "]: unknown key " + l.Key); break;
                    }
                }
                _p.Trainers.Add(d);
            }
        }

        static bool IsTrainerPokemonKey(string k)
        {
            switch (k) { case "Form": case "Name": case "Moves": case "Ability": case "AbilityIndex": case "Item": case "Gender": case "Nature": case "IV": case "EV": case "Happiness": case "Shiny": case "SuperShiny": case "Shadow": case "Ball": return true; }
            return false;
        }

        void ApplyTrainerPokemonKey(TrainerPokemon p, PbsLine l, string header)
        {
            string v = l.Value;
            switch (l.Key)
            {
                case "Form": p.Form = int.Parse(v, Inv); break;
                case "Name": p.Name = v; break;
                case "Moves": p.Moves = PbsFile.Csv(v); break;
                case "Ability": p.Ability = v; break;
                case "AbilityIndex": p.AbilityIndex = int.Parse(v, Inv); break;
                case "Item": p.Item = v; break;
                case "Gender": p.Gender = v; break;
                case "Nature": p.Nature = v; break;
                case "IV": p.IV = ExpandStats(v); break;
                case "EV": p.EV = ExpandStats(v); break;
                case "Happiness": p.Happiness = int.Parse(v, Inv); break;
                case "Shiny": p.Shiny = (bool)ConvertValue(v, typeof(bool)); break;
                case "SuperShiny": p.SuperShiny = (bool)ConvertValue(v, typeof(bool)); break;
                case "Shadow": p.Shadow = (bool)ConvertValue(v, typeof(bool)); break;
                case "Ball": p.Ball = v; break;
                default: Warn("trainers [" + header + "]: unknown Pokémon key " + l.Key); break;
            }
        }

        static List<int> ExpandStats(string v)
        {
            var vals = PbsFile.Csv(v).Select(x => int.Parse(x, Inv)).ToList();
            if (vals.Count == 1) return Enumerable.Repeat(vals[0], 6).ToList();
            return vals;
        }

        void ImportEncounters(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var h = PbsFile.Csv(s.Header);
                var d = new EncounterData { Map = int.Parse(h[0], Inv), Version = h.Count > 1 ? int.Parse(h[1], Inv) : 0, Comment = s.Comment };
                d.Id = d.Map.ToString("000") + (d.Version > 0 ? "," + d.Version : "");
                EncounterMethod cur = null;
                foreach (var l in s.Lines)
                {
                    var parts = PbsFile.Csv(l.Value);
                    int chance;
                    if (parts.Count >= 3 && int.TryParse(parts[0], NumberStyles.Integer, Inv, out chance) && cur != null)
                    {
                        int min = int.Parse(parts[2], Inv);
                        cur.Slots.Add(new EncounterSlot { Chance = chance, Species = parts[1], MinLevel = min, MaxLevel = parts.Count > 3 ? int.Parse(parts[3], Inv) : min });
                    }
                    else
                    {
                        cur = new EncounterMethod { Type = parts[0], Density = parts.Count > 1 && parts[1].Length > 0 ? int.Parse(parts[1], Inv) : 0 };
                        d.Methods.Add(cur);
                    }
                }
                _p.Encounters.Add(d);
            }
        }

        void ImportMetadata(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                if (s.Header == "0")
                {
                    WarnUnknown(this, "metadata", s.Header, Apply(_p.Settings, s.Lines));
                }
                else
                {
                    var pc = new PlayerCharacter();
                    WarnUnknown(this, "metadata", s.Header, Apply(pc, s.Lines));
                    _p.Players.Add(pc);
                }
            }
        }

        void ImportTownMap(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var r = new TownMapRegion { Id = s.Header };
                foreach (var l in s.Lines)
                {
                    if (l.Key == "Name") r.Name = l.Value;
                    else if (l.Key == "Filename") r.Filename = l.Value;
                    else if (l.Key == "Point")
                    {
                        var p = PbsFile.Csv(l.Value);
                        Func<int, string> at = i => i < p.Count ? p[i] : "";
                        Func<int, int> num = i => { int x; return int.TryParse(at(i), NumberStyles.Integer, Inv, out x) ? x : 0; };
                        r.Points.Add(new TownMapPoint { X = num(0), Y = num(1), Name = at(2), PointOfInterest = at(3), FlyMap = num(4), FlyX = num(5), FlyY = num(6), Switch = at(7) });
                    }
                }
                _p.TownMaps.Add(r);
            }
        }

        // PBS/berry_plants.txt: growth speed, drying and yield of each berry
        void ImportBerryPlants(List<PbsSection> secs, List<PbsLine> loose)
        {
            int n = 0;
            foreach (var s in secs)
            {
                var it = _p.Items.FirstOrDefault(i => i.Id == s.Header);
                if (it == null) { Warn("berry_plants.txt: unknown item " + s.Header); continue; }
                WarnUnknown(this, "berry_plants", s.Header, Apply(it, s.Lines)); n++;
            }
            Log("  " + n + " berry plants");
        }

        // PBS/shadow_pokemon.txt: heart gauge size and Shadow moves per species
        void ImportShadow(List<PbsSection> secs, List<PbsLine> loose)
        {
            int n = 0;
            foreach (var s in secs)
            {
                var d = _p.Species.FirstOrDefault(x => x.Id == s.Header && x.Form == 0) ?? _p.Species.FirstOrDefault(x => x.Id == s.Header);
                if (d == null) { Warn("shadow_pokemon.txt: unknown species " + s.Header); continue; }
                foreach (var l in s.Lines)
                {
                    if (l.Key == null) continue;
                    if (l.Key.Equals("GaugeSize", StringComparison.OrdinalIgnoreCase)) d.ShadowGaugeSize = int.Parse(l.Value.Trim(), Inv);
                    else if (l.Key.Equals("Moves", StringComparison.OrdinalIgnoreCase)) d.ShadowMoves = l.Value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                }
                n++;
            }
            Log("  " + n + " Shadow Pokémon");
        }

        // PBS/ribbons.txt: ribbon names and descriptions
        void ImportRibbons(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var r = _p.Ribbons.FirstOrDefault(x => x.Id == s.Header);
                if (r == null) { r = new RibbonData { Id = s.Header }; _p.Ribbons.Add(r); }
                WarnUnknown(this, "ribbons", s.Header, Apply(r, s.Lines));
            }
            Log("  " + _p.Ribbons.Count + " ribbons");
        }

        void ImportDexes(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var s in secs)
            {
                var d = new RegionalDex { Id = s.Header };
                foreach (var l in s.Lines) d.Species.AddRange(PbsFile.Csv(l.Value).Where(x => x.Length > 0));
                _p.RegionalDexes.Add(d);
            }
        }

        void ImportConnections(List<PbsSection> secs, List<PbsLine> loose)
        {
            foreach (var l in loose)
            {
                var p = PbsFile.Csv(l.Value);
                if (p.Count < 6) { Warn("map_connections line " + l.LineNumber + ": expected 6 values"); continue; }
                _p.Connections.Add(new MapConnection
                {
                    Map1 = int.Parse(p[0], Inv), Edge1 = NormEdge(p[1]), Offset1 = int.Parse(p[2], Inv),
                    Map2 = int.Parse(p[3], Inv), Edge2 = NormEdge(p[4]), Offset2 = int.Parse(p[5], Inv)
                });
            }
        }

        static string NormEdge(string e)
        {
            switch (e.Trim().ToUpperInvariant()) { case "N": case "NORTH": return "North"; case "S": case "SOUTH": return "South"; case "E": case "EAST": return "East"; case "W": case "WEST": return "West"; }
            return e;
        }

        // ------------------------------------------------------------------ RMXP data
        string RxPath(string name) { return Path.Combine(_src, "Data", name); }

        void ImportSystem()
        {
            string f = RxPath("System.rxdata");
            if (!File.Exists(f)) { Warn("Data/System.rxdata not found"); return; }
            Log("Reading Data/System.rxdata");
            var sys = (RObject)MarshalReader.Load(f);
            _p.System.Switches = sys.List("switches").Select(o => o == null ? null : o.ToString()).ToList();
            _p.System.Variables = sys.List("variables").Select(o => o == null ? null : o.ToString()).ToList();
            _p.Settings.StartMap = sys.Int("start_map_id");
            _p.Settings.StartX = sys.Int("start_x");
            _p.Settings.StartY = sys.Int("start_y");
            if (sys.Str("windowskin_name").Length > 0) _p.Settings.Windowskin = sys.Str("windowskin_name");
            var titleBgm = sys.Obj("title_bgm");
            if (titleBgm != null) _p.Settings.TitleBGM = titleBgm.Str("name");
        }

        /// <summary>RPG Maker map animations (Data/Animations.rxdata) for Show Animation.</summary>
        void ImportMapAnimations()
        {
            string f = RxPath("Animations.rxdata");
            if (!File.Exists(f)) return;
            try
            {
                _p.MapAnimations = ReadMapAnimations(f);
                Log("  " + _p.MapAnimations.Count + " map animations");
            }
            catch (Exception ex) { Warn("Animations.rxdata: " + ex.Message); }
        }

        public static List<MapAnimation> ReadMapAnimations(string file)
        {
            var result = new List<MapAnimation>();
            var list = MarshalReader.Load(file) as List<object>;
            if (list == null) return result;
            foreach (var o in list)
            {
                var ob = o as RObject;
                if (ob == null || ob.Int("id") <= 0 || ob.Str("animation_name").Length == 0) continue;
                var an = new MapAnimation { Id = ob.Int("id"), Name = ob.Str("name"), Graphic = ob.Str("animation_name"), Hue = ob.Int("animation_hue"), Position = ob.Int("position") };
                foreach (var fo in ob.List("frames"))
                {
                    var fr = fo as RObject;
                    var cells = new List<List<int>>();
                    var ud = fr != null ? fr.Get("cell_data") as RUserData : null;
                    if (ud != null)
                    {
                        var t = ud.AsTable();
                        for (int c = 0; c < t.XSize; c++)
                        {
                            var cell = new List<int>();
                            for (int k = 0; k < 8 && k < t.YSize; k++) cell.Add(t[c, k]);
                            if (cell.Count == 8 && cell[0] >= 0) cells.Add(cell);
                        }
                    }
                    an.Frames.Add(cells);
                }
                foreach (var to in ob.List("timings"))
                {
                    var ti = to as RObject;
                    if (ti == null) continue;
                    var se = ti.Obj("se");
                    var tm = new MapAnimationTiming { Frame = ti.Int("frame"), FlashScope = ti.Int("flash_scope"), FlashDuration = ti.Int("flash_duration") };
                    if (se != null && se.Str("name").Length > 0) { tm.Se = se.Str("name"); tm.Volume = se.Int("volume"); tm.Pitch = se.Int("pitch"); }
                    var col = ti.Get("flash_color") as RUserData;
                    if (col != null) tm.FlashColor = col.AsDoubles().Select(d => (int)d).ToList();
                    an.Timings.Add(tm);
                }
                result.Add(an);
            }
            return result;
        }

        void ImportAnimations()
        {
            string f = RxPath("PkmnAnimations.rxdata");
            if (!File.Exists(f)) { Log("  (no Data/PkmnAnimations.rxdata, battle animations skipped)"); return; }
            Log("Reading Data/PkmnAnimations.rxdata");
            try
            {
                _p.Animations = ReadAnimations(f);
                Log("  " + _p.Animations.Count + " battle animations");
            }
            catch (Exception ex) { Warn("PkmnAnimations.rxdata: " + ex.Message); }
        }

        /// <summary>Reads Essentials' PkmnAnimations.rxdata (PBAnimations of PBAnimation) into battle animations.</summary>
        public static List<BattleAnimation> ReadAnimations(string file)
        {
            var result = new List<BattleAnimation>();
            var root = MarshalReader.Load(file);
            var ua = root as RUserArray;
            var list = ua != null ? (ua.Get("array") as List<object> ?? ua) : root as List<object>;
            if (list == null) return result;
            // Essentials finds a move's extra-hit and charging animations by position (the move's index + hit number):
            // "Fury Attack hit 2" right after "Move:FURYATTACK" becomes "Move:FURYATTACK:1"
            string baseName = null; int baseIndex = 0;
            for (int idx = 0; idx < list.Count; idx++)
            {
                var an = list[idx] as RUserArray;
                if (an == null) { baseName = null; continue; }
                string name = an.Str("name");
                if (string.IsNullOrEmpty(name)) { baseName = null; continue; }
                if (name.StartsWith("Move:") || name.StartsWith("OppMove:")) { baseName = name; baseIndex = idx; }
                else if (!name.StartsWith("Common:") && baseName != null) name = baseName + ":" + (idx - baseIndex);
                result.RemoveAll(x => x.Id == name); // as in Essentials, a later animation with the same name wins
                var ba = new BattleAnimation { Id = name, Graphic = an.Str("graphic"), Hue = an.Int("hue"), Position = an.Int("position") };
                var frames = an.Get("array") as List<object> ?? an;
                foreach (var fo in frames)
                {
                    var cels = new List<int[]>();
                    var fl = fo as List<object>;
                    if (fl != null)
                        foreach (var co in fl)
                        {
                            var cl = co as List<object>;
                            if (cl == null) { cels.Add(null); continue; }
                            var cel = new int[BattleAnimation.CelLength];
                            for (int i = 0; i < cel.Length && i < cl.Count; i++)
                                cel[i] = cl[i] == null ? 0 : cl[i] is bool ? ((bool)cl[i] ? 1 : 0) : Convert.ToInt32(cl[i]);
                            cels.Add(cel);
                        }
                    while (cels.Count > 0 && cels[cels.Count - 1] == null) cels.RemoveAt(cels.Count - 1);
                    ba.Frames.Add(cels);
                }
                var timings = an.Get("timing") as List<object>;
                if (timings != null)
                    foreach (var to in timings)
                    {
                        var t = to as RObject;
                        if (t == null) continue;
                        var at = new AnimationTiming { Frame = t.Int("frame"), Type = t.Int("timingType"), Sound = t.Str("name"), Volume = t.Int("volume"), Pitch = t.Int("pitch"), FlashScope = t.Int("flashScope"), FlashDuration = t.Int("flashDuration") };
                        var fc = t.Get("flashColor") as RUserData;
                        if (fc != null) { var d = fc.AsDoubles(); if (d.Length >= 4) at.FlashColor = new[] { (int)d[0], (int)d[1], (int)d[2], (int)d[3] }; }
                        ba.Timings.Add(at);
                    }
                result.Add(ba);
            }
            return result;
        }

        void ImportCommonEvents()
        {
            string f = RxPath("CommonEvents.rxdata");
            if (!File.Exists(f)) { Warn("Data/CommonEvents.rxdata not found"); return; }
            Log("Reading Data/CommonEvents.rxdata");
            var list = (List<object>)MarshalReader.Load(f);
            foreach (var o in list)
            {
                var c = o as RObject;
                if (c == null) continue;
                var ce = new CommonEventData { Id = c.Int("id"), Name = c.Str("name"), Trigger = c.Int("trigger"), SwitchId = c.Int("switch_id") };
                foreach (RObject cmd in c.List("list"))
                    ce.List.Add(new EventCommand { Code = cmd.Int("code"), Indent = cmd.Int("indent"), Params = cmd.List("parameters").Select(ToPlain).ToList() });
                _p.CommonEvents.Add(ce);
            }
            Log("  " + _p.CommonEvents.Count(x => x.List.Count > 1 || x.Name.Length > 0) + " common events");
        }

        void ImportTilesets()
        {
            string f = RxPath("Tilesets.rxdata");
            if (!File.Exists(f)) { Warn("Data/Tilesets.rxdata not found"); return; }
            Log("Reading Data/Tilesets.rxdata");
            var list = (List<object>)MarshalReader.Load(f);
            foreach (var o in list)
            {
                var t = o as RObject;
                if (t == null) continue;
                var ts = new TilesetData
                {
                    Id = t.Int("id"), Name = t.Str("name"), TilesetName = t.Str("tileset_name"),
                    AutotileNames = t.List("autotile_names").Select(x => x == null ? "" : x.ToString()).ToList(),
                    PanoramaName = t.Str("panorama_name"), PanoramaHue = t.Int("panorama_hue"),
                    FogName = t.Str("fog_name"), BattlebackName = t.Str("battleback_name"),
                    FogHue = t.Int("fog_hue"), FogOpacity = t.Int("fog_opacity"), FogBlend = t.Int("fog_blend_type"), FogZoom = t.Int("fog_zoom"),
                    FogSx = t.Int("fog_sx"), FogSy = t.Int("fog_sy")
                };
                while (ts.AutotileNames.Count < 7) ts.AutotileNames.Add("");
                ts.Passages = TableList(t.Get("passages"));
                ts.Priorities = TableList(t.Get("priorities"));
                ts.TerrainTags = TableList(t.Get("terrain_tags"));
                _p.Tilesets.Add(ts);
            }
        }

        static List<int> TableList(object o)
        {
            var u = o as RUserData;
            return u == null ? new List<int>() : u.AsTable().Data.ToList();
        }

        void ImportMaps()
        {
            string f = RxPath("MapInfos.rxdata");
            if (!File.Exists(f)) { Warn("Data/MapInfos.rxdata not found"); return; }
            Log("Reading Data/MapInfos.rxdata");
            var infos = (RHash)MarshalReader.Load(f);
            foreach (var kv in infos.Pairs)
            {
                var mi = (RObject)kv.Value;
                _p.MapInfos.Add(new MapInfo { Id = Convert.ToInt32(kv.Key), Name = mi.Str("name"), ParentId = mi.Int("parent_id"), Order = mi.Int("order"), Expanded = mi.Bool("expanded") });
            }
            _p.MapInfos.Sort((a, b) => a.Order.CompareTo(b.Order));

            // Pokémon map settings from PBS/map_metadata.txt
            var meta = new Dictionary<int, PbsSection>();
            string mm = Path.Combine(_src, "PBS", "map_metadata.txt");
            if (File.Exists(mm)) { List<PbsLine> loose; foreach (var s in PbsFile.Read(mm, out loose)) { int id; if (int.TryParse(s.Header, out id)) meta[id] = s; } }

            Directory.CreateDirectory(_p.MapsDir);
            foreach (var info in _p.MapInfos)
            {
                string mf = RxPath("Map" + info.Id.ToString("000") + ".rxdata");
                if (!File.Exists(mf)) { Warn("Map file missing for map " + info.Id + " (" + info.Name + ")"); continue; }
                try
                {
                    var map = ConvertMap(info.Id, (RObject)MarshalReader.Load(mf));
                    PbsSection ms;
                    if (meta.TryGetValue(info.Id, out ms)) WarnUnknown(this, "map_metadata", ms.Header, Apply(map.Properties, ms.Lines));
                    _p.SaveMap(map);
                    _p.ForgetMap(map.Id);
                }
                catch (Exception ex) { Warn("Map " + info.Id + " (" + info.Name + "): " + ex.Message); }
            }
            Log("Converted " + _p.MapInfos.Count + " maps");

            // Essentials' start map is usually the intro (driven by Ruby script calls).
            // Start new games at the "Home" location from metadata instead when one is set.
            if (_p.Settings.Home != null && _p.Settings.Home.Count >= 3)
            {
                _p.Settings.StartMap = _p.Settings.Home[0];
                _p.Settings.StartX = _p.Settings.Home[1];
                _p.Settings.StartY = _p.Settings.Home[2];
                if (_p.Settings.Home.Count >= 4) _p.Settings.StartDirection = _p.Settings.Home[3];
            }
        }

        MapData ConvertMap(int id, RObject m)
        {
            int w = m.Int("width"), h = m.Int("height");
            var map = MapData.Create(id, w, h, m.Int("tileset_id"));
            map.AutoplayBgm = m.Bool("autoplay_bgm");
            map.Bgm = ConvertAudio(m.Obj("bgm"));
            map.AutoplayBgs = m.Bool("autoplay_bgs");
            map.Bgs = ConvertAudio(m.Obj("bgs"));
            map.EncounterStep = m.Int("encounter_step");
            var data = m.Get("data") as RUserData;
            if (data != null)
            {
                var t = data.AsTable();
                for (int z = 0; z < Math.Min(3, t.ZSize); z++)
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            map.Layers[z][y][x] = t[x, y, z];
            }
            var events = m.Get("events") as RHash;
            if (events != null)
            {
                foreach (var kv in events.Pairs)
                {
                    var e = (RObject)kv.Value;
                    var ev = new MapEvent { Id = e.Int("id"), Name = e.Str("name"), X = e.Int("x"), Y = e.Int("y"), Pages = new List<EventPage>() };
                    foreach (RObject pg in e.List("pages")) ev.Pages.Add(ConvertPage(pg));
                    map.Events.Add(ev);
                }
                map.Events.Sort((a, b) => a.Id.CompareTo(b.Id));
            }
            return map;
        }

        static AudioFile ConvertAudio(RObject a)
        {
            if (a == null) return new AudioFile();
            return new AudioFile(a.Str("name"), a.Int("volume"), a.Int("pitch"));
        }

        EventPage ConvertPage(RObject pg)
        {
            var p = new EventPage();
            var c = pg.Obj("condition");
            if (c != null)
            {
                p.Condition = new EventCondition
                {
                    Switch1Valid = c.Bool("switch1_valid"), Switch1Id = c.Int("switch1_id"),
                    Switch2Valid = c.Bool("switch2_valid"), Switch2Id = c.Int("switch2_id"),
                    VariableValid = c.Bool("variable_valid"), VariableId = c.Int("variable_id"), VariableValue = c.Int("variable_value"),
                    SelfSwitchValid = c.Bool("self_switch_valid"), SelfSwitchCh = c.Str("self_switch_ch")
                };
            }
            var g = pg.Obj("graphic");
            if (g != null)
            {
                p.Graphic = new EventGraphic
                {
                    TileId = g.Int("tile_id"), CharacterName = g.Str("character_name"), CharacterHue = g.Int("character_hue"),
                    Direction = g.Int("direction"), Pattern = g.Int("pattern"), Opacity = g.Int("opacity"), BlendType = g.Int("blend_type")
                };
            }
            p.MoveType = pg.Int("move_type");
            p.MoveSpeed = pg.Int("move_speed");
            p.MoveFrequency = pg.Int("move_frequency");
            p.MoveRoute = ConvertMoveRoute(pg.Obj("move_route"));
            p.WalkAnime = pg.Bool("walk_anime");
            p.StepAnime = pg.Bool("step_anime");
            p.DirectionFix = pg.Bool("direction_fix");
            p.Through = pg.Bool("through");
            p.AlwaysOnTop = pg.Bool("always_on_top");
            p.Trigger = pg.Int("trigger");
            p.List = new List<EventCommand>();
            foreach (RObject cmd in pg.List("list"))
                p.List.Add(new EventCommand { Code = cmd.Int("code"), Indent = cmd.Int("indent"), Params = cmd.List("parameters").Select(ToPlain).ToList() });
            return p;
        }

        static MoveRoute ConvertMoveRoute(RObject r)
        {
            var mr = new MoveRoute();
            if (r == null) return mr;
            mr.Repeat = r.Bool("repeat");
            mr.Skippable = r.Bool("skippable");
            mr.List = new List<EventCommand>();
            foreach (RObject c in r.List("list"))
                mr.List.Add(new EventCommand { Code = c.Int("code"), Params = c.List("parameters").Select(ToPlain).ToList() });
            return mr;
        }

        /// <summary>Converts an event command parameter into a plain JSON value.</summary>
        static object ToPlain(object o)
        {
            if (o == null || o is bool || o is int || o is long || o is double) return o;
            if (o is RString || o is RSymbol) return o.ToString();
            var list = o as List<object>;
            if (list != null) return list.Select(ToPlain).ToList();
            var ro = o as RObject;
            if (ro != null)
            {
                if (ro.ClassName == "RPG::AudioFile")
                {
                    var a = new JsonObject(); a["Name"] = ro.Str("name"); a["Volume"] = ro.Int("volume"); a["Pitch"] = ro.Int("pitch"); return a;
                }
                if (ro.ClassName == "RPG::MoveRoute") return Json.Json.ToJsonObject(ConvertMoveRoute(ro));
                if (ro.ClassName == "RPG::MoveCommand")
                {
                    var mc = new JsonObject(); mc["Code"] = ro.Int("code"); mc["Params"] = ro.List("parameters").Select(ToPlain).ToList(); return mc;
                }
                var jo = new JsonObject(); jo["_class"] = ro.ClassName;
                foreach (var kv in ro.Ivars) jo[kv.Key] = ToPlain(kv.Value);
                return jo;
            }
            var u = o as RUserData;
            if (u != null)
            {
                var jo = new JsonObject(); jo["_class"] = u.ClassName;
                jo["Values"] = u.AsDoubles().Select(d => (object)Math.Round(d, 3)).ToList();
                return jo;
            }
            var h = o as RHash;
            if (h != null)
            {
                var jo = new JsonObject();
                foreach (var kv in h.Pairs) jo[Convert.ToString(ToPlain(kv.Key), Inv)] = ToPlain(kv.Value);
                return jo;
            }
            return o.ToString();
        }

        // ------------------------------------------------------------------ files
        static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
            {
                string name = Path.GetFileName(f);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) continue;
                string dest = Path.Combine(to, name);
                if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(f).Length) File.Copy(f, dest, true);
            }
            foreach (var d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}
