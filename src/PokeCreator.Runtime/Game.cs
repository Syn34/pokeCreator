using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.Render;
using PokeCreator.Runtime.Script;
using PokeCreator.Runtime.UI;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime
{
    public class GameOptions
    {
        public string ProjectDir;
        public int StartMap = -1, StartX, StartY, StartDir = 2;
        public string AutotestScript;
        public bool NoEncounters;
        public bool SkipTitle;
        public int Scale = 0;
    }

    /// <summary>Top-level game: owns the window, the current map and the main loop.</summary>
    public class Game : IDisposable
    {

        public Project Project;
        public Gfx Gfx;
        public Audio Audio;
        public GameState State = new GameState();
        public Player Player;
        public GameMap Map;
        public MessageWindow Message;
        public PlayerOptions Options;
        public Interpreter Interpreter;
        public EssentialsCompat Compat;
        public EncounterSystem Encounters;
        public readonly Random Rng = new Random();
        public List<PokeCreator.Pkmn.Pokemon> Party { get { return State.Party; } }
        MapRenderer _renderer;

        public int DisplayX, DisplayY;
        /// <summary>Overworld weather (map metadata "Weather" or the Set Weather Effects command).</summary>
        public readonly World.WeatherSystem Weather = new World.WeatherSystem();
        public int FrameCount;
        bool _quit;

        public bool MessageBusy { get { return Message.Busy; } }
        public bool InterpreterRunning { get { return Interpreter.Running; } }
        public bool Transferring { get { return _transfer != null; } }

        class TransferRequest { public int Map, X, Y, Dir; public bool Fade; public int Phase; }
        TransferRequest _transfer;
        int _fadeAlpha;          // 0..255 black overlay
        double[] _tone = new double[4], _toneTarget = new double[4];
        int _toneFrames;
        int _flash;
        string _signText; int _signTimer;
        readonly GameOptions _opt;

        public Game(GameOptions opt) { _opt = opt; }

        // ------------------------------------------------------------------ startup
        public void Run()
        {
            Project = Project.Load(_opt.ProjectDir);
            if (SDL.SDL_Init(SDL.INIT_VIDEO | SDL.INIT_EVENTS) != 0) throw new Exception("SDL_Init failed: " + SDL.GetError());
            SDL.SDL_InitSubSystem(SDL.INIT_AUDIO);
            SDL.IMG_Init(0x2 | 0x1); // PNG | JPG
            SDL.TTF_Init();

            var s = Project.Settings;
            int scale = _opt.Scale > 0 ? _opt.Scale : Math.Max(1, s.WindowScale);
            Gfx = new Gfx(s.Title ?? Project.Info.Name, s.ScreenWidth > 0 ? s.ScreenWidth : 512, s.ScreenHeight > 0 ? s.ScreenHeight : 384, scale);
            int fontSize = s.FontSize > 0 ? s.FontSize : 26;
            if (!Gfx.LoadFont(Project.MessageFontFile(), fontSize))
            {
                // No font in the project or BaseData/Fonts: use one that comes with the system so text still shows.
                // System fonts are much larger than the pixel fonts the size is set for, so scale them down.
                string win = Environment.GetEnvironmentVariable("WINDIR");
                var system = new List<string>();
                if (!string.IsNullOrEmpty(win)) foreach (var f in new[] { "segoeui.ttf", "tahoma.ttf", "arial.ttf" }) system.Add(Path.Combine(win, "Fonts", f));
                system.Add("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
                system.Add("/Library/Fonts/Arial.ttf");
                var sys = system.FirstOrDefault(File.Exists);
                if (sys != null && Gfx.LoadFont(sys, Math.Max(10, fontSize * 2 / 3))) Console.WriteLine("No message font found in Fonts or BaseData/Fonts; using " + sys);
                else Console.WriteLine("No font found: text will not show. Put a .ttf file in the project's Fonts folder.");
            }
            Audio = new Audio(Project);
            Message = new MessageWindow(this);
            LoadFrames();
            Options = PlayerOptions.Load(this);
            Options.Apply(this);
            Interpreter = new Interpreter(this);
            Compat = new EssentialsCompat(this);
            Encounters = new EncounterSystem(this) { Enabled = !_opt.NoEncounters };
            _renderer = new MapRenderer(Gfx, Project);
            ScriptLoader.Load(this, Project.ScriptsDir);

            Player = new Player(this);
            if (_opt.StartMap > 0 || _opt.SkipTitle) NewGame();
            else PushScene(new TitleScene(this));
            Autotest test = _opt.AutotestScript != null ? new Autotest(this, _opt.AutotestScript) : null;
            MessageWindow.Echo = test != null;

            ulong freq = SDL.SDL_GetPerformanceFrequency();
            double frameTime = 1.0 / 60.0;
            ulong last = SDL.SDL_GetPerformanceCounter();
            while (!_quit)
            {
                SDL_Event e;
                while (SDL.SDL_PollEvent(out e) != 0) if (e.type == SDL.QUIT) _quit = true;
                if (test != null) { if (!test.Step()) _quit = true; }
                Input.Update();
                Update();
                Draw();
                if (test != null) test.AfterDraw();
                Gfx.Present();
                FrameCount++;
                // Frame limiter (vsync usually handles this)
                double elapsed = (SDL.SDL_GetPerformanceCounter() - last) / (double)freq;
                if (elapsed < frameTime) SDL.SDL_Delay((uint)Math.Max(0, (frameTime - elapsed) * 1000));
                last = SDL.SDL_GetPerformanceCounter();
            }
        }

        void NewGame()
        {
            var s = Project.Settings;
            State = new GameState();
            State.Money = s.StartMoney;
            State.PlayerName = string.IsNullOrEmpty(s.DefaultPlayerName) ? "Red" : s.DefaultPlayerName;
            State.TrainerId = Rng.Next(65536);
            State.PlayerCharacter = Math.Max(0, s.DefaultPlayer);
            Player = new Player(this);
            Player.Look = Project.Players.Count > 0 ? Project.Players[Math.Min(Math.Max(0, s.DefaultPlayer), Project.Players.Count - 1)] : null;
            Player.CharacterName = Player.WalkCharset;
            int map = _opt.StartMap > 0 ? _opt.StartMap : s.StartMap;
            int x = _opt.StartMap > 0 ? _opt.StartX : s.StartX;
            int y = _opt.StartMap > 0 ? _opt.StartY : s.StartY;
            int dir = _opt.StartMap > 0 ? _opt.StartDir : (s.StartDirection == 0 ? 2 : s.StartDirection);
            SetupMap(map, x, y, dir);
        }

        public void Quit() { _quit = true; }

        void LoadGame()
        {
            State = new GameState();
            var d = SaveSystem.Load(Project, State);
            Player = new Player(this);
            Player.Look = Project.Players.Count > 0 ? Project.Players[Math.Min(Math.Max(0, State.PlayerCharacter), Project.Players.Count - 1)] : null;
            Player.CharacterName = Player.WalkCharset;
            SetupMap(d.MapId, d.X, d.Y, d.Direction);
            Player.Surfing = d.Surfing;
            Player.Cycling = d.Cycling && !d.Surfing;
            if (d.Weather != null && d.Weather.Count >= 2) Weather.Set(d.Weather[0], d.Weather[1], 0);
            if (!string.IsNullOrEmpty(d.Bgm) && string.IsNullOrEmpty(Audio.CurrentBgm)) Audio.PlayBgm(d.Bgm);
        }

        /// <summary>Called by the title screen once the player picks Continue or New Game.</summary>
        public void FinishTitle(bool continueGame)
        {
            try { if (continueGame) LoadGame(); else NewGame(); }
            catch (Exception ex)
            {
                Console.WriteLine("Could not load save: " + ex.Message);
                NewGame();
                Message.Show("The save file couldn't be loaded, so a new game was started.\\n(" + ex.Message + ")");
            }
            _fadeAlpha = 255;
        }

        /// <summary>Opens the name entry screen; the running event waits until it's done.</summary>
        public void AskName(string prompt, string initial, int max, Action<string> set, Script.Interpreter ctx)
        {
            PushScene(new NameEntryScene(this, prompt, initial, max, n => { if (!string.IsNullOrEmpty(n)) set(n); }));
        }

        /// <summary>Switches the player character (index into Player Characters) and updates the sprite.</summary>
        public void ChangePlayer(int index)
        {
            if (Project.Players.Count == 0) return;
            State.PlayerCharacter = Math.Max(0, Math.Min(index, Project.Players.Count - 1));
            if (Player != null) Player.Look = Project.Players[State.PlayerCharacter];
        }

        /// <summary>Plays the current map's music again (after the evolution tune).</summary>
        public void RestoreMapBgm()
        {
            var data = Map != null ? Map.Data : null;
            if (data != null && data.AutoplayBgm && data.Bgm != null && !string.IsNullOrEmpty(data.Bgm.Name)) Audio.PlayBgm(data.Bgm.Name, data.Bgm.Volume);
        }

        /// <summary>
        /// Essentials: entering a map with "Weather" metadata (e.g. Rain,50) starts that weather with that % chance;
        /// otherwise the weather stops. Walking across a connection into the same weather keeps it.
        /// </summary>
        void RollMapWeather(bool warped)
        {
            var w = Map.Data.Properties != null ? Map.Data.Properties.Weather : null;
            int type = World.WeatherSystem.None, chance = 0;
            if (!string.IsNullOrEmpty(w))
            {
                var parts = w.Split(',');
                type = World.WeatherSystem.TypeFromName(parts[0]);
                if (parts.Length < 2 || !int.TryParse(parts[1].Trim(), out chance)) chance = 100;
            }
            if (!warped && type == Weather.Type && type != World.WeatherSystem.None) return;
            if (type != World.WeatherSystem.None && Rng.Next(100) < chance) Weather.Set(type, 9, 0);
            else Weather.Set(World.WeatherSystem.None, 0, 0);
        }

        /// <summary>Loads the speech and menu frames from Game Settings (missing files fall back to the built-in box).</summary>
        public void LoadFrames()
        {
            var st = Project.Settings;
            MessageWindow.SpeechSkin = string.IsNullOrEmpty(st.SpeechFrame) ? null : WindowSkin.Load(Gfx, Project.FindAsset("Graphics/Windowskins", st.SpeechFrame));
            MessageWindow.MenuSkin = string.IsNullOrEmpty(st.MenuFrame) ? null : WindowSkin.Load(Gfx, Project.FindAsset("Graphics/Windowskins", st.MenuFrame));
        }

        /// <summary>Map music, or the Bicycle / Surf music while riding.</summary>
        public void PlayMapMusic()
        {
            string ride = Player == null ? null : Player.Cycling ? Project.Settings.BicycleBGM : Player.Surfing ? Project.Settings.SurfBGM : null;
            if (!string.IsNullOrEmpty(ride)) { if (Audio.CurrentBgm != ride) Audio.PlayBgm(ride); return; }
            var data = Map != null ? Map.Data : null;
            if (data != null && data.AutoplayBgm && data.Bgm != null && !string.IsNullOrEmpty(data.Bgm.Name)) Audio.PlayBgm(data.Bgm.Name, data.Bgm.Volume);
        }

        /// <summary>Gets off the bike on maps that don't allow it, and on maps where you're always on it.</summary>
        void UpdateRiding()
        {
            if (Player == null || Map == null || Map.Data.Properties == null) return;
            var pr = Map.Data.Properties;
            if (pr.BicycleAlways && !Player.Surfing) Player.Cycling = true;
            else if (Player.Cycling && !World.FieldMoves.CanBike(this)) Player.Cycling = false;
        }

        public void ReturnToTitle()
        {
            _scenes.Clear();
            Interpreter.Setup(null, 0);
            Audio.StopBgm();
            Map = null;
            PushScene(new TitleScene(this));
        }

        // ------------------------------------------------------------------ scenes (menus, title)
        readonly List<Scene> _scenes = new List<Scene>();
        public void PushScene(Scene s) { _scenes.Add(s); }
        public bool SceneOpen { get { return _scenes.Count > 0; } }

        // ------------------------------------------------------------------ maps
        public void SetupMap(int id, int x, int y, int dir)
        {
            StrengthActive = false; // Strength lasts until the player leaves the map
            if (State != null) State.Visited.Add(id);
            var data = Project.LoadMap(id);
            if (data == null) throw new Exception("Map " + id + " does not exist in this project.");
            Map = new GameMap(Project, data, this, true);
            Map.LoadNeighbors(Project, this);
            Player.MoveTo(x, y);
            if (dir != 0) Player.Turn(dir);
            Interpreter.Setup(null, 0);
            CenterCamera();
            UpdateRiding();
            PlayMapMusic();
            RollMapWeather(true);
            var props = data.Properties;
            if (props != null && props.ShowArea) { _signText = Message.Expand(Map.DisplayName); _signTimer = 150; }
            Gfx.SetTitle((Project.Settings.Title ?? "PokeCreator") + " - " + Message.Expand(Map.DisplayName));
        }

        /// <summary>Called when the player walks across a map connection.</summary>
        public void EnterNeighbor(GameMap neighbor, int offX, int offY)
        {
            int px = Player.X - offX, py = Player.Y - offY;
            int dx = DisplayX - offX * 32, dy = DisplayY - offY * 32;
            var data = neighbor.Data;
            Map = new GameMap(Project, data, this, true);
            Map.LoadNeighbors(Project, this);
            Player.MoveTo(px, py);
            DisplayX = dx; DisplayY = dy;
            if (State != null) State.Visited.Add(data.Id);
            UpdateRiding();
            PlayMapMusic();
            RollMapWeather(false);
            var props = data.Properties;
            if (props != null && props.ShowArea) { _signText = Message.Expand(Map.DisplayName); _signTimer = 150; }
            Gfx.SetTitle((Project.Settings.Title ?? "PokeCreator") + " - " + Message.Expand(Map.DisplayName));
        }

        public void RequestTransfer(int map, int x, int y, int dir, bool fade)
        {
            _transfer = new TransferRequest { Map = map, X = x, Y = y, Dir = dir, Fade = fade };
        }

        void UpdateTransfer()
        {
            var t = _transfer;
            if (t.Phase == 0)
            {
                if (!t.Fade) { t.Phase = 1; }
                else { _fadeAlpha = Math.Min(255, _fadeAlpha + 32); if (_fadeAlpha >= 255) t.Phase = 1; return; }
            }
            if (t.Phase == 1)
            {
                SetupMap(t.Map, t.X, t.Y, t.Dir);
                t.Phase = 2;
                if (!t.Fade) { _transfer = null; return; }
            }
            if (t.Phase == 2)
            {
                _fadeAlpha = Math.Max(0, _fadeAlpha - 32);
                if (_fadeAlpha <= 0) _transfer = null;
            }
        }

        // ------------------------------------------------------------------ battles (placeholders until the battle system lands)
        // ------------------------------------------------------------------ battles
        /// <summary>
        /// The result of the last battle, for the event that started it: a Conditional Branch with
        /// TrainerBattle.start(...) re-checks its condition after the battle and gets this instead of a new battle.
        /// </summary>
        public PokeCreator.Battles.BattleOutcome? ResumeOutcome;
        /// <summary>Set when a battle starts during an event command, so the event waits for it.</summary>
        public bool BattleJustStarted;
        /// <summary>A Yes/No field-move question is open for an event's Conditional Branch (see FieldMoves.Ask).</summary>
        public bool PromptPending;
        object _promptResult, _promptOwner;
        public void SetPrompt(object owner, object result) { _promptOwner = owner; _promptResult = result; }
        public bool TakePrompt(object owner, out object result)
        {
            result = null;
            if (_promptResult == null || owner == null || _promptOwner != owner) return false;
            result = _promptResult; _promptResult = null; _promptOwner = null;
            return true;
        }
        /// <summary>Strength has been used on this map: boulders can be pushed.</summary>
        public bool StrengthActive;

        /// <summary>Closes every open screen (e.g. to fish after choosing a rod in the Bag).</summary>
        public void CloseAllScenes() { foreach (var sc in _scenes.ToList()) sc.Close(); }
        /// <summary>The event waiting for ResumeOutcome (only it may take the result).</summary>
        public object ResumeOwner;
        public PokeCreator.Battles.BattleOutcome LastOutcome;

        public bool PartyCanBattle { get { return State.Party.Any(PokeCreator.Battles.Battle.Able); } }

        /// <summary>Wild battle (from grass, or pbWildBattle).</summary>
        public void StartWildEncounter(string species, int level) { StartWildBattle(species, level, null); }

        public void StartWildBattle(string species, int level, Action<PokeCreator.Battles.BattleOutcome> onEnd)
        {
            var wild = PokeCreator.Pkmn.Pokemon.Create(Project, species, level, Rng);
            if (wild.Data(Project) == null || !PartyCanBattle) { if (onEnd != null) onEnd(PokeCreator.Battles.BattleOutcome.Win); return; }
            var b = new PokeCreator.Battles.Battle(Project, State.Party, new List<PokeCreator.Pkmn.Pokemon> { wild }, true, Rng) { PlayerName = State.PlayerName };
            b.AlreadyCaught = sp => State.Caught.Contains(sp);
            StartBattle(b, new BattleSetup { Wild = true, Backdrop = Backdrop(), OnEnd = onEnd });
        }

        /// <summary>Trainer battle from trainers.json. Returns false if there's no such trainer.</summary>
        public bool StartTrainerBattle(string type, string name, int version, bool canLose, Action<PokeCreator.Battles.BattleOutcome> onEnd)
        {
            var tr = Project.Trainers.FirstOrDefault(t => t.TrainerType == type && t.Name == name && t.Version == version)
                  ?? Project.Trainers.FirstOrDefault(t => t.TrainerType == type && t.Name == name);
            if (tr == null || tr.Pokemon.Count == 0) { Console.WriteLine("Trainer not found: " + type + " " + name + " " + version); return false; }
            if (!PartyCanBattle) return false;
            var tt = Project.TrainerTypes.FirstOrDefault(t => t.Id == type);
            var foes = tr.Pokemon.Select(tp => PokeCreator.Pkmn.Pokemon.FromTrainer(Project, tp, Rng)).ToList();
            var b = new PokeCreator.Battles.Battle(Project, State.Party, foes, false, Rng)
            {
                PlayerName = State.PlayerName, TrainerName = tr.Name,
                TrainerTypeName = tt != null && !string.IsNullOrEmpty(tt.Name) ? tt.Name : type,
                TrainerSkill = tt != null ? tt.SkillLevel : 0,
            };
            StartBattle(b, new BattleSetup { Wild = false, Trainer = tr, TrainerType = tt, Backdrop = Backdrop(), CanLose = canLose, OnEnd = onEnd });
            return true;
        }

        void StartBattle(PokeCreator.Battles.Battle b, BattleSetup setup)
        {
            var end = setup.OnEnd;
            // Rain, snow, sandstorm and sun on the map carry into the battle (as Essentials)
            if (Weather.Type != World.WeatherSystem.None && Weather.Power > 0)
                b.Weather = PokeCreator.Battles.Battle.FromFieldWeather(World.WeatherSystem.Names[Weather.Type]);
            setup.OnEnd = o => { LastOutcome = o; ResumeOutcome = o; if (end != null) end(o); };
            BattleJustStarted = true;
            Audio.PlaySe("Encounter");
            Flash(20);
            PushScene(new BattleScene(this, b, setup));
        }

        /// <summary>Battle backdrop: the map's metadata, else by time of day.</summary>
        string Backdrop()
        {
            var props = Map != null ? Map.Data.Properties : null;
            string bd = props != null && !string.IsNullOrEmpty(props.BattleBack) ? props.BattleBack : "field";
            int h = DateTime.Now.Hour;
            string time = h >= 20 || h < 6 ? "_night" : h >= 17 ? "_eve" : "";
            if (time != "" && Project.FindAsset("Graphics/Battlebacks", bd + time + "_bg") != null) bd += time;
            return bd;
        }

        // ------------------------------------------------------------------ trainers who spot the player
        static readonly System.Text.RegularExpressions.Regex TrainerName = new System.Text.RegularExpressions.Regex(@"trainer\((\d+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Essentials trainers: an event named "Trainer(n)" with an Event Touch page starts when the player walks into
        /// its line of sight (n tiles in the direction it faces, nothing in between). Returns true if one started.
        /// </summary>
        public bool CheckTrainerSight()
        {
            if (Map == null || !PartyCanBattle) return false;
            foreach (var e in Map.Events)
            {
                if (e.Erased || e.Starting || e.Trigger != Triggers.EventTouch || !e.HasCommands) continue;
                var m = TrainerName.Match(e.Name ?? "");
                if (!m.Success) continue;
                if (InSight(e, int.Parse(m.Groups[1].Value))) { e.Start(); return true; }
            }
            return false;
        }

        bool InSight(GameEvent e, int range)
        {
            int d = e.Direction;
            int dx = d == 4 ? -1 : d == 6 ? 1 : 0, dy = d == 8 ? -1 : d == 2 ? 1 : 0;
            int x = e.X, y = e.Y;
            for (int i = 1; i <= range; i++)
            {
                if (x + dx == Player.X && y + dy == Player.Y) return true;
                if (!e.Passable(x, y, d)) return false;
                x += dx; y += dy;
            }
            return false;
        }

        Character _exclaimOn;
        int _exclaimTimer;

        /// <summary>Shows a "!" over a character for a moment (trainers noticing the player).</summary>
        public void Exclaim(Character c, int frames)
        {
            _exclaimOn = c; _exclaimTimer = frames;
            Audio.PlaySe("Exclaim");
        }

        void DrawExclaim()
        {
            if (_exclaimTimer <= 0 || _exclaimOn == null) return;
            _exclaimTimer--;
            int x = _exclaimOn.ScreenX(DisplayX) - 10, y = _exclaimOn.ScreenY(DisplayY) - 64 - Math.Max(0, 8 - (30 - _exclaimTimer) * 2);
            Gfx.FillRect(x - 2, y - 2, 24, 28, new Color(40, 40, 48));
            Gfx.FillRect(x, y, 20, 24, new Color(248, 248, 248));
            Gfx.FillRect(x + 8, y + 3, 4, 12, new Color(232, 48, 40));
            Gfx.FillRect(x + 8, y + 17, 4, 4, new Color(232, 48, 40));
        }

        /// <summary>After losing: heal the party and go back to the last healing spot (Home in Game Settings, else the start).</summary>
        public void Whiteout()
        {
            foreach (var pk in State.Party) pk.Heal(Project);
            ResumeOutcome = null; ResumeOwner = null; // the event that started the battle is cancelled
            var s = Project.Settings;
            int map = s.StartMap, x = s.StartX, y = s.StartY, dir = 2;
            if (State.HealMap > 0) { map = State.HealMap; x = State.HealX; y = State.HealY; dir = State.HealDir > 0 ? State.HealDir : 2; }
            else if (s.Home != null && s.Home.Count >= 3) { map = s.Home[0]; x = s.Home[1]; y = s.Home[2]; if (s.Home.Count > 3) dir = s.Home[3]; }
            Interpreter.Setup(null, 0);
            RequestTransfer(map, x, y, dir, true);
        }

        // ------------------------------------------------------------------ screen effects
        public void SetScreenTone(List<object> values, int frames)
        {
            if (values == null) return;
            for (int i = 0; i < 4 && i < values.Count; i++) _toneTarget[i] = Convert.ToDouble(values[i], CultureInfo.InvariantCulture);
            _toneFrames = Math.Max(0, frames);
            if (_toneFrames == 0) Array.Copy(_toneTarget, _tone, 4);
        }

        public void Flash(int frames) { _flash = Math.Max(1, frames); }

        // ------------------------------------------------------------------ update
        void Update()
        {
            if (_flash > 0) _flash--; // fades over screens too (battles start with a flash)
            if (_scenes.Count > 0)
            {
                if (Message.Busy) Message.Update();
                else _scenes[_scenes.Count - 1].Tick();
                _scenes.RemoveAll(sc => sc.Closed);
                if (Map != null) State.PlayFrames++;
                return;
            }
            if (Map == null) return;
            State.PlayFrames++;
            if (_transfer == null && _fadeAlpha > 0) _fadeAlpha = Math.Max(0, _fadeAlpha - 16);
            if (_transfer != null) { UpdateTransfer(); if (_transfer != null && _transfer.Phase < 2) return; }

            if (_toneFrames > 0)
            {
                for (int i = 0; i < 4; i++) _tone[i] += (_toneTarget[i] - _tone[i]) / _toneFrames;
                _toneFrames--;
            }
            if (_signTimer > 0) _signTimer--;
            Weather.Update(DisplayX, DisplayY, Gfx.Width, Gfx.Height);

            foreach (var e in Map.Events) e.Update();
            Player.Update();

            if (!Interpreter.Running && !Transferring)
            {
                var starter = Map.Events.FirstOrDefault(e => e.Starting && !e.Erased);
                if (starter != null)
                {
                    starter.Starting = false;
                    if (starter.Trigger <= Triggers.EventTouch) starter.Lock();
                    Interpreter.Setup(starter.List, starter.Id);
                }
                else
                {
                    // Autorun common events: run while their switch is on, after map events get their turn
                    var auto = Project.CommonEvents.FirstOrDefault(ce => ce.Trigger == 1 && ce.List.Count > 1 && State.GetSwitch(ce.SwitchId));
                    if (auto != null) Interpreter.Setup(auto.List, 0);
                }
            }
            UpdateParallelCommonEvents();
            Interpreter.Update();
            // Autorun events restart after finishing while their page stays active
            if (!Interpreter.Running) foreach (var e in Map.Events) if (e.Trigger == Triggers.Autorun && !e.Erased && !e.Starting) e.Start();
            Message.Update();
            UpdateCamera();
        }

        // Parallel common events: one interpreter each, alive while the switch is on
        readonly Dictionary<int, Interpreter> _commonParallel = new Dictionary<int, Interpreter>();

        void UpdateParallelCommonEvents()
        {
            foreach (var ce in Project.CommonEvents)
            {
                if (ce.Trigger != 2 || ce.List.Count <= 1) continue;
                Interpreter it;
                bool on = State.GetSwitch(ce.SwitchId);
                if (!on) { _commonParallel.Remove(ce.Id); continue; }
                if (!_commonParallel.TryGetValue(ce.Id, out it)) _commonParallel[ce.Id] = it = Interpreter.ForCommonEvent(this, ce);
                if (!Transferring) it.Update();
            }
        }

        void CenterCamera()
        {
            DisplayX = (int)(Player.RealX / 4) - (Gfx.Width / 2 - 16);
            DisplayY = (int)(Player.RealY / 4) - (Gfx.Height / 2 - 16);
            ClampCamera();
        }

        void UpdateCamera()
        {
            DisplayX = (int)(Player.RealX / 4) - (Gfx.Width / 2 - 16);
            DisplayY = (int)(Player.RealY / 4) - (Gfx.Height / 2 - 16);
            ClampCamera();
        }

        void ClampCamera()
        {
            int mw = Map.Width * 32, mh = Map.Height * 32;
            bool snap = Map.Data.Properties != null && Map.Data.Properties.SnapEdges;
            if (Map.HasConnections && !snap) return;
            if (mw <= Gfx.Width) DisplayX = -(Gfx.Width - mw) / 2;
            else DisplayX = Math.Max(0, Math.Min(DisplayX, mw - Gfx.Width));
            if (mh <= Gfx.Height) DisplayY = -(Gfx.Height - mh) / 2;
            else DisplayY = Math.Max(0, Math.Min(DisplayY, mh - Gfx.Height));
        }

        // ------------------------------------------------------------------ draw
        void Draw()
        {
            Gfx.Clear(Color.Black);
            int firstOpaque = _scenes.FindLastIndex(sc => sc.Opaque);
            if (Map != null && firstOpaque < 0)
            {
                var chars = new List<Character>(Map.Events.Where(e => !e.Erased).Cast<Character>()) { Player };
                _renderer.Draw(Map, DisplayX, DisplayY, chars, FrameCount);
                DrawTone();
                Weather.Draw(Gfx);
                DrawExclaim();
                DrawLocationSign();
            }
            for (int i = Math.Max(0, firstOpaque); i < _scenes.Count; i++) _scenes[i].Draw(Gfx);
            Message.Draw(Gfx);
            if (_flash > 0) Gfx.FillRect(0, 0, Gfx.Width, Gfx.Height, new Color(255, 255, 255, (byte)Math.Min(255, _flash * 12)));
            if (_fadeAlpha > 0) Gfx.FillRect(0, 0, Gfx.Width, Gfx.Height, new Color(0, 0, 0, (byte)_fadeAlpha));
        }

        void DrawTone()
        {
            double r = _tone[0], g = _tone[1], b = _tone[2];
            // Outdoor day/night tint (Essentials-style), applied on top of event tones
            if (Map.Data.Properties != null && Map.Data.Properties.Outdoor)
            {
                int h = DateTime.Now.Hour;
                if (h >= 20 || h < 5) { r -= 60; g -= 60; b -= 10; }
                else if (h >= 18 || h < 7) { r += 10; g -= 20; b -= 40; }
            }
            double dark = -(Math.Min(0, r) + Math.Min(0, g) + Math.Min(0, b)) / 3.0;
            if (dark > 1)
            {
                byte cr = (byte)Math.Max(0, Math.Min(255, 20 + (b < r ? 0 : 30)));
                Gfx.FillRect(0, 0, Gfx.Width, Gfx.Height, new Color(0, 0, cr, (byte)Math.Min(220, dark * 1.2)));
            }
            double light = (Math.Max(0, r) + Math.Max(0, g) + Math.Max(0, b)) / 3.0;
            if (light > 1) Gfx.FillRect(0, 0, Gfx.Width, Gfx.Height, new Color((byte)Math.Min(255, 128 + Math.Max(0, r)), (byte)Math.Min(255, 96 + Math.Max(0, g)), (byte)Math.Min(255, 64 + Math.Max(0, b)), (byte)Math.Min(120, light)));
        }

        void DrawLocationSign()
        {
            if (_signTimer <= 0 || string.IsNullOrEmpty(_signText)) return;
            int w = Math.Max(160, Gfx.TextWidth(_signText) + 40), h = 44;
            int slide = _signTimer > 135 ? (_signTimer - 135) * 4 : (_signTimer < 15 ? (15 - _signTimer) * 4 : 0);
            int y = 4 - slide;
            MessageWindow.DrawBox(Gfx, 4, y, w, h);
            Gfx.DrawText(24, y + 8, _signText, Color.TextBase, Color.TextShadow);
        }

        public void Dispose()
        {
            if (Audio != null) Audio.Dispose();
            if (Gfx != null) Gfx.Dispose();
            SDL.SDL_Quit();
        }
    }

    /// <summary>
    /// Scripted input for automated testing: one command per line —
    /// "wait N", "hold up|down|left|right|action|cancel|run N", "tap action", "shot file.png", "quit".
    /// </summary>
    public class Autotest
    {
        readonly Game _g;
        readonly Queue<string> _cmds;
        int _wait; Button? _held; string _shot;
        public Autotest(Game g, string path) { _g = g; _cmds = new Queue<string>(File.ReadAllLines(path).Where(l => l.Trim().Length > 0 && !l.StartsWith("#"))); }

        static Button Parse(string s)
        {
            switch (s.ToLowerInvariant()) { case "up": return Button.Up; case "down": return Button.Down; case "left": return Button.Left; case "right": return Button.Right; case "action": return Button.Action; case "cancel": return Button.Cancel; case "run": return Button.Run; case "menu": return Button.Menu; }
            return Button.Action;
        }

        public bool Step()
        {
            if (_wait > 0)
            {
                _wait--;
                if (_wait == 0 && _held.HasValue) { Input.Simulate(_held.Value, false); _held = null; _wait = 1; }
                return true;
            }
            if (_cmds.Count == 0) return false;
            var p = _cmds.Dequeue().Trim().Split(' ');
            switch (p[0])
            {
                case "wait": _wait = int.Parse(p[1]); break;
                case "hold": _held = Parse(p[1]); Input.Simulate(_held.Value, true); _wait = p.Length > 2 ? int.Parse(p[2]) : 1; break;
                case "tap": _held = Parse(p[1]); Input.Simulate(_held.Value, true); _wait = 2; break;
                case "shot": _shot = p.Length > 1 ? string.Join(" ", p.Skip(1)) : "shot.png"; break;
                case "give": _g.Compat.RunScript("pbAddPokemonSilent(:" + p[1] + "," + p[2] + ")", null); break;
                case "item": _g.State.AddItem(p[1], p.Length > 2 ? int.Parse(p[2]) : 1); break;
                case "switch": _g.State.SetSwitch(int.Parse(p[1]), p.Length < 3 || p[2] == "on"); if (_g.Map != null) _g.Map.RefreshEvents(); break;
                case "log": if (_g.Map == null) { Console.WriteLine("[autotest] (title screen) scenes=" + _g.SceneOpen); break; } Console.WriteLine("[autotest] map=" + _g.Map.Id + " player=" + _g.Player.X + "," + _g.Player.Y + " dir=" + _g.Player.Direction + " msg=" + _g.MessageBusy + " interp=" + _g.InterpreterRunning); break;
                case "flag": _g.State.Flags.Add(p[1]); break;
                case "moves": // moves partyIndex MOVE1,MOVE2,...
                    {
                        var pk = _g.State.Party[int.Parse(p[1])];
                        pk.Moves.Clear();
                        foreach (var m in p[2].Split(',')) { var md = _g.Project.Moves.FirstOrDefault(x => x.Id == m); pk.Moves.Add(new PokeCreator.Pkmn.PokemonMove(m, md != null ? md.TotalPP : 10)); }
                        break;
                    }
                case "money": Console.WriteLine("[autotest] money=" + _g.State.Money); break;
                case "nearlevel": // nearlevel partyIndex: 1 EXP short of the next level
                    {
                        var pk = _g.State.Party[int.Parse(p[1])];
                        pk.Exp = pk.ExpForNextLevel(_g.Project) - 1;
                        break;
                    }
                case "script": _g.Compat.RunScript(string.Join(" ", p.Skip(1)), null); break;
                case "scene":
                    switch (p[1])
                    {
                        case "menu": _g.PushScene(new PauseMenu(_g)); break;
                        case "bag": _g.PushScene(new BagScene(_g)); break;
                        case "party": _g.PushScene(new PartyScene(_g)); break;
                        case "dex": _g.PushScene(new PokedexScene(_g)); break;
                        case "options": _g.PushScene(new OptionsScene(_g)); break;
                        case "name": _g.AskName("Your name?", "", 10, n => _g.State.PlayerName = n, null); break;
                    }
                    break;
                case "use": // use ITEM partyIndex [moveSlot is chosen in the message]
                    {
                        var it = _g.Project.Items.FirstOrDefault(i => i.Id == p[1]);
                        var pk = _g.State.Party[int.Parse(p[2])];
                        ItemFlow.UseOn(_g, pk, it, PokeCreator.Pkmn.ItemEffects.Info(_g.Project, it), used => Console.WriteLine("[autotest] used=" + used));
                        break;
                    }
                case "fish": World.FieldMoves.Fish(_g, p[1]); break;
                case "bagitem": UI.ItemFlow.UseFromBag(_g, p[1], null); break;
                case "weather": _g.Weather.Set(int.Parse(p[1]), p.Length > 2 ? int.Parse(p[2]) : 9, p.Length > 3 ? int.Parse(p[3]) : 0); break;
                case "ability": _g.State.Party[int.Parse(p[1])].Ability = p[2]; break;
                case "steps": for (int k = int.Parse(p[1]); k > 0; k--) World.DayCare.OnStep(_g); break;
                case "gender": _g.State.Party[int.Parse(p[1])].Gender = p[2]; break;
                case "visit": foreach (var v in p[1].Split(',')) _g.State.Visited.Add(int.Parse(v)); break;
                case "party":
                    foreach (var pk in _g.State.Party)
                        Console.WriteLine("[autotest] " + pk.Name(_g.Project) + " (" + pk.Species + ") Lv" + pk.Level + " HP " + pk.HP + "/" + pk.TotalHP(_g.Project) + " item=" + (pk.Item ?? "-") + " moves=" + string.Join(",", pk.Moves.Select(m => m.Id + ":" + m.PP)));
                    Console.WriteLine("[autotest] bag: " + string.Join(", ", _g.State.Bag.Select(kv => kv.Key + "x" + kv.Value)) + "  seen=" + _g.State.Seen.Count + " caught=" + _g.State.Caught.Count + " repel=" + _g.State.RepelSteps + " name=" + _g.State.PlayerName);
                    break;
                case "quit": return false;
            }
            return true;
        }

        public void AfterDraw()
        {
            if (_shot != null) { _g.Gfx.SaveScreenshot(_shot); Console.WriteLine("[autotest] saved " + _shot); _shot = null; }
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var opt = new GameOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--map" && i + 1 < args.Length) opt.StartMap = int.Parse(args[++i]);
                else if (a == "--x" && i + 1 < args.Length) opt.StartX = int.Parse(args[++i]);
                else if (a == "--y" && i + 1 < args.Length) opt.StartY = int.Parse(args[++i]);
                else if (a == "--dir" && i + 1 < args.Length) opt.StartDir = int.Parse(args[++i]);
                else if (a == "--autotest" && i + 1 < args.Length) opt.AutotestScript = args[++i];
                else if (a == "--scale" && i + 1 < args.Length) opt.Scale = int.Parse(args[++i]);
                else if (a == "--no-encounters") opt.NoEncounters = true;
                else if (a == "--skip-title") opt.SkipTitle = true;
                else if (!a.StartsWith("--")) opt.ProjectDir = a;
            }
            if (opt.ProjectDir == null)
            {
                // Default: a project.json next to Game.exe, or in its parent folder
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (var d in new[] { exeDir, Path.Combine(exeDir, ".."), Directory.GetCurrentDirectory() })
                    if (Project.IsProjectFolder(d)) { opt.ProjectDir = d; break; }
            }
            if (opt.ProjectDir == null || !Project.IsProjectFolder(opt.ProjectDir))
            {
                ShowError("No PokeCreator project found.\n\nRun Game.exe from a project folder, or pass the project folder as the first argument.");
                return 1;
            }
            try
            {
                using (var g = new Game(opt)) g.Run();
                return 0;
            }
            catch (Exception ex)
            {
                ShowError("The game stopped because of an error:\n\n" + ex.Message + "\n\n" + ex.StackTrace);
                return 2;
            }
        }

        static void ShowError(string msg)
        {
            Console.Error.WriteLine(msg);
            try
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "errorlog.txt"), DateTime.Now + "\n" + msg);
                SDL.SDL_ShowSimpleMessageBox(0x10, SDL.U8("PokeCreator"), SDL.U8(msg), IntPtr.Zero);
            }
            catch { }
        }
    }
}
