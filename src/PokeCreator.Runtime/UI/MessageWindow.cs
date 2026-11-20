using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.UI
{
    /// <summary>
    /// Bottom-of-screen message box with typewriter text, paging, choice lists and Essentials text codes:
    /// \PN, \v[n], \n, \b / \r / \c[n] colours, &lt;c2=..&gt; / &lt;c3=..&gt; colours, \. \| \wt[n] pauses, \! wait for a key,
    /// \^ and \wtnp[n] (close without a key), \ts[n] text speed, \se[..] / \me[..] sounds, \G money window,
    /// \wu / \wm / \wd window position, \l[n] lines, \xn[name] speaker name, &lt;ac&gt; / &lt;ar&gt; alignment.
    /// </summary>
    public class MessageWindow
    {
        enum TokKind { Char, Pause, WaitKey, Se, Me, Speed, AutoClose }

        class Token
        {
            public TokKind Kind;
            public char C;
            public Color Base, Shadow;
            public int Arg;
            public string Text;
        }

        class Line { public List<Token> Tokens = new List<Token>(); public int Align; } // 0 left, 1 centre, 2 right

        readonly Game _game;
        List<Line> _lines = new List<Line>();
        int _lineIndex;            // first line of the current page
        int _revealed;             // tokens revealed on the current page
        bool _pageDone;
        List<string> _choices;
        int _cancelIndex = -1;
        int _cursor;
        Action<int> _onChoice;
        int _blink;

        // Per-message state from text codes
        int _linesPerPage = 2;
        int _waitFrames;           // pause in progress
        bool _waitingKey;          // \! reached
        int _framesPerChar;        // \ts[n]: 0 = use CharsPerFrame
        int _charTimer;
        bool _noKeyAtEnd;          // \^
        int _autoClose = -1;       // \wtnp[n]: frames before closing by itself
        int _position = 2;         // 0 top, 1 middle, 2 bottom
        bool _showGold;
        string _speaker;

        const int Margin = 4, PadX = 22, PadY = 14;

        public bool Busy { get; private set; }
        /// <summary>Characters revealed per frame (Options: Slow 1, Normal 2, Fast 4, Instant 999).</summary>
        public int CharsPerFrame = 2;
        /// <summary>Autotest: print every message to the console.</summary>
        public static bool Echo;
        public int LastChoice { get; private set; }

        public MessageWindow(Game g) { _game = g; }

        /// <summary>Shows a message. If choices are given, the result is passed to onChoice (cancel = cancelIndex).</summary>
        public void Show(string text, List<string> choices = null, int cancelIndex = -1, Action<int> onChoice = null)
        {
            text = text ?? "";
            if (Echo) Console.WriteLine("[msg] " + Expand(text).Replace("\n", " / "));
            _linesPerPage = 2; _framesPerChar = 0; _noKeyAtEnd = false; _autoClose = -1; _position = 2; _showGold = false; _speaker = null;
            _waitFrames = 0; _waitingKey = false; _charTimer = 0;
            var tokens = Parse(text);
            int width = _game.Gfx.Width - Margin * 2 - PadX * 2;
            _lines = Wrap(tokens, width);
            if (_lines.Count == 0) _lines.Add(new Line());
            _lineIndex = 0; _revealed = 0; _pageDone = false;
            _choices = choices != null && choices.Count > 0 ? choices : null;
            _cancelIndex = cancelIndex; _cursor = 0; _onChoice = onChoice;
            Busy = true;
            RunCommandsAtStart();
        }

        /// <summary>Plain text: replaces \PN, \v[n], \n and removes every formatting code (for signs, choices, logs).</summary>
        public string Expand(string s)
        {
            s = Variables(s ?? "");
            s = Regex.Replace(s, @"\\xn\[[^\]]*\]", "");
            s = Regex.Replace(s, @"\\[a-zA-Z]+\[[^\]]*\]", "");                    // codes with arguments: \c[1], \se[..], \wtnp[..]
            s = Regex.Replace(s, @"\\(?:op|cl|wu|wd|wm|pm|pg|cn|CN|[brigG])", "");      // argument-less codes
            s = Regex.Replace(s, @"<[^>]+>", "");                                // <ac>, <b> style tags
            s = s.Replace("\\.", "").Replace("\\|", "").Replace("\\!", "").Replace("\\^", "").Replace("\\\\", "\\");
            return s;
        }

        string Variables(string s)
        {
            s = s.Replace("\\PN", _game.State.PlayerName).Replace("\\pn", _game.State.PlayerName);
            s = Regex.Replace(s, @"\\[vV]\[(\d+)\]", m => _game.State.VarText(int.Parse(m.Groups[1].Value)));
            return s.Replace("\\n", "\n").Replace("\\N", "\n");
        }

        // ------------------------------------------------------------------ parsing
        static readonly Color[] CodeBase =
        {
            Color.TextBase, new Color(0, 112, 248), new Color(232, 32, 16), new Color(96, 176, 72), new Color(72, 168, 216),
            new Color(208, 56, 184), new Color(232, 208, 32), new Color(160, 160, 168), new Color(248, 248, 248)
        };
        static readonly Color[] CodeShadow =
        {
            Color.TextShadow, new Color(120, 184, 232), new Color(248, 168, 184), new Color(176, 208, 144), new Color(168, 224, 224),
            new Color(232, 160, 224), new Color(248, 232, 136), new Color(208, 208, 216), new Color(160, 160, 168)
        };

        static Color Hex(string h)
        {
            h = h.Trim().TrimStart('#');
            int v; if (!int.TryParse(h.Length >= 6 ? h.Substring(0, 6) : h, NumberStyles.HexNumber, null, out v)) return Color.TextBase;
            return new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        /// <summary>15-bit colour as Essentials writes them in &lt;c2=BBBBSSSS&gt;.</summary>
        static Color Rgb15(string h)
        {
            int v; if (!int.TryParse(h, NumberStyles.HexNumber, null, out v)) return Color.TextBase;
            return new Color((byte)((v & 0x1F) << 3), (byte)(((v >> 5) & 0x1F) << 3), (byte)(((v >> 10) & 0x1F) << 3));
        }

        List<Token> Parse(string text)
        {
            string s = Variables(text);
            var list = new List<Token>();
            Color b = Color.TextBase, sh = Color.TextShadow;
            int i = 0;
            Func<string, int, string> argAt = (str, pos) =>
            {
                if (pos >= str.Length || str[pos] != '[') return null;
                int end = str.IndexOf(']', pos);
                return end < 0 ? null : str.Substring(pos + 1, end - pos - 1);
            };
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    // Longest code names first
                    var m = Regex.Match(s.Substring(i + 1), @"^(wtnp|wt|ts|se|me|xn|dxn|fs|fn|ff|op|cl|wu|wd|wm|pm|pg|CN|cn|l|c|w|[brigG.|!^\\])");
                    if (m.Success)
                    {
                        string code = m.Groups[1].Value;
                        int after = i + 1 + code.Length;
                        string arg = argAt(s, after);
                        int next = arg != null ? after + arg.Length + 2 : after;
                        switch (code)
                        {
                            case "b": b = CodeBase[1]; sh = CodeShadow[1]; break;
                            case "r": b = CodeBase[2]; sh = CodeShadow[2]; break;
                            case "c":
                                {
                                    int n; if (arg != null && int.TryParse(arg, out n)) { n = Math.Max(0, Math.Min(CodeBase.Length - 1, n)); b = CodeBase[n]; sh = CodeShadow[n]; }
                                    break;
                                }
                            case ".": list.Add(new Token { Kind = TokKind.Pause, Arg = 15 }); break;
                            case "|": list.Add(new Token { Kind = TokKind.Pause, Arg = 60 }); break;
                            case "wt": { int n; if (arg != null && int.TryParse(arg, out n)) list.Add(new Token { Kind = TokKind.Pause, Arg = n * 3 }); break; }
                            case "wtnp": { int n; if (arg != null && int.TryParse(arg, out n)) list.Add(new Token { Kind = TokKind.AutoClose, Arg = n * 3 }); break; }
                            case "!": list.Add(new Token { Kind = TokKind.WaitKey }); break;
                            case "^": _noKeyAtEnd = true; break;
                            case "ts": { int n; list.Add(new Token { Kind = TokKind.Speed, Arg = arg != null && int.TryParse(arg, out n) ? n : -1 }); break; }
                            case "se": if (!string.IsNullOrEmpty(arg)) list.Add(new Token { Kind = TokKind.Se, Text = arg }); break;
                            case "me": if (!string.IsNullOrEmpty(arg)) list.Add(new Token { Kind = TokKind.Me, Text = arg }); break;
                            case "G": _showGold = true; break;
                            case "wu": _position = 0; break;
                            case "wm": _position = 1; break;
                            case "wd": _position = 2; break;
                            case "l": { int n; if (arg != null && int.TryParse(arg, out n)) _linesPerPage = Math.Max(1, Math.Min(4, n)); break; }
                            case "xn": case "dxn": _speaker = arg; break;
                            case "\\": list.Add(new Token { Kind = TokKind.Char, C = '\\', Base = b, Shadow = sh }); break;
                            // \i, \g, \op, \cl, \pm, \pg, \cn, \w[..], \fs[..], \fn[..], \ff[..]: accepted, no effect yet
                        }
                        i = next;
                        continue;
                    }
                }
                if (c == '<')
                {
                    int end = s.IndexOf('>', i);
                    if (end > i)
                    {
                        string tag = s.Substring(i + 1, end - i - 1);
                        var tm = Regex.Match(tag, @"^(/?)(c2|c3|ac|ar|al|b|i|u|o|outln|outln2|br|fs|fn|icon|r)(?:=(.*))?$", RegexOptions.IgnoreCase);
                        if (tm.Success)
                        {
                            bool close = tm.Groups[1].Value == "/";
                            string name = tm.Groups[2].Value.ToLowerInvariant(), val = tm.Groups[3].Value;
                            if (name == "c3" && !close) { var p = val.Split(','); b = Hex(p[0]); if (p.Length > 1) sh = Hex(p[1]); }
                            else if (name == "c2" && !close && val.Length >= 8) { b = Rgb15(val.Substring(0, 4)); sh = Rgb15(val.Substring(4, 4)); }
                            else if ((name == "c2" || name == "c3") && close) { b = Color.TextBase; sh = Color.TextShadow; }
                            else if (name == "br") list.Add(new Token { Kind = TokKind.Char, C = '\n', Base = b, Shadow = sh });
                            else if (name == "ac" || name == "ar" || name == "al") list.Add(new Token { Kind = TokKind.Char, C = close ? '\u0010' : name == "ac" ? '\u0011' : name == "ar" ? '\u0012' : '\u0010', Base = b, Shadow = sh });
                            i = end + 1;
                            continue;
                        }
                    }
                }
                list.Add(new Token { Kind = TokKind.Char, C = c, Base = b, Shadow = sh });
                i++;
            }
            return list;
        }

        static string Str(IEnumerable<Token> toks) { return new string(toks.Where(t => t.Kind == TokKind.Char).Select(t => t.C).ToArray()); }

        /// <summary>Word-wraps tokens into lines (commands stay attached to the text around them).</summary>
        List<Line> Wrap(List<Token> tokens, int width)
        {
            var lines = new List<Line>();
            var line = new Line();
            var word = new List<Token>();
            int align = 0;
            Action flushWord = () =>
            {
                if (word.Count == 0) return;
                string trial = Str(line.Tokens) + Str(word);
                if (line.Tokens.Any(t => t.Kind == TokKind.Char) && _game.Gfx.TextWidth(trial.TrimEnd()) > width)
                {
                    // Drop trailing spaces of the full line
                    while (line.Tokens.Count > 0 && line.Tokens[line.Tokens.Count - 1].Kind == TokKind.Char && line.Tokens[line.Tokens.Count - 1].C == ' ') line.Tokens.RemoveAt(line.Tokens.Count - 1);
                    lines.Add(line); line = new Line { Align = align };
                    while (word.Count > 0 && word[0].Kind == TokKind.Char && word[0].C == ' ') word.RemoveAt(0);
                }
                line.Tokens.AddRange(word); word.Clear();
            };
            line.Align = align;
            foreach (var t in tokens)
            {
                if (t.Kind == TokKind.Char && (t.C == '\u0010' || t.C == '\u0011' || t.C == '\u0012'))
                {
                    align = t.C == '\u0011' ? 1 : t.C == '\u0012' ? 2 : 0;
                    if (!line.Tokens.Any(x => x.Kind == TokKind.Char) && word.Count == 0) line.Align = align;
                    continue;
                }
                if (t.Kind == TokKind.Char && t.C == '\n') { flushWord(); lines.Add(line); line = new Line { Align = align }; continue; }
                if (t.Kind == TokKind.Char && t.C == ' ') { flushWord(); word.Add(t); continue; }
                word.Add(t);
            }
            flushWord();
            lines.Add(line);
            return lines;
        }

        // ------------------------------------------------------------------ reveal
        IEnumerable<Token> PageTokens()
        {
            for (int i = _lineIndex; i < Math.Min(_lines.Count, _lineIndex + _linesPerPage); i++)
                foreach (var t in _lines[i].Tokens) yield return t;
        }

        int PageTokenCount() { return PageTokens().Count(); }

        bool LastPage { get { return _lineIndex + _linesPerPage >= _lines.Count; } }

        /// <summary>Commands before the first character (\ts, \se at the very start) run straight away.</summary>
        void RunCommandsAtStart()
        {
            var toks = PageTokens().ToList();
            while (_revealed < toks.Count && toks[_revealed].Kind != TokKind.Char && toks[_revealed].Kind != TokKind.Pause && toks[_revealed].Kind != TokKind.WaitKey)
                Execute(toks[_revealed++]);
        }

        void Execute(Token t)
        {
            switch (t.Kind)
            {
                case TokKind.Pause: _waitFrames = t.Arg; break;
                case TokKind.WaitKey: _waitingKey = true; break;
                case TokKind.Se: _game.Audio.PlaySe(t.Text); break;
                case TokKind.Me: _game.Audio.PlayMe(t.Text); break;
                case TokKind.Speed: _framesPerChar = t.Arg < 0 ? 0 : t.Arg; break;
                case TokKind.AutoClose: _autoClose = t.Arg; break;
            }
        }

        /// <summary>Reveals up to n characters, running commands on the way; stops at pauses and \!.</summary>
        void Reveal(int n, bool skipping)
        {
            var toks = PageTokens().ToList();
            while (_revealed < toks.Count)
            {
                var t = toks[_revealed];
                if (t.Kind == TokKind.Char)
                {
                    if (n <= 0) break;
                    n--; _revealed++;
                    continue;
                }
                _revealed++;
                if (skipping && t.Kind == TokKind.Pause) continue; // A skips pauses
                Execute(t);
                if (_waitFrames > 0 || _waitingKey) break;
            }
            if (_revealed >= toks.Count) _pageDone = true;
        }

        public void Update()
        {
            if (!Busy) return;
            _blink++;
            if (_waitingKey)
            {
                if (Input.Trigger(Button.Action) || Input.Trigger(Button.Cancel)) _waitingKey = false;
                return;
            }
            if (!_pageDone)
            {
                if (_waitFrames > 0) { _waitFrames--; if (!Input.Trigger(Button.Action)) return; _waitFrames = 0; }
                if (Input.Trigger(Button.Action) && _revealed > 2) { Reveal(int.MaxValue, true); return; }
                if (_framesPerChar > 0) { if (++_charTimer >= _framesPerChar) { _charTimer = 0; Reveal(1, false); } }
                else Reveal(_framesPerChar == 0 && CharsPerFrame >= 999 ? int.MaxValue : Math.Max(1, CharsPerFrame), false);
                return;
            }
            if (_autoClose >= 0)
            {
                if (_autoClose-- <= 0 && LastPage) { Finish(0); return; }
                if (_autoClose < 0 && !LastPage) { NextPage(); return; }
                return;
            }
            if (LastPage && _choices != null)
            {
                if (Input.Repeat(Button.Down)) { _cursor = (_cursor + 1) % _choices.Count; _game.Audio.PlaySe("GUI sel cursor"); }
                if (Input.Repeat(Button.Up)) { _cursor = (_cursor + _choices.Count - 1) % _choices.Count; _game.Audio.PlaySe("GUI sel cursor"); }
                if (Input.Trigger(Button.Action)) { Finish(_cursor); _game.Audio.PlaySe("GUI sel decision"); }
                else if (Input.Trigger(Button.Cancel) && _cancelIndex >= 0) { Finish(_cancelIndex); _game.Audio.PlaySe("GUI sel cancel"); }
                return;
            }
            if (LastPage && _noKeyAtEnd) { Finish(0); return; }
            if (Input.Trigger(Button.Action) || Input.Trigger(Button.Cancel))
            {
                if (LastPage) Finish(0);
                else NextPage();
            }
        }

        void NextPage()
        {
            _lineIndex += _linesPerPage; _revealed = 0; _pageDone = false; _autoClose = -1;
            RunCommandsAtStart();
        }

        void Finish(int choice)
        {
            Busy = false;
            LastChoice = choice;
            var cb = _onChoice; _onChoice = null;
            if (cb != null) cb(choice);
        }

        // ------------------------------------------------------------------ drawing
        /// <summary>Frames from Game Settings (Speech frame / Menu frame); null = the built-in box.</summary>
        public static WindowSkin SpeechSkin, MenuSkin;

        /// <summary>Message box frame.</summary>
        public static void DrawSpeechBox(Gfx g, int x, int y, int w, int h)
        {
            if (SpeechSkin != null) SpeechSkin.Draw(g, x, y, w, h);
            else DrawPlainBox(g, x, y, w, h);
        }

        /// <summary>Menu / choice / sign frame.</summary>
        public static void DrawBox(Gfx g, int x, int y, int w, int h)
        {
            if (MenuSkin != null) MenuSkin.Draw(g, x, y, w, h);
            else DrawPlainBox(g, x, y, w, h);
        }

        public static void DrawPlainBox(Gfx g, int x, int y, int w, int h)
        {
            g.FillRect(x, y, w, h, new Color(72, 72, 80));
            g.FillRect(x + 2, y + 2, w - 4, h - 4, new Color(248, 248, 248));
            g.FillRect(x + 4, y + 4, w - 8, h - 8, new Color(120, 168, 208));
            g.FillRect(x + 6, y + 6, w - 12, h - 12, new Color(248, 248, 248));
        }

        int LineH(Gfx g) { return Math.Max(30, g.LineHeight); }
        int BoxHeight(Gfx g) { return _linesPerPage * LineH(g) + PadY * 2 + 8; }

        public void Draw(Gfx g)
        {
            if (!Busy) return;
            int bh = BoxHeight(g);
            int bx = Margin, bw = g.Width - Margin * 2;
            int by = _position == 0 ? Margin : _position == 1 ? (g.Height - bh) / 2 : g.Height - bh - Margin;
            DrawSpeechBox(g, bx, by, bw, bh);
            if (!string.IsNullOrEmpty(_speaker))
            {
                string sp = Expand(_speaker);
                int nw = g.TextWidth(sp) + 36, nh = LineH(g) + 16;
                int ny = _position == 0 ? by + bh + 2 : by - nh - 2;
                DrawBox(g, bx, ny, nw, nh);
                g.DrawText(bx + 18, ny + 8, sp, Color.TextBase, Color.TextShadow);
            }
            if (_showGold)
            {
                string money = "$" + _game.State.Money.ToString("N0", CultureInfo.InvariantCulture);
                int gw = Math.Max(150, g.TextWidth(money) + 40), gh = LineH(g) * 2 + 20;
                DrawBox(g, g.Width - gw - Margin, Margin, gw, gh);
                g.DrawText(g.Width - gw - Margin + 16, Margin + 10, "Money:", Color.TextBase, Color.TextShadow);
                g.DrawText(g.Width - Margin - 16 - g.TextWidth(money), Margin + 10 + LineH(g), money, Color.TextBase, Color.TextShadow);
            }
            int remaining = _revealed;
            int lh = LineH(g);
            int innerW = bw - PadX * 2;
            for (int i = 0; i < _linesPerPage && _lineIndex + i < _lines.Count; i++)
            {
                var line = _lines[_lineIndex + i];
                if (remaining <= 0) break;
                int count = Math.Min(remaining, line.Tokens.Count);
                remaining -= line.Tokens.Count;
                var shown = line.Tokens.Take(count).Where(t => t.Kind == TokKind.Char).ToList();
                int x = bx + PadX, y = by + PadY + i * lh;
                if (line.Align != 0)
                {
                    int full = g.TextWidth(Str(line.Tokens));
                    x += line.Align == 1 ? (innerW - full) / 2 : innerW - full;
                }
                // Draw runs of the same colour
                int k = 0;
                while (k < shown.Count)
                {
                    int j = k;
                    var sb = new StringBuilder();
                    while (j < shown.Count && shown[j].Base.Packed == shown[k].Base.Packed && shown[j].Shadow.Packed == shown[k].Shadow.Packed) { sb.Append(shown[j].C); j++; }
                    string run = sb.ToString();
                    g.DrawText(x, y, run, shown[k].Base, shown[k].Shadow);
                    x += g.TextWidth(run);
                    k = j;
                }
            }
            bool showArrow = (_pageDone && !(LastPage && _choices != null) && _autoClose < 0 && !(LastPage && _noKeyAtEnd)) || _waitingKey;
            if (showArrow && (_blink / 16) % 2 == 0)
            {
                int ax = bx + bw - 30, ay = by + bh - 26;
                for (int r = 0; r < 6; r++) g.FillRect(ax + r, ay + r, 12 - r * 2, 1, new Color(224, 8, 8));
            }
            if (_pageDone && LastPage && _choices != null) DrawChoices(g, by, bh);
        }

        void DrawChoices(Gfx g, int messageTop, int messageH)
        {
            int w = 0;
            foreach (var c in _choices) w = Math.Max(w, g.TextWidth(Expand(c)));
            w += 64;
            int lh = LineH(g);
            int h = _choices.Count * lh + 28;
            int x = g.Width - w - Margin, y = _position == 0 ? messageTop + messageH + 2 : messageTop - h - 2;
            DrawBox(g, x, y, w, h);
            for (int i = 0; i < _choices.Count; i++)
            {
                int ty = y + 14 + i * lh;
                if (i == _cursor)
                    for (int r = 0; r < 7; r++) g.FillRect(x + 16 + r, ty + 6 + r, 1, 14 - r * 2, new Color(80, 80, 88));
                g.DrawText(x + 34, ty, Expand(_choices[i]), Color.TextBase, Color.TextShadow);
            }
        }
    }
}
