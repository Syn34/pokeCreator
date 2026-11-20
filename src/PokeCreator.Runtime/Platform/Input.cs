using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using PokeCreator.Projects;

namespace PokeCreator.Runtime.Platform
{
    public enum Button { Down, Left, Right, Up, Action, Cancel, Run, Menu, Count }

    /// <summary>Keyboard input mapped to game buttons, with press / trigger / repeat states.</summary>
    public static class Input
    {
        static readonly int[][] Keys =
        {
            new[] { Scancode.Down, Scancode.S },
            new[] { Scancode.Left, Scancode.A },
            new[] { Scancode.Right, Scancode.D },
            new[] { Scancode.Up, Scancode.W },
            new[] { Scancode.Z, Scancode.Space, Scancode.Return, Scancode.C, Scancode.KpEnter },
            new[] { Scancode.X, Scancode.Escape, Scancode.Backspace },
            new[] { Scancode.LShift, Scancode.RShift },
            new[] { Scancode.Escape, Scancode.X },
        };

        static readonly int[] _held = new int[(int)Button.Count];
        static readonly bool[] _simulated = new bool[(int)Button.Count];

        public static void Update()
        {
            int n;
            IntPtr state = SDL.SDL_GetKeyboardState(out n);
            for (int b = 0; b < (int)Button.Count; b++)
            {
                bool down = _simulated[b];
                foreach (int sc in Keys[b]) if (sc < n && Marshal.ReadByte(state, sc) != 0) { down = true; break; }
                _held[b] = down ? _held[b] + 1 : 0;
            }
        }

        public static bool Press(Button b) { return _held[(int)b] > 0; }
        public static bool Trigger(Button b) { return _held[(int)b] == 1; }
        public static bool Repeat(Button b) { int h = _held[(int)b]; return h == 1 || (h > 20 && (h - 20) % 6 == 0); }

        /// <summary>Held direction as RMXP numbers: 2 down, 4 left, 6 right, 8 up, 0 none (most recent wins).</summary>
        public static int Dir4
        {
            get
            {
                int best = 0, bestH = int.MaxValue;
                int[] dirs = { 2, 4, 6, 8 };
                for (int i = 0; i < 4; i++)
                {
                    int h = _held[i];
                    if (h > 0 && h < bestH) { bestH = h; best = dirs[i]; }
                }
                return best;
            }
        }

        public static void Simulate(Button b, bool down) { _simulated[(int)b] = down; }
    }

    /// <summary>Background music, music effects and sound effects through SDL_mixer.</summary>
    public class Audio : IDisposable
    {
        readonly Project _project;
        readonly bool _ok;
        IntPtr _music;
        string _bgmName = "";
        readonly Dictionary<string, IntPtr> _chunks = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        public Audio(Project p)
        {
            _project = p;
            try
            {
                SDL.Mix_Init(0x08 | 0x10 | 0x20); // MP3 | OGG | MID
                _ok = SDL.Mix_OpenAudio(44100, 0x8010, 2, 2048) == 0;
                if (_ok) SDL.Mix_AllocateChannels(16);
                else Console.WriteLine("Audio disabled: " + SDL.GetError());
            }
            catch (Exception ex) { Console.WriteLine("Audio disabled: " + ex.Message); _ok = false; }
        }

        public string CurrentBgm { get { return _bgmName; } }

        /// <summary>Volume settings from the Options menu (percent).</summary>
        public int BgmVolume = 100, SeVolume = 100;
        int _bgmBase = 100;

        public void ApplyVolume() { if (_ok && _music != IntPtr.Zero) SDL.Mix_VolumeMusic(_bgmBase * BgmVolume / 100 * 128 / 100); }

        public void PlayBgm(string name, int volume = 100)
        {
            if (!_ok) { _bgmName = name ?? ""; return; }
            if (string.IsNullOrEmpty(name)) { StopBgm(); return; }
            if (string.Equals(name, _bgmName, StringComparison.OrdinalIgnoreCase) && _music != IntPtr.Zero) return;
            StopBgm();
            string path = _project.FindAsset("Audio/BGM", name);
            if (path == null) { Console.WriteLine("BGM not found: " + name); return; }
            _music = SDL.Mix_LoadMUS(SDL.U8(path));
            if (_music == IntPtr.Zero) { Console.WriteLine("Could not play " + path + ": " + SDL.GetError()); return; }
            _bgmBase = volume;
            SDL.Mix_VolumeMusic(volume * BgmVolume / 100 * 128 / 100);
            SDL.Mix_PlayMusic(_music, -1);
            _bgmName = name;
        }

        public void StopBgm()
        {
            if (_ok && _music != IntPtr.Zero) { SDL.Mix_HaltMusic(); SDL.Mix_FreeMusic(_music); }
            _music = IntPtr.Zero; _bgmName = "";
        }

        public void FadeOutBgm(int seconds)
        {
            if (_ok && _music != IntPtr.Zero) SDL.Mix_FadeOutMusic(seconds * 1000);
            _bgmName = "";
        }

        public void PlaySe(string name, int volume = 100) { PlayChunk("Audio/SE", name, volume); }
        public void PlayMe(string name, int volume = 100) { PlayChunk("Audio/ME", name, volume); }
        /// <summary>A Pokémon's cry (Audio/SE/Cries/SPECIES, or SPECIES_form).</summary>
        public void PlayCry(PokeCreator.Pkmn.Pokemon pk)
        {
            if (pk == null || pk.IsEgg) return;
            string name = pk.Form > 0 && _project.FindAsset("Audio/SE", "Cries/" + pk.Species + "_" + pk.Form) != null ? pk.Species + "_" + pk.Form : pk.Species;
            PlayChunk("Audio/SE", "Cries/" + name, 100);
        }
        public void PlayBgs(string name, int volume = 100) { /* ambient loops: planned */ }

        void PlayChunk(string folder, string name, int volume)
        {
            if (!_ok || string.IsNullOrEmpty(name)) return;
            string key = folder + "/" + name;
            IntPtr c;
            if (!_chunks.TryGetValue(key, out c))
            {
                string path = _project.FindAsset(folder, name);
                c = path == null ? IntPtr.Zero : SDL.Mix_LoadWAV_RW(SDL.SDL_RWFromFile(SDL.U8(path), SDL.U8("rb")), 1);
                _chunks[key] = c;
            }
            if (c == IntPtr.Zero) return;
            int ch = SDL.Mix_PlayChannelTimed(-1, c, 0, -1);
            if (ch >= 0) SDL.Mix_Volume(ch, volume * SeVolume / 100 * 128 / 100);
        }

        public void Dispose()
        {
            StopBgm();
            foreach (var c in _chunks.Values) if (c != IntPtr.Zero) SDL.Mix_FreeChunk(c);
            if (_ok) SDL.Mix_CloseAudio();
        }
    }
}
