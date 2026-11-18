using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.UI
{
    /// <summary>Bottom-of-screen message box with typewriter text, paging and choice lists.</summary>
    public class MessageWindow
    {
        readonly Game _game;
        List<string> _lines = new List<string>();
        int _lineIndex;            // first line of the current page
        int _shownChars;           // typewriter progress on the current page
        bool _pageDone;
        List<string> _choices;
        int _cancelIndex = -1;
        int _cursor;
        Action<int> _onChoice;
        int _blink;

        const int LinesPerPage = 2;
        const int BoxH = 96, Margin = 4, PadX = 22, PadY = 14;

        public bool Busy { get; private set; }
        public int LastChoice { get; private set; }

        public MessageWindow(Game g) { _game = g; }

        /// <summary>Shows a message. If choices are given, the result is passed to onChoice (cancel = cancelIndex).</summary>
        public void Show(string text, List<string> choices = null, int cancelIndex = -1, Action<int> onChoice = null)
        {
            string plain = Expand(text ?? "");
            int width = _game.Gfx.Width - Margin * 2 - PadX * 2;
            _lines = Wrap(plain, width);
            if (_lines.Count == 0) _lines.Add("");
            _lineIndex = 0; _shownChars = 0; _pageDone = false;
            _choices = choices != null && choices.Count > 0 ? choices : null;
            _cancelIndex = cancelIndex; _cursor = 0; _onChoice = onChoice;
            Busy = true;
        }

        /// <summary>Replaces Essentials/RMXP text codes: \PN, \v[n], \n; strips formatting codes we don't render yet.</summary>
        public string Expand(string s)
        {
            s = s.Replace("\\PN", _game.State.PlayerName).Replace("\\pn", _game.State.PlayerName);
            s = Regex.Replace(s, @"\\[vV]\[(\d+)\]", m => _game.State.GetVar(int.Parse(m.Groups[1].Value)).ToString());
            s = s.Replace("\\n", "\n").Replace("\\N", "\n");
            s = Regex.Replace(s, @"\\[a-zA-Z]+\[[^\]]*\]", "");                    // codes with arguments: \c[1], \se[..], \wtnp[..]
            s = Regex.Replace(s, @"\\(?:op|cl|wu|wd|pm|pg|cn|[brigG])", "");          // argument-less codes: \b (blue), \r (red) ...
            s = Regex.Replace(s, @"<[^>]+>", "");                           // <ac>, <b> style tags
            s = s.Replace("\\.", "").Replace("\\|", "").Replace("\\!", "").Replace("\\^", "").Replace("\\\\", "\\");
            return s;
        }

        List<string> Wrap(string text, int width)
        {
            var result = new List<string>();
            foreach (var para in text.Split('\n'))
            {
                var words = para.Split(' ');
                var line = new StringBuilder();
                foreach (var w in words)
                {
                    if (w.Length == 0) continue;
                    string trial = line.Length == 0 ? w : line + " " + w;
                    if (line.Length > 0 && _game.Gfx.TextWidth(trial) > width) { result.Add(line.ToString()); line.Clear(); line.Append(w); }
                    else { line.Clear(); line.Append(trial); }
                }
                result.Add(line.ToString());
            }
            return result;
        }

        int PageCharCount()
        {
            int n = 0;
            for (int i = _lineIndex; i < Math.Min(_lines.Count, _lineIndex + LinesPerPage); i++) n += _lines[i].Length;
            return n;
        }

        bool LastPage { get { return _lineIndex + LinesPerPage >= _lines.Count; } }

        public void Update()
        {
            if (!Busy) return;
            _blink++;
            if (!_pageDone)
            {
                _shownChars += 2;
                if (Input.Trigger(Button.Action) && _shownChars > 2) _shownChars = PageCharCount();
                if (_shownChars >= PageCharCount()) { _shownChars = PageCharCount(); _pageDone = true; }
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
            if (Input.Trigger(Button.Action) || Input.Trigger(Button.Cancel))
            {
                if (LastPage) Finish(0);
                else { _lineIndex += LinesPerPage; _shownChars = 0; _pageDone = false; }
            }
        }

        void Finish(int choice)
        {
            Busy = false;
            LastChoice = choice;
            var cb = _onChoice; _onChoice = null;
            if (cb != null) cb(choice);
        }

        // ------------------------------------------------------------------ drawing
        public static void DrawBox(Gfx g, int x, int y, int w, int h)
        {
            g.FillRect(x, y, w, h, new Color(72, 72, 80));
            g.FillRect(x + 2, y + 2, w - 4, h - 4, new Color(248, 248, 248));
            g.FillRect(x + 4, y + 4, w - 8, h - 8, new Color(120, 168, 208));
            g.FillRect(x + 6, y + 6, w - 12, h - 12, new Color(248, 248, 248));
        }

        public void Draw(Gfx g)
        {
            if (!Busy) return;
            int bx = Margin, by = g.Height - BoxH - Margin, bw = g.Width - Margin * 2;
            DrawBox(g, bx, by, bw, BoxH);
            int remaining = _shownChars;
            int lh = Math.Max(30, g.LineHeight);
            for (int i = 0; i < LinesPerPage && _lineIndex + i < _lines.Count; i++)
            {
                string line = _lines[_lineIndex + i];
                if (remaining <= 0) break;
                string part = line.Length <= remaining ? line : line.Substring(0, remaining);
                remaining -= line.Length;
                g.DrawText(bx + PadX, by + PadY + i * lh, part, Color.TextBase, Color.TextShadow);
            }
            if (_pageDone && !(LastPage && _choices != null) && (_blink / 16) % 2 == 0)
            {
                // "more" arrow
                int ax = bx + bw - 30, ay = by + BoxH - 26;
                for (int r = 0; r < 6; r++) g.FillRect(ax + r, ay + r, 12 - r * 2, 1, new Color(224, 8, 8));
            }
            if (_pageDone && LastPage && _choices != null) DrawChoices(g, by);
        }

        void DrawChoices(Gfx g, int messageTop)
        {
            int w = 0;
            foreach (var c in _choices) w = Math.Max(w, g.TextWidth(Expand(c)));
            w += 64;
            int lh = Math.Max(30, g.LineHeight);
            int h = _choices.Count * lh + 28;
            int x = g.Width - w - Margin, y = messageTop - h - 2;
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
