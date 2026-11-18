using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Brings tileset and autotile images into a project, converting them to the layout the engine
    /// uses (RPG Maker XP style: 32×32 tiles, tilesets 8 tiles = 256 px wide, autotiles 96×128 per frame).
    /// 16×16 sheets are doubled and wide sheets are cut into 8-tile columns stacked top to bottom.
    /// </summary>
    public static class TilesetImport
    {
        public const string Filter = "Images|*.png;*.bmp;*.gif;*.jpg";

        /// <summary>Converts any tile sheet to 256 px wide, 32 px tiles. tileSize is 16 or 32.</summary>
        public static Bitmap ToEngineTileset(Bitmap src, int tileSize, out string note)
        {
            note = "";
            if (src.Width % tileSize != 0) throw new InvalidDataException("The image is " + src.Width + " px wide, which isn't a whole number of " + tileSize + " px tiles.");
            Bitmap img = tileSize == 32 ? new Bitmap(src) : Scale(src, 32 / tileSize);
            if (tileSize != 32) note += "Doubled the " + tileSize + "×" + tileSize + " tiles to 32×32. ";
            int cols = img.Width / 32;
            int rows = (img.Height + 31) / 32;
            if (cols == 8 && img.Height % 32 == 0) return img;
            int strips = (cols + 7) / 8;
            var dst = new Bitmap(256, strips * rows * 32, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                for (int s = 0; s < strips; s++)
                {
                    int w = Math.Min(256, img.Width - s * 256);
                    g.DrawImage(img, new Rectangle(0, s * rows * 32, w, img.Height), new Rectangle(s * 256, 0, w, img.Height), GraphicsUnit.Pixel);
                }
            }
            if (strips > 1) note += "The sheet was " + cols + " tiles wide, so it was cut into " + strips + " columns of 8 tiles stacked top to bottom. ";
            else if (cols < 8) note += "Padded to 8 tiles wide. ";
            img.Dispose();
            return dst;
        }

        /// <summary>Checks and converts an autotile image: 96×128 per frame, a strip of 32×32 frames, or either at 16 px.</summary>
        public static Bitmap ToEngineAutotile(Bitmap src, out string note)
        {
            note = "";
            if (src.Height == 64 && src.Width % 48 == 0) { note = "Doubled the 16×16 autotile to 32×32. "; return Scale(src, 2); }
            if (src.Height == 128 && src.Width % 96 == 0) return new Bitmap(src);
            // Single animated tiles (flowers, water sparkles): one 32×32 frame after another
            if (src.Height == 32 && src.Width % 32 == 0) return new Bitmap(src);
            if (src.Height == 16 && src.Width % 16 == 0) { note = "Doubled the 16×16 animated tile to 32×32. "; return Scale(src, 2); }
            throw new InvalidDataException("Autotiles are 96×128 pixels per animation frame, or a strip of 32×32 frames for a single animated tile (half those sizes for 16×16 tiles). This image is " + src.Width + "×" + src.Height + ".");
        }

        /// <summary>Exact pixel doubling (GDI+ nearest-neighbour scaling isn't exact on every platform).</summary>
        static Bitmap Scale(Bitmap src, int f)
        {
            using (var s32 = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(s32)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(src, 0, 0); }
                var dst = new Bitmap(src.Width * f, src.Height * f, PixelFormat.Format32bppArgb);
                var sd = s32.LockBits(new Rectangle(0, 0, s32.Width, s32.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var dd = dst.LockBits(new Rectangle(0, 0, dst.Width, dst.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var srow = new int[s32.Width];
                    var drow = new int[dst.Width];
                    for (int y = 0; y < s32.Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(sd.Scan0 + y * sd.Stride, srow, 0, srow.Length);
                        for (int x = 0; x < drow.Length; x++) drow[x] = srow[x / f];
                        for (int k = 0; k < f; k++) System.Runtime.InteropServices.Marshal.Copy(drow, 0, dd.Scan0 + (y * f + k) * dd.Stride, drow.Length);
                    }
                }
                finally { s32.UnlockBits(sd); dst.UnlockBits(dd); }
                return dst;
            }
        }

        /// <summary>Saves an image into a Graphics sub-folder under a name that isn't taken. Returns the name without extension.</summary>
        public static string SaveInto(Project p, string folder, string baseName, Bitmap img)
        {
            string dir = Path.Combine(p.Root, "Graphics", folder);
            Directory.CreateDirectory(dir);
            string name = baseName; int n = 2;
            while (File.Exists(Path.Combine(dir, name + ".png"))) name = baseName + " " + n++;
            img.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
            return name;
        }

        static bool IsImage(string f) { var e = Path.GetExtension(f).ToLowerInvariant(); return e == ".png" || e == ".bmp" || e == ".gif" || e == ".jpg"; }

        /// <summary>Tile size to assume for a sheet when nobody is asked: 256 wide = 32 px, 128 wide = 16 px.</summary>
        public static int GuessTileSize(int width)
        {
            if (width % 256 == 0) return 32;
            if (width == 128 || width % 32 != 0) return 16;
            return 32;
        }

        /// <summary>True if an image already has the engine's layout (256 px wide, whole 32 px rows).</summary>
        public static bool IsEngineSheet(int w, int h) { return w == 256 && h % 32 == 0; }

        /// <summary>Converts a sheet file to the engine layout in place. Returns a note, or null if it was already fine.</summary>
        public static string ConvertInPlace(string file, int tileSize = 0)
        {
            Bitmap src;
            using (var img = (Bitmap)Theme.LoadImage(file)) src = img == null ? null : new Bitmap(img);
            if (src == null) throw new InvalidDataException("Couldn't read " + Path.GetFileName(file) + ".");
            using (src)
            {
                if (IsEngineSheet(src.Width, src.Height)) return null;
                string note;
                using (var conv = ToEngineTileset(src, tileSize > 0 ? tileSize : GuessTileSize(src.Width), out note))
                {
                    string png = Path.ChangeExtension(file, ".png");
                    conv.Save(png + ".tmp", ImageFormat.Png);
                    if (!string.Equals(png, file, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
                    if (File.Exists(png)) File.Delete(png);
                    File.Move(png + ".tmp", png);
                }
                return Path.GetFileName(file) + ": " + note.Trim();
            }
        }

        /// <summary>
        /// Copies every image in srcDir into Graphics/Tilesets/&lt;folderName&gt;, converting each to the engine layout,
        /// and any images in srcDir/Autotiles into Graphics/Autotiles. Returns the autotile names copied.
        /// </summary>
        public static List<string> CopyFolder(Project p, string srcDir, string folderName, int tileSize, List<string> notes)
        {
            string dest = Path.Combine(p.TilesetsDir, folderName);
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(srcDir).Where(IsImage).OrderBy(x => x))
            {
                string target = Path.Combine(dest, Path.GetFileNameWithoutExtension(f) + ".png");
                using (var img = (Bitmap)Theme.LoadImage(f))
                {
                    if (img == null) { notes.Add(Path.GetFileName(f) + ": couldn't be read, skipped."); continue; }
                    if (IsEngineSheet(img.Width, img.Height)) { img.Save(target, ImageFormat.Png); continue; }
                    string note;
                    using (var conv = ToEngineTileset(img, tileSize > 0 ? tileSize : GuessTileSize(img.Width), out note)) conv.Save(target, ImageFormat.Png);
                    notes.Add(Path.GetFileName(f) + ": " + note.Trim());
                }
            }
            var autotiles = new List<string>();
            string autoDir = Path.Combine(srcDir, "Autotiles");
            if (Directory.Exists(autoDir))
            {
                string adest = Path.Combine(p.Root, "Graphics", "Autotiles");
                Directory.CreateDirectory(adest);
                foreach (var f in Directory.GetFiles(autoDir).Where(IsImage).OrderBy(x => x))
                {
                    using (var img = (Bitmap)Theme.LoadImage(f))
                    {
                        if (img == null) continue;
                        try
                        {
                            string note;
                            using (var conv = ToEngineAutotile(img, out note)) conv.Save(Path.Combine(adest, Path.GetFileNameWithoutExtension(f) + ".png"), ImageFormat.Png);
                            autotiles.Add(Path.GetFileNameWithoutExtension(f));
                        }
                        catch (InvalidDataException ex) { notes.Add("Autotiles/" + Path.GetFileName(f) + ": " + ex.Message); }
                    }
                }
            }
            return autotiles;
        }

        /// <summary>Picks a folder of tileset images (one per area) and makes a new tileset from it. Returns it, or null.</summary>
        public static TilesetData PickFolder(IWin32Window owner, Project p)
        {
            using (var d = new FolderBrowserDialog { Description = "Choose a folder of tileset images, one image per area (Outdoor, Cave, Poké Center...). An Autotiles sub-folder is imported too." })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                var images = Directory.GetFiles(d.SelectedPath).Where(IsImage).ToList();
                if (images.Count == 0) { MessageBox.Show(owner, "There are no images in that folder.", "New Tileset Folder"); return null; }
                // Ask once if any sheet's tile size can't be told from its width
                int tile = 0;
                bool unsure = false;
                foreach (var f in images)
                    using (var img = Theme.LoadImage(f)) if (img != null && img.Width != 256 && img.Width != 128 && img.Width % 256 != 0) unsure = true;
                if (unsure)
                {
                    int k = ChoiceDialog.Ask(owner, "How big are the tiles in these images?", new[] { "32×32 (RPG Maker XP / Essentials)", "16×16 (GBA style, will be doubled)" });
                    if (k < 0) return null;
                    tile = k == 0 ? 32 : 16;
                }
                string baseName = Path.GetFileName(d.SelectedPath.TrimEnd('\\', '/'));
                string name = baseName; int n = 2;
                while (Directory.Exists(Path.Combine(p.TilesetsDir, name))) name = baseName + " " + n++;
                var notes = new List<string>();
                List<string> autotiles;
                try { autotiles = CopyFolder(p, d.SelectedPath, name, tile, notes); }
                catch (Exception ex) { MessageBox.Show(owner, ex.Message, "New Tileset Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning); return null; }
                var ts = new TilesetData { Id = p.Tilesets.Count == 0 ? 1 : p.Tilesets.Max(t => t.Id) + 1, Name = name, Folder = name };
                for (int i = 0; i < Math.Min(7, autotiles.Count); i++) ts.AutotileNames[i] = autotiles[i];
                ts.SyncFolder(p.TilesetsDir);
                p.Tilesets.Add(ts);
                MessageBox.Show(owner, "Created tileset \"" + name + "\" with " + ts.AllSheets().Count + " areas" + (autotiles.Count > 0 ? " and " + Math.Min(7, autotiles.Count) + " autotiles" : "") + "." +
                    (notes.Count > 0 ? "\n\n" + string.Join("\n", notes.Take(12)) : ""), "New Tileset Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return ts;
            }
        }

        /// <summary>Asks for a tileset image and converts it. Returns the saved name, or null if cancelled.</summary>
        public static string PickTileset(IWin32Window owner, Project p)
        {
            using (var dlg = new OpenFileDialog { Title = "Import Tileset Image", Filter = Filter })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
                try
                {
                    using (var src = (Bitmap)Theme.LoadImage(dlg.FileName))
                    {
                        if (src == null) throw new InvalidDataException("Couldn't read the image.");
                        int tile;
                        if (src.Width == 256) tile = 32;
                        else if (src.Width == 128) tile = 16;
                        else if (src.Width % 32 == 0)
                        {
                            int k = ChoiceDialog.Ask(owner, "How big are the tiles?", new[] { "32×32 (RPG Maker XP / Essentials)", "16×16 (GBA style, will be doubled)" });
                            if (k < 0) return null;
                            tile = k == 0 ? 32 : 16;
                        }
                        else if (src.Width % 16 == 0) tile = 16;
                        else throw new InvalidDataException("The image is " + src.Width + " px wide. Tilesets need to be made of 16×16 or 32×32 tiles.");
                        string note;
                        using (var img = ToEngineTileset(src, tile, out note))
                        {
                            string name = SaveInto(p, "Tilesets", Path.GetFileNameWithoutExtension(dlg.FileName), img);
                            MessageBox.Show(owner, "Imported \"" + name + "\" (" + img.Height / 32 + " rows of tiles).\n" + note, "Import Tileset", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return name;
                        }
                    }
                }
                catch (Exception ex) { MessageBox.Show(owner, ex.Message, "Import Tileset", MessageBoxButtons.OK, MessageBoxIcon.Warning); return null; }
            }
        }

        /// <summary>Asks for an autotile image and converts it. Returns the saved name, or null if cancelled.</summary>
        public static string PickAutotile(IWin32Window owner, Project p)
        {
            using (var dlg = new OpenFileDialog { Title = "Import Autotile Image", Filter = Filter })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
                try
                {
                    using (var src = (Bitmap)Theme.LoadImage(dlg.FileName))
                    {
                        if (src == null) throw new InvalidDataException("Couldn't read the image.");
                        string note;
                        using (var img = ToEngineAutotile(src, out note))
                        {
                            string name = SaveInto(p, "Autotiles", Path.GetFileNameWithoutExtension(dlg.FileName), img);
                            if (note != "") MessageBox.Show(owner, note, "Import Autotile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return name;
                        }
                    }
                }
                catch (Exception ex) { MessageBox.Show(owner, ex.Message, "Import Autotile", MessageBoxButtons.OK, MessageBoxIcon.Warning); return null; }
            }
        }
    }
}
