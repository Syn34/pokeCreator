using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Maps;
using PokeCreator.Projects;

namespace PokeCreator.Runtime.World
{
    /// <summary>Global progress: switches, variables, self switches, money, bag.</summary>
    public class GameState
    {
        public readonly Dictionary<int, bool> Switches = new Dictionary<int, bool>();
        public readonly Dictionary<int, int> Variables = new Dictionary<int, int>();
        public readonly HashSet<string> SelfSwitches = new HashSet<string>();
        public readonly Dictionary<string, int> Bag = new Dictionary<string, int>();
        public int Money;
        public string PlayerName = "Red";
        public int Steps;
        public int PlayerCharacter;
        public int TrainerId;
        public long PlayFrames;
        public List<PokeCreator.Pkmn.Pokemon> Party = new List<PokeCreator.Pkmn.Pokemon>();
        public const int MaxParty = 6;
        /// <summary>Pokémon sent to the PC when the party is full (boxes UI comes later).</summary>
        public List<PokeCreator.Pkmn.Pokemon> Storage = new List<PokeCreator.Pkmn.Pokemon>();
        /// <summary>Player flags set by scripts: has_pokedex, has_running_shoes, has_pokegear...</summary>
        public readonly HashSet<string> Flags = new HashSet<string>();
        /// <summary>Items stored in the player's PC.</summary>
        public readonly Dictionary<string, int> PcItems = new Dictionary<string, int>();
        /// <summary>Pokédex: species seen and caught (owned).</summary>
        public readonly HashSet<string> Seen = new HashSet<string>();
        public readonly HashSet<string> Caught = new HashSet<string>();
        /// <summary>Where the player goes after blacking out (set by pbSetPokemonCenter at a Pokémon Center).</summary>
        public int HealMap, HealX, HealY, HealDir;
        /// <summary>Steps left on a Repel.</summary>
        public int RepelSteps;
        /// <summary>When timed events last happened (pbSetEventTime), "map,event" -> seconds since 2000.</summary>
        public readonly Dictionary<string, long> EventTimes = new Dictionary<string, long>();
        /// <summary>Berries planted in soil, "map,event" -> plant.</summary>
        public Dictionary<string, BerryPlantState> BerryPlants = new Dictionary<string, BerryPlantState>();
        /// <summary>Pokégear phone contacts.</summary>
        public List<PhoneContact> Phone = new List<PhoneContact>();
        /// <summary>Game Corner coins, Battle Points and gym badges (0-based, as $player.badges[n]).</summary>
        public int Coins, BattlePoints;
        public readonly HashSet<int> Badges = new HashSet<int>();
        /// <summary>Teams entered into the Hall of Fame, newest last.</summary>
        public List<HallOfFameEntry> HallOfFame = new List<HallOfFameEntry>();
        /// <summary>Partner, roamers, Safari Zone, Bug-Catching Contest, Mystery Gift and other optional systems.</summary>
        public GameExtras Extras = new GameExtras();
        /// <summary>Pokémon left at the Day Care, its egg counter and whether an Egg is waiting.</summary>
        public List<DayCareSlot> DayCare = new List<DayCareSlot>();
        public int DayCareEggSteps;
        public bool DayCareEgg;
        /// <summary>Maps the player has been to (Fly only goes to visited places).</summary>
        public readonly HashSet<int> Visited = new HashSet<int>();
        /// <summary>Where Dig / the Escape Rope go back to: map, x, y, direction (set by pbSetEscapePoint).</summary>
        public List<int> EscapePoint;

        public bool HasPokedex { get { return Flags.Contains("has_pokedex"); } }

        /// <summary>Marks a species seen and caught in the Pokédex.</summary>
        public void Register(PokeCreator.Pkmn.Pokemon pk) { if (pk == null || pk.IsEgg) return; Seen.Add(pk.Species); Caught.Add(pk.Species); }

