using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PokeCreator.Runtime.Platform
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_Rect
    {
        public int x, y, w, h;
        public SDL_Rect(int x, int y, int w, int h) { this.x = x; this.y = y; this.w = w; this.h = h; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_FRect
    {
        public float x, y, w, h;
        public SDL_FRect(float x, float y, float w, float h) { this.x = x; this.y = y; this.w = w; this.h = h; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_FPoint { public float x, y; public SDL_FPoint(float x, float y) { this.x = x; this.y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_Point { public int x, y; public SDL_Point(int x, int y) { this.x = x; this.y = y; } }

    /// <summary>Raw SDL_Event (56 bytes in SDL2). Only the type field is read directly.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct SDL_Event
    {
        public uint type;
    }

    /// <summary>
    /// Minimal hand-written bindings for SDL2, SDL2_image, SDL2_ttf and SDL2_mixer.
    /// Strings are passed as UTF-8 byte arrays so non-ASCII paths ("Pokémon") work on Windows.
    /// </summary>
    public static class SDL
    {
        const string Lib = "SDL2", Img = "SDL2_image", Ttf = "SDL2_ttf", Mix = "SDL2_mixer";

        public const uint INIT_VIDEO = 0x20, INIT_AUDIO = 0x10, INIT_EVENTS = 0x4000, INIT_GAMECONTROLLER = 0x2000, INIT_JOYSTICK = 0x200;
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int index);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GameControllerOpen(int index);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GameControllerGetButton(IntPtr gc, int button);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern short SDL_GameControllerGetAxis(IntPtr gc, int axis);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GameControllerGetAttached(IntPtr gc);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GameControllerUpdate();
        public const uint QUIT = 0x100, KEYDOWN = 0x300, WINDOWEVENT = 0x200;
        public const int WINDOWPOS_CENTERED = 0x2FFF0000;
        public const uint WINDOW_SHOWN = 0x4, WINDOW_RESIZABLE = 0x20;
        public const uint RENDERER_SOFTWARE = 1, RENDERER_ACCELERATED = 2, RENDERER_PRESENTVSYNC = 4, RENDERER_TARGETTEXTURE = 8;
        public const int BLENDMODE_NONE = 0, BLENDMODE_BLEND = 1, BLENDMODE_ADD = 2, BLENDMODE_MOD = 4;
        public const uint PIXELFORMAT_ARGB8888 = 0x16362004;

        public static byte[] U8(string s)
        {
            if (s == null) return new byte[] { 0 };
            var b = Encoding.UTF8.GetBytes(s);
            var r = new byte[b.Length + 1];
            Buffer.BlockCopy(b, 0, r, 0, b.Length);
            return r;
        }

        public static string GetError() { return PtrToUtf8(SDL_GetError()); }
        public static string PtrToUtf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int n = 0; while (Marshal.ReadByte(p, n) != 0) n++;
            var b = new byte[n]; Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        // ---------------------------------------------------------------- core
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_Init(uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_InitSubSystem(uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_Quit();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetError();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetHint(byte[] name, byte[] value);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_GetTicks();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ulong SDL_GetPerformanceCounter();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ulong SDL_GetPerformanceFrequency();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_Delay(uint ms);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_PollEvent(out SDL_Event e);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetKeyboardState(out int numkeys);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_ShowSimpleMessageBox(uint flags, byte[] title, byte[] message, IntPtr window);

        // ---------------------------------------------------------------- video
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateWindow(byte[] title, int x, int y, int w, int h, uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyWindow(IntPtr window);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_SetWindowTitle(IntPtr window, byte[] title);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_SetWindowSize(IntPtr window, int w, int h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetWindowFullscreen(IntPtr window, uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_SetWindowPosition(IntPtr window, int x, int y);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateRenderer(IntPtr window, int index, uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyRenderer(IntPtr renderer);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderSetLogicalSize(IntPtr renderer, int w, int h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetRenderDrawColor(IntPtr renderer, byte r, byte g, byte b, byte a);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetRenderDrawBlendMode(IntPtr renderer, int mode);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderClear(IntPtr renderer);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderCopy(IntPtr renderer, IntPtr texture, ref SDL_Rect src, ref SDL_Rect dst);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderCopyEx(IntPtr renderer, IntPtr texture, ref SDL_Rect src, ref SDL_Rect dst, double angle, ref SDL_Point center, int flip);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_ComposeCustomBlendMode(int srcColorFactor, int dstColorFactor, int colorOperation, int srcAlphaFactor, int dstAlphaFactor, int alphaOperation);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderFillRect(IntPtr renderer, ref SDL_Rect rect);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderDrawRect(IntPtr renderer, ref SDL_Rect rect);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_RenderPresent(IntPtr renderer);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderReadPixels(IntPtr renderer, IntPtr rect, uint format, IntPtr pixels, int pitch);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetRenderTarget(IntPtr renderer, IntPtr texture);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateTexture(IntPtr renderer, uint format, int access, int w, int h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_UpdateTexture(IntPtr texture, IntPtr rect, IntPtr pixels, int pitch);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateTextureFromSurface(IntPtr renderer, IntPtr surface);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyTexture(IntPtr texture);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_QueryTexture(IntPtr texture, out uint format, out int access, out int w, out int h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetTextureAlphaMod(IntPtr texture, byte alpha);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetTextureColorMod(IntPtr texture, byte r, byte g, byte b);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetTextureBlendMode(IntPtr texture, int mode);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_FreeSurface(IntPtr surface);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_ConvertSurfaceFormat(IntPtr src, uint format, uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateRGBSurfaceWithFormat(uint flags, int w, int h, int depth, uint format);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_RWFromFile(byte[] file, byte[] mode);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SaveBMP_RW(IntPtr surface, IntPtr dst, int freedst);

        // ---------------------------------------------------------------- image
        [DllImport(Img, CallingConvention = CallingConvention.Cdecl)] public static extern int IMG_Init(int flags);
        [DllImport(Img, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr IMG_Load(byte[] file);
        [DllImport(Img, CallingConvention = CallingConvention.Cdecl)] public static extern int IMG_SavePNG(IntPtr surface, byte[] file);

        // ---------------------------------------------------------------- ttf
        [DllImport(Ttf, CallingConvention = CallingConvention.Cdecl)] public static extern int TTF_Init();
        [DllImport(Ttf, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr TTF_OpenFont(byte[] file, int ptsize);
        [DllImport(Ttf, CallingConvention = CallingConvention.Cdecl)] public static extern void TTF_CloseFont(IntPtr font);
        [DllImport(Ttf, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr TTF_RenderUTF8_Blended(IntPtr font, byte[] text, uint color);
        [DllImport(Ttf, CallingConvention = CallingConvention.Cdecl)] public static extern int TTF_SizeUTF8(IntPtr font, byte[] text, out int w, out int h);
        [DllImport(Ttf, CallingConvention = CallingConvention.Cdecl)] public static extern int TTF_FontHeight(IntPtr font);

        // ---------------------------------------------------------------- mixer
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_Init(int flags);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_OpenAudio(int frequency, ushort format, int channels, int chunksize);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern void Mix_CloseAudio();
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_AllocateChannels(int numchans);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr Mix_LoadMUS(byte[] file);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern void Mix_FreeMusic(IntPtr music);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_PlayMusic(IntPtr music, int loops);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_HaltMusic();
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_FadeOutMusic(int ms);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_VolumeMusic(int volume);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr Mix_LoadWAV_RW(IntPtr src, int freesrc);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern void Mix_FreeChunk(IntPtr chunk);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_PlayChannelTimed(int channel, IntPtr chunk, int loops, int ticks);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_Volume(int channel, int volume);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_HaltChannel(int channel);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_FadeOutChannel(int channel, int ms);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_Playing(int channel);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern int Mix_ReserveChannels(int num);
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern void Mix_PauseMusic();
        [DllImport(Mix, CallingConvention = CallingConvention.Cdecl)] public static extern void Mix_ResumeMusic();
    }

    /// <summary>SDL scancodes used by the engine.</summary>
    public static class Scancode
    {
        public const int A = 4, C = 6, D = 7, Q = 20, S = 22, W = 26, X = 27, Z = 29;
        public const int Return = 40, Escape = 41, Backspace = 42, Space = 44;
        public const int F5 = 62, F9 = 66, F12 = 69, Right = 79, Left = 80, Down = 81, Up = 82;
        public const int KpEnter = 88, LShift = 225, RShift = 229, LCtrl = 224;
    }
}
