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
        }

        public void SetTitle(string t) { SDL.SDL_SetWindowTitle(Window, SDL.U8(t)); }

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

        public void Draw(Texture t, int sx, int sy, int sw, int sh, int dx, int dy, int dw = -1, int dh = -1, int alpha = 255)
        {
            if (t == null) return;
            var src = new SDL_Rect(sx, sy, sw, sh);
            var dst = new SDL_Rect(dx, dy, dw < 0 ? sw : dw, dh < 0 ? sh : dh);
            if (alpha < 255) SDL.SDL_SetTextureAlphaMod(t.Handle, (byte)Math.Max(0, alpha));
            SDL.SDL_RenderCopy(Renderer, t.Handle, ref src, ref dst);
            if (alpha < 255) SDL.SDL_SetTextureAlphaMod(t.Handle, 255);
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

        public void Present() { SDL.SDL_RenderPresent(Renderer); }

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
