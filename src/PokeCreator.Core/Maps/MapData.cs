using System;
using System.Collections.Generic;
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
        [Field("Weather", Help = "e.g. Rain,50 (type, % chance)")] public string Weather { get; set; }
        [Field("Town map position (region,x,y)"), JsonCompact] public List<int> MapPosition { get; set; }
        [Field("Dive map ID")] public int DiveMap { get; set; }
        [Field("Dark (needs Flash)")] public bool DarkMap { get; set; }
        [Field("Safari Zone")] public bool SafariMap { get; set; }
        [Field("Snap edges", Help = "Camera stops at the map edge.")] public bool SnapEdges { get; set; }
        [Field("Random dungeon")] public bool Dungeon { get; set; }
        [Field("Battle background", Help = "Name in Graphics/Battlebacks, e.g. field")] public string BattleBack { get; set; }
        [Field("Battle environment")] public string Environment { get; set; }
        [Field("Wild battle BGM", Asset = "Audio/BGM")] public string WildBattleBGM { get; set; }
        [Field("Trainer battle BGM", Asset = "Audio/BGM")] public string TrainerBattleBGM { get; set; }
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

        public int NextEventId()
        {
            int max = 0; foreach (var e in Events) max = Math.Max(max, e.Id); return max + 1;
        }
    }

    public class TilesetData
    {
        public int Id { get; set; }
        public string Name { get; set; }
        /// <summary>Image in Graphics/Tilesets (8 tiles = 256px wide).</summary>
        public string TilesetName { get; set; }
        /// <summary>7 autotile images in Graphics/Autotiles.</summary>
        public List<string> AutotileNames { get; set; }
        public string PanoramaName { get; set; }
        public int PanoramaHue { get; set; }
        public string FogName { get; set; }
        public string BattlebackName { get; set; }
        /// <summary>Per tile ID: bit 1 down, 2 left, 4 right, 8 up blocked; 0x40 bush; 0x80 counter.</summary>
        [JsonCompact] public List<int> Passages { get; set; }
        /// <summary>Per tile ID: 0 = below characters, 1-5 = drawn above.</summary>
        [JsonCompact] public List<int> Priorities { get; set; }
        /// <summary>Per tile ID: Essentials terrain tag (2 grass, 5 ledge, 6 water...).</summary>
        [JsonCompact] public List<int> TerrainTags { get; set; }

        public TilesetData()
        {
            Name = ""; TilesetName = ""; AutotileNames = new List<string> { "", "", "", "", "", "", "" };
            Passages = new List<int>(); Priorities = new List<int>(); TerrainTags = new List<int>();
        }

        public int Passage(int id) { return id >= 0 && id < Passages.Count ? Passages[id] : 0; }
        public int Priority(int id) { return id >= 0 && id < Priorities.Count ? Priorities[id] : 0; }
        public int TerrainTag(int id) { return id >= 0 && id < TerrainTags.Count ? TerrainTags[id] : 0; }

        /// <summary>Make the per-tile tables long enough for a tileset image with the given number of rows.</summary>
        public void EnsureSize(int tileRows)
        {
            int n = 384 + tileRows * 8;
            while (Passages.Count < n) Passages.Add(0);
            while (Priorities.Count < n) Priorities.Add(0);
            while (TerrainTags.Count < n) TerrainTags.Add(0);
        }
        public override string ToString() { return Id.ToString("000") + ": " + Name; }
    }

    public static class TerrainTags
    {
        public static readonly string[] Names = { "None", "Ledge", "Grass", "Sand", "Rock", "Deep water", "Still water", "Water", "Waterfall", "Waterfall crest", "Tall grass", "Underwater grass", "Ice", "Neutral", "Sooty grass", "Bridge", "Puddle", "No effect" };
    }
}
