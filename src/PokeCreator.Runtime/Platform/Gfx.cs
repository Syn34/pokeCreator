using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace PokeCreator.Runtime.Platform
{
    public class Texture
    {
        public IntPtr Handle;
        public int Width, Height;
        public string Path;
    }

    public struct Color
    {
        public byte R, G, B, A;
        public Color(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }
        public uint Packed { get { return (uint)(R | (G << 8) | (B << 16) | (A << 24)); } }
        public static readonly Color White = new Color(255, 255, 255);
        public static readonly Color Black = new Color(0, 0, 0);
        // Essentials default message colours (base / shadow)
        public static readonly Color TextBase = new Color(80, 80, 88);
        public static readonly Color TextShadow = new Color(160, 160, 168);
    }

    /// <summary>2D renderer: texture cache, sprites, rectangles and TrueType text.</summary>
    public class Gfx : IDisposable
    {
        public IntPtr Window { get; private set; }
        public IntPtr Renderer { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        readonly Dictionary<string, Texture> _textures = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Texture> _textCache = new Dictionary<string, Texture>();
        IntPtr _font;
        public int LineHeight { get; private set; }

        public Gfx(string title, int width, int height, int scale, bool hidden = false)
        {
            Width = width; Height = height;
            SDL.SDL_SetHint(SDL.U8("SDL_RENDER_SCALE_QUALITY"), SDL.U8("0"));
            Window = SDL.SDL_CreateWindow(SDL.U8(title), SDL.WINDOWPOS_CENTERED, SDL.WINDOWPOS_CENTERED, width * scale, height * scale,
                SDL.WINDOW_SHOWN | SDL.WINDOW_RESIZABLE);
            if (Window == IntPtr.Zero) throw new Exception("Could not create window: " + SDL.GetError());
            Renderer = SDL.SDL_CreateRenderer(Window, -1, SDL.RENDERER_ACCELERATED | SDL.RENDERER_PRESENTVSYNC | SDL.RENDERER_TARGETTEXTURE);
            if (Renderer == IntPtr.Zero) Renderer = SDL.SDL_CreateRenderer(Window, -1, SDL.RENDERER_SOFTWARE | SDL.RENDERER_TARGETTEXTURE);
            if (Renderer == IntPtr.Zero) throw new Exception("Could not create renderer: " + SDL.GetError());
            SDL.SDL_RenderSetLogicalSize(Renderer, width, height);
            SDL.SDL_SetRenderDrawBlendMode(Renderer, SDL.BLENDMODE_BLEND);
            try
            {
                SDL.SDL_RendererInfo info;
                if (SDL.SDL_GetRendererInfo(Renderer, out info) == 0)
                {
                    RendererName = Marshal.PtrToStringAnsi(info.name) ?? "";
                    SoftwareRenderer = (info.flags & SDL.RENDERER_SOFTWARE) != 0 || RendererName == "software";
                }
            }
            catch { }
        }

        /// <summary>The SDL renderer in use ("direct3d", "opengl", "software"...).</summary>
        public string RendererName = "";
        public bool SoftwareRenderer;

        public void SetTitle(string t) { SDL.SDL_SetWindowTitle(Window, SDL.U8(t)); }

        /// <summary>Window size: scale 1-4 times the game size, or 0 for full screen (desktop resolution, letterboxed).</summary>
        int _pendingSize = -1;
        public void SetScreenSize(int scale) { _pendingSize = scale; } // applied after the frame is shown

        void ApplyScreenSize(int scale)
        {
            if (Window == IntPtr.Zero) return;
            // Some Linux OpenGL drivers (Mesa under X without a window manager) crash when the window is resized
            // from code; there the window can still be resized by hand.
            if (Environment.OSVersion.Platform == PlatformID.Unix && RendererName == "opengl" && Environment.GetEnvironmentVariable("POKECREATOR_ALLOW_RESIZE") == null) return;
            if (scale <= 0) { SDL.SDL_SetWindowFullscreen(Window, 0x00001001); return; } // SDL_WINDOW_FULLSCREEN_DESKTOP
            SDL.SDL_SetWindowFullscreen(Window, 0);
            SDL.SDL_SetWindowSize(Window, Width * scale, Height * scale);
            SDL.SDL_SetWindowPosition(Window, SDL.WINDOWPOS_CENTERED, SDL.WINDOWPOS_CENTERED);
        }

        public bool LoadFont(string path, int size)
        {
            if (path == null || !File.Exists(path)) return false;
            _font = SDL.TTF_OpenFont(SDL.U8(path), size);
            if (_font == IntPtr.Zero) return false;
            LineHeight = Math.Max(SDL.TTF_FontHeight(_font), size);
            return true;
        }
        public bool HasFont { get { return _font != IntPtr.Zero; } }

        // ------------------------------------------------------------ textures
        public Texture Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Texture t;
            if (_textures.TryGetValue(path, out t)) return t;
            t = null;
            if (File.Exists(path))
            {
                IntPtr surf = SDL.IMG_Load(SDL.U8(path));
                if (surf != IntPtr.Zero)
                {
                    IntPtr h = SDL.SDL_CreateTextureFromSurface(Renderer, surf);
                    SDL.SDL_FreeSurface(surf);
                    if (h != IntPtr.Zero)
                    {
                        uint fmt; int access, w, hh;
                        SDL.SDL_QueryTexture(h, out fmt, out access, out w, out hh);
                        SDL.SDL_SetTextureBlendMode(h, SDL.BLENDMODE_BLEND);
                        t = new Texture { Handle = h, Width = w, Height = hh, Path = path };
                    }
                }
                else Console.WriteLine("Could not load image " + path + ": " + SDL.GetError());
            }
            _textures[path] = t; // cache misses too
            return t;
        }

        /// <summary>Loads an image with its hue rotated (RMXP's hue setting for animations), cached per hue.</summary>
        public Texture LoadHued(string path, int hue)
        {
            hue = ((hue % 360) + 360) % 360;
            if (hue == 0 || string.IsNullOrEmpty(path)) return Load(path);
            string key = path + "#hue" + hue;
            Texture t;
            if (_textures.TryGetValue(key, out t)) return t;
            t = null;
            var px = ReadPixels(path);
            if (px != null)
            {
                int h = px.GetLength(0), w = px.GetLength(1);
                var data = new int[w * h];
                double rad = hue * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
                // Rotation of the colour around the grey axis (keeps brightness), like RGSS's hue change
                double[] m = {
                    cos + (1 - cos) / 3, (1 - cos) / 3 - Math.Sqrt(1 / 3.0) * sin, (1 - cos) / 3 + Math.Sqrt(1 / 3.0) * sin,
                    (1 - cos) / 3 + Math.Sqrt(1 / 3.0) * sin, cos + (1 - cos) / 3, (1 - cos) / 3 - Math.Sqrt(1 / 3.0) * sin,
                    (1 - cos) / 3 - Math.Sqrt(1 / 3.0) * sin, (1 - cos) / 3 + Math.Sqrt(1 / 3.0) * sin, cos + (1 - cos) / 3 };
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        uint c = px[y, x];
                        int a = (int)(c >> 24), r = (int)((c >> 16) & 255), g = (int)((c >> 8) & 255), b = (int)(c & 255);
                        int nr = Clamp(r * m[0] + g * m[1] + b * m[2]), ng = Clamp(r * m[3] + g * m[4] + b * m[5]), nb = Clamp(r * m[6] + g * m[7] + b * m[8]);
                        data[y * w + x] = (a << 24) | (nr << 16) | (ng << 8) | nb;
                    }
                IntPtr tex = SDL.SDL_CreateTexture(Renderer, SDL.PIXELFORMAT_ARGB8888, 0, w, h);
                if (tex != IntPtr.Zero)
                {
                    var handle = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try { SDL.SDL_UpdateTexture(tex, IntPtr.Zero, handle.AddrOfPinnedObject(), w * 4); }
                    finally { handle.Free(); }
                    SDL.SDL_SetTextureBlendMode(tex, SDL.BLENDMODE_BLEND);
                    t = new Texture { Handle = tex, Width = w, Height = h, Path = key };
                }
            }
            _textures[key] = t;
            return t;
        }

        static int Clamp(double v) { return v < 0 ? 0 : v > 255 ? 255 : (int)v; }

        readonly Dictionary<string, int> _bottomPad = new Dictionary<string, int>();
        /// <summary>How many fully transparent rows are at the bottom of an image (of its first frameW columns).</summary>
        public int BottomPadding(Texture t, int frameW)
        {
            if (t == null || t.Path == null) return 0;
            int pad;
            if (_bottomPad.TryGetValue(t.Path, out pad)) return pad;
            pad = 0;
            var px = ReadPixels(t.Path);
            if (px != null)
            {
                int h = px.GetLength(0), w = Math.Min(px.GetLength(1), frameW > 0 ? frameW : px.GetLength(1));
                for (int y = h - 1; y >= 0; y--)
                {
                    bool empty = true;
                    for (int x = 0; x < w && empty; x++) if ((px[y, x] >> 24) > 16) empty = false;
                    if (!empty) break;
                    pad++;
                }
                if (pad >= h) pad = 0;
            }
            _bottomPad[t.Path] = pad;
            return pad;
        }

        /// <summary>Reads an image's pixels as ARGB values ([y, x]); null if it can't be loaded.</summary>
        public uint[,] ReadPixels(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            IntPtr surf = SDL.IMG_Load(SDL.U8(path));
            if (surf == IntPtr.Zero) return null;
            IntPtr conv = SDL.SDL_ConvertSurfaceFormat(surf, SDL.PIXELFORMAT_ARGB8888, 0);
            SDL.SDL_FreeSurface(surf);
            if (conv == IntPtr.Zero) return null;
            int w = Marshal.ReadInt32(conv, 16), h = Marshal.ReadInt32(conv, 20), pitch = Marshal.ReadInt32(conv, 24);
            IntPtr px = Marshal.ReadIntPtr(conv, 32);
            var result = new uint[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    result[y, x] = (uint)Marshal.ReadInt32(px, y * pitch + x * 4);
            SDL.SDL_FreeSurface(conv);
            return result;
        }

        public void Draw(Texture t, int sx, int sy, int sw, int sh, int dx, int dy, int dw = -1, int dh = -1, int alpha = 255)
        {
            if (t == null) return;
            var src = new SDL_Rect(sx, sy, sw, sh);
            var dst = new SDL_Rect(dx, dy, dw < 0 ? sw : dw, dh < 0 ? sh : dh);
            if (alpha < 255) SDL.SDL_SetTextureAlphaMod(t.Handle, (byte)Math.Max(0, alpha));
            SDL.SDL_RenderCopy(Renderer, t.Handle, ref src, ref dst);
            if (alpha < 255) SDL.SDL_SetTextureAlphaMod(t.Handle, 255);
        }

        int _subtractMode = -1;

        /// <summary>
        /// Draws part of a texture centred at (cx, cy), zoomed, rotated (degrees, counter-clockwise as in RMXP),
        /// mirrored, with opacity, blend (0 normal, 1 additive, 2 subtractive), an optional colour tint
        /// (R,G,B,A blend toward that colour, like RMXP's Sprite#color).
        /// </summary>
        public void DrawEx(Texture t, int sx, int sy, int sw, int sh, double cx, double cy, double zoomX, double zoomY, double angle, bool mirror, int opacity, int blend, Color color = default(Color))
        {
            if (t == null || opacity <= 0 || zoomX == 0 || zoomY == 0) return;
            int dw = (int)Math.Round(sw * Math.Abs(zoomX)), dh = (int)Math.Round(sh * Math.Abs(zoomY));
            if (dw <= 0 || dh <= 0) return;
            var src = new SDL_Rect(sx, sy, sw, sh);
            var dst = new SDL_Rect((int)Math.Round(cx - dw / 2.0), (int)Math.Round(cy - dh / 2.0), dw, dh);
            var center = new SDL_Point(dw / 2, dh / 2);
            int flip = (mirror ^ zoomX < 0 ? 1 : 0) | (zoomY < 0 ? 2 : 0);
            int mode = 1;
            if (blend == 1) mode = 2;
            else if (blend == 2)
            {
                if (_subtractMode == -1) { try { _subtractMode = SDL.SDL_ComposeCustomBlendMode(5, 2, 3, 1, 2, 1); } catch { _subtractMode = 0; } }
                mode = _subtractMode != 0 ? _subtractMode : 2;
            }
            // A colour tint darkens channels toward the colour, then an additive pass brightens toward it
            double a = color.A / 255.0;
            byte mr = 255, mg = 255, mb = 255;
            SDL.SDL_SetTextureBlendMode(t.Handle, mode);
            SDL.SDL_SetTextureAlphaMod(t.Handle, (byte)Math.Max(0, Math.Min(255, opacity)));
            if (a > 0)
            {
                mr = (byte)(255 * (1 - a) + color.R * a * (color.R / 255.0));
                mg = (byte)(255 * (1 - a) + color.G * a * (color.G / 255.0));
                mb = (byte)(255 * (1 - a) + color.B * a * (color.B / 255.0));
                SDL.SDL_SetTextureColorMod(t.Handle, mr, mg, mb);
            }
            SDL.SDL_RenderCopyEx(Renderer, t.Handle, ref src, ref dst, -angle, ref center, flip);
            if (a > 0)
            {
                // Additive pass so white or light tints can brighten the sprite
                SDL.SDL_SetTextureBlendMode(t.Handle, 2);
                SDL.SDL_SetTextureColorMod(t.Handle, (byte)(color.R * a), (byte)(color.G * a), (byte)(color.B * a));
                SDL.SDL_RenderCopyEx(Renderer, t.Handle, ref src, ref dst, -angle, ref center, flip);
                SDL.SDL_SetTextureColorMod(t.Handle, 255, 255, 255);
            }
            SDL.SDL_SetTextureAlphaMod(t.Handle, 255);
            SDL.SDL_SetTextureBlendMode(t.Handle, 1);
        }

        /// <summary>
        /// Draws like DrawEx with an RPG Maker tone (red, green, blue -255..255): negative values darken that channel,
        /// positive ones brighten it with an additive pass.
        /// </summary>
        public void DrawToned(Texture t, int sx, int sy, int sw, int sh, double cx, double cy, double zoomX, double zoomY, double angle, int opacity, int blend, int tr, int tg, int tb)
        {
            if (t == null) return;
            if (tr == 0 && tg == 0 && tb == 0) { DrawEx(t, sx, sy, sw, sh, cx, cy, zoomX, zoomY, angle, false, opacity, blend); return; }
            if (opacity <= 0 || zoomX == 0 || zoomY == 0) return;
            int dw = (int)Math.Round(sw * Math.Abs(zoomX)), dh = (int)Math.Round(sh * Math.Abs(zoomY));
            if (dw <= 0 || dh <= 0) return;
            var src = new SDL_Rect(sx, sy, sw, sh);
            var dst = new SDL_Rect((int)Math.Round(cx - dw / 2.0), (int)Math.Round(cy - dh / 2.0), dw, dh);
            var center = new SDL_Point(dw / 2, dh / 2);
            Func<int, byte> dark = v => (byte)Math.Max(0, Math.Min(255, 255 + Math.Min(0, v)));
            Func<int, byte> light = v => (byte)Math.Max(0, Math.Min(255, v));
            SDL.SDL_SetTextureBlendMode(t.Handle, blend == 1 ? 2 : 1);
            SDL.SDL_SetTextureAlphaMod(t.Handle, (byte)Math.Max(0, Math.Min(255, opacity)));
            SDL.SDL_SetTextureColorMod(t.Handle, dark(tr), dark(tg), dark(tb));
            SDL.SDL_RenderCopyEx(Renderer, t.Handle, ref src, ref dst, -angle, ref center, 0);
            if (tr > 0 || tg > 0 || tb > 0)
            {
                SDL.SDL_SetTextureBlendMode(t.Handle, 2);
                SDL.SDL_SetTextureColorMod(t.Handle, light(tr), light(tg), light(tb));
                SDL.SDL_RenderCopyEx(Renderer, t.Handle, ref src, ref dst, -angle, ref center, 0);
            }
            SDL.SDL_SetTextureColorMod(t.Handle, 255, 255, 255);
            SDL.SDL_SetTextureAlphaMod(t.Handle, 255);
            SDL.SDL_SetTextureBlendMode(t.Handle, 1);
        }

        public void DrawTinted(Texture t, int sx, int sy, int sw, int sh, int dx, int dy, Color tint)
        {
            if (t == null) return;
            SDL.SDL_SetTextureColorMod(t.Handle, tint.R, tint.G, tint.B);
            Draw(t, sx, sy, sw, sh, dx, dy);
            SDL.SDL_SetTextureColorMod(t.Handle, 255, 255, 255);
        }

        public void FillRect(int x, int y, int w, int h, Color c)
        {
            SDL.SDL_SetRenderDrawColor(Renderer, c.R, c.G, c.B, c.A);
            var r = new SDL_Rect(x, y, w, h);
            SDL.SDL_RenderFillRect(Renderer, ref r);
        }

        public void DrawRect(int x, int y, int w, int h, Color c)
        {
            SDL.SDL_SetRenderDrawColor(Renderer, c.R, c.G, c.B, c.A);
            var r = new SDL_Rect(x, y, w, h);
            SDL.SDL_RenderDrawRect(Renderer, ref r);
        }

        public void Clear(Color c)
        {
            SDL.SDL_SetRenderDrawColor(Renderer, c.R, c.G, c.B, 255);
            SDL.SDL_RenderClear(Renderer);
        }

        public void Present()
        {
            SDL.SDL_RenderPresent(Renderer);
            if (_pendingSize >= 0) { int s = _pendingSize; _pendingSize = -1; ApplyScreenSize(s); }
        }

        // ------------------------------------------------------------ text
        Texture TextTexture(string text, Color c)
        {
            if (_font == IntPtr.Zero || string.IsNullOrEmpty(text)) return null;
            string key = c.Packed + "|" + text;
            Texture t;
            if (_textCache.TryGetValue(key, out t)) return t;
            if (_textCache.Count > 400) ClearTextCache();
            IntPtr surf = SDL.TTF_RenderUTF8_Blended(_font, SDL.U8(text), c.Packed);
            if (surf == IntPtr.Zero) return null;
            IntPtr h = SDL.SDL_CreateTextureFromSurface(Renderer, surf);
            SDL.SDL_FreeSurface(surf);
            uint fmt; int access, w, hh;
            SDL.SDL_QueryTexture(h, out fmt, out access, out w, out hh);
            t = new Texture { Handle = h, Width = w, Height = hh };
            _textCache[key] = t;
            return t;
        }

        void ClearTextCache()
        {
            foreach (var t in _textCache.Values) if (t != null) SDL.SDL_DestroyTexture(t.Handle);
            _textCache.Clear();
        }

        public int TextWidth(string text)
        {
            if (_font == IntPtr.Zero || string.IsNullOrEmpty(text)) return (text ?? "").Length * 10;
            int w, h; SDL.TTF_SizeUTF8(_font, SDL.U8(text), out w, out h);
            return w;
        }

        /// <summary>Draws text with the classic Pokémon drop shadow (right, below and diagonal).</summary>
        public void DrawText(int x, int y, string text, Color baseColor, Color shadow)
        {
            var s = TextTexture(text, shadow);
            if (s != null)
            {
                Draw(s, 0, 0, s.Width, s.Height, x + 2, y);
                Draw(s, 0, 0, s.Width, s.Height, x, y + 2);
                Draw(s, 0, 0, s.Width, s.Height, x + 2, y + 2);
            }
            var b = TextTexture(text, baseColor);
            if (b != null) Draw(b, 0, 0, b.Width, b.Height, x, y);
        }

        public void DrawText(int x, int y, string text, Color c)
        {
            var b = TextTexture(text, c);
            if (b != null) Draw(b, 0, 0, b.Width, b.Height, x, y);
        }

        // ------------------------------------------------------------ screenshots (used by automated tests)
        public void SaveScreenshot(string path)
        {
            int w = Width, h = Height;
            IntPtr surf = SDL.SDL_CreateRGBSurfaceWithFormat(0, w, h, 32, SDL.PIXELFORMAT_ARGB8888);
            // SDL_Surface: flags(4) format(8) w(4) h(4) pitch(4) pixels(8) on 64-bit
            int pitch = Marshal.ReadInt32(surf, 24);
            IntPtr pixels = Marshal.ReadIntPtr(surf, 32);
            SDL.SDL_RenderReadPixels(Renderer, IntPtr.Zero, SDL.PIXELFORMAT_ARGB8888, pixels, pitch);
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) SDL.IMG_SavePNG(surf, SDL.U8(path));
            else SDL.SDL_SaveBMP_RW(surf, SDL.SDL_RWFromFile(SDL.U8(path), SDL.U8("wb")), 1);
            SDL.SDL_FreeSurface(surf);
        }

        public void Dispose()
        {
            ClearTextCache();
            foreach (var t in _textures.Values) if (t != null) SDL.SDL_DestroyTexture(t.Handle);
            _textures.Clear();
            if (_font != IntPtr.Zero) SDL.TTF_CloseFont(_font);
            if (Renderer != IntPtr.Zero) SDL.SDL_DestroyRenderer(Renderer);
            if (Window != IntPtr.Zero) SDL.SDL_DestroyWindow(Window);
        }
    }
}
