using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Json;
using PokeCreator.Maps;

namespace PokeCreator.Projects
{
    public class ProjectInfo
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string EngineVersion { get; set; }
        public string ImportedFrom { get; set; }
        public ProjectInfo() { Name = "New Game"; EngineVersion = Project.EngineVersion; }
    }

    /// <summary>Global switch / variable names (RMXP System data).</summary>
    public class SystemData
    {
        public List<string> Switches { get; set; }
        public List<string> Variables { get; set; }
        public SystemData() { Switches = new List<string>(); Variables = new List<string>(); }
    }

    /// <summary>One editable list in the database (Pokémon, Moves, Items, ...).</summary>
    public class DatabaseTable
    {
        public string Key;        // "species"
        public string Label;      // "Pokémon"
        public Type RecordType;
        public string FileName;   // "species.json"
        public Func<IList> Get;
        public IList Items { get { return Get(); } }

        public GameRecord Find(string id)
        {
            foreach (GameRecord r in Items) if (string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }
    }

    /// <summary>
    /// A PokeCreator game project: a folder containing project.json, Data/*.json,
    /// Data/Maps/*.json, Graphics/, Audio/, Fonts/ and Scripts/.
    /// </summary>
    public class Project
    {
        public const string EngineVersion = "0.3.26";
        public const string ProjectFileName = "project.json";

        public string Root { get; private set; }
        public ProjectInfo Info = new ProjectInfo();
        public GameSettings Settings = new GameSettings();
        /// <summary>Custom battle and menu pictures (Data/ui.json).</summary>
        public UiSkin Ui = new UiSkin();
        public SystemData System = new SystemData();
        public List<PlayerCharacter> Players = new List<PlayerCharacter>();

        public List<TypeData> Types = new List<TypeData>();
        public List<AbilityData> Abilities = new List<AbilityData>();
        public List<MoveData> Moves = new List<MoveData>();
        public List<ItemData> Items = new List<ItemData>();
        public List<SpeciesData> Species = new List<SpeciesData>();
        public List<TrainerTypeData> TrainerTypes = new List<TrainerTypeData>();
        public List<TrainerData> Trainers = new List<TrainerData>();
        public List<EncounterData> Encounters = new List<EncounterData>();
        public List<TownMapRegion> TownMaps = new List<TownMapRegion>();
        public List<RegionalDex> RegionalDexes = new List<RegionalDex>();
        public List<BattleAnimation> Animations = new List<BattleAnimation>();
        public List<MysteryGiftData> MysteryGifts = new List<MysteryGiftData>();
        /// <summary>RPG Maker map animations for Show Animation (map_animations.json).</summary>
        public List<MapAnimation> MapAnimations = new List<MapAnimation>();
        /// <summary>Ribbon names and descriptions (ribbons.json, from PBS ribbons.txt).</summary>
        public List<RibbonData> Ribbons = new List<RibbonData>();
        /// <summary>Phone conversations (phone_messages.json, from PBS phone.txt).</summary>
        public List<PhoneMessageData> PhoneMessages = new List<PhoneMessageData>();

        public List<MapInfo> MapInfos = new List<MapInfo>();
        public List<TilesetData> Tilesets = new List<TilesetData>();
        public List<MapConnection> Connections = new List<MapConnection>();
        public List<CommonEventData> CommonEvents = new List<CommonEventData>();

        readonly Dictionary<int, MapData> _maps = new Dictionary<int, MapData>();
        public readonly List<DatabaseTable> Tables = new List<DatabaseTable>();

        public Project()
        {
            AddTable("types", "Types", typeof(TypeData), "types.json", () => Types);
            AddTable("abilities", "Abilities", typeof(AbilityData), "abilities.json", () => Abilities);
            AddTable("moves", "Moves", typeof(MoveData), "moves.json", () => Moves);
            AddTable("items", "Items", typeof(ItemData), "items.json", () => Items);
            AddTable("species", "Pokémon", typeof(SpeciesData), "species.json", () => Species);
            AddTable("trainer_types", "Trainer Types", typeof(TrainerTypeData), "trainer_types.json", () => TrainerTypes);
            AddTable("trainers", "Trainers", typeof(TrainerData), "trainers.json", () => Trainers);
            AddTable("encounters", "Wild Encounters", typeof(EncounterData), "encounters.json", () => Encounters);
            AddTable("town_maps", "Town Maps", typeof(TownMapRegion), "town_maps.json", () => TownMaps);
            AddTable("regional_dexes", "Regional Dexes", typeof(RegionalDex), "regional_dexes.json", () => RegionalDexes);
            AddTable("animations", "Battle Animations", typeof(BattleAnimation), "animations.json", () => Animations);
            AddTable("mystery_gifts", "Mystery Gifts", typeof(MysteryGiftData), "mystery_gifts.json", () => MysteryGifts);
            AddTable("ribbons", "Ribbons", typeof(RibbonData), "ribbons.json", () => Ribbons);
            AddTable("phone_messages", "Phone Messages", typeof(PhoneMessageData), "phone_messages.json", () => PhoneMessages);
        }

        void AddTable(string key, string label, Type t, string file, Func<IList> get)
        {
            Tables.Add(new DatabaseTable { Key = key, Label = label, RecordType = t, FileName = file, Get = get });
        }

        public DatabaseTable Table(string key) { return Tables.FirstOrDefault(t => t.Key == key); }

        // ------------------------------------------------------------------ paths
        public string DataDir { get { return Path.Combine(Root, "Data"); } }
        public string MapsDir { get { return Path.Combine(DataDir, "Maps"); } }
        public string GraphicsDir { get { return Path.Combine(Root, "Graphics"); } }
        public string AudioDir { get { return Path.Combine(Root, "Audio"); } }
        public string FontsDir { get { return Path.Combine(Root, "Fonts"); } }
        public string ScriptsDir { get { return Path.Combine(Root, "Scripts"); } }
        public string DataFile(string name) { return Path.Combine(DataDir, name); }
        public string MapFile(int id) { return Path.Combine(MapsDir, "Map" + id.ToString("000") + ".json"); }

        /// <summary>Finds a resource like ("Graphics/Characters", "NPC 01") trying common extensions.</summary>
        /// <summary>More folders searched for assets after the project's own (switched-on plugins).</summary>
        public readonly List<string> ExtraAssetRoots = new List<string>();

        public string FindAsset(string folder, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string found = FindIn(Root, folder, name);
            for (int i = 0; found == null && i < ExtraAssetRoots.Count; i++) found = FindIn(ExtraAssetRoots[i], folder, name);
            // Fonts fall back to the shared defaults next to the programs (BaseData/Fonts) until the project has its own
            if (found == null && folder == "Fonts" && SharedFontsDir != null) found = FindIn(Path.GetDirectoryName(SharedFontsDir), "Fonts", name);
            return found;
        }

        static string FindIn(string root, string folder, string name)
        {
            string basePath = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar), name);
            if (File.Exists(basePath)) return basePath;
            foreach (var ext in new[] { ".png", ".gif", ".bmp", ".jpg", ".ogg", ".wav", ".mp3", ".mid", ".midi", ".m4a", ".mp4", ".flac", ".opus", ".ttf", ".otf" })
                if (File.Exists(basePath + ext)) return basePath + ext;
            // Case-insensitive fallback (projects copied from Windows to Linux)
            var dir = Path.GetDirectoryName(basePath);
            if (Directory.Exists(dir))
            {
                string want = Path.GetFileName(basePath).ToLowerInvariant();
                foreach (var f in Directory.GetFiles(dir))
                {
                    string fn = Path.GetFileName(f).ToLowerInvariant();
                    if (fn == want || Path.GetFileNameWithoutExtension(f).ToLowerInvariant() == want) return f;
                }
            }
            return null;
        }

        /// <summary>BaseData/Fonts next to the programs: the default fonts every project uses until it has its own. Null if missing.</summary>
        public static string SharedFontsDir
        {
            get
            {
                string b = BaseData.DefaultFolder;
                string d = b == null ? null : Path.Combine(b, "Fonts");
                return d != null && Directory.Exists(d) ? d : null;
            }
        }

        static string[] FontFiles(string dir)
        {
            if (dir == null || !Directory.Exists(dir)) return new string[0];
            return Directory.GetFiles(dir).Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToArray();
        }

        /// <summary>The project's own fonts (its Fonts folder).</summary>
        public string[] ProjectFonts { get { return FontFiles(FontsDir); } }
        /// <summary>The shared default fonts (BaseData/Fonts).</summary>
        public static string[] SharedFonts { get { return FontFiles(SharedFontsDir); } }

        string InSharedFonts(string name) { string d = SharedFontsDir; return d == null || string.IsNullOrEmpty(name) ? null : FindIn(Path.GetDirectoryName(d), "Fonts", name); }
        public string InProjectFonts(string name) { return string.IsNullOrEmpty(name) ? null : FindIn(Root, "Fonts", name); }

        /// <summary>The default font: the Message font if it's one of the shared defaults, else Power Green, else any default.</summary>
        public string DefaultFontFile() { return InSharedFonts(Settings.Font) ?? InSharedFonts("power green") ?? SharedFonts.FirstOrDefault(); }

        /// <summary>
        /// The message font file to use. With "Use my own font" off (the default), the shared default font, even if the
        /// project has fonts; with it on, the Message font from the project's Fonts folder. Each falls back to the other
        /// so text always shows. Null if there are no fonts at all.
        /// </summary>
        public string MessageFontFile()
        {
            if (Settings.UseProjectFont) return InProjectFonts(Settings.Font) ?? ProjectFonts.FirstOrDefault() ?? DefaultFontFile();
            return DefaultFontFile() ?? InProjectFonts(Settings.Font) ?? InProjectFonts("power green") ?? ProjectFonts.FirstOrDefault();
        }

        // ------------------------------------------------------------------ create / load / save
        public static Project Create(string root, string name)
        {
            var p = new Project { Root = Path.GetFullPath(root) };
            p.Info.Name = name;
            p.Settings.Title = name;
            foreach (var d in new[] { "Data/Maps", "Graphics/Characters", "Graphics/Tilesets", "Graphics/Autotiles", "Graphics/Pokemon/Front", "Graphics/Pokemon/Back", "Graphics/Pokemon/Icons", "Graphics/Pokemon/Animated/Front", "Graphics/Pokemon/Animated/Back", "Graphics/Pokemon/Animated/Front shiny", "Graphics/Pokemon/Animated/Back shiny", "Graphics/Items", "Graphics/Trainers", "Graphics/Windowskins", "Graphics/Pictures", "Graphics/Battlebacks", "Audio/BGM", "Audio/BGS", "Audio/ME", "Audio/SE", "Fonts", "Scripts" })
                Directory.CreateDirectory(Path.Combine(p.Root, d));
            p.System.Switches.AddRange(new string[51]);
            p.System.Variables.AddRange(new string[51]);
            p.Tilesets.Add(new TilesetData { Id = 1, Name = "Default" });
            var map = MapData.Create(1, 20, 15, 1);
            p.MapInfos.Add(new MapInfo { Id = 1, Name = "MAP001", Order = 1 });
            p._maps[1] = map;
            p.Settings.StartMap = 1; p.Settings.StartX = 10; p.Settings.StartY = 7;
            // Start with the complete built-in database (every Pokémon, move, ability, item...)
            BaseData.Fill(p, BaseData.DefaultFolder);
            p.Players.Add(new PlayerCharacter { TrainerType = "POKEMONTRAINER_Red", WalkCharset = "trainer_POKEMONTRAINER_Red" });
            p.Save();
            p.SaveMap(map);
            return p;
        }

        public static bool IsProjectFolder(string dir) { return File.Exists(Path.Combine(dir, ProjectFileName)); }

        public static Project Load(string root)
        {
            if (File.Exists(root) && Path.GetFileName(root) == ProjectFileName) root = Path.GetDirectoryName(root);
            var p = new Project { Root = Path.GetFullPath(root) };
            p.Info = Json.Json.FromFile<ProjectInfo>(Path.Combine(p.Root, ProjectFileName));
            p.Settings = LoadOr(p.DataFile("settings.json"), new GameSettings());
            p.Ui = LoadOr(p.DataFile("ui.json"), new UiSkin());
            p.System = LoadOr(p.DataFile("system.json"), new SystemData());
            p.Players = LoadOr(p.DataFile("players.json"), new List<PlayerCharacter>());
            p.Types = LoadOr(p.DataFile("types.json"), p.Types);
            p.Abilities = LoadOr(p.DataFile("abilities.json"), p.Abilities);
            p.Moves = LoadOr(p.DataFile("moves.json"), p.Moves);
            p.Items = LoadOr(p.DataFile("items.json"), p.Items);
            p.Species = LoadOr(p.DataFile("species.json"), p.Species);
            p.TrainerTypes = LoadOr(p.DataFile("trainer_types.json"), p.TrainerTypes);
            p.Trainers = LoadOr(p.DataFile("trainers.json"), p.Trainers);
            p.Encounters = LoadOr(p.DataFile("encounters.json"), p.Encounters);
            p.TownMaps = LoadOr(p.DataFile("town_maps.json"), p.TownMaps);
            p.RegionalDexes = LoadOr(p.DataFile("regional_dexes.json"), p.RegionalDexes);
            p.Animations = LoadOr(p.DataFile("animations.json"), p.Animations);
            p.MysteryGifts = LoadOr(p.DataFile("mystery_gifts.json"), p.MysteryGifts);
            p.MapInfos = LoadOr(p.DataFile("mapinfos.json"), p.MapInfos);
            p.Tilesets = LoadOr(p.DataFile("tilesets.json"), p.Tilesets);
            p.Connections = LoadOr(p.DataFile("map_connections.json"), p.Connections);
            p.CommonEvents = LoadOr(p.DataFile("common_events.json"), p.CommonEvents);
            p.MapAnimations = LoadOr(p.DataFile("map_animations.json"), p.MapAnimations);
            p.Ribbons = LoadOr(p.DataFile("ribbons.json"), p.Ribbons);
            p.PhoneMessages = LoadOr(p.DataFile("phone_messages.json"), p.PhoneMessages);
            PokeCreator.Pkmn.FormRules.EnsureGen9Forms(p);
            PokeCreator.Pkmn.Pokemon.UseSettings(p);
            return p;
        }

        public string TilesetsDir { get { return Path.Combine(Root, "Graphics", "Tilesets"); } }

        /// <summary>Adds new images found in tileset folders as sheets. Returns true if anything changed (save tilesets.json).</summary>
        public bool SyncTilesetFolders()
        {
            bool changed = false;
            foreach (var t in Tilesets) if (t.SyncFolder(TilesetsDir).Count > 0) changed = true;
            return changed;
        }

        static T LoadOr<T>(string path, T fallback)
        {
            if (!File.Exists(path)) return fallback;
            try { return Json.Json.FromFile<T>(path); }
            catch (Exception ex) { throw new InvalidDataException("Could not read " + Path.GetFileName(path) + ": " + ex.Message, ex); }
        }

        /// <summary>Folders of animated (Gen 5 style) Pokémon sprites: horizontal strips of square frames named like the still sprites.</summary>
        public static readonly string[] AnimatedSpriteFolders = { "Graphics/Pokemon/Animated/Front", "Graphics/Pokemon/Animated/Back", "Graphics/Pokemon/Animated/Front shiny", "Graphics/Pokemon/Animated/Back shiny" };

        /// <summary>Creates the animated sprite folders if the project doesn't have them yet (projects made before they existed).</summary>
        public void EnsureAnimatedFolders()
        {
            // (the four audio folders too, so Studio always shows where music and sounds go)
            foreach (var d in AnimatedSpriteFolders.Concat(new[] { "Audio/BGM", "Audio/BGS", "Audio/ME", "Audio/SE" }))
                try { Directory.CreateDirectory(Path.Combine(Root, d.Replace('/', Path.DirectorySeparatorChar))); } catch { }
        }

        public void Save()
        {
            Directory.CreateDirectory(DataDir);
            Json.Json.ToFile(Path.Combine(Root, ProjectFileName), Info);
            Json.Json.ToFile(DataFile("settings.json"), Settings);
            Json.Json.ToFile(DataFile("ui.json"), Ui);
            Json.Json.ToFile(DataFile("system.json"), System);
            Json.Json.ToFile(DataFile("players.json"), Players);
            foreach (var t in Tables) Json.Json.ToFile(DataFile(t.FileName), t.Items);
            Json.Json.ToFile(DataFile("mapinfos.json"), MapInfos);
            Json.Json.ToFile(DataFile("tilesets.json"), Tilesets);
            Json.Json.ToFile(DataFile("map_connections.json"), Connections);
            Json.Json.ToFile(DataFile("common_events.json"), CommonEvents);
            if (MapAnimations.Count > 0) Json.Json.ToFile(DataFile("map_animations.json"), MapAnimations);
        }

        public CommonEventData GetCommonEvent(int id) { return CommonEvents.FirstOrDefault(c => c.Id == id); }

        /// <summary>Changes the project folder (used by importers before the first Save).</summary>
        public void SetRoot(string root) { Root = Path.GetFullPath(root); }

        // ------------------------------------------------------------------ maps
        public MapInfo GetMapInfo(int id) { return MapInfos.FirstOrDefault(m => m.Id == id); }
        public TilesetData GetTileset(int id) { return Tilesets.FirstOrDefault(t => t.Id == id); }

        public MapData LoadMap(int id)
        {
            MapData m;
            if (_maps.TryGetValue(id, out m)) return m;
            string f = MapFile(id);
            if (!File.Exists(f)) return null;
            m = Json.Json.FromFile<MapData>(f);
            m.Id = id;
            _maps[id] = m;
            return m;
        }

        public void SaveMap(MapData m)
        {
            _maps[m.Id] = m;
            Json.Json.ToFile(MapFile(m.Id), m);
        }

        public void ForgetMap(int id) { _maps.Remove(id); }

        public MapData CreateMap(string name, int parentId, int w, int h, int tilesetId)
        {
            int id = 1; while (MapInfos.Any(mi => mi.Id == id)) id++;
            var info = new MapInfo { Id = id, Name = name, ParentId = parentId, Order = MapInfos.Count + 1 };
            MapInfos.Add(info);
            var m = MapData.Create(id, w, h, tilesetId);
            SaveMap(m);
            return m;
        }

        /// <summary>Copies a map (tiles, events, properties) to a new map right after it in the map tree.</summary>
        public MapData DuplicateMap(int sourceId)
        {
            var src = LoadMap(sourceId);
            var srcInfo = MapInfos.FirstOrDefault(mi => mi.Id == sourceId);
            if (src == null || srcInfo == null) return null;
            int id = 1; while (MapInfos.Any(mi => mi.Id == id)) id++;
            var copy = Json.Json.Deserialize<MapData>(Json.Json.Write(src, false));
            copy.Id = id;
            foreach (var mi in MapInfos) if (mi.Order > srcInfo.Order) mi.Order++;
            MapInfos.Add(new MapInfo { Id = id, Name = srcInfo.Name + " (copy)", ParentId = srcInfo.ParentId, Order = srcInfo.Order + 1 });
            SaveMap(copy);
            return copy;
        }

        public void DeleteMap(int id)
        {
            MapInfos.RemoveAll(mi => mi.Id == id);
            foreach (var mi in MapInfos) if (mi.ParentId == id) mi.ParentId = 0;
            _maps.Remove(id);
            if (File.Exists(MapFile(id))) File.Delete(MapFile(id));
        }

        public string SwitchName(int id) { return id > 0 && id < System.Switches.Count ? System.Switches[id] ?? "" : ""; }
        public string VariableName(int id) { return id > 0 && id < System.Variables.Count ? System.Variables[id] ?? "" : ""; }
    }
}
