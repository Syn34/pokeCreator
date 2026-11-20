using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Maps;

namespace PokeCreator.Runtime.World
{
    public class DungeonState { public int MapId { get; set; } public int Seed { get; set; } }

    /// <summary>
    /// Random dungeons (a map whose properties tick "Random dungeon", as Essentials): each time the player enters,
    /// a new layout of rooms joined by corridors is made from the map's own tiles — its most common walkable
    /// ground tile becomes the floor and its most common blocking tile the walls. The map's events (ladders, items,
    /// trainers) are scattered into the rooms and the player starts in one of them.
    /// $PokemonGlobal.dungeon_area (:cave, :forest…) picks the shape: forests have wider paths and bigger rooms.
    /// Saving and reloading inside keeps the same layout.
    /// </summary>
    public static class Dungeon
    {
        const int V = 1, H = 2, D = 4;
        static int[][] _signatures;

        static int Classify(int cell, int corner)
        {
            int c = cell - 1, col = c % 6, row = c / 6;
            if (row < 2)
            {
                if (cell == 1 || cell == 2 || cell == 7 || cell == 8) return V | H;
                if (cell == 5 || cell == 6 || cell == 11 || cell == 12) return D;
                return 0;
            }
            int bx = col / 2, qx = col % 2, by = (row - 2) / 2, qy = (row - 2) % 2;
            bool top = corner < 2, left = corner % 2 == 0;
            bool vMissing = top ? (by == 0 && qy == 0) : (by == 2 && qy == 1);
            bool hMissing = left ? (bx == 0 && qx == 0) : (bx == 2 && qx == 1);
            return (vMissing ? V : 0) | (hMissing ? H : 0);
        }

        static void BuildSignatures()
        {
            if (_signatures != null) return;
            _signatures = new int[48][];
            for (int p = 0; p < 48; p++)
            {
                _signatures[p] = new int[4];
                for (int q = 0; q < 4; q++) _signatures[p][q] = Classify(Render.AutotileTable.Patterns[p][q], q);
            }
        }

        /// <summary>Picks the autotile pattern for a tile given which of its 8 neighbours are the same autotile.</summary>
        public static int PatternFor(Func<int, int, bool> same)
        {
            BuildSignatures();
            var want = new int[4];
            int[,] dirs = { { 0, -1, -1, 0, -1, -1 }, { 0, -1, 1, 0, 1, -1 }, { 0, 1, -1, 0, -1, 1 }, { 0, 1, 1, 0, 1, 1 } }; // v(dx,dy) h(dx,dy) d(dx,dy) per corner TL,TR,BL,BR
            for (int q = 0; q < 4; q++)
            {
                bool v = same(dirs[q, 0], dirs[q, 1]), h = same(dirs[q, 2], dirs[q, 3]), d = same(dirs[q, 4], dirs[q, 5]);
                int w = (v ? 0 : V) | (h ? 0 : H);
                if (w == 0 && !d) w = D;
                want[q] = w;
            }
            for (int p = 0; p < 48; p++)
            {
                var s = _signatures[p];
                if (s[0] == want[0] && s[1] == want[1] && s[2] == want[2] && s[3] == want[3]) return p;
            }
            return 0;
        }


        static void FixAll(MapData m)
        {
            for (int z = 0; z < m.Layers.Count; z++)
                for (int y = 0; y < m.Height; y++)
                    for (int x = 0; x < m.Width; x++)
                    {
                        int id = m.GetTile(x, y, z);
                        if (id < 48 || id >= 384) continue;
                        int kind = id / 48, px = x, py = y, zz = z;
                        int pat = PatternFor((dx, dy) =>
                        {
                            int nx = px + dx, ny = py + dy;
                            if (nx < 0 || ny < 0 || nx >= m.Width || ny >= m.Height) return true;
                            int n = m.GetTile(nx, ny, zz);
                            return n >= 48 && n < 384 && n / 48 == kind;
                        });
                        m.SetTile(x, y, z, kind * 48 + pat);
                    }
        }

        static bool Blocks(TilesetData ts, int id) { return id > 0 && (ts.Passage(id) & 0x0f) == 0x0f; }

