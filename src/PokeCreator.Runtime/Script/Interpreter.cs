using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Json;
using PokeCreator.Maps;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime.Script
{
    /// <summary>
    /// Runs an event's command list. Command codes match RPG Maker XP so imported events work
    /// unchanged; Essentials "Script" commands are routed to <see cref="EssentialsCompat"/>.
    /// </summary>
    public class Interpreter
    {
        readonly Game _game;
        public int MapId, EventId;
        List<EventCommand> _list;
        int _index;
        int _wait;
        bool _waitForMoves;
        readonly Dictionary<int, int> _branch = new Dictionary<int, int>();
        readonly bool _parallel;
        List<EventCommand> _parallelSource;
        Interpreter _child;          // a called common event runs here until it finishes
        bool _isChild;
        int _depth;
        static readonly HashSet<int> _warned = new HashSet<int>();

        public Interpreter(Game g) { _game = g; }
        public Interpreter(Game g, int mapId, int eventId, List<EventCommand> parallelList) : this(g)
        {
            _parallel = true; _parallelSource = parallelList; MapId = mapId; EventId = eventId;
        }

        public bool Running { get { return _list != null; } }

        /// <summary>Parallel interpreter for a common event: runs on every map while its switch is on.</summary>
        public static Interpreter ForCommonEvent(Game g, CommonEventData ce) { return new Interpreter(g, -1, 0, ce.List); }

        /// <summary>Runs a common event inside this one (Call Common Event); this event waits until it ends.</summary>
        public bool CallCommonEvent(int id)
        {
            var ce = _game.Project.GetCommonEvent(id);
            if (ce == null || ce.List == null || ce.List.Count == 0) { Warn(117, "Call Common Event: common event " + id + " doesn't exist"); return false; }
            if (_depth > 50) { Warn(117, "Call Common Event: common events call each other too deeply"); return false; }
            _child = new Interpreter(_game) { _isChild = true, _depth = _depth + 1 };
            _child.Setup(ce.List, EventId);
            return true;
        }

        /// <summary>Ruby local variables of scripts in this event run (they carry from one Script command to the next, as in Essentials).</summary>
        public readonly Dictionary<string, object> Locals = new Dictionary<string, object>();

        public void Setup(List<EventCommand> list, int eventId)
        {
            Locals.Clear();
            _list = list; _index = 0; EventId = eventId; MapId = _game.Map.Id;
            _branch.Clear(); _wait = 0; _waitForMoves = false; _resumeBattleCondition = false;
        }

        void Finish()
        {
            _child = null;
            if (!_parallel && !_isChild && EventId > 0)
            {
                var ev = GetEvent(EventId);
                if (ev != null) ev.Unlock();
            }
            _list = null;
        }

        GameEvent GetEvent(int id) { return _game.Map.Events.FirstOrDefault(e => e.Id == id); }

        /// <summary>-1 = player, 0 = this event, n = event n on the current map.</summary>
        public Character GetCharacter(int id)
        {
            if (id < 0) return _game.Player;
            return GetEvent(id == 0 ? EventId : id);
        }

        public void Wait(int frames) { _wait = Math.Max(_wait, frames); }
        /// <summary>Waits until every forced move route (player and events) has finished.</summary>
        /// <summary>Weather type of a Set Weather Effects command: a number (Essentials order) or a name like :Rain.</summary>
        static int WeatherType(object o)
        {
            if (o == null) return WeatherSystem.None;
            var str = o as string;
            if (str != null) { int n; return int.TryParse(str, out n) ? n : WeatherSystem.TypeFromName(str); }
            try { return Convert.ToInt32(o); } catch { return WeatherSystem.None; }
        }

        public void WaitForMoves() { _waitForMoves = true; }
        bool _waitForScroll;
        /// <summary>pbScrollMap waits until the camera has finished scrolling.</summary>
        public void WaitForScroll() { _waitForScroll = true; }

        public void Update()
        {
            for (int guard = 0; guard < 500; guard++)
            {
                if (_list == null)
                {
                    if (_parallel && _parallelSource != null && (MapId < 0 || _game.Map.Id == MapId)) { _list = _parallelSource; _index = 0; _branch.Clear(); }
                    else return;
                }
                if (_child != null)
                {
                    _child.Update();
                    if (_child.Running) return;
                    _child = null;
                    continue;
                }
                if (_wait > 0) { _wait--; return; }
                if (_game.MessageBusy || _game.Transferring) return;
                if (_game.SceneOpen) return; // a battle, name entry or other screen opened by the last command
                if (_game.ScriptHolds > 0) return; // a multi-step script call (Battle Tower) is still going
                if (_waitForScroll) { if (_game.Scrolling) return; _waitForScroll = false; }
                if (_waitForMoves)
                {
                    if (_game.Player.MoveRouteForcing || _game.Map.Events.Any(e => e.MoveRouteForcing)) return;
                    _waitForMoves = false;
                }
                if (_index >= _list.Count) { Finish(); if (_parallel) return; return; }
                if (!Execute(_list[_index])) return;
                _index++;
            }
        }

        // ------------------------------------------------------------------ helpers
        EventCommand Cur { get { return _list[_index]; } }
        EventCommand Next { get { return _index + 1 < _list.Count ? _list[_index + 1] : null; } }

        /// <summary>Skips the current block (until the next command at the same indent).</summary>
        bool Skip()
        {
            int indent = Cur.Indent;
            while (_index + 1 < _list.Count && _list[_index + 1].Indent > indent) _index++;
            return true;
        }

        int Operand(int type, int value) { return type == 0 ? value : _game.State.GetVar(value); }

        static bool Compare(int a, int b, int op)
        {
            switch (op) { case 0: return a == b; case 1: return a >= b; case 2: return a <= b; case 3: return a > b; case 4: return a < b; case 5: return a != b; }
            return false;
        }

        void SetFlag(string flag, bool on) { if (on) _game.State.Flags.Add(flag); else _game.State.Flags.Remove(flag); }

        void Warn(int code, string what)
        {
            if (_warned.Add(code)) Console.WriteLine("[events] Command " + code + " (" + what + ") is not supported yet; skipped.");
        }

        // ------------------------------------------------------------------ commands
        bool Execute(EventCommand c)
        {
            switch (c.Code)
            {
                case 0: case 108: case 408: case 412: case 404: case 118: case 112: return true;

                case 101: return ShowText();
                case 102: return ShowChoices(c, null);
                case 402: if (_branch.ContainsKey(c.Indent) && _branch[c.Indent] == c.IntParam(0)) { _branch.Remove(c.Indent); return true; } return Skip();
                case 403: if (_branch.ContainsKey(c.Indent) && _branch[c.Indent] == 4) { _branch.Remove(c.Indent); return true; } return Skip();
                case 106: Wait(c.IntParam(0) * 3); _index++; return false;

                case 111:
                    {
                        _game.BattleJustStarted = false; _game.PromptPending = false;
                        bool result = Condition(c);
                        if (_game.PromptPending) { _game.PromptPending = false; return false; } // asked Yes/No: check again after the answer
                        if (_game.BattleJustStarted)
                        {
                            // The condition started a battle: wait, then check again (the battle call then returns its result)
                            _game.BattleJustStarted = false; _resumeBattleCondition = true;
                            _game.ResumeOwner = this;
                            return false;
                        }
                        if (_resumeBattleCondition && _game.ResumeOwner == this) _game.ResumeOwner = null;
                        _resumeBattleCondition = false;
                        _branch[c.Indent] = result ? 1 : 0;
                        if (result) { _branch.Remove(c.Indent); return true; }
                        return Skip();
                    }
                case 411:
                    {
                        // Else: run only if the matching conditional was false
                        int r; bool hasResult = _branch.TryGetValue(c.Indent, out r);
                        if (hasResult && r == 0) { _branch.Remove(c.Indent); return true; }
                        return Skip();
                    }
                case 413:
                    {
                        int indent = c.Indent;
                        do { _index--; } while (_index > 0 && !(_list[_index].Indent == indent && _list[_index].Code == 112));
                        return true;
                    }
                case 113:
                    {
                        int indent = c.Indent;
                        while (_index + 1 < _list.Count) { _index++; if (_list[_index].Code == 413 && _list[_index].Indent < indent) break; }
                        return true;
                    }
                case 115: _index = _list.Count; return false;
                case 116: { var e = GetEvent(EventId); if (e != null) e.Erase(); return true; }
                case 117: CallCommonEvent(c.IntParam(0)); return true;
                case 119:
                    {
                        string label = c.StrParam(0);
                        for (int i = 0; i < _list.Count; i++) if (_list[i].Code == 118 && _list[i].StrParam(0) == label) { _index = i; break; }
                        return true;
                    }
                case 121:
                    for (int id = c.IntParam(0); id <= c.IntParam(1); id++) _game.State.SetSwitch(id, c.IntParam(2) == 0);
                    _game.Map.RefreshEvents(); return true;
                case 122: ControlVariables(c); _game.Map.RefreshEvents(); return true;
                case 123:
                    if (EventId > 0) { _game.State.SetSelf(MapId, EventId, c.StrParam(0), c.IntParam(1) == 0); _game.Map.RefreshEvents(); }
                    return true;
                case 124: // Control Timer: start (seconds) / stop
                    if (c.IntParam(0) == 0) { _game.State.Extras.TimerOn = true; _game.State.Extras.TimerFrames = c.IntParam(1) * 60; }
                    else _game.State.Extras.TimerOn = false;
                    return true;
                case 125:
                    {
                        int v = Operand(c.IntParam(1), c.IntParam(2));
                        _game.State.Money = Math.Max(0, _game.State.Money + (c.IntParam(0) == 0 ? v : -v)); return true;
                    }

                case 201: return Transfer(c);
                case 202:
                    {
                        var ch = GetCharacter(c.IntParam(0)); if (ch == null) return true;
                        int t = c.IntParam(1);
                        if (t == 0) ch.MoveTo(c.IntParam(2), c.IntParam(3));
                        else if (t == 1) ch.MoveTo(_game.State.GetVar(c.IntParam(2)), _game.State.GetVar(c.IntParam(3)));
                        else { var o = GetCharacter(c.IntParam(2)); if (o != null) { int ox = o.X, oy = o.Y; o.MoveTo(ch.X, ch.Y); ch.MoveTo(ox, oy); } }
                        if (c.IntParam(4) != 0) ch.Turn(c.IntParam(4));
                        return true;
                    }
                case 204: // Change Map Settings: panorama, fog or battleback
                    {
                        int kind = c.IntParam(0);
                        var pl = _game.Planes;
                        if (kind == 0) { pl.Panorama = c.StrParam(1); pl.PanoramaHue = c.IntParam(2); }
                        else if (kind == 1)
                        {
                            pl.Fog = c.StrParam(1); pl.FogHue = c.IntParam(2); pl.FogOpacity = c.IntParam(3); pl.FogBlend = c.IntParam(4);
                            pl.FogZoom = c.IntParam(5) > 0 ? c.IntParam(5) : 100; pl.FogSx = c.IntParam(6); pl.FogSy = c.IntParam(7);
                        }
                        return true;
                    }
                case 205:
                    {
                        // Change Fog Color Tone: [tone, duration]
                        var tone = c.Params.Count > 0 ? c.Params[0] as JsonObject : null;
                        var vals = tone != null ? tone["Values"] as List<object> : null;
                        Func<int, double> v = i => vals != null && i < vals.Count ? Convert.ToDouble(vals[i]) : 0;
                        _game.Planes.ToneFog(v(0), v(1), v(2), v(3), c.IntParam(1) * 3 / 2);
                        return true;
                    }
                case 206: _game.Planes.FadeFog(c.IntParam(0), c.IntParam(1) * 3 / 2); return true; // Change Fog Opacity (duration in RMXP frames at 40 fps)
                case 207:
                    {
                        // Show Animation on a character: Essentials' overworld animations (1 grass, 2 dust, 3 "!")
                        var ch = GetCharacter(c.IntParam(0));
                        // The project's own RPG Maker animations (imported Animations.rxdata) first
                        if (ch != null && _game.Effects.PlayAnimation(c.IntParam(1), ch)) return true;
                        string kind = World.MapEffects.KindForAnimation(c.IntParam(1));
                        if (ch != null && kind != null) { if (kind == "exclaim") _game.Exclaim(ch, 30); else _game.Effects.Play(kind, ch.X, ch.Y); }
                        return true;
                    }
                case 203:
                    if (_game.Scrolling) return false; // a scroll still going: wait for it first
                    _game.ScrollMap(c.IntParam(0), c.IntParam(1), c.IntParam(2)); return true;
                case 221: return true; // Prepare for Transition
                case 222: _game.FadeInFromBlack(); return true; // Execute Transition: a fade in from black
                case 131: _game.State.Extras.Windowskin = c.StrParam(0); _game.LoadFrames(); return true;
                case 236: _game.Weather.Set(WeatherType(c.Params.Count > 0 ? c.Params[0] : null), c.IntParam(1), c.IntParam(2)); return true;
                case 208: _game.Player.Transparent = c.IntParam(0) == 0; return true;
                case 209:
                    {
                        var ch = GetCharacter(c.IntParam(0));
                        var routeObj = c.Params.Count > 1 ? c.Params[1] as JsonObject : null;
                        if (ch != null && routeObj != null) ch.ForceMoveRoute((MoveRoute)Json.Json.ConvertTo(routeObj, typeof(MoveRoute)));
                        return true;
                    }
                case 509: return true; // move-route continuation lines (display only)
                case 210: _waitForMoves = true; _index++; return false;
                case 223:
                    {
                        var tone = c.Params.Count > 0 ? c.Params[0] as JsonObject : null;
                        if (tone != null) _game.SetScreenTone(tone.GetArray("Values"), c.IntParam(1) * 3 / 2);
                        return true;
                    }
                case 224: _game.Flash(c.IntParam(1) * 3 / 2); return true;
                case 225: _game.Shake(c.IntParam(0), c.IntParam(1), c.IntParam(2)); return true;
                case 231:
                    {
                        // Show Picture: number, name, origin, by variables?, x, y, zoom x, zoom y, opacity, blend
                        double x = c.IntParam(3) == 0 ? c.IntParam(4) : _game.State.GetVar(c.IntParam(4));
                        double y = c.IntParam(3) == 0 ? c.IntParam(5) : _game.State.GetVar(c.IntParam(5));
                        _game.Pictures.Get(c.IntParam(0)).Show(c.StrParam(1), c.IntParam(2), x, y, c.IntParam(6), c.IntParam(7), c.IntParam(8), c.IntParam(9));
                        return true;
                    }
                case 232:
                    {
                        // Move Picture: number, duration, origin, by variables?, x, y, zoom x, zoom y, opacity, blend
                        double x = c.IntParam(3) == 0 ? c.IntParam(4) : _game.State.GetVar(c.IntParam(4));
                        double y = c.IntParam(3) == 0 ? c.IntParam(5) : _game.State.GetVar(c.IntParam(5));
                        _game.Pictures.Get(c.IntParam(0)).Move(c.IntParam(1), c.IntParam(2), x, y, c.IntParam(6), c.IntParam(7), c.IntParam(8), c.IntParam(9));
                        return true;
                    }
                case 233: _game.Pictures.Get(c.IntParam(0)).RotateSpeed = c.IntParam(1); return true;
                case 234: // Change Picture Tone: number, tone, duration
                    {
                        var tone = c.Params.Count > 1 ? ToneValues(c.Params[1]) : null;
                        if (tone != null) _game.Pictures.Get(c.IntParam(0)).SetTone(tone, c.IntParam(2) * 3 / 2);
                        return true;
                    }
                case 235: _game.Pictures.Erase(c.IntParam(0)); return true;
                case 103:
                    {
                        // Input Number: variable, digits
                        int v = c.IntParam(0), digits = Math.Max(1, Math.Min(8, c.IntParam(1)));
                        _game.PushScene(new UI.NumberInputScene(_game, digits, _game.State.GetVar(v), n => { _game.State.SetVar(v, n); _game.Map.RefreshEvents(); }));
                        _index++;
                        return false;
                    }
                case 105:
                    {
                        // Button Input Processing: waits for a press and stores its code (2/4/6/8 = directions, 13 = Action, 12 = Cancel, 14 = Menu)
                        int code = Input.Trigger(Button.Down) ? 2 : Input.Trigger(Button.Left) ? 4 : Input.Trigger(Button.Right) ? 6 : Input.Trigger(Button.Up) ? 8 :
                                   Input.Trigger(Button.Action) ? 13 : Input.Trigger(Button.Cancel) ? 12 : Input.Trigger(Button.Menu) ? 14 : 0;
                        if (code == 0) return false;
                        _game.State.SetVar(c.IntParam(0), code); return true;
                    }
                case 132: { var a = c.Params.Count > 0 ? c.Params[0] as JsonObject : null; _game.State.Extras.BattleBgm = a != null ? a.GetString("Name") : null; return true; }
                case 133: { var a = c.Params.Count > 0 ? c.Params[0] as JsonObject : null; _game.State.Extras.BattleEndMe = a != null ? a.GetString("Name") : null; return true; }
                case 134: SetFlag("save_disabled", c.IntParam(0) == 0); return true;
                case 135: SetFlag("menu_disabled", c.IntParam(0) == 0); return true;
                case 136: SetFlag("encounter_disabled", c.IntParam(0) == 0); return true;
                case 303: _game.AskName("Your name?", _game.State.PlayerName, Math.Max(1, c.IntParam(1) > 0 ? c.IntParam(1) : 10), n => { if (!string.IsNullOrEmpty(n)) _game.State.PlayerName = n; }, this); return true;
                case 104: UI.MessageWindow.DefaultPosition = Math.Max(0, Math.Min(2, c.IntParam(0))); UI.MessageWindow.FrameHidden = c.IntParam(1) == 1; return true;

                case 241: { var a = c.Params[0] as JsonObject; if (a != null) _game.Audio.PlayBgm(a.GetString("Name"), a.GetInt("Volume", 100)); return true; }
                case 242: _game.Audio.FadeOutBgm(c.IntParam(0)); return true;
                case 245: { var a = c.Params[0] as JsonObject; if (a != null) _game.Audio.PlayBgs(a.GetString("Name"), a.GetInt("Volume", 100)); return true; }
                case 247: _game.MemorizedBgm = _game.Audio.CurrentBgm; return true;
                case 248: if (!string.IsNullOrEmpty(_game.MemorizedBgm)) _game.Audio.PlayBgm(_game.MemorizedBgm); return true;
                case 246: _game.Audio.FadeOutBgs(c.IntParam(0)); return true;
                case 251: return true;
                case 249: { var a = c.Params[0] as JsonObject; if (a != null) _game.Audio.PlayMe(a.GetString("Name"), a.GetInt("Volume", 100)); return true; }
                case 250: { var a = c.Params[0] as JsonObject; if (a != null) _game.Audio.PlaySe(a.GetString("Name"), a.GetInt("Volume", 100)); return true; }

                case 301: Warn(301, "RPG Maker troop battles aren't used in Pokémon games; use TrainerBattle.start"); return true;
                case 601: case 602: case 603: return BattleBranch(c);
                case 314: foreach (var pk in _game.State.Party) pk.Heal(_game.Project); return true; // Recover All
                case 354: _game.ReturnToTitle(); return false;
                case 351: _game.PushScene(new UI.PauseMenu(_game)); return true;   // Call Menu Screen
                case 352: return RunCall("pbSaveScreen");                        // Call Save Screen
                case 353: return RunCall("pbStartOver");                         // Game Over: back to the last Pokémon Center

                case 355: return RunScript();
                case 655: return true;

                default:
                    Warn(c.Code, "unknown");
                    return true;
            }
        }

        bool _resumeBattleCondition;

        /// <summary>An RPG Maker Tone parameter ({"_class":"Tone","Values":[r,g,b,gray]}) as numbers.</summary>
        static double[] ToneValues(object p)
        {
            var jo = p as JsonObject;
            var vals = jo != null ? jo.GetArray("Values") : p as List<object>;
            if (vals == null) return null;
            var r = new double[4];
            for (int i = 0; i < 4 && i < vals.Count; i++) r[i] = Convert.ToDouble(vals[i], System.Globalization.CultureInfo.InvariantCulture);
            return r;
        }

        bool BattleBranch(EventCommand c)
        {
            // RPG Maker's If Win / If Escape / If Lose after a battle
            var o = _game.LastOutcome;
            bool match = c.Code == 601 ? o == PokeCreator.Battles.BattleOutcome.Win || o == PokeCreator.Battles.BattleOutcome.Caught
                       : c.Code == 602 ? o == PokeCreator.Battles.BattleOutcome.Escaped
                       : o == PokeCreator.Battles.BattleOutcome.Loss;
            if (match) return true;
            return Skip();
        }

        bool ShowText()
        {
            string text = Cur.StrParam(0);
            while (Next != null && Next.Code == 401)
            {
                _index++;
                string t = Cur.StrParam(0);
                if (t.Length > 0 && text.Length > 0 && !text.EndsWith(" ")) text += " ";
                text += t;
            }
            // Message followed directly by Show Choices: show them together
            if (Next != null && Next.Code == 102)
            {
                _index++;
                return ShowChoices(Cur, text);
            }
            _game.Message.Show(text);
            return true;
        }

        /// <summary>Set by rename_choice(n, name) / hide_choice(n) for the next Show Choices (1-based).</summary>
        public readonly Dictionary<int, string> ChoiceRenames = new Dictionary<int, string>();
        public readonly HashSet<int> ChoiceHidden = new HashSet<int>();

        bool ShowChoices(EventCommand c, string text)
        {
            var raw = c.Params.Count > 0 ? c.Params[0] as List<object> : null;
            var choices = raw == null ? new List<string>() : raw.Select(o => Convert.ToString(o)).ToList();
            int cancel = c.IntParam(1); // RMXP: 0 = disallow, 1-4 = choice n, 5 = branch
            int indent = c.Indent;
            int cancelIndex = cancel == 0 ? -1 : (cancel == 5 ? 4 : cancel - 1);
            if (ChoiceRenames.Count == 0 && ChoiceHidden.Count == 0)
            {
                _game.Message.Show(text ?? "", choices, cancelIndex, r => _branch[indent] = r);
                return true;
            }
            // rename_choice / hide_choice (Essentials) change this Show Choices only
            var map = new List<int>(); var shown = new List<string>();
            for (int k = 0; k < choices.Count; k++)
            {
                if (ChoiceHidden.Contains(k + 1)) continue;
                string nm; map.Add(k); shown.Add(ChoiceRenames.TryGetValue(k + 1, out nm) ? nm : choices[k]);
            }
            ChoiceRenames.Clear(); ChoiceHidden.Clear();
            int shownCancel = cancelIndex == 4 ? -2 : map.IndexOf(cancelIndex);
            if (cancelIndex >= 0 && cancelIndex < 4 && shownCancel < 0) shownCancel = -1;
            _game.Message.Show(text ?? "", shown, shownCancel == -2 ? 4 : shownCancel, r => _branch[indent] = r >= 0 && r < map.Count ? map[r] : (cancelIndex == 4 ? 4 : r));
            return true;
        }

        bool Condition(EventCommand c)
        {
            var s = _game.State;
            switch (c.IntParam(0))
            {
                case 0: return _game.SwitchOn(c.IntParam(1), MapId, EventId) == (c.IntParam(2) == 0);
                case 1:
                    {
                        int a = s.GetVar(c.IntParam(1));
                        int b = Operand(c.IntParam(2), c.IntParam(3));
                        return Compare(a, b, c.IntParam(4));
                    }
                case 2: return EventId > 0 && s.GetSelf(MapId, EventId, c.StrParam(1)) == (c.IntParam(2) == 0);
                case 6: { var ch = GetCharacter(c.IntParam(1)); return ch != null && ch.Direction == c.IntParam(2); }
                case 7: return c.IntParam(2) == 0 ? s.Money >= c.IntParam(1) : s.Money <= c.IntParam(1);
                case 3: // Timer: seconds left >= / <= value
                    {
                        if (!s.Extras.TimerOn) return false;
                        int secs = s.Extras.TimerFrames / 60;
                        return c.IntParam(2) == 0 ? secs >= c.IntParam(1) : secs <= c.IntParam(1);
                    }
                case 11:
                    {
                        int b = c.IntParam(1);
                        return (b == 2 && Input.Press(Button.Down)) || (b == 4 && Input.Press(Button.Left)) || (b == 6 && Input.Press(Button.Right)) ||
                               (b == 8 && Input.Press(Button.Up)) || (b == 13 && Input.Press(Button.Action)) || (b == 12 && Input.Press(Button.Cancel));
                    }
                case 12: return _game.Compat.Evaluate(c.StrParam(1), this);
            }
            return false;
        }

        void ControlVariables(EventCommand c)
        {
            int op = c.IntParam(2), type = c.IntParam(3);
            int value;
            switch (type)
            {
                case 0: value = c.IntParam(4); break;
                case 1: value = _game.State.GetVar(c.IntParam(4)); break;
                case 2: value = _game.Rng.Next(c.IntParam(4), c.IntParam(5) + 1); break;
                case 6:
                    {
                        var ch = GetCharacter(c.IntParam(4)); value = 0;
                        if (ch != null) switch (c.IntParam(5)) { case 0: value = ch.X; break; case 1: value = ch.Y; break; case 2: value = ch.Direction; break; }
                        break;
                    }
                case 7:
                    switch (c.IntParam(4))
                    {
                        case 0: value = _game.Map.Id; break;
                        case 1: value = _game.State.Party.Count; break;
                        case 2: value = _game.State.Money; break;
                        case 3: value = _game.State.Steps; break;
                        case 4: value = (int)(_game.State.PlayFrames / 60); break;
                        case 5: value = _game.State.Extras.TimerOn ? _game.State.Extras.TimerFrames / 60 : 0; break;
                        case 6: value = World.GameStats.Int(_game, "save_count"); break;
                        default: value = 0; break;
                    }
                    break;
                default: value = 0; break;
            }
            for (int id = c.IntParam(0); id <= c.IntParam(1); id++)
            {
                int v = _game.State.GetVar(id);
                switch (op)
                {
                    case 0: v = value; break;
                    case 1: v += value; break;
                    case 2: v -= value; break;
                    case 3: v *= value; break;
                    case 4: if (value != 0) v /= value; break;
                    case 5: if (value != 0) v %= value; break;
                }
                _game.State.SetVar(id, v);
            }
        }

        bool Transfer(EventCommand c)
        {
            if (_game.Transferring) return false;
            int map, x, y;
            if (c.IntParam(0) == 0) { map = c.IntParam(1); x = c.IntParam(2); y = c.IntParam(3); }
            else { map = _game.State.GetVar(c.IntParam(1)); x = _game.State.GetVar(c.IntParam(2)); y = _game.State.GetVar(c.IntParam(3)); }
            _game.RequestTransfer(map, x, y, c.IntParam(4), c.IntParam(5) == 0);
            _index++;
            return false;
        }

        /// <summary>Runs one Essentials call as if it were a Script command (waits for its questions).</summary>
        bool RunCall(string script)
        {
            _game.PromptPending = false;
            _game.Compat.RunScript(script, this);
            if (_game.PromptPending) { _game.PromptPending = false; return false; }
            return true;
        }

        bool RunScript()
        {
            int start = _index;
            string script = Cur.StrParam(0);
            while (Next != null && Next.Code == 655) { _index++; script += "\n" + Cur.StrParam(0); }
            _game.PromptPending = false;
            _game.Compat.RunScript(script, this);
            // A call asked the player something (pbEnterNPCName...): run the script again once it's answered
            if (_game.PromptPending) { _game.PromptPending = false; _index = start; return false; }
            return true;
        }
    }
}
