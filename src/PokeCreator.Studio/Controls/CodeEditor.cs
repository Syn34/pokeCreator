using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PokeCreator.Studio.Controls
{
    /// <summary>
    /// Code editor in the style of Visual Studio, drawn by hand so it only ever paints the lines on screen:
    /// multi-megabyte files (species.json) scroll and type smoothly. Line numbers, C#/JSON colours (worked
    /// out in the background), Tab = 4 spaces, Enter keeps the indent, smart Home, Ctrl+/ comments,
    /// plain-text clipboard and its own undo history.
    /// </summary>
    public class CodeEditor : Control, IWheelTarget
    {
        // ------------------------------------------------------------------ document
        List<string> _lines = new List<string> { "" };
        List<List<CodeSpan>> _spans = new List<List<CodeSpan>> { null }; // colours per line, positions within the line
        int[] _lineStarts;          // offset of each line (rebuilt when needed)
        string _textCache;          // whole text (rebuilt when needed)
        int _version;               // bumps on every edit; background colouring checks it
        int _longestLine = -1;      // cached length of the longest line, for the horizontal scroll bar

        // ------------------------------------------------------------------ caret and view
        int _caretLine, _caretCol, _anchorLine, _anchorCol;
        int _wantX = -1;            // column Up/Down try to stay in
        int _top;                   // first visible line
        int _scrollX;               // horizontal scroll in pixels
        float _charW = 8; int _lineH = 16;
        readonly VScrollBar _vs = new VScrollBar { Dock = DockStyle.Right };
        readonly HScrollBar _hs = new HScrollBar { Dock = DockStyle.Bottom };
        readonly System.Windows.Forms.Timer _blink = new System.Windows.Forms.Timer { Interval = 530 };
        readonly System.Windows.Forms.Timer _lexTimer = new System.Windows.Forms.Timer { Interval = 150 };
        bool _caretOn = true, _mouseSelecting, _selectingLines;
        int _lineSelectAnchor;
        string _language = "";
        const int TabSize = 4;
        public const int MaxHighlight = 8 * 1024 * 1024;

        // ------------------------------------------------------------------ undo
        class Edit { public int Start; public string Removed, Inserted; public int CaretBefore; public char Kind; public DateTime When; }
        readonly List<Edit> _undo = new List<Edit>(), _redo = new List<Edit>();

        public event EventHandler TextEdited;
        public event EventHandler SelectionChanged;

        public CodeEditor()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.IBeam;
            Font = MonoFont(10f);
            Controls.Add(_vs); Controls.Add(_hs);
            _vs.Scroll += (s, e) => { _top = _vs.Value; Invalidate(); };
            _hs.Scroll += (s, e) => { _scrollX = _hs.Value; Invalidate(); };
            _vs.Cursor = Cursors.Default; _hs.Cursor = Cursors.Default;
            _blink.Tick += (s, e) => { _caretOn = !_caretOn; InvalidateCaretLine(); };
            _lexTimer.Tick += (s, e) => { _lexTimer.Stop(); StartLex(); };
            Theme.Keep(this);
            Action themed = () => { if (!IsDisposed) { BackColor = CodeColors.Background(Theme.Dark); Invalidate(); } };
            Theme.Changed += themed;
            Disposed += (s, e) => { Theme.Changed -= themed; _blink.Dispose(); _lexTimer.Dispose(); };
            BackColor = CodeColors.Background(Theme.Dark);
            MeasureFont();
            UpdateScrollBars();
            ContextMenuStrip = BuildMenu();
        }

        public static Font MonoFont(float size)
        {
            foreach (var name in new[] { "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "Courier New" })
                using (var f = new Font(name, size))
                    if (f.Name == name) return new Font(name, size);
            return new Font(FontFamily.GenericMonospace, size);
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); MeasureFont(); UpdateScrollBars(); Invalidate(); }

        void MeasureFont()
        {
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var w = TextRenderer.MeasureText(new string('M', 100), Font, new Size(int.MaxValue, int.MaxValue), flags).Width;
            _charW = Math.Max(4f, w / 100f);
            _lineH = Math.Max(10, Font.Height + 2);
        }

        // ------------------------------------------------------------------ public API (used by the script editor)
        /// <summary>"cs", "json" or "" (plain text).</summary>
        public string Language { get { return _language; } set { _language = value ?? ""; ClearColours(); StartLex(); } }

        public override string Text { get { return GetText(); } set { LoadText(value); } }
        public int TextLength { get { return TextLengthFast(); } }
        public bool CanUndo { get { return _undo.Count > 0; } }

        public void LoadText(string text)
        {
            text = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            _lines = text.Split('\n').ToList();
            _spans = new List<List<CodeSpan>>(new List<CodeSpan>[_lines.Count]);
            Changed();
            _undo.Clear(); _redo.Clear();
            _caretLine = _caretCol = _anchorLine = _anchorCol = 0;
            _top = 0; _scrollX = 0;
            UpdateScrollBars();
            StartLex();
            Invalidate();
            RaiseSelection();
        }

        public int SelectionStart { get { int a = Offset(_anchorLine, _anchorCol), c = Offset(_caretLine, _caretCol); return Math.Min(a, c); } }
        public int SelectionLength { get { int a = Offset(_anchorLine, _anchorCol), c = Offset(_caretLine, _caretCol); return Math.Abs(a - c); } }
        public string SelectedText { get { return GetRange(SelectionStart, SelectionLength); } }

        public void Select(int start, int length)
        {
            int total = TextLengthFast();
            start = Math.Max(0, Math.Min(start, total));
            int end = Math.Max(start, Math.Min(start + Math.Max(0, length), total));
            Position(start, out _anchorLine, out _anchorCol);
            Position(end, out _caretLine, out _caretCol);
            _wantX = -1;
            Invalidate(); RaiseSelection();
        }

        public void ScrollToCaret() { EnsureVisible(_caretLine, _caretCol, true); }
        public int GetLineFromCharIndex(int index) { int l, c; Position(index, out l, out c); return l; }
        public int GetFirstCharIndexFromLine(int line) { return line < 0 || line >= _lines.Count ? -1 : LineStarts()[line]; }

        // ------------------------------------------------------------------ offsets
        int[] LineStarts()
        {
            if (_lineStarts != null) return _lineStarts;
            var a = new int[_lines.Count];
            int o = 0;
            for (int i = 0; i < _lines.Count; i++) { a[i] = o; o += _lines[i].Length + 1; }
            return _lineStarts = a;
        }

        int Offset(int line, int col) { return LineStarts()[line] + col; }

        void Position(int offset, out int line, out int col)
        {
            var ls = LineStarts();
            int lo = 0, hi = ls.Length - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (ls[mid] <= offset) lo = mid; else hi = mid - 1; }
            line = lo; col = Math.Max(0, Math.Min(offset - ls[lo], _lines[lo].Length));
        }

        int TextLengthFast() { var ls = LineStarts(); return ls[ls.Length - 1] + _lines[_lines.Count - 1].Length; }

        string GetText()
        {
            if (_textCache == null) _textCache = string.Join("\n", _lines);
            return _textCache;
        }

        string GetRange(int start, int length)
        {
            if (length <= 0) return "";
            int l1, c1, l2, c2;
            Position(start, out l1, out c1); Position(start + length, out l2, out c2);
            if (l1 == l2) return _lines[l1].Substring(c1, c2 - c1);
            var sb = new StringBuilder();
            sb.Append(_lines[l1].Substring(c1));
            for (int l = l1 + 1; l < l2; l++) sb.Append('\n').Append(_lines[l]);
            sb.Append('\n').Append(_lines[l2].Substring(0, c2));
            return sb.ToString();
        }

        void Changed() { _lineStarts = null; _textCache = null; _longestLine = -1; _version++; }

        // ------------------------------------------------------------------ editing core
        /// <summary>Replaces text and records it for Undo. 'kind' groups typing into words ('w', ' ', 'p', 'd' for delete).</summary>
        void Replace(int start, int length, string text, char kind = '\0', bool record = true)
        {
            text = text ?? "";
            string removed = GetRange(start, length);
            if (removed.Length == 0 && text.Length == 0) return;
            int caretBefore = Offset(_caretLine, _caretCol);
            int l1, c1, l2, c2;
            Position(start, out l1, out c1); Position(start + length, out l2, out c2);
            string before = _lines[l1].Substring(0, c1), after = _lines[l2].Substring(c2);
            var parts = text.Split('\n');
            var newLines = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i];
                if (i == 0) s = before + s;
                if (i == parts.Length - 1) s = s + after;
                newLines.Add(s);
            }
            // Keep colours on a one-line edit (shift them); lines that changed shape are coloured again shortly
            List<CodeSpan> keep = null;
            if (l1 == l2 && parts.Length == 1 && _spans[l1] != null) keep = ShiftSpans(_spans[l1], c1, c2 - c1, text.Length);
            _lines.RemoveRange(l1, l2 - l1 + 1); _lines.InsertRange(l1, newLines);
            _spans.RemoveRange(l1, l2 - l1 + 1); _spans.InsertRange(l1, new List<CodeSpan>[newLines.Count]);
            if (keep != null) _spans[l1] = keep;
            Changed();
            if (record) RecordUndo(new Edit { Start = start, Removed = removed, Inserted = text, CaretBefore = caretBefore, Kind = kind, When = DateTime.Now });
            // Caret after the inserted text
            Position(start + text.Length, out _caretLine, out _caretCol);
            _anchorLine = _caretLine; _anchorCol = _caretCol; _wantX = -1;
            UpdateScrollBars();
            EnsureVisible(_caretLine, _caretCol, false);
            ScheduleLex();
            _caretOn = true;
            Invalidate();
            if (TextEdited != null) TextEdited(this, EventArgs.Empty);
            RaiseSelection();
        }

        static List<CodeSpan> ShiftSpans(List<CodeSpan> spans, int at, int removed, int inserted)
        {
            var r = new List<CodeSpan>(spans.Count);
            int delta = inserted - removed;
            foreach (var sp in spans)
            {
                int s = sp.Start, e = sp.Start + sp.Length;
                if (e <= at) r.Add(sp);
                else if (s >= at + removed) r.Add(new CodeSpan(s + delta, sp.Length, sp.Kind));
                else { int ns = Math.Min(s, at), ne = Math.Max(at + inserted, e + delta); if (ne > ns) r.Add(new CodeSpan(ns, ne - ns, sp.Kind)); }
            }
            return r;
        }

        void RecordUndo(Edit e)
        {
            _redo.Clear();
            var last = _undo.Count > 0 ? _undo[_undo.Count - 1] : null;
            bool recent = last != null && (e.When - last.When).TotalMilliseconds < 900 && e.Kind != '\0' && e.Kind == last.Kind && e.Kind != '\n';
            if (recent && e.Removed == "" && last.Removed == "" && last.Start + last.Inserted.Length == e.Start) { last.Inserted += e.Inserted; last.When = e.When; return; }
            if (recent && e.Inserted == "" && last.Inserted == "" && e.Start + e.Removed.Length == last.Start) { last.Start = e.Start; last.Removed = e.Removed + last.Removed; last.When = e.When; return; }
            if (recent && e.Inserted == "" && last.Inserted == "" && e.Start == last.Start) { last.Removed += e.Removed; last.When = e.When; return; }
            _undo.Add(e);
            if (_undo.Count > 2000) _undo.RemoveAt(0);
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var e = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1);
            var redo = new List<Edit>(_redo);
            Replace(e.Start, e.Inserted.Length, e.Removed, '\0', false);
            _redo.Clear(); _redo.AddRange(redo); _redo.Add(e);
            Select(e.CaretBefore, 0);
            EnsureVisible(_caretLine, _caretCol, true);
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            var e = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1);
            var redo = new List<Edit>(_redo);
            Replace(e.Start, e.Removed.Length, e.Inserted, '\0', false);
            _redo.Clear(); _redo.AddRange(redo);
            _undo.Add(e);
            EnsureVisible(_caretLine, _caretCol, true);
        }

        /// <summary>Types text at the caret, replacing the selection.</summary>
        public void Insert(string s, char kind = '\0') { Replace(SelectionStart, SelectionLength, s, kind); }

        bool HasSelection { get { return _anchorLine != _caretLine || _anchorCol != _caretCol; } }

        // ------------------------------------------------------------------ colours (worked out off the UI thread)
        void ClearColours() { for (int i = 0; i < _spans.Count; i++) _spans[i] = null; Invalidate(); }

        void ScheduleLex() { _lexTimer.Stop(); _lexTimer.Interval = TextLengthFast() < 50000 ? 30 : 200; _lexTimer.Start(); }

        void StartLex()
        {
            if (IsDisposed) return;
            if (_language == "") { ClearColours(); return; }
            string text = GetText();
            if (text.Length > MaxHighlight) { ClearColours(); return; }
            int version = _version;
            string lang = _language;
            var starts = (int[])LineStarts().Clone();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<List<CodeSpan>> perLine;
                try { perLine = SplitByLine(CodeLexer.Lex(text, lang), starts, text.Length); }
                catch { return; }
                Action apply = () =>
                {
                    // Only use the result if nothing was typed meanwhile (another pass is coming then)
                    if (version != _version || lang != _language || perLine.Count != _spans.Count) return;
                    _spans = perLine;
                    Invalidate();
                };
                try
                {
                    if (IsDisposed) return;
                    if (IsHandleCreated) BeginInvoke(apply);
                    else _pendingColours = apply;
                }
                catch (InvalidOperationException) { }
            });
        }

        Action _pendingColours;
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var p = _pendingColours; _pendingColours = null;
            if (p != null) BeginInvoke(p); else StartLex();
        }

        static List<List<CodeSpan>> SplitByLine(List<CodeSpan> spans, int[] starts, int textLength)
        {
            var result = new List<List<CodeSpan>>(starts.Length);
            for (int i = 0; i < starts.Length; i++) result.Add(new List<CodeSpan>());
            int line = 0;
            foreach (var sp in spans.OrderBy(x => x.Start))
            {
                if (sp.Kind == CodeKind.Plain || sp.Length <= 0) continue;
                int s = sp.Start, e = sp.Start + sp.Length;
                while (line + 1 < starts.Length && starts[line + 1] <= s) line++;
                // A span can run over several lines (block comments, verbatim strings)
                for (int l = line; l < starts.Length && starts[l] < e; l++)
                {
                    int ls = starts[l], le = l + 1 < starts.Length ? starts[l + 1] - 1 : textLength;
                    int a = Math.Max(s, ls), b = Math.Min(e, le);
                    if (b > a) result[l].Add(new CodeSpan(a - ls, b - a, sp.Kind));
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ geometry
        int GutterWidth { get { return (int)Math.Ceiling(Math.Max(3, _lines.Count.ToString().Length) * _charW) + 22; } }
        int TextLeft { get { return GutterWidth + 4; } }
        int VisibleLines { get { return Math.Max(1, (ClientSize.Height - (_hs.Visible ? _hs.Height : 0)) / _lineH); } }
        int TextAreaWidth { get { return Math.Max(1, ClientSize.Width - TextLeft - (_vs.Visible ? _vs.Width : 0)); } }

        /// <summary>Screen column of a character, counting tabs as up to 4 columns.</summary>
        int VisualCol(int line, int col)
        {
            string s = _lines[line];
            if (s.IndexOf('\t') < 0) return Math.Min(col, s.Length);
            int v = 0;
            for (int i = 0; i < col && i < s.Length; i++) v = s[i] == '\t' ? (v / TabSize + 1) * TabSize : v + 1;
            return v;
        }

        int ColFromVisual(int line, int visual)
        {
            string s = _lines[line];
            if (s.IndexOf('\t') < 0) return Math.Max(0, Math.Min(visual, s.Length));
            int v = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int next = s[i] == '\t' ? (v / TabSize + 1) * TabSize : v + 1;
                if (visual < (v + next + 1) / 2) return i;
                v = next;
            }
            return s.Length;
        }

        void HitTest(Point p, out int line, out int col)
        {
            line = Math.Max(0, Math.Min(_lines.Count - 1, _top + (int)Math.Floor((double)p.Y / _lineH)));
            int visual = (int)Math.Round((p.X - TextLeft + _scrollX) / _charW);
            col = ColFromVisual(line, Math.Max(0, visual));
        }

        int LongestLine()
        {
            if (_longestLine >= 0) return _longestLine;
            int m = 0; foreach (var l in _lines) if (l.Length > m) m = l.Length;
            return _longestLine = m;
        }

        void UpdateScrollBars()
        {
            int vis = VisibleLines;
            _vs.Minimum = 0;
            _vs.LargeChange = vis;
            _vs.SmallChange = 1;
            _vs.Maximum = Math.Max(0, _lines.Count - 1 + vis - 1);
            _top = Math.Max(0, Math.Min(_top, Math.Max(0, _lines.Count - 1)));
            _vs.Value = Math.Min(_top, Math.Max(0, _vs.Maximum - _vs.LargeChange + 1));
            int contentW = (int)((LongestLine() + 4) * _charW);
            int areaW = TextAreaWidth;
            _hs.Minimum = 0;
            _hs.LargeChange = Math.Max(1, areaW);
            _hs.SmallChange = (int)Math.Max(1, _charW * 4);
            _hs.Maximum = Math.Max(0, contentW);
            _hs.Enabled = contentW > areaW;
            _scrollX = Math.Max(0, Math.Min(_scrollX, Math.Max(0, contentW - areaW + 1)));
            if (_hs.Enabled) _hs.Value = Math.Min(_scrollX, Math.Max(0, _hs.Maximum - _hs.LargeChange + 1)); else _scrollX = 0;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScrollBars(); }

        void ScrollTo(int top)
        {
            int max = Math.Max(0, _lines.Count - VisibleLines);
            top = Math.Max(0, Math.Min(top, max));
            if (top == _top) return;
            _top = top;
            _vs.Value = Math.Min(_top, Math.Max(0, _vs.Maximum - _vs.LargeChange + 1));
            Invalidate();
        }

        void ScrollXTo(int x)
        {
            int max = Math.Max(0, _hs.Maximum - _hs.LargeChange + 1);
            x = Math.Max(0, Math.Min(x, max));
            if (x == _scrollX) return;
            _scrollX = x; if (_hs.Enabled) _hs.Value = x;
            Invalidate();
        }

        void EnsureVisible(int line, int col, bool center)
        {
            int vis = VisibleLines;
            if (line < _top || line >= _top + vis) ScrollTo(center ? line - vis / 2 : (line < _top ? line : line - vis + 1));
            int x = (int)(VisualCol(line, col) * _charW);
            int areaW = TextAreaWidth;
            if (x < _scrollX) ScrollXTo(x - (int)(_charW * 8));
            else if (x > _scrollX + areaW - _charW * 2) ScrollXTo(x - areaW + (int)(_charW * 8));
        }

        public void Wheel(int delta)
        {
            if ((ModifierKeys & Keys.Shift) != 0) ScrollXTo(_scrollX - delta * (int)(_charW * 6) / 120);
            else ScrollTo(_top - delta * 3 / 120);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
            Wheel(e.Delta);
        }

        // ------------------------------------------------------------------ painting
        void InvalidateCaretLine()
        {
            int y = (_caretLine - _top) * _lineH;
            Invalidate(new Rectangle(TextLeft - 2, y, ClientSize.Width, _lineH));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            bool dark = Theme.Dark;
            g.Clear(CodeColors.Background(dark));
            int vis = VisibleLines + 1;
            int gutter = GutterWidth, left = TextLeft;
            int areaRight = ClientSize.Width - (_vs.Visible ? _vs.Width : 0);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoClipping | TextFormatFlags.PreserveGraphicsClipping;
            int sel1 = SelectionStart, sel2 = sel1 + SelectionLength;
            bool hasSel = sel2 > sel1;
            var selColor = dark ? Color.FromArgb(38, 79, 120) : Color.FromArgb(173, 214, 255);
            var curLine = dark ? Color.FromArgb(40, 40, 44) : Color.FromArgb(240, 242, 248);
            var plain = CodeColors.Of(CodeKind.Plain, dark);
            int firstVisCol = (int)(_scrollX / _charW);
            int visCols = (int)(TextAreaWidth / _charW) + 3;
            var ls = LineStarts();

            g.SetClip(new Rectangle(gutter, 0, Math.Max(0, areaRight - gutter), ClientSize.Height));
            for (int i = 0; i < vis; i++)
            {
                int line = _top + i;
                if (line >= _lines.Count) break;
                int y = i * _lineH;
                string s = _lines[line];
                if (line == _caretLine && !hasSel) using (var b = new SolidBrush(curLine)) g.FillRectangle(b, gutter, y, areaRight - gutter, _lineH);
                // Selection
                if (hasSel)
                {
                    int lineStart = ls[line], lineEnd = lineStart + s.Length;
                    int a = Math.Max(sel1, lineStart), bnd = Math.Min(sel2, lineEnd + 1);
                    if (bnd > a)
                    {
                        int va = VisualCol(line, a - lineStart), vb = VisualCol(line, Math.Min(bnd - lineStart, s.Length)) + (sel2 > lineEnd ? 1 : 0);
                        using (var b = new SolidBrush(selColor))
                            g.FillRectangle(b, left + va * _charW - _scrollX, y, Math.Max(_charW / 2, (vb - va) * _charW), _lineH);
                    }
                }
                if (s.Length == 0) continue;
                // Text, coloured run by run; only the columns that are on screen
                var spans = line < _spans.Count ? _spans[line] : null;
                bool tabs = s.IndexOf('\t') >= 0;
                int pos = 0, si = 0;
                while (pos < s.Length)
                {
                    int end; CodeKind kind;
                    while (spans != null && si < spans.Count && spans[si].Start + spans[si].Length <= pos) si++;
                    if (spans != null && si < spans.Count && spans[si].Start <= pos)
                    {
                        end = Math.Min(s.Length, spans[si].Start + spans[si].Length); kind = spans[si].Kind; si++;
                    }
                    else
                    {
                        end = spans != null && si < spans.Count ? Math.Min(s.Length, spans[si].Start) : s.Length;
                        kind = CodeKind.Plain;
                    }
                    if (end <= pos) { pos++; continue; }
                    int c0 = pos, c1 = end;
                    if (!tabs) { c0 = Math.Max(pos, firstVisCol - 1); c1 = Math.Min(end, firstVisCol + visCols); }
                    if (c1 > c0)
                    {
                        int vx = tabs ? VisualCol(line, c0) : c0;
                        if (tabs || (vx < firstVisCol + visCols && c1 > firstVisCol - 1))
                        {
                            string run = s.Substring(c0, c1 - c0);
                            if (tabs) run = ExpandTabs(run, vx);
                            TextRenderer.DrawText(g, run, Font, new Point((int)(left + vx * _charW - _scrollX), y + 1), kind == CodeKind.Plain ? plain : CodeColors.Of(kind, dark), flags);
                        }
                    }
                    pos = end;
                }
            }
            // Caret
            if (Focused && _caretOn && _caretLine >= _top && _caretLine < _top + vis)
            {
                int cx = (int)(left + VisualCol(_caretLine, _caretCol) * _charW - _scrollX);
                if (cx >= left - 1)
                    using (var p = new Pen(dark ? Color.FromArgb(220, 220, 220) : Color.Black, 2)) g.DrawLine(p, cx, (_caretLine - _top) * _lineH + 1, cx, (_caretLine - _top + 1) * _lineH - 1);
            }
            g.ResetClip();

            // Line numbers
            using (var gb = new SolidBrush(CodeColors.GutterBack(dark))) g.FillRectangle(gb, 0, 0, gutter, ClientSize.Height);
            var numColor = CodeColors.LineNumber(dark); var curColor = CodeColors.CurrentLineNumber(dark);
            var numFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Right;
            for (int i = 0; i < vis; i++)
            {
                int line = _top + i;
                if (line >= _lines.Count) break;
                TextRenderer.DrawText(g, (line + 1).ToString(), Font, new Rectangle(0, i * _lineH + 1, gutter - 12, _lineH), line == _caretLine ? curColor : numColor, numFlags);
            }
            using (var p = new Pen(CodeColors.Divider(dark))) g.DrawLine(p, gutter - 3, 0, gutter - 3, ClientSize.Height);
            if (_vs.Visible && _hs.Visible) using (var b = new SolidBrush(SystemColors.Control)) g.FillRectangle(b, _vs.Left, _hs.Top, _vs.Width, _hs.Height);
        }

        static string ExpandTabs(string s, int startVisual)
        {
            if (s.IndexOf('\t') < 0) return s;
            var sb = new StringBuilder();
            int v = startVisual;
            foreach (char c in s)
            {
                if (c == '\t') { int n = (v / TabSize + 1) * TabSize - v; sb.Append(' ', n); v += n; }
                else { sb.Append(c); v++; }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ mouse
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            int line, col; HitTest(e.Location, out line, out col);
            if (e.X < GutterWidth)
            {
                // Click a line number to select the line; drag to select more
                _selectingLines = true; _lineSelectAnchor = line;
                SelectLines(line, line);
                Capture = true;
                return;
            }
            if (e.Clicks == 2) { SelectWord(line, col); return; }
            _caretLine = line; _caretCol = col;
            if ((ModifierKeys & Keys.Shift) == 0) { _anchorLine = line; _anchorCol = col; }
            _wantX = -1; _mouseSelecting = true; Capture = true;
            ResetBlink(); Invalidate(); RaiseSelection();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = e.X < GutterWidth ? Cursors.Arrow : Cursors.IBeam;
            if (e.Button != MouseButtons.Left) return;
            int line, col; HitTest(e.Location, out line, out col);
            if (_selectingLines) { SelectLines(_lineSelectAnchor, line); return; }
            if (!_mouseSelecting) return;
            // Scroll when dragging past the edges
            if (e.Y < 0) ScrollTo(_top - 1); else if (e.Y > ClientSize.Height - _lineH) ScrollTo(_top + 1);
            if (line != _caretLine || col != _caretCol) { _caretLine = line; _caretCol = col; Invalidate(); RaiseSelection(); }
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _mouseSelecting = false; _selectingLines = false; Capture = false; }

        void SelectLines(int a, int b)
        {
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            _anchorLine = lo; _anchorCol = 0;
            if (hi + 1 < _lines.Count) { _caretLine = hi + 1; _caretCol = 0; } else { _caretLine = hi; _caretCol = _lines[hi].Length; }
            Invalidate(); RaiseSelection();
        }

        static bool WordChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        void SelectWord(int line, int col)
        {
            string s = _lines[line];
            int a = Math.Min(col, s.Length), b = a;
            if ((a < s.Length && WordChar(s[a])) || (a > 0 && WordChar(s[a - 1])))
            {
                while (a > 0 && WordChar(s[a - 1])) a--;
                while (b < s.Length && WordChar(s[b])) b++;
            }
            else if (a < s.Length) b = a + 1;
            _anchorLine = _caretLine = line; _anchorCol = a; _caretCol = b;
            Invalidate(); RaiseSelection();
        }

        // ------------------------------------------------------------------ keyboard
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.Tab: case Keys.Enter:
                case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown: case Keys.Escape:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        // Every typed character is text: without this, letters act as menu shortcuts (typing "o" opened File > Open)
        protected override bool IsInputChar(char charCode) { return true; }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); ResetBlink(); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _blink.Stop(); Invalidate(); }

        void ResetBlink() { _caretOn = true; _blink.Stop(); if (Focused) _blink.Start(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled) return;
            bool shift = e.Shift, ctrl = e.Control;
            bool handled = true;
            switch (e.KeyCode)
            {
                case Keys.Left:
                    if (HasSelection && !shift) { CollapseTo(true); break; }
                    if (ctrl) WordLeft(); else if (_caretCol > 0) _caretCol--; else if (_caretLine > 0) { _caretLine--; _caretCol = _lines[_caretLine].Length; }
                    Moved(shift); break;
                case Keys.Right:
                    if (HasSelection && !shift) { CollapseTo(false); break; }
                    if (ctrl) WordRight(); else if (_caretCol < _lines[_caretLine].Length) _caretCol++; else if (_caretLine < _lines.Count - 1) { _caretLine++; _caretCol = 0; }
                    Moved(shift); break;
                case Keys.Up:
                    if (ctrl) { ScrollTo(_top - 1); break; }
                    MoveLines(-1, shift); break;
                case Keys.Down:
                    if (ctrl) { ScrollTo(_top + 1); break; }
                    MoveLines(1, shift); break;
                case Keys.PageUp: ScrollTo(_top - VisibleLines + 1); MoveLines(-VisibleLines + 1, shift); break;
                case Keys.PageDown: ScrollTo(_top + VisibleLines - 1); MoveLines(VisibleLines - 1, shift); break;
                case Keys.Home:
                    if (ctrl) { _caretLine = 0; _caretCol = 0; }
                    else { string s = _lines[_caretLine]; int first = 0; while (first < s.Length && (s[first] == ' ' || s[first] == '\t')) first++; _caretCol = _caretCol == first ? 0 : first; }
                    Moved(shift); break;
                case Keys.End:
                    if (ctrl) _caretLine = _lines.Count - 1;
                    _caretCol = _lines[_caretLine].Length; Moved(shift); break;
                case Keys.Back:
                    if (HasSelection) Insert("", 'd');
                    else if (ctrl) { int end = Offset(_caretLine, _caretCol); WordLeft(); int start = Offset(_caretLine, _caretCol); Replace(start, end - start, "", 'd'); }
                    else
                    {
                        int at = Offset(_caretLine, _caretCol);
                        if (at == 0) break;
                        // Backspace in indentation goes back to the previous tab stop
                        string s = _lines[_caretLine];
                        int n = 1;
                        if (_caretCol > 0 && s.Substring(0, _caretCol).Trim().Length == 0 && s[_caretCol - 1] == ' ') n = (_caretCol - 1) % TabSize + 1;
                        Replace(at - n, n, "", 'd');
                    }
                    break;
                case Keys.Delete:
                    if (HasSelection) Insert("", 'x');
                    else if (shift) { int a = Offset(_caretLine, 0); int len = _lines[_caretLine].Length + (_caretLine < _lines.Count - 1 ? 1 : 0); Replace(a, len, "", '\0'); } // Shift+Del deletes the line
                    else if (ctrl) { int start = Offset(_caretLine, _caretCol); WordRight(); int end = Offset(_caretLine, _caretCol); Replace(start, end - start, "", 'x'); }
                    else { int at = Offset(_caretLine, _caretCol); if (at < TextLengthFast()) Replace(at, 1, "", 'x'); }
                    break;
                case Keys.Enter: NewLine(); break;
                case Keys.Tab: Indent(shift); break;
                case Keys.Escape: if (HasSelection) CollapseTo(false); else handled = false; break;
                case Keys.Insert:
                    if (ctrl) CopySelection(); else if (shift) Paste(); else handled = false;
                    break;
                default:
                    handled = false;
                    if (ctrl && !e.Alt)
                    {
                        handled = true;
                        switch (e.KeyCode)
                        {
                            case Keys.A: _anchorLine = 0; _anchorCol = 0; _caretLine = _lines.Count - 1; _caretCol = _lines[_caretLine].Length; Invalidate(); RaiseSelection(); break;
                            case Keys.C: CopySelection(); break;
                            case Keys.X: if (CopySelection()) Insert("", '\0'); break;
                            case Keys.V: Paste(); break;
                            case Keys.Z: if (shift) Redo(); else Undo(); break;
                            case Keys.Y: Redo(); break;
                            case Keys.OemQuestion: case Keys.Divide: ToggleComment(); break;
                            default: handled = false; break;
                        }
                    }
                    break;
            }
            if (handled) { e.Handled = true; e.SuppressKeyPress = true; ResetBlink(); }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (e.Handled || e.KeyChar < 32 || e.KeyChar == 127) return;
            if ((ModifierKeys & Keys.Control) != 0 && (ModifierKeys & Keys.Alt) == 0) return;
            char c = e.KeyChar;
            Insert(c.ToString(), char.IsLetterOrDigit(c) || c == '_' ? 'w' : c == ' ' ? ' ' : 'p');
            e.Handled = true;
            ResetBlink();
        }

        void Moved(bool extend)
        {
            if (!extend) { _anchorLine = _caretLine; _anchorCol = _caretCol; }
            _wantX = -1;
            EnsureVisible(_caretLine, _caretCol, false);
            Invalidate(); RaiseSelection();
        }

        void CollapseTo(bool start)
        {
            int a = Offset(_anchorLine, _anchorCol), c = Offset(_caretLine, _caretCol);
            Position(start ? Math.Min(a, c) : Math.Max(a, c), out _caretLine, out _caretCol);
            Moved(false);
        }

        void MoveLines(int delta, bool extend)
        {
            if (_wantX < 0) _wantX = VisualCol(_caretLine, _caretCol);
            int want = _wantX;
            int line = Math.Max(0, Math.Min(_lines.Count - 1, _caretLine + delta));
            if (line == _caretLine && delta != 0) _caretCol = delta < 0 ? 0 : _lines[line].Length;
            else _caretCol = ColFromVisual(line, want);
            _caretLine = line;
            if (!extend) { _anchorLine = _caretLine; _anchorCol = _caretCol; }
            EnsureVisible(_caretLine, _caretCol, false);
            _wantX = want;
            Invalidate(); RaiseSelection();
        }

        void WordLeft()
        {
            if (_caretCol == 0) { if (_caretLine > 0) { _caretLine--; _caretCol = _lines[_caretLine].Length; } return; }
            string s = _lines[_caretLine]; int i = _caretCol;
            while (i > 0 && char.IsWhiteSpace(s[i - 1])) i--;
            if (i > 0 && WordChar(s[i - 1])) { while (i > 0 && WordChar(s[i - 1])) i--; }
            else if (i > 0) i--;
            _caretCol = i;
        }

        void WordRight()
        {
            string s = _lines[_caretLine];
            if (_caretCol >= s.Length) { if (_caretLine < _lines.Count - 1) { _caretLine++; _caretCol = 0; } return; }
            int i = _caretCol;
            if (WordChar(s[i])) { while (i < s.Length && WordChar(s[i])) i++; }
            else i++;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            _caretCol = i;
        }

        bool CopySelection()
        {
            if (!HasSelection) return false;
            try { Clipboard.SetText(SelectedText.Replace("\n", Environment.NewLine)); return true; } catch { return false; }
        }

        void Paste()
        {
            string s = null;
            try { if (Clipboard.ContainsText()) s = Clipboard.GetText(); } catch { }
            if (string.IsNullOrEmpty(s)) return;
            Insert(s.Replace("\r\n", "\n").Replace('\r', '\n'), '\0');
        }

        void NewLine()
        {
            int sel = SelectionStart, selEnd = sel + SelectionLength;
            int l, c; Position(sel, out l, out c);
            string line = _lines[l];
            int ws = 0; while (ws < line.Length && ws < c && (line[ws] == ' ' || line[ws] == '\t')) ws++;
            string indent = line.Substring(0, ws);
            int before = c - 1; while (before >= 0 && (line[before] == ' ' || line[before] == '\t')) before--;
            bool opened = before >= 0 && (line[before] == '{' || line[before] == '[');
            int al, ac; Position(selEnd, out al, out ac);
            bool closes = ac < _lines[al].Length && (_lines[al][ac] == '}' || _lines[al][ac] == ']');
            if (opened && closes)
            {
                Insert("\n" + indent + "    \n" + indent, '\n');
                Select(sel + 1 + indent.Length + 4, 0);
            }
            else Insert("\n" + indent + (opened ? "    " : ""), '\n');
        }

        void Indent(bool outdent)
        {
            bool multi = HasSelection && _anchorLine != _caretLine;
            if (!multi && !outdent)
            {
                int vcol = VisualCol(_caretLine, _caretCol);
                Insert(new string(' ', TabSize - vcol % TabSize), ' ');
                return;
            }
            int l1 = Math.Min(_anchorLine, _caretLine), l2 = Math.Max(_anchorLine, _caretLine);
            if (multi && l2 > l1 && (l2 == _caretLine ? _caretCol : _anchorCol) == 0) l2--; // ends at a line start: that line isn't included
            var sb = new StringBuilder();
            for (int l = l1; l <= l2; l++)
            {
                string s = _lines[l];
                if (outdent) { int n = 0; while (n < TabSize && n < s.Length && s[n] == ' ') n++; if (n == 0 && s.StartsWith("\t")) n = 1; s = s.Substring(n); }
                else if (s.Length > 0) s = new string(' ', TabSize) + s;
                if (l > l1) sb.Append('\n');
                sb.Append(s);
            }
            int start = Offset(l1, 0), end = Offset(l2, _lines[l2].Length);
            Replace(start, end - start, sb.ToString(), '\0');
            Select(start, sb.Length);
        }

        void ToggleComment()
        {
            int l1 = Math.Min(_anchorLine, _caretLine), l2 = Math.Max(_anchorLine, _caretLine);
            if (HasSelection && l2 > l1 && (l2 == _caretLine ? _caretCol : _anchorCol) == 0) l2--;
            var lines = _lines.GetRange(l1, l2 - l1 + 1);
            var nonEmpty = lines.Where(x => x.Trim().Length > 0).ToList();
            if (nonEmpty.Count == 0) return;
            bool allCommented = nonEmpty.All(x => x.TrimStart().StartsWith("//"));
            int minIndent = nonEmpty.Min(x => x.Length - x.TrimStart().Length);
            var outLines = new List<string>();
            foreach (var s in lines)
            {
                if (s.Trim().Length == 0) { outLines.Add(s); continue; }
                if (allCommented) { int k = s.IndexOf("//"); outLines.Add(s.Remove(k, k + 2 < s.Length && s[k + 2] == ' ' ? 3 : 2)); }
                else outLines.Add(s.Insert(minIndent, "// "));
            }
            string joined = string.Join("\n", outLines);
            int start = Offset(l1, 0), end = Offset(l2, _lines[l2].Length);
            Replace(start, end - start, joined, '\0');
            Select(start, joined.Length);
        }

        void RaiseSelection() { if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); }

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Undo", null, (s, e) => Undo());
            m.Items.Add("Redo", null, (s, e) => Redo());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Cut", null, (s, e) => { if (CopySelection()) Insert("", '\0'); });
            m.Items.Add("Copy", null, (s, e) => CopySelection());
            m.Items.Add("Paste", null, (s, e) => Paste());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Select All", null, (s, e) => Select(0, TextLengthFast()));
            m.Items.Add("Toggle Comment (Ctrl+/)", null, (s, e) => ToggleComment());
            m.Opening += (s, e) =>
            {
                m.Items[0].Enabled = _undo.Count > 0; m.Items[1].Enabled = _redo.Count > 0;
                m.Items[3].Enabled = m.Items[4].Enabled = HasSelection;
                Theme.Apply(m);
            };
            return m;
        }
    }
}