        class Room { public int X, Y, W, H; public int CX { get { return X + W / 2; } } public int CY { get { return Y + H / 2; } } }

        /// <summary>Makes the layout for this visit. Returns the generated copy of the map and the player's start.</summary>
        public static MapData Generate(Game g, MapData template, int seed, out int startX, out int startY)
        {
            var rng = new Random(seed);
            var ts = g.Project.GetTileset(template.TilesetId) ?? new TilesetData();
            string area = (g.State.Extras.DungeonArea ?? "cave").TrimStart(':').ToLowerInvariant();
            bool wide = area == "forest";

            // Floor = most common walkable bottom tile with nothing blocking above; wall = most common blocking tile
            var floorCount = new Dictionary<int, int>(); var wallCount = new Dictionary<string, int>();
            for (int y = 0; y < template.Height; y++)
                for (int x = 0; x < template.Width; x++)
                {
                    int bottom = template.GetTile(x, y, 0);
                    bool blocked = false;
                    for (int z = 0; z < template.Layers.Count; z++)
                    {
                        int id = template.GetTile(x, y, z);
                        if (Blocks(ts, id)) { blocked = true; string k = z + ":" + (id >= 48 && id < 384 ? id / 48 * 48 : id); int c; wallCount.TryGetValue(k, out c); wallCount[k] = c + 1; }
                    }
                    if (!blocked && bottom > 0) { int key = bottom >= 48 && bottom < 384 ? bottom / 48 * 48 : bottom; int c; floorCount.TryGetValue(key, out c); floorCount[key] = c + 1; }
                }
            int floor = floorCount.Count > 0 ? floorCount.OrderByDescending(kv => kv.Value).First().Key : template.GetTile(0, 0, 0);
            int wallLayer = 0, wall = 0;
            if (wallCount.Count > 0)
            {
                var w = wallCount.OrderByDescending(kv => kv.Value).First().Key.Split(':');
                wallLayer = int.Parse(w[0]); wall = int.Parse(w[1]);
            }
            // A blank template (as Essentials' dungeon maps): take them from the tileset's own settings
            int count = ts.Passages != null ? ts.Passages.Count : 0;
            if (floor <= 0) for (int id = 385; id < count; id++) if (ts.Passage(id) == 0 && ts.Priority(id) == 0) { floor = id; break; }
            if (wall <= 0) for (int id = 385; id < count; id++) if ((ts.Passage(id) & 0x0f) == 0x0f) { wall = id; wallLayer = 0; break; }
            var props = template.Properties;
            if (props != null && props.DungeonFloor > 0) floor = props.DungeonFloor;
            if (props != null && props.DungeonWall > 0) { wall = props.DungeonWall; wallLayer = 0; }
            if (wallLayer > 0 && wallLayer >= 1 && Blocks(ts, floor)) wallLayer = 0;

            int width = Math.Max(template.Width, wide ? 40 : 44), height = Math.Max(template.Height, wide ? 30 : 34);
            var m = MapData.Create(template.Id, width, height, template.TilesetId);
            m.Bgm = template.Bgm; m.Bgs = template.Bgs; m.AutoplayBgm = template.AutoplayBgm; m.AutoplayBgs = template.AutoplayBgs;
            m.EncounterStep = template.EncounterStep; m.Properties = template.Properties;
            var open = new bool[width, height];

            // Rooms
            var rooms = new List<Room>();
            int want = wide ? 7 : 9;
            for (int tries = 0; tries < 400 && rooms.Count < want; tries++)
            {
                var r = new Room { W = rng.Next(wide ? 5 : 4, wide ? 10 : 8), H = rng.Next(wide ? 4 : 3, wide ? 8 : 6) };
                r.X = rng.Next(2, width - r.W - 2); r.Y = rng.Next(2, height - r.H - 2);
                if (r.X < 2 || r.Y < 2) continue;
                if (rooms.Any(o => r.X < o.X + o.W + 2 && o.X < r.X + r.W + 2 && r.Y < o.Y + o.H + 2 && o.Y < r.Y + r.H + 2)) continue;
                rooms.Add(r);
            }
            if (rooms.Count == 0) rooms.Add(new Room { X = 2, Y = 2, W = width - 4, H = height - 4 });
            foreach (var r in rooms) for (int y = r.Y; y < r.Y + r.H; y++) for (int x = r.X; x < r.X + r.W; x++) open[x, y] = true;

            // Corridors: each room joins the nearest already-joined one (so everything is reachable), plus a loop or two
            Action<int, int, int, int> dig = (x1, y1, x2, y2) =>
            {
                bool hFirst = rng.Next(2) == 0;
                int cx = x1, cy = y1;
                Action<int, int> mark = (ax, ay) =>
                {
                    for (int oy = 0; oy < (wide ? 2 : 1); oy++) for (int ox = 0; ox < (wide ? 2 : 1); ox++)
                    {
                        int px = Math.Min(width - 3, Math.Max(2, ax + ox)), py = Math.Min(height - 3, Math.Max(2, ay + oy));
                        open[px, py] = true;
                    }
                };
                if (hFirst) { while (cx != x2) { mark(cx, cy); cx += Math.Sign(x2 - cx); } while (cy != y2) { mark(cx, cy); cy += Math.Sign(y2 - cy); } }
                else { while (cy != y2) { mark(cx, cy); cy += Math.Sign(y2 - cy); } while (cx != x2) { mark(cx, cy); cx += Math.Sign(x2 - cx); } }
                mark(x2, y2);
            };
            var joined = new List<Room> { rooms[0] };
            foreach (var r in rooms.Skip(1))
            {
                var near = joined.OrderBy(o => Math.Abs(o.CX - r.CX) + Math.Abs(o.CY - r.CY)).First();
                dig(r.CX, r.CY, near.CX, near.CY); joined.Add(r);
            }
            for (int i = 0; i < rooms.Count / 4; i++) { var a = rooms[rng.Next(rooms.Count)]; var b = rooms[rng.Next(rooms.Count)]; if (a != b) dig(a.CX, a.CY, b.CX, b.CY); }

            // Tiles
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    m.SetTile(x, y, 0, floor);
                    if (!open[x, y])
                    {
                        if (wall > 0) m.SetTile(x, y, wallLayer, wall);
                    }
                }
            FixAll(m);

