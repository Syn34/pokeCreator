using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PokeCreator.Data;

namespace PokeCreator.Projects
{
    /// <summary>
    /// PokeCreator's built-in database (every Pokémon, move, ability, item and type), shipped in a
    /// "BaseData" folder next to the programs. New projects start from it, Essentials imports are
    /// topped up from it, and existing projects can add whatever they're missing.
    /// </summary>
    public static class BaseData
    {
        public static readonly string[] Files = { "types.json", "abilities.json", "moves.json", "items.json", "species.json", "trainer_types.json" };

        /// <summary>The BaseData folder next to the running program (or in a parent "data/base" when running from source).</summary>
        public static string DefaultFolder
        {
            get
            {
                string exe = AppDomain.CurrentDomain.BaseDirectory;
                foreach (var dir in new[] { Path.Combine(exe, "BaseData"), Path.Combine(exe, "..", "data", "base") })
                    if (File.Exists(Path.Combine(dir, "species.json"))) return Path.GetFullPath(dir);
                return null;
            }
        }

        public class MergeResult
        {
            public readonly Dictionary<string, int> Added = new Dictionary<string, int>();
            public int EvolutionsAdded;
            public int Total { get { return Added.Values.Sum() + EvolutionsAdded; } }
            public override string ToString()
            {
                var parts = Added.Where(kv => kv.Value > 0).Select(kv => kv.Value + " " + kv.Key).ToList();
                if (EvolutionsAdded > 0) parts.Add(EvolutionsAdded + " evolutions");
                return parts.Count == 0 ? "Nothing was missing." : "Added " + string.Join(", ", parts) + ".";
            }
        }

        /// <summary>Adds every record the project doesn't have yet. Existing records are never changed,
        /// except that new evolutions leading to newly added Pokémon are appended.</summary>
        public static MergeResult MergeInto(Project p, string folder)
        {
            var r = new MergeResult();
            if (folder == null || !Directory.Exists(folder)) return r;
            r.Added["types"] = MergeList(p.Types, Load<TypeData>(folder, "types.json"));
            r.Added["abilities"] = MergeList(p.Abilities, Load<AbilityData>(folder, "abilities.json"));
            r.Added["moves"] = MergeList(p.Moves, Load<MoveData>(folder, "moves.json"));
            r.Added["items"] = MergeList(p.Items, Load<ItemData>(folder, "items.json"));
            r.Added["trainer types"] = MergeList(p.TrainerTypes, Load<TrainerTypeData>(folder, "trainer_types.json"));

            var baseSpecies = Load<SpeciesData>(folder, "species.json");
            var have = new HashSet<string>(p.Species.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in baseSpecies)
            {
                if (have.Contains(s.Id)) continue;
                if (s.Form > 0)
                {
                    int last = p.Species.FindLastIndex(x => x.Species == s.Species);
                    if (last >= 0) p.Species.Insert(last + 1, s); else p.Species.Add(s);
                }
                else p.Species.Add(s);
                have.Add(s.Id); added.Add(s.Id);
            }
            r.Added["Pokémon"] = baseSpecies.Count(s => s.Form == 0 && added.Contains(s.Id));
            r.Added["forms"] = baseSpecies.Count(s => s.Form > 0 && added.Contains(s.Id));

            // Old Pokémon gain evolutions into the newly added ones (e.g. Dunsparce -> Dudunsparce)
            var byId = p.Species.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var bs in baseSpecies)
            {
                SpeciesData mine;
                if (!byId.TryGetValue(bs.Id, out mine) || added.Contains(bs.Id)) continue;
                foreach (var evo in bs.Evolutions)
                {
                    if (!added.Contains(evo.Species) || mine.Evolutions.Any(e => e.Species == evo.Species)) continue;
                    mine.Evolutions.Add(evo);
                    r.EvolutionsAdded++;
                }
            }
            return r;
        }

        /// <summary>Fills an empty project's database with the complete base data.</summary>
        public static void Fill(Project p, string folder)
        {
            if (folder == null) return;
            p.Types = Load<TypeData>(folder, "types.json");
            p.Abilities = Load<AbilityData>(folder, "abilities.json");
            p.Moves = Load<MoveData>(folder, "moves.json");
            p.Items = Load<ItemData>(folder, "items.json");
            p.Species = Load<SpeciesData>(folder, "species.json");
            p.TrainerTypes = Load<TrainerTypeData>(folder, "trainer_types.json");
        }

        static List<T> Load<T>(string folder, string file)
        {
            string path = Path.Combine(folder, file);
            return File.Exists(path) ? Json.Json.FromFile<List<T>>(path) : new List<T>();
        }

        static int MergeList<T>(List<T> mine, List<T> theirs) where T : GameRecord
        {
            var have = new HashSet<string>(mine.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var t in theirs) if (have.Add(t.Id)) { mine.Add(t); n++; }
            return n;
        }
    }
}
