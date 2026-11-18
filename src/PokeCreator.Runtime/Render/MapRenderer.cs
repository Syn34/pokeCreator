using System;
using System.Collections.Generic;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime.Render
{
    /// <summary>Quarter-tile layout of RMXP's 48 autotile patterns (1-based 16x16 cells in a 96x128 sheet).</summary>
    public static class AutotileTable
    {
        public static readonly int[][] Patterns =
        {
            new[]{27,28,33,34}, new[]{ 5,28,33,34}, new[]{27, 6,33,34}, new[]{ 5, 6,33,34},
            new[]{27,28,33,12}, new[]{ 5,28,33,12}, new[]{27, 6,33,12}, new[]{ 5, 6,33,12},
            new[]{27,28,11,34}, new[]{ 5,28,11,34}, new[]{27, 6,11,34}, new[]{ 5, 6,11,34},
            new[]{27,28,11,12}, new[]{ 5,28,11,12}, new[]{27, 6,11,12}, new[]{ 5, 6,11,12},
            new[]{25,26,31,32}, new[]{25, 6,31,32}, new[]{25,26,31,12}, new[]{25, 6,31,12},
            new[]{15,16,21,22}, new[]{15,16,21,12}, new[]{15,16,11,22}, new[]{15,16,11,12},
            new[]{29,30,35,36}, new[]{29,30,11,36}, new[]{ 5,30,35,36}, new[]{ 5,30,11,36},
            new[]{39,40,45,46}, new[]{ 5,40,45,46}, new[]{39, 6,45,46}, new[]{ 5, 6,45,46},
            new[]{25,30,31,36}, new[]{15,16,45,46}, new[]{13,14,19,20}, new[]{13,14,19,12},
            new[]{17,18,23,24}, new[]{17,18,11,24}, new[]{41,42,47,48}, new[]{ 5,42,47,48},
            new[]{37,38,43,44}, new[]{37, 6,43,44}, new[]{13,18,19,24}, new[]{13,14,43,44},
            new[]{37,42,43,48}, new[]{17,18,47,48}, new[]{13,18,43,48}, new[]{ 1, 2, 7, 8},
        };
    }

    /// <summary>Draws a tile (from any tileset) — shared by the map renderer and event tile graphics.</summary>
    public class TileDrawer
    {
        readonly Gfx _g;
        readonly Project _p;
        public int Frame;
        public TileDrawer(Gfx g, Project p) { _g = g; _p = p; }

        public Texture TilesetTexture(TilesetData ts, int slot = 0) { return _g.Load(_p.FindAsset("Graphics/Tilesets", ts.SheetImage(slot))); }
        public Texture AutotileTexture(TilesetData ts, int index)
        {
            if (index < 0 || index >= ts.AutotileNames.Count) return null;
            return _g.Load(_p.FindAsset("Graphics/Autotiles", ts.AutotileNames[index]));
        }

        public void Draw(TilesetData ts, int id, int x, int y, int alpha = 255)
        {
            if (id >= 384)
            {
                var tex = TilesetTexture(ts, TilesetData.SlotOf(id));
                if (tex == null) return;
                int i = TilesetData.IndexOf(id);
                _g.Draw(tex, (i % 8) * 32, (i / 8) * 32, 32, 32, x, y, -1, -1, alpha);
                return;
            }
            if (id < 48) return;
            var at = AutotileTexture(ts, id / 48 - 1);
            if (at == null) return;
            if (at.Height <= 32)
            {
                // Single-tile animated autotile: frames laid out horizontally
                int frames = Math.Max(1, at.Width / 32);
                int f = (Frame / 24) % frames;
                _g.Draw(at, f * 32, 0, 32, 32, x, y, -1, -1, alpha);
                return;
            }
            int nframes = Math.Max(1, at.Width / 96);
            int fx = ((Frame / 24) % nframes) * 96;
            var pat = AutotileTable.Patterns[id % 48];
            for (int q = 0; q < 4; q++)
            {
                int cell = pat[q] - 1;
                int sx = fx + (cell % 6) * 16, sy = (cell / 6) * 16;
                _g.Draw(at, sx, sy, 16, 16, x + (q % 2) * 16, y + (q / 2) * 16, -1, -1, alpha);
            }
        }
    }

    /// <summary>Renders the current map, its connected neighbours and all characters with correct layering.</summary>
    public class MapRenderer
    {
        readonly Gfx _g;
        readonly Project _p;
        public readonly TileDrawer Tiles;

        struct Overlay
        {
            public int Z, Order;
            public TilesetData Ts; public int TileId, X, Y;
            public Character Char;
        }
        readonly List<Overlay> _overlays = new List<Overlay>(512);

        public MapRenderer(Gfx g, Project p) { _g = g; _p = p; Tiles = new TileDrawer(g, p); }

        public void Draw(GameMap map, int displayX, int displayY, IEnumerable<Character> characters, int frame)
        {
            Tiles.Frame = frame;
            _overlays.Clear();
            int order = 0;
            foreach (var n in map.Neighbors) DrawMapTiles(n.Map, n.OffsetX, n.OffsetY, displayX, displayY, ref order);
            DrawMapTiles(map, 0, 0, displayX, displayY, ref order);

            foreach (var c in characters)
            {
                if (c.Transparent || c.Opacity <= 0) continue;
                if (string.IsNullOrEmpty(c.CharacterName) && c.TileId <= 0) continue;
                int sy = (int)Math.Round(c.RealY / 4.0) + 32;
                int z = c.AlwaysOnTop ? int.MaxValue - 1 : sy + (c.TileId > 0 ? map.Tileset.Priority(c.TileId) * 32 : 0);
                _overlays.Add(new Overlay { Z = z, Order = order++ + 1000000, Char = c });
            }
            _overlays.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Order.CompareTo(b.Order));
            foreach (var o in _overlays)
            {
                if (o.Char != null) DrawCharacter(map, o.Char, displayX, displayY);
                else Tiles.Draw(o.Ts, o.TileId, o.X - displayX, o.Y - displayY);
            }
        }

        void DrawMapTiles(GameMap m, int offX, int offY, int displayX, int displayY, ref int order)
        {
            int tx0 = Math.Max(0, displayX / 32 - offX - 1), ty0 = Math.Max(0, displayY / 32 - offY - 1);
            int tx1 = Math.Min(m.Width - 1, (displayX + _g.Width) / 32 - offX + 1);
            int ty1 = Math.Min(m.Height - 1, (displayY + _g.Height) / 32 - offY + 2);
            var ts = m.Tileset;
            for (int z = 0; z < 3; z++)
                for (int y = ty0; y <= ty1; y++)
                    for (int x = tx0; x <= tx1; x++)
                    {
                        int id = m.Tile(x, y, z);
                        if (id <= 0) continue;
                        int px = (x + offX) * 32, py = (y + offY) * 32;
                        int pr = ts.Priority(id);
                        if (pr == 0) Tiles.Draw(ts, id, px - displayX, py - displayY);
                        else _overlays.Add(new Overlay { Z = py + 32 + pr * 32, Order = order++, Ts = ts, TileId = id, X = px, Y = py });
                    }
        }

        void DrawCharacter(GameMap map, Character c, int displayX, int displayY)
        {
            int sx = c.ScreenX(displayX), sy = c.ScreenY(displayY);
            if (c.TileId > 0)
            {
                Tiles.Draw(map.Tileset, c.TileId, sx - 16, sy - 32, c.Opacity);
                return;
            }
            var tex = _g.Load(_p.FindAsset("Graphics/Characters", c.CharacterName));
            if (tex == null) return;
            int cw = tex.Width / 4, ch = tex.Height / 4;
            int row = c.Direction == 2 ? 0 : c.Direction == 4 ? 1 : c.Direction == 6 ? 2 : 3;
            int col = c.Pattern % 4;
            _g.Draw(tex, col * cw, row * ch, cw, ch, sx - cw / 2, sy - ch, -1, -1, c.Opacity);
        }
    }
}
