using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace PokeCreator.Studio.Controls
{
    /// <summary>
    /// Reads every frame of an animated GIF as full-size pictures (frames drawn over each other the way a browser shows them,
    /// with transparency and disposal), independent of the system's GIF support. Used for animated sprite import.
    /// </summary>
    public static class GifDecoder
    {
        public static List<Bitmap> ReadFrames(string file)
        {
            var data = File.ReadAllBytes(file);
            if (data.Length < 13 || data[0] != 'G' || data[1] != 'I' || data[2] != 'F') throw new InvalidDataException("Not a GIF file.");
            int pos = 6;
            int w = data[pos] | data[pos + 1] << 8, h = data[pos + 2] | data[pos + 3] << 8;
            int flags = data[pos + 4];
            pos += 7;
            int[] global = null;
            if ((flags & 0x80) != 0) { global = ReadPalette(data, ref pos, 2 << (flags & 7)); }

            var canvas = new int[w * h];          // ARGB, starts transparent
            var frames = new List<Bitmap>();
            int transparent = -1, disposal = 0;
            while (pos < data.Length)
            {
                int block = data[pos++];
                if (block == 0x3B) break;                  // trailer
                if (block == 0x21)                         // extension
                {
                    int label = data[pos++];
                    if (label == 0xF9 && data[pos] >= 4)   // graphic control: disposal and transparent colour
                    {
                        int packed = data[pos + 1];
                        disposal = (packed >> 2) & 7;
                        transparent = (packed & 1) != 0 ? data[pos + 4] : -1;
                    }
                    SkipSubBlocks(data, ref pos);
                    continue;
                }
                if (block != 0x2C) break;                  // unknown: stop
                int fx = data[pos] | data[pos + 1] << 8, fy = data[pos + 2] | data[pos + 3] << 8;
                int fw = data[pos + 4] | data[pos + 5] << 8, fh = data[pos + 6] | data[pos + 7] << 8;
                int iflags = data[pos + 8];
                pos += 9;
                int[] pal = global;
                if ((iflags & 0x80) != 0) pal = ReadPalette(data, ref pos, 2 << (iflags & 7));
                bool interlaced = (iflags & 0x40) != 0;
                int minCode = data[pos++];
                var lzw = new MemoryStream();
                while (pos < data.Length) { int n = data[pos++]; if (n == 0) break; lzw.Write(data, pos, Math.Min(n, data.Length - pos)); pos += n; }
                var indices = Lzw(lzw.ToArray(), minCode, fw * fh);

                int[] before = disposal == 3 ? (int[])canvas.Clone() : null;
                // Rows of interlaced images come in four passes
                int[] rowOrder = new int[fh];
                if (interlaced)
                {
                    int r = 0;
                    foreach (var pass in new[] { new[] { 0, 8 }, new[] { 4, 8 }, new[] { 2, 4 }, new[] { 1, 2 } })
                        for (int y = pass[0]; y < fh; y += pass[1]) rowOrder[r++] = y;
                }
                else for (int y = 0; y < fh; y++) rowOrder[y] = y;
                for (int i = 0; i < fh; i++)
                {
                    int y = fy + rowOrder[i];
                    if (y < 0 || y >= h) continue;
                    for (int x = 0; x < fw; x++)
                    {
                        int k = i * fw + x;
                        if (k >= indices.Length) break;
                        int idx = indices[k];
                        if (idx == transparent || pal == null || idx >= pal.Length) continue;
                        int cx = fx + x;
                        if (cx >= 0 && cx < w) canvas[y * w + cx] = pal[idx];
                    }
                }
                frames.Add(ToBitmap(canvas, w, h));
                // What happens to this frame before the next one is drawn
                if (disposal == 2)
                {
                    for (int y = fy; y < Math.Min(h, fy + fh); y++)
                        for (int x = fx; x < Math.Min(w, fx + fw); x++) canvas[y * w + x] = 0;
                }
                else if (disposal == 3 && before != null) canvas = before;
                transparent = -1; disposal = 0;
            }
            if (frames.Count == 0) throw new InvalidDataException("The GIF has no pictures.");
            return frames;
        }

        static int[] ReadPalette(byte[] d, ref int pos, int count)
        {
            var p = new int[count];
            for (int i = 0; i < count && pos + 2 < d.Length; i++, pos += 3) p[i] = unchecked((int)0xFF000000) | d[pos] << 16 | d[pos + 1] << 8 | d[pos + 2];
            return p;
        }

        static void SkipSubBlocks(byte[] d, ref int pos)
        {
            while (pos < d.Length) { int n = d[pos++]; if (n == 0) break; pos += n; }
        }

        static byte[] Lzw(byte[] src, int minCode, int pixels)
        {
            var output = new byte[pixels];
            int clear = 1 << minCode, end = clear + 1;
            int codeSize = minCode + 1, next = end + 1;
            var prefix = new int[4096]; var suffix = new byte[4096]; var stack = new byte[4097];
            for (int i = 0; i < clear; i++) suffix[i] = (byte)i;
            int bits = 0, buffer = 0, sp = 0, outPos = 0, old = -1; byte first = 0;
            for (int i = 0; i < src.Length && outPos < pixels; i++)
            {
                buffer |= src[i] << bits; bits += 8;
                while (bits >= codeSize && outPos < pixels)
                {
                    int code = buffer & ((1 << codeSize) - 1);
                    buffer >>= codeSize; bits -= codeSize;
                    if (code == clear) { codeSize = minCode + 1; next = end + 1; old = -1; continue; }
                    if (code == end) return output;
                    if (old == -1) { output[outPos++] = suffix[code]; old = code; first = suffix[code]; continue; }
                    int c = code;
                    if (code >= next) { stack[sp++] = first; c = old; }
                    while (c >= clear && sp < 4096) { stack[sp++] = suffix[c]; c = prefix[c]; }
                    first = suffix[c];
                    stack[sp++] = first;
                    while (sp > 0 && outPos < pixels) output[outPos++] = stack[--sp];
                    sp = 0;
                    if (next < 4096) { prefix[next] = old; suffix[next] = first; next++; if (next == (1 << codeSize) && codeSize < 12) codeSize++; }
                    old = code;
                }
            }
            return output;
        }

        static Bitmap ToBitmap(int[] argb, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(argb, y * w, bd.Scan0 + y * bd.Stride, w);
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }
    }
}
