using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Json;

namespace PokeCreator.Maps
{
    public class AudioFile
    {
        public string Name { get; set; }
        public int Volume { get; set; }
        public int Pitch { get; set; }
        public AudioFile() { Name = ""; Volume = 100; Pitch = 100; }
        public AudioFile(string name, int volume = 100, int pitch = 100) { Name = name; Volume = volume; Pitch = pitch; }
        public override string ToString() { return string.IsNullOrEmpty(Name) ? "(none)" : Name; }
    }

    /// <summary>Entry in the map tree (Maps panel).</summary>
    public class MapInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int ParentId { get; set; }
        public int Order { get; set; }
        public bool Expanded { get; set; }
        /// <summary>Optional icon shown in the Studio's map tree ("town", "route", "cave"...). Empty = guess from the name.</summary>
        public string Icon { get; set; }
        public override string ToString() { return Id.ToString("000") + ": " + Name; }
    }

    /// <summary>Pokémon-specific map settings (Essentials map_metadata.txt).</summary>
    public class MapProperties
    {
        [Field("Display name", Help = "Shown in location signs. \\PN = player name.")] public string Name { get; set; }
        [Field("Outdoor", Help = "Day/night tinting applies.")] public bool Outdoor { get; set; }
        [Field("Show location sign")] public bool ShowArea { get; set; }
        [Field("Bicycle allowed")] public bool Bicycle { get; set; }
        [Field("Always on bicycle")] public bool BicycleAlways { get; set; }
        [Field("Healing spot (map,x,y)", Help = "Where you respawn after blacking out."), JsonCompact] public List<int> HealingSpot { get; set; }
        [Field("Weather", Help = "Type and % chance it happens when the player arrives, e.g. Rain,50. Types: Rain, Storm, Snow, Blizzard, Sandstorm, HeavyRain, Sun, Fog.")] public string Weather { get; set; }
        [Field("Town map position (region,x,y)"), JsonCompact] public List<int> MapPosition { get; set; }
        [Field("Dive map ID")] public int DiveMap { get; set; }
        [Field("Dark (needs Flash)")] public bool DarkMap { get; set; }
        [Field("Safari Zone")] public bool SafariMap { get; set; }
        [Field("Snap edges", Help = "Camera stops at the map edge.")] public bool SnapEdges { get; set; }
        [Field("Still reflections", Help = "Reflections in water and puddles don't ripple.")] public bool StillReflections { get; set; }
        [Field("Random dungeon", Help = "A new layout of rooms and corridors is made each time the player enters.")] public bool Dungeon { get; set; }
        [Field("Dungeon floor tile", Help = "Tile ID for a random dungeon's floor (0 = the most common walkable tile painted on the map).")] public int DungeonFloor { get; set; }
        [Field("Dungeon wall tile", Help = "Tile ID for a random dungeon's walls (0 = the most common blocking tile painted on the map).")] public int DungeonWall { get; set; }
        [Field("Battle background", Help = "Name in Graphics/Battlebacks, e.g. field")] public string BattleBack { get; set; }
        [Field("Battle environment", Choices = new[] { "", "None", "Grass", "TallGrass", "MovingWater", "StillWater", "Puddle", "Underwater", "Cave", "Rock", "Sand", "Forest", "ForestGrass", "Snow", "Ice", "Volcano", "Graveyard", "Sky", "Space", "UltraSpace" }, Help = "Empty: from the tile the player stands on. Changes Nature Power, Camouflage, Secret Power and the battle bases.")] public string Environment { get; set; }
        [Field("Wild battle BGM", Asset = "Audio/BGM")] public string WildBattleBGM { get; set; }
        [Field("Trainer battle BGM", Asset = "Audio/BGM")] public string TrainerBattleBGM { get; set; }
        [Field("Wild victory music", Asset = "Audio/BGM", Help = "Instead of the game's wild victory music on this map.")] public string WildVictoryBGM { get; set; }
        [Field("Trainer victory music", Asset = "Audio/BGM", Help = "Instead of the game's trainer victory music on this map.")] public string TrainerVictoryBGM { get; set; }
        [Field("Wild capture ME", Asset = "Audio/ME", Help = "Instead of the game's capture jingle on this map.")] public string WildCaptureME { get; set; }
        [Field("Map size (width, squares)")] public string MapSize { get; set; }
        [Field("Flags")] public List<string> Flags { get; set; }
        public MapProperties() { HealingSpot = new List<int>(); MapPosition = new List<int>(); Flags = new List<string>(); }
    }

    public class EventCommand
    {
        /// <summary>RPG Maker XP compatible command code (101 = Show Text, 201 = Transfer Player ...).</summary>
        public int Code { get; set; }
        public int Indent { get; set; }
        public List<object> Params { get; set; }
        public EventCommand() { Params = new List<object>(); }
        public EventCommand(int code, int indent, params object[] p) { Code = code; Indent = indent; Params = new List<object>(p); }

        public string StrParam(int i) { return i < Params.Count && Params[i] != null ? Convert.ToString(Params[i], System.Globalization.CultureInfo.InvariantCulture) : ""; }
        public int IntParam(int i) { if (i >= Params.Count || Params[i] == null) return 0; var o = Params[i]; if (o is bool) return (bool)o ? 1 : 0; try { return Convert.ToInt32(o, System.Globalization.CultureInfo.InvariantCulture); } catch { return 0; } }
        public bool BoolParam(int i) { if (i >= Params.Count || Params[i] == null) return false; var o = Params[i]; if (o is bool) return (bool)o; return IntParam(i) != 0; }
    }

    public class MoveRoute
    {
        public bool Repeat { get; set; }
        public bool Skippable { get; set; }
        public List<EventCommand> List { get; set; }
        public MoveRoute() { Repeat = true; List = new List<EventCommand>(); }
    }

    public class EventCondition
    {
        public bool Switch1Valid { get; set; }
        public int Switch1Id { get; set; }
        public bool Switch2Valid { get; set; }
        public int Switch2Id { get; set; }
        public bool VariableValid { get; set; }
        public int VariableId { get; set; }
        public int VariableValue { get; set; }
        public bool SelfSwitchValid { get; set; }
        public string SelfSwitchCh { get; set; }
        public EventCondition() { Switch1Id = 1; Switch2Id = 1; VariableId = 1; SelfSwitchCh = "A"; }
    }

    public class EventGraphic
    {
        public int TileId { get; set; }
        public string CharacterName { get; set; }
        public int CharacterHue { get; set; }
        public int Direction { get; set; }
        public int Pattern { get; set; }
        public int Opacity { get; set; }
        public int BlendType { get; set; }
        public EventGraphic() { CharacterName = ""; Direction = 2; Opacity = 255; }
    }

    public static class Triggers
    {
        public const int Action = 0, PlayerTouch = 1, EventTouch = 2, Autorun = 3, Parallel = 4;
        public static readonly string[] Names = { "Action button", "Player touch", "Event touch", "Autorun", "Parallel process" };
    }

    public class EventPage
    {
        public EventCondition Condition { get; set; }
        public EventGraphic Graphic { get; set; }
        public int MoveType { get; set; }
        public int MoveSpeed { get; set; }
        public int MoveFrequency { get; set; }
        public MoveRoute MoveRoute { get; set; }
        public bool WalkAnime { get; set; }
        public bool StepAnime { get; set; }
        public bool DirectionFix { get; set; }
        public bool Through { get; set; }
        public bool AlwaysOnTop { get; set; }
        public int Trigger { get; set; }
        public List<EventCommand> List { get; set; }
        public EventPage()
        {
            Condition = new EventCondition(); Graphic = new EventGraphic(); MoveRoute = new MoveRoute();
            MoveSpeed = 3; MoveFrequency = 3; WalkAnime = true; List = new List<EventCommand> { new EventCommand(0, 0) };
        }
    }

    /// <summary>
    /// An event that isn't on a map: called from other events (Call Common Event), or run by itself
    /// while a switch is on (Autorun stops the player; Parallel runs alongside play).
    /// </summary>
    public class CommonEventData
    {
        public static readonly string[] TriggerNames = { "None (only when called)", "Autorun (while switch is ON)", "Parallel (while switch is ON)" };
        public int Id { get; set; }
        public string Name { get; set; }
        /// <summary>0 = only when called, 1 = autorun, 2 = parallel.</summary>
        public int Trigger { get; set; }
        /// <summary>Switch that must be ON for autorun/parallel common events.</summary>
        public int SwitchId { get; set; }
        public List<EventCommand> List { get; set; }
        public CommonEventData() { Name = ""; SwitchId = 1; List = new List<EventCommand>(); }
        public override string ToString() { return Id.ToString("000") + ": " + Name; }
    }

    public class MapEvent
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public List<EventPage> Pages { get; set; }
        public MapEvent() { Name = ""; Pages = new List<EventPage> { new EventPage() }; }
        public override string ToString() { return Id.ToString("000") + ": " + Name + " (" + X + "," + Y + ")"; }
    }

    public class MapData
    {
        public int Id { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int TilesetId { get; set; }
        public bool AutoplayBgm { get; set; }
        public AudioFile Bgm { get; set; }
        public bool AutoplayBgs { get; set; }
        public AudioFile Bgs { get; set; }
        public int EncounterStep { get; set; }
        public MapProperties Properties { get; set; }
        /// <summary>Tile IDs: Layers[layer][y][x]. 0 = empty, 48..383 = autotiles, 384+ = tileset tiles.</summary>
        public List<List<List<int>>> Layers { get; set; }
        public List<MapEvent> Events { get; set; }

        public MapData()
        {
            Bgm = new AudioFile(); Bgs = new AudioFile("", 80, 100); Properties = new MapProperties();
            Layers = new List<List<List<int>>>(); Events = new List<MapEvent>(); EncounterStep = 30;
        }
        public static MapData Create(int id, int w, int h, int tileset)
        {
            var m = new MapData { Id = id, Width = w, Height = h, TilesetId = tileset };
            for (int z = 0; z < 3; z++)
            {
                var layer = new List<List<int>>();
                for (int y = 0; y < h; y++) { var row = new List<int>(new int[w]); layer.Add(row); }
                m.Layers.Add(layer);
            }
            return m;
        }

        public bool Valid(int x, int y) { return x >= 0 && y >= 0 && x < Width && y < Height; }

        public int GetTile(int x, int y, int z)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || z < 0 || z >= Layers.Count) return 0;
            var row = Layers[z][y];
            return x < row.Count ? row[x] : 0;
        }
        public void SetTile(int x, int y, int z, int id)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || z < 0 || z >= Layers.Count) return;
            Layers[z][y][x] = id;
        }

        public void Resize(int w, int h)
        {
            foreach (var layer in Layers)
            {
                while (layer.Count > h) layer.RemoveAt(layer.Count - 1);
                while (layer.Count < h) layer.Add(new List<int>(new int[w]));
                foreach (var row in layer)
                {
                    while (row.Count > w) row.RemoveAt(row.Count - 1);
                    while (row.Count < w) row.Add(0);
                }
            }
            Width = w; Height = h;
            Events.RemoveAll(e => e.X >= w || e.Y >= h);
        }

        /// <summary>Moves every tile and event by (dx, dy); what falls off the map is dropped, new space is empty.</summary>
        public void Shift(int dx, int dy)
        {
            foreach (var layer in Layers)
            {
                var old = layer.Select(r => new List<int>(r)).ToList();
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        int sx = x - dx, sy = y - dy;
                        layer[y][x] = sy >= 0 && sy < old.Count && sx >= 0 && sx < old[sy].Count ? old[sy][sx] : 0;
                    }
            }
            foreach (var e in Events) { e.X += dx; e.Y += dy; }
            Events.RemoveAll(e => e.X < 0 || e.Y < 0 || e.X >= Width || e.Y >= Height);
        }

        public int NextEventId()
        {
            int max = 0; foreach (var e in Events) max = Math.Max(max, e.Id); return max + 1;
        }
    }

    /// <summary>
    /// One image of a tileset folder, usually one per area (Outdoor, Cave, Poké Center...).
    /// Its tiles have IDs 384 + Slot × 32768 + index, so a map can mix tiles from every sheet in its folder.
    /// </summary>
    public class TileSheet
    {
        /// <summary>Image in Graphics/Tilesets without extension, e.g. "FireRed/Cave".</summary>
        public string Name { get; set; }
        /// <summary>Fixed number that tile IDs use to point at this sheet. Never reused or changed.</summary>
        public int Slot { get; set; }
        /// <summary>Per tile (row × 8 + column): passage bits, see TilesetData.Passages.</summary>
        [JsonCompact] public List<int> Passages { get; set; }
        [JsonCompact] public List<int> Priorities { get; set; }
        [JsonCompact] public List<int> TerrainTags { get; set; }

        public TileSheet() { Name = ""; Passages = new List<int>(); Priorities = new List<int>(); TerrainTags = new List<int>(); }

        /// <summary>Area name shown in the editor: the image's file name.</summary>
        public string Label { get { int k = (Name ?? "").LastIndexOfAny(new[] { '/', '\\' }); return k >= 0 ? Name.Substring(k + 1) : Name; } }

        public void EnsureSize(int tileRows)
        {
            int n = tileRows * 8;
            while (Passages.Count < n) Passages.Add(0);
            while (Priorities.Count < n) Priorities.Add(0);
            while (TerrainTags.Count < n) TerrainTags.Add(0);
        }
        public override string ToString() { return Label; }
    }

    /// <summary>
    /// A tileset: 7 autotiles plus one or more tile sheets. With <see cref="Folder"/> set, every image in
    /// Graphics/Tilesets/&lt;Folder&gt; is a sheet, and maps using this tileset can paint from all of them.
    /// </summary>
    public class TilesetData
    {
        public const int FirstTile = 384;
        /// <summary>Tile IDs reserved per sheet (8 columns × 4096 rows).</summary>
        public const int SheetTiles = 8 * 4096;

        public int Id { get; set; }
        public string Name { get; set; }
        /// <summary>Folder in Graphics/Tilesets holding this tileset's sheets (one image per area). Empty = single image.</summary>
        [Field("Folder (one image per area)", Help = "A folder in Graphics/Tilesets. Every image in it becomes an area this tileset's maps can paint from.")]
        public string Folder { get; set; }
        /// <summary>First sheet (slot 0): image in Graphics/Tilesets (8 tiles = 256px wide).</summary>
        public string TilesetName { get; set; }
        /// <summary>7 autotile images in Graphics/Autotiles.</summary>
        public List<string> AutotileNames { get; set; }
        /// <summary>Picture in Graphics/Panoramas shown behind the map, scrolling at half speed (RMXP).</summary>
        [Field("Panorama (Graphics/Panoramas)", Asset = "Graphics/Panoramas", Group = "Panorama and fog")] public string PanoramaName { get; set; }
        [Field("Panorama hue", Group = "Panorama and fog")] public int PanoramaHue { get; set; }
        /// <summary>Picture in Graphics/Fogs tiled over the map (RMXP fog).</summary>
        [Field("Fog (Graphics/Fogs)", Asset = "Graphics/Fogs", Group = "Panorama and fog")] public string FogName { get; set; }
        [Field("Fog hue", Group = "Panorama and fog")] public int FogHue { get; set; }
        [Field("Fog opacity (0-255)", Group = "Panorama and fog")] public int FogOpacity { get; set; }
        [Field("Fog blend", Help = "0 normal, 1 add, 2 subtract", Group = "Panorama and fog")] public int FogBlend { get; set; }
        [Field("Fog zoom %", Group = "Panorama and fog")] public int FogZoom { get; set; }
        [Field("Fog scroll X", Help = "Pixels per 8 frames", Group = "Panorama and fog")] public int FogSx { get; set; }
        [Field("Fog scroll Y", Group = "Panorama and fog")] public int FogSy { get; set; }
        public string BattlebackName { get; set; }
        /// <summary>More sheets (slot 1 and up) from the tileset's folder.</summary>
        public List<TileSheet> Sheets { get; set; }
        /// <summary>Per tile ID (autotiles and the first sheet): bit 1 down, 2 left, 4 right, 8 up blocked; 0x40 bush; 0x80 counter.</summary>
        [JsonCompact] public List<int> Passages { get; set; }
        /// <summary>Per tile ID: 0 = below characters, 1-5 = drawn above.</summary>
        [JsonCompact] public List<int> Priorities { get; set; }
        /// <summary>Per tile ID: Essentials terrain tag (1 ledge, 2 grass, 5-7 water, 10 tall grass... see TerrainTags.Names).</summary>
        [JsonCompact] public List<int> TerrainTags { get; set; }

        public TilesetData()
        {
            Name = ""; Folder = ""; TilesetName = ""; AutotileNames = new List<string> { "", "", "", "", "", "", "" };
            FogOpacity = 64; FogZoom = 100;
            Sheets = new List<TileSheet>();
            Passages = new List<int>(); Priorities = new List<int>(); TerrainTags = new List<int>();
        }

        // ------------------------------------------------------------------ tile IDs
        public static int TileId(int slot, int index) { return FirstTile + slot * SheetTiles + index; }
        /// <summary>Which sheet a tile comes from (-1 for autotiles and blank).</summary>
        public static int SlotOf(int id) { return id < FirstTile ? -1 : (id - FirstTile) / SheetTiles; }
        /// <summary>Tile index within its sheet (row × 8 + column).</summary>
        public static int IndexOf(int id) { return id < FirstTile ? -1 : (id - FirstTile) % SheetTiles; }

        /// <summary>Image name for a slot (slot 0 = TilesetName), or null.</summary>
        public string SheetImage(int slot)
        {
            if (slot == 0) return string.IsNullOrEmpty(TilesetName) ? null : TilesetName;
            var s = Sheet(slot);
            return s == null ? null : s.Name;
        }

        public TileSheet Sheet(int slot)
        {
            if (Sheets == null) return null;
            foreach (var s in Sheets) if (s.Slot == slot) return s;
            return null;
        }

        /// <summary>Every sheet as (slot, image name, label), first sheet first.</summary>
        public List<KeyValuePair<int, string>> AllSheets()
        {
            var list = new List<KeyValuePair<int, string>>();
            if (!string.IsNullOrEmpty(TilesetName)) list.Add(new KeyValuePair<int, string>(0, TilesetName));
            if (Sheets != null) foreach (var s in Sheets.OrderBy(x => x.Slot)) list.Add(new KeyValuePair<int, string>(s.Slot, s.Name));
            return list;
        }

        public static string LabelOf(string image) { int k = (image ?? "").LastIndexOfAny(new[] { '/', '\\' }); return k >= 0 ? image.Substring(k + 1) : image ?? ""; }

        // ------------------------------------------------------------------ per-tile settings
        static int Get(List<int> l, int i) { return l != null && i >= 0 && i < l.Count ? l[i] : 0; }
        static void Set(List<int> l, int i, int v) { if (i < 0) return; while (l.Count <= i) l.Add(0); l[i] = v; }

        List<int> Table(int id, int which, out int index)
        {
            int slot = SlotOf(id);
            if (slot <= 0) { index = id; return which == 0 ? Passages : which == 1 ? Priorities : TerrainTags; }
            index = IndexOf(id);
            var s = Sheet(slot);
            if (s == null) return null;
            return which == 0 ? s.Passages : which == 1 ? s.Priorities : s.TerrainTags;
        }

        public int Passage(int id) { int i; return Get(Table(id, 0, out i), i); }
        public int Priority(int id) { int i; return Get(Table(id, 1, out i), i); }
        public int TerrainTag(int id) { int i; return Get(Table(id, 2, out i), i); }
        public void SetPassage(int id, int v) { int i; var t = Table(id, 0, out i); if (t != null) Set(t, i, v); }
        public void SetPriority(int id, int v) { int i; var t = Table(id, 1, out i); if (t != null) Set(t, i, v); }
        public void SetTerrainTag(int id, int v) { int i; var t = Table(id, 2, out i); if (t != null) Set(t, i, v); }

        /// <summary>Make the per-tile tables long enough for the first sheet with the given number of rows.</summary>
        public void EnsureSize(int tileRows)
        {
            int n = 384 + tileRows * 8;
            while (Passages.Count < n) Passages.Add(0);
            while (Priorities.Count < n) Priorities.Add(0);
            while (TerrainTags.Count < n) TerrainTags.Add(0);
        }

        public void EnsureSize(int slot, int tileRows)
        {
            if (slot <= 0) { EnsureSize(tileRows); return; }
            var s = Sheet(slot); if (s != null) s.EnsureSize(tileRows);
        }

        // ------------------------------------------------------------------ folders
        static readonly string[] ImageExts = { ".png", ".bmp", ".gif", ".jpg" };

        /// <summary>
        /// Adds every image in Graphics/Tilesets/&lt;Folder&gt; that isn't a sheet yet (new slots, alphabetical).
        /// Sheets whose image was deleted are kept so maps that use them don't change. Returns the images added.
        /// </summary>
        public List<string> SyncFolder(string tilesetsDir)
        {
            var added = new List<string>();
            if (string.IsNullOrEmpty(Folder)) return added;
            string dir = System.IO.Path.Combine(tilesetsDir, Folder);
            if (!System.IO.Directory.Exists(dir)) return added;
            var known = new HashSet<string>(AllSheets().Select(k => k.Value.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
            var files = System.IO.Directory.GetFiles(dir)
                .Where(f => ImageExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => SortKey(System.IO.Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                string name = Folder.Replace('\\', '/').TrimEnd('/') + "/" + System.IO.Path.GetFileNameWithoutExtension(f);
                if (known.Contains(name)) continue;
                if (string.IsNullOrEmpty(TilesetName)) TilesetName = name;
                else
                {
                    int slot = 1;
                    foreach (var s in Sheets) slot = Math.Max(slot, s.Slot + 1);
                    Sheets.Add(new TileSheet { Name = name, Slot = slot });
                }
                known.Add(name);
                added.Add(name);
            }
            return added;
        }

        /// <summary>Outdoor/Outside sheets sort first so they become the first sheet.</summary>
        static string SortKey(string n)
        {
            string l = n.ToLowerInvariant();
            return (l.StartsWith("outdoor") || l.StartsWith("outside") || l.StartsWith("overworld") || l.StartsWith("general") ? "0" : "1") + n;
        }

        public override string ToString() { return Id.ToString("000") + ": " + Name; }
    }

    public static class TerrainTags
    {
        public static readonly string[] Names = { "None", "Ledge", "Grass", "Sand", "Rock", "Deep water", "Still water", "Water", "Waterfall", "Waterfall crest", "Tall grass", "Underwater grass", "Ice", "Neutral", "Sooty grass", "Bridge", "Puddle", "No effect" };
    }
}