            // Events: one per room where possible (the first room is the player's)
            var spots = new List<Tuple<int, int>>();
            var order = rooms.OrderBy(_ => rng.Next()).ToList();
            var startRoom = order[0];
            startX = startRoom.X + rng.Next(startRoom.W); startY = startRoom.Y + rng.Next(startRoom.H);
            var used = new HashSet<int> { startX * 1000 + startY };
            int ri = 0;
            foreach (var ev in template.Events)
            {
                var copy = PokeCreator.Json.Json.Deserialize<MapEvent>(PokeCreator.Json.Json.Write(ev, false));
                var room = order.Count > 1 ? order[1 + (ri++ % (order.Count - 1))] : startRoom;
                for (int t = 0; t < 50; t++)
                {
                    int x = room.X + rng.Next(room.W), y = room.Y + rng.Next(room.H);
                    if (used.Add(x * 1000 + y)) { copy.X = x; copy.Y = y; break; }
                }
                m.Events.Add(copy);
            }
            return m;
        }

        /// <summary>Called for each map setup: a dungeon map gets its layout (a new one unless reloading a save there).</summary>
        public static MapData Prepare(Game g, MapData data, bool keepLayout, ref int x, ref int y)
        {
            if (data == null || data.Properties == null || !data.Properties.Dungeon || g.State == null) return data;
            var st = g.State.Extras.Dungeon;
            if (st == null || st.MapId != data.Id || !keepLayout)
            {
                st = new DungeonState { MapId = data.Id, Seed = g.Rng.Next(1, int.MaxValue) };
                g.State.Extras.Dungeon = st;
                keepLayout = false;
            }
            int sx, sy;
            var m = Generate(g, data, st.Seed, out sx, out sy);
            if (!keepLayout) { x = sx; y = sy; }
            return m;
        }
    }
}
