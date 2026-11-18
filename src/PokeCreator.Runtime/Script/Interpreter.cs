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
        static readonly HashSet<int> _warned = new HashSet<int>();

        public Interpreter(Game g) { _game = g; }
        public Interpreter(Game g, int mapId, int eventId, List<EventCommand> parallelList) : this(g)
        {
            _parallel = true; _parallelSource = parallelList; MapId = mapId; EventId = eventId;
        }

        public bool Running { get { return _list != null; } }

        public void Setup(List<EventCommand> list, int eventId)
        {
            _list = list; _index = 0; EventId = eventId; MapId = _game.Map.Id;
            _branch.Clear(); _wait = 0; _waitForMoves = false;
        }

        void Finish()
        {
            if (!_parallel && EventId > 0)
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

        public void Update()
        {
            for (int guard = 0; guard < 500; guard++)
            {
                if (_list == null)
                {
                    if (_parallel && _parallelSource != null && _game.Map.Id == MapId) { _list = _parallelSource; _index = 0; _branch.Clear(); }
                    else return;
                }
                if (_wait > 0) { _wait--; return; }
                if (_game.MessageBusy || _game.Transferring) return;
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
                case 103: _game.State.SetVar(c.IntParam(0), 0); Warn(103, "Input Number"); return true;
                case 106: Wait(c.IntParam(0) * 3); _index++; return false;

                case 111:
                    {
                        bool result = Condition(c);
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
                case 117: Warn(117, "Call Common Event"); return true;
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
                case 124: Warn(124, "Timer"); return true;
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
                case 203: case 204: case 205: case 206: case 207: case 221: case 222: case 236: return true;
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
                case 225: return true;
                case 231: case 232: case 233: case 234: case 235: Warn(231, "Pictures"); return true;

                case 241: { var a = c.Params[0] as JsonObject; if (a != null) _game.Audio.PlayBgm(a.GetString("Name"), a.GetInt("Volume", 100)); return true; }
                case 242: _game.Audio.FadeOutBgm(c.IntParam(0)); return true;
                case 245: { var a = c.Params[0] as JsonObject; if (a != null) _game.Audio.PlayBgs(a.GetString("Name"), a.GetInt("Volume", 100)); return true; }
                case 246: case 247: case 248: case 251: return true;
                case 249: { var a = c.Params[0] as JsonObject; if (a != null) _game.Audio.PlayMe(a.GetString("Name"), a.GetInt("Volume", 100)); return true; }
                case 250: { var a = c.Params[0] as JsonObject; if (a != null) _game.Audio.PlaySe(a.GetString("Name"), a.GetInt("Volume", 100)); return true; }

                case 301: _game.StartPlaceholderBattle("a trainer"); _branch[c.Indent] = 0; return true;
                case 601: case 602: case 603: return BattleBranch(c);
                case 314: foreach (var pk in _game.State.Party) pk.Heal(_game.Project); return true; // Recover All
                case 351: case 352: case 353: case 354: return true;

                case 355: return RunScript();
                case 655: return true;

                default:
                    Warn(c.Code, "unknown");
                    return true;
            }
        }

        bool BattleBranch(EventCommand c)
        {
            // Battle placeholder always counts as a win
            if (c.Code == 601) return true;
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

        bool ShowChoices(EventCommand c, string text)
        {
            var raw = c.Params.Count > 0 ? c.Params[0] as List<object> : null;
            var choices = raw == null ? new List<string>() : raw.Select(o => Convert.ToString(o)).ToList();
            int cancel = c.IntParam(1); // RMXP: 0 = disallow, 1-4 = choice n, 5 = branch
            int indent = c.Indent;
            int cancelIndex = cancel == 0 ? -1 : (cancel == 5 ? 4 : cancel - 1);
            _game.Message.Show(text ?? "", choices, cancelIndex, r => _branch[indent] = r);
            return true;
        }

        bool Condition(EventCommand c)
        {
            var s = _game.State;
            switch (c.IntParam(0))
            {
                case 0: return s.GetSwitch(c.IntParam(1)) == (c.IntParam(2) == 0);
                case 1:
                    {
                        int a = s.GetVar(c.IntParam(1));
                        int b = Operand(c.IntParam(2), c.IntParam(3));
                        return Compare(a, b, c.IntParam(4));
                    }
                case 2: return EventId > 0 && s.GetSelf(MapId, EventId, c.StrParam(1)) == (c.IntParam(2) == 0);
                case 6: { var ch = GetCharacter(c.IntParam(1)); return ch != null && ch.Direction == c.IntParam(2); }
                case 7: return c.IntParam(2) == 0 ? s.Money >= c.IntParam(1) : s.Money <= c.IntParam(1);
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
                    switch (c.IntParam(4)) { case 0: value = _game.Map.Id; break; case 2: value = _game.State.Money; break; case 3: value = _game.State.Steps; break; default: value = 0; break; }
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

        bool RunScript()
        {
            string script = Cur.StrParam(0);
            while (Next != null && Next.Code == 655) { _index++; script += "\n" + Cur.StrParam(0); }
            _game.Compat.RunScript(script, this);
            return true;
        }
    }
}