        public bool GetSwitch(int id) { bool v; return Switches.TryGetValue(id, out v) && v; }
        public void SetSwitch(int id, bool v) { Switches[id] = v; }
        public int GetVar(int id) { int v; return Variables.TryGetValue(id, out v) ? v : 0; }
        public void SetVar(int id, int v) { Variables[id] = v; StringVars.Remove(id); }
        /// <summary>Variables holding text (Essentials scripts store names in variables, shown with \v[n]).</summary>
        public readonly Dictionary<int, string> StringVars = new Dictionary<int, string>();
        public void SetStr(int id, string s) { Variables[id] = 0; StringVars[id] = s ?? ""; }
        /// <summary>A variable as text: its string if it holds one, otherwise its number.</summary>
        public string VarText(int id) { string s; return StringVars.TryGetValue(id, out s) ? s : GetVar(id).ToString(); }
        public bool GetSelf(int map, int ev, string ch) { return SelfSwitches.Contains(map + "," + ev + "," + ch); }
        public void SetSelf(int map, int ev, string ch, bool v)
        {
            string k = map + "," + ev + "," + ch;
            if (v) SelfSwitches.Add(k); else SelfSwitches.Remove(k);
        }
        public int ItemCount(string item) { int n; return item != null && Bag.TryGetValue(item, out n) ? n : 0; }
        public void RemoveItem(string item, int qty) { AddItem(item, -qty); }
        public void AddItem(string item, int qty)
        {
            int n; Bag.TryGetValue(item, out n);
            n = Math.Max(0, n + qty);
            if (n == 0) Bag.Remove(item); else Bag[item] = n;
        }
    }

    /// <summary>A map adjacent to the current one through PBS map connections.</summary>
    public class MapNeighbor
    {
        public GameMap Map;
        public int OffsetX, OffsetY; // neighbor tile (0,0) sits at (OffsetX, OffsetY) in current map coordinates
    }

    /// <summary>A loaded map: tiles, tileset rules, events and connections.</summary>
    public class GameMap
    {
        public readonly MapData Data;
        public readonly TilesetData Tileset;
        public readonly MapInfo Info;
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public readonly List<MapNeighbor> Neighbors = new List<MapNeighbor>();
        public int Id { get { return Data.Id; } }
        public int Width { get { return Data.Width; } }
        public int Height { get { return Data.Height; } }

        public static readonly int[] WaterTags = { 5, 6, 7, 8, 9 };
        public const int TagLedge = 1, TagGrass = 2, TagTallGrass = 10;
        public const int TagDeepWater = 5, TagWaterfall = 8, TagWaterfallCrest = 9, TagIce = 12;

        public GameMap(Project p, MapData data, Game game, bool withEvents)
        {
            Data = data;
            Tileset = p.GetTileset(data.TilesetId) ?? new TilesetData();
            Info = p.GetMapInfo(data.Id);
            if (withEvents) foreach (var e in data.Events) Events.Add(new GameEvent(game, this, e));
        }

        public string DisplayName
        {
            get
            {
                string n = Data.Properties != null && !string.IsNullOrEmpty(Data.Properties.Name) ? Data.Properties.Name : (Info != null ? Info.Name : "Map " + Id);
                return n;
            }
        }

        public bool Valid(int x, int y) { return x >= 0 && y >= 0 && x < Width && y < Height; }

        public int Tile(int x, int y, int z) { return Data.GetTile(x, y, z); }

        /// <summary>Terrain tag of the topmost tile with a tag.</summary>
        public int TerrainTag(int x, int y)
        {
            if (!Valid(x, y)) return 0;
            for (int z = 2; z >= 0; z--)
            {
                int id = Tile(x, y, z);
                if (id <= 0) continue;
                int tag = Tileset.TerrainTag(id);
                if (tag > 0) return tag;
            }
            return 0;
        }

        public bool IsCounter(int x, int y)
        {
            for (int z = 2; z >= 0; z--)
            {
                int id = Tile(x, y, z);
                if (id > 0 && (Tileset.Passage(id) & 0x80) != 0) return true;
            }
            return false;
        }

        public bool IsBush(int x, int y)
        {
            for (int z = 2; z >= 0; z--)
            {
                int id = Tile(x, y, z);
                if (id > 0 && (Tileset.Passage(id) & 0x40) != 0) return true;
            }
            return false;
        }

        /// <summary>RMXP passability rules: can something leave/enter tile (x,y) in direction d?</summary>
        /// <summary>Bridge height (pbBridgeOn / pbBridgeOff): while above 0, Bridge-tagged tiles carry the player over what's below.</summary>
        public static int BridgeHeight;
        public const int BridgeTag = 15;

