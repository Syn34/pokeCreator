using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>GDI+ tile drawing shared by the map editor, tileset editor and event previews.</summary>
    public class TileGraphics : IDisposable
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

        readonly Project _p;
        public TilesetData Tileset { get; private set; }
        /// <summary>Sheet images by slot (slot 0 is the first sheet).</summary>
        public readonly Dictionary<int, Bitmap> SheetImages = new Dictionary<int, Bitmap>();
        public readonly Bitmap[] Autotiles = new Bitmap[7];
        /// <summary>The first sheet's image (slot 0).</summary>
        public Bitmap TilesetImage { get { return Image(0); } }
        public int TileRows { get { return Rows(0); } }

        public Bitmap Image(int slot) { Bitmap b; return SheetImages.TryGetValue(slot, out b) ? b : null; }
        public int Rows(int slot) { var b = Image(slot); return b == null ? 0 : b.Height / 32; }

        /// <summary>Sheets as (slot, label), first sheet first. Missing images still appear so their tiles can be found.</summary>
        public List<KeyValuePair<int, string>> Sheets
        {
            get { return Tileset.AllSheets().Select(k => new KeyValuePair<int, string>(k.Key, TilesetData.LabelOf(k.Value) + (Image(k.Key) == null ? " (missing)" : ""))).ToList(); }
        }

        public TileGraphics(Project p, TilesetData ts) { _p = p; Load(ts); }

        public void Load(TilesetData ts)
        {
            DisposeImages();
            Tileset = ts ?? new TilesetData();
            foreach (var sh in Tileset.AllSheets())
            {
                var img = Theme.LoadImage(_p.FindAsset("Graphics/Tilesets", sh.Value)) as Bitmap;
                if (img == null) continue;
                SheetImages[sh.Key] = img;
                Tileset.EnsureSize(sh.Key, img.Height / 32);
            }
            for (int i = 0; i < 7; i++)
                Autotiles[i] = i < Tileset.AutotileNames.Count ? Theme.LoadImage(_p.FindAsset("Graphics/Autotiles", Tileset.AutotileNames[i])) as Bitmap : null;
        }

        /// <summary>Draws one tile at (x,y) in a 32x32 cell scaled by 'size'.</summary>
        public void Draw(Graphics g, int id, int x, int y, int size = 32)
        {
            if (id >= TilesetData.FirstTile)
            {
                var img = Image(TilesetData.SlotOf(id));
                if (img == null) return;
                int i = TilesetData.IndexOf(id);
                if ((i / 8) * 32 >= img.Height) return;
                g.DrawImage(img, new Rectangle(x, y, size, size), new Rectangle((i % 8) * 32, (i / 8) * 32, 32, 32), GraphicsUnit.Pixel);
                return;
            }
            if (id < 48) return;
            var at = Autotiles[id / 48 - 1];
            if (at == null) return;
            if (at.Height <= 32) { g.DrawImage(at, new Rectangle(x, y, size, size), new Rectangle(0, 0, 32, 32), GraphicsUnit.Pixel); return; }
            var pat = Patterns[id % 48];
            int h = size / 2;
            for (int q = 0; q < 4; q++)
            {
                int cell = pat[q] - 1;
                g.DrawImage(at, new Rectangle(x + (q % 2) * h, y + (q / 2) * h, h, h), new Rectangle((cell % 6) * 16, (cell / 6) * 16, 16, 16), GraphicsUnit.Pixel);
            }
        }

        // ------------------------------------------------------------------ automatic autotile shapes
        // Each quarter of a pattern is classified by which neighbours are "missing" on that corner.
        // Painting computes the wanted classification from real neighbours and picks the matching pattern.
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
                for (int q = 0; q < 4; q++) _signatures[p][q] = Classify(Patterns[p][q], q);
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

        /// <summary>Recomputes autotile shapes around (x,y) on one layer.</summary>
        public static void FixAutotiles(MapData m, int layer, int cx, int cy)
        {
            for (int y = cy - 1; y <= cy + 1; y++)
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    if (x < 0 || y < 0 || x >= m.Width || y >= m.Height) continue;
                    int id = m.GetTile(x, y, layer);
                    if (id < 48 || id >= 384) continue;
                    int kind = id / 48;
                    int px = x, py = y;
                    int pat = PatternFor((dx, dy) =>
                    {
                        int nx = px + dx, ny = py + dy;
                        if (nx < 0 || ny < 0 || nx >= m.Width || ny >= m.Height) return true;
                        int n = m.GetTile(nx, ny, layer);
                        return n >= 48 && n < 384 && n / 48 == kind;
                    });
                    m.SetTile(x, y, layer, kind * 48 + pat);
                }
        }

        void DisposeImages()
        {
            foreach (var b in SheetImages.Values) b.Dispose();
            SheetImages.Clear();
            for (int i = 0; i < 7; i++) if (Autotiles[i] != null) { Autotiles[i].Dispose(); Autotiles[i] = null; }
        }

        public void Dispose() { DisposeImages(); }

        /// <summary>Draws the first frame (facing down) of a character sheet centred in a 32x32 cell.</summary>
        public static void DrawCharacter(Graphics g, Bitmap sheet, int dir, int pattern, int x, int y, int size = 32)
        {
            if (sheet == null) return;
            int cw = sheet.Width / 4, ch = sheet.Height / 4;
            int row = dir == 4 ? 1 : dir == 6 ? 2 : dir == 8 ? 3 : 0;
            float scale = size / 32f;
            int dw = (int)(cw * scale), dh = (int)(ch * scale);
            g.DrawImage(sheet, new Rectangle(x + size / 2 - dw / 2, y + size - dh, dw, dh), new Rectangle((pattern % 4) * cw, row * ch, cw, ch), GraphicsUnit.Pixel);
        }
    }
}
