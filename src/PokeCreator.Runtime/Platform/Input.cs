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
            foreach (int sc in WatchedKeys) { int h; _keyHeld.TryGetValue(sc, out h); _keyHeld[sc] = sc < n && Marshal.ReadByte(state, sc) != 0 ? h + 1 : 0; }
        }

        static readonly Dictionary<int, int> _keyHeld = new Dictionary<int, int>();
        /// <summary>A raw key (SDL scancode) pressed this frame.</summary>
        public static bool KeyTrigger(int scancode) { int h; return _keyHeld.TryGetValue(scancode, out h) && h == 1; }
        public static bool KeyPress(int scancode) { int h; return _keyHeld.TryGetValue(scancode, out h) && h > 0; }
        static readonly int[] WatchedKeys = { Scancode.F9, Scancode.F5, Scancode.LCtrl, 228 /* right Ctrl */ };

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
                if (_ok) { SDL.Mix_AllocateChannels(16); SDL.Mix_ReserveChannels(2); } // 0 = BGS, 1 = ME
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
            _music = IntPtr.Zero; _bgmName = ""; _musicPausedForMe = false;
        }

        public void FadeOutBgm(int seconds)
        {
            if (_ok && _music != IntPtr.Zero) SDL.Mix_FadeOutMusic(seconds * 1000);
            _bgmName = "";
        }

        public void PlaySe(string name, int volume = 100) { PlayChunk("Audio/SE", name, volume); }
        const int BgsChannel = 0, MeChannel = 1;
        bool _musicPausedForMe;
        string _bgsName = "";
        public string CurrentBgs { get { return _bgsName; } }

        /// <summary>A music effect (jingle): the BGM pauses while it plays and carries on afterwards (as RMXP).</summary>
        public void PlayMe(string name, int volume = 100)
        {
            if (!_ok || string.IsNullOrEmpty(name)) return;
            IntPtr c = Chunk("Audio/ME", name);
            if (c == IntPtr.Zero) return;
            if (_music != IntPtr.Zero && !_musicPausedForMe) { SDL.Mix_PauseMusic(); _musicPausedForMe = true; }
            SDL.Mix_PlayChannelTimed(MeChannel, c, 0, -1);
            SDL.Mix_Volume(MeChannel, volume * BgmVolume / 100 * 128 / 100);
        }

        public bool MePlaying { get { return _ok && SDL.Mix_Playing(MeChannel) != 0; } }

        public void StopMe() { if (_ok) SDL.Mix_HaltChannel(MeChannel); Update(); }

        /// <summary>Background sound: an ambient loop (rain, wind, waves) under the music (Audio/BGS).</summary>
        public void PlayBgs(string name, int volume = 100)
        {
            if (string.IsNullOrEmpty(name)) { StopBgs(); return; }
            if (!_ok) { _bgsName = name; return; }
            if (string.Equals(name, _bgsName, StringComparison.OrdinalIgnoreCase) && SDL.Mix_Playing(BgsChannel) != 0) { SDL.Mix_Volume(BgsChannel, volume * BgmVolume / 100 * 128 / 100); return; }
            IntPtr c = Chunk("Audio/BGS", name);
            if (c == IntPtr.Zero) { Console.WriteLine("BGS not found: " + name); return; }
            SDL.Mix_PlayChannelTimed(BgsChannel, c, -1, -1);
            SDL.Mix_Volume(BgsChannel, volume * BgmVolume / 100 * 128 / 100);
            _bgsName = name;
        }

        public void StopBgs() { if (_ok) SDL.Mix_HaltChannel(BgsChannel); _bgsName = ""; }
        public void FadeOutBgs(int seconds) { if (_ok) SDL.Mix_FadeOutChannel(BgsChannel, Math.Max(0, seconds) * 1000); _bgsName = ""; }

        /// <summary>Called every frame: resumes the BGM once a music effect has finished.</summary>
        public void Update()
        {
            if (_musicPausedForMe && (!_ok || SDL.Mix_Playing(MeChannel) == 0)) { _musicPausedForMe = false; if (_ok && _music != IntPtr.Zero) SDL.Mix_ResumeMusic(); }
        }

        IntPtr Chunk(string folder, string name)
        {
            string key = folder + "/" + name;
            IntPtr c;
            if (!_chunks.TryGetValue(key, out c))
            {
                string path = _project.FindAsset(folder, name);
                c = path == null ? IntPtr.Zero : SDL.Mix_LoadWAV_RW(SDL.SDL_RWFromFile(SDL.U8(path), SDL.U8("rb")), 1);
                _chunks[key] = c;
            }
            return c;
        }
        /// <summary>A Pokémon's cry (Audio/SE/Cries/SPECIES, or SPECIES_form).</summary>
        public void PlayCry(PokeCreator.Pkmn.Pokemon pk)
        {
            if (pk == null || pk.IsEgg) return;
            string name = pk.Form > 0 && _project.FindAsset("Audio/SE", "Cries/" + pk.Species + "_" + pk.Form) != null ? pk.Species + "_" + pk.Form : pk.Species;
            PlayChunk("Audio/SE", "Cries/" + name, 100);
        }

        void PlayChunk(string folder, string name, int volume)
        {
            if (!_ok || string.IsNullOrEmpty(name)) return;
            IntPtr c = Chunk(folder, name);
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