        public bool TilePassable(int x, int y, int d, GameEvent self)
        {
            if (!Valid(x, y)) return false;
            int bit = (1 << (d / 2 - 1)) & 0x0f;
            foreach (var e in Events)
            {
                if (e == self || e.Erased || e.Through || e.TileId <= 0 || e.X != x || e.Y != y) continue;
                int pe = Tileset.Passage(e.TileId);
                if ((pe & bit) != 0 || (pe & 0x0f) == 0x0f) return false;
                if (Tileset.Priority(e.TileId) == 0) return true;
            }
            for (int z = 2; z >= 0; z--)
            {
                int id = Tile(x, y, z);
                if (id <= 0) continue; // empty layer
                if (Tileset.TerrainTag(id) == BridgeTag)
                {
                    if (BridgeHeight <= 0) continue;          // walking under the bridge
                    int bp = Tileset.Passage(id);
                    return (bp & bit) == 0 && (bp & 0x0f) != 0x0f; // on the bridge: only the bridge tile counts
                }
                int pass = Tileset.Passage(id);
                if ((pass & bit) != 0 || (pass & 0x0f) == 0x0f) return false;
                if (Tileset.Priority(id) == 0) return true;
            }
            return true;
        }

        /// <summary>
        /// Passability for a surfing player (as Essentials' playerPassable?): surfable water tiles count as open
        /// whatever their passage flags; anything above the water still blocks.
        /// </summary>
        public bool SurfPassable(int x, int y, int d)
        {
            if (!Valid(x, y)) return false;
            int bit = (1 << (d / 2 - 1)) & 0x0f;
            for (int z = 2; z >= 0; z--)
            {
                int id = Tile(x, y, z);
                if (id <= 0) continue;
                int tag = Tileset.TerrainTag(id);
                if (Array.IndexOf(WaterTags, tag) >= 0 && tag != 8) return true; // not up a waterfall
                int pass = Tileset.Passage(id);
                if ((pass & bit) != 0 || (pass & 0x0f) == 0x0f) return false;
                if (Tileset.Priority(id) == 0) return true;
            }
            return true;
        }

        public IEnumerable<GameEvent> EventsAt(int x, int y)
        {
            foreach (var e in Events) if (!e.Erased && e.X == x && e.Y == y) yield return e;
        }

        public void RefreshEvents() { foreach (var e in Events) e.Refresh(); }

        /// <summary>Finds what lies at (x,y) in this map's coordinates, looking into connected maps.</summary>
        public bool Resolve(int x, int y, out GameMap map, out int lx, out int ly)
        {
            if (Valid(x, y)) { map = this; lx = x; ly = y; return true; }
            foreach (var n in Neighbors)
            {
                int nx = x - n.OffsetX, ny = y - n.OffsetY;
                if (n.Map.Valid(nx, ny)) { map = n.Map; lx = nx; ly = ny; return true; }
            }
            map = null; lx = ly = 0;
            return false;
        }

        // ------------------------------------------------------------------ connections
        static void EdgePoint(string edge, int offset, MapData m, out int x, out int y)
        {
            switch (edge)
            {
                case "North": x = offset; y = 0; break;
                case "South": x = offset; y = m.Height; break;
                case "East": x = m.Width; y = offset; break;
                default: x = 0; y = offset; break; // West
            }
        }

        /// <summary>Loads maps connected to this one (Essentials map_connections.txt semantics).</summary>
        public void LoadNeighbors(Project p, Game game)
        {
            Neighbors.Clear();
            foreach (var c in p.Connections)
            {
                int otherId; string myEdge, otherEdge; int myOff, otherOff;
                if (c.Map1 == Id) { otherId = c.Map2; myEdge = c.Edge1; myOff = c.Offset1; otherEdge = c.Edge2; otherOff = c.Offset2; }
                else if (c.Map2 == Id) { otherId = c.Map1; myEdge = c.Edge2; myOff = c.Offset2; otherEdge = c.Edge1; otherOff = c.Offset1; }
                else continue;
                if (otherId == Id) continue;
                var od = p.LoadMap(otherId);
                if (od == null) continue;
                int x1, y1, x2, y2;
                EdgePoint(myEdge, myOff, Data, out x1, out y1);
                EdgePoint(otherEdge, otherOff, od, out x2, out y2);
                Neighbors.Add(new MapNeighbor { Map = new GameMap(p, od, game, false), OffsetX = x1 - x2, OffsetY = y1 - y2 });
            }
        }

        public bool HasConnections { get { return Neighbors.Count > 0; } }
    }
}
