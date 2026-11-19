using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PokeCreator.Studio.Controls
{
    /// <summary>
    /// Code editor in the style of Visual Studio: line numbers in a gutter, C#/JSON colouring as you
    /// type, Tab inserts 4 spaces, Enter keeps the indent (and indents after '{'), plain-text paste,
    /// Ctrl+/ toggles // comments, and its own undo history.
    /// </summary>
    public class CodeEditor : UserControl
    {
        public readonly CodeBox Box;
        readonly Gutter _gutter;
        readonly Timer _timer = new Timer { Interval = 120 };
        string _language = "";
        bool _busy;              // true while we change colours or text ourselves
        int[] _lineHashes;       // per line: hash of text + colours currently shown
        /// <summary>One undo step: what was replaced where. Only the change is kept, not the whole file.</summary>
        class Edit { public int Start; public string Removed, Inserted; public int CaretBefore; }
        readonly List<Edit> _undo = new List<Edit>(), _redo = new List<Edit>();
        string _lastText = ""; int _lastSel; DateTime _lastEdit; char _lastKind;
        public const int MaxHighlight = 1000000;
        public event EventHandler TextEdited;

        static readonly bool Native = !(Type.GetType("Mono.Runtime") != null && Environment.OSVersion.Platform == PlatformID.Unix);

        public CodeEditor()
        {
            Box = new CodeBox(this) { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, WordWrap = false, AcceptsTab = true, DetectUrls = false, HideSelection = false, ScrollBars = RichTextBoxScrollBars.ForcedBoth };
            Box.Font = MonoFont(10f);
            _gutter = new Gutter(this) { Dock = DockStyle.Left };
            Controls.Add(Box);
            Controls.Add(_gutter);
            Theme.Keep(Box); Theme.Keep(_gutter); Theme.Keep(this);
            Box.TextChanged += (s, e) => OnTextChanged();
            Box.VScroll += (s, e) => _gutter.Invalidate();
            Box.SelectionChanged += (s, e) => { if (!_busy) _gutter.Invalidate(); };
            Box.Resize += (s, e) => _gutter.Invalidate();
            // Creating the window can drop formatting that was applied before it existed
            Box.HandleCreated += (s, e) => { _lineHashes = null; BeginInvokeSafe(() => { if (_lineHashes == null) Recolor(true); }); };
            VisibleChanged += (s, e) => { if (Visible && _lineHashes == null) Recolor(true); };
            _timer.Tick += (s, e) => { _timer.Stop(); Recolor(false); };
            Action themed = () => { if (!IsDisposed) ApplyTheme(); };
            Theme.Changed += themed;
            Disposed += (s, e) => { Theme.Changed -= themed; _timer.Dispose(); };
            ApplyTheme();
        }

        void BeginInvokeSafe(Action a)
        {
            try { if (Box.IsHandleCreated) Box.BeginInvoke(a); else a(); } catch (InvalidOperationException) { }
        }

        public static Font MonoFont(float size)
        {
            foreach (var name in new[] { "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "Courier New" })
                using (var f = new Font(name, size))
                    if (f.Name == name) return new Font(name, size);
            return new Font(FontFamily.GenericMonospace, size);
        }

        /// <summary>"cs", "json" or "" (plain text).</summary>
        public string Language { get { return _language; } set { _language = value ?? ""; Recolor(true); } }

        public override string Text { get { return Box.Text; } set { LoadText(value); } }

        public void LoadText(string text)
        {
            text = (text ?? "").Replace("\r\n", "\n");
            _busy = true;
            // With the language known and the window ready, load the text already coloured (one step);
            // otherwise load plain text and let HandleCreated colour it once
            if (_language != "" && text.Length <= MaxHighlight && Box.IsHandleCreated)
            {
                _lineHashes = null;
                Box.Text = "";
                ApplyRtf(text, CodeLexer.Lex(text, _language), Theme.Dark);
                if (Box.TextLength != text.Length) Box.Text = text;
                _hashOnly = true; try { Recolor(false); } finally { _hashOnly = false; } // remember lines for later edits
            }
            else { Box.Text = text; Recolor(true); }
            _busy = false;
            _undo.Clear(); _redo.Clear();
            _lastText = Box.Text; _lastSel = 0;
            Box.Select(0, 0);
        }

        void ApplyTheme()
        {
            bool dark = Theme.Dark;
            Box.BackColor = CodeColors.Background(dark);
            Box.ForeColor = CodeColors.Of(CodeKind.Plain, dark);
            _gutter.BackColor = CodeColors.GutterBack(dark);
            BackColor = Box.BackColor;
            Recolor(true);
            _gutter.Invalidate();
        }

        // ------------------------------------------------------------------ editing
        bool _pending;

        void OnTextChanged()
        {
            if (_busy) return;
            // Finish the work after the edit completes (some text boxes report the change mid-update)
            if (_pending) return;
            _pending = true;
            if (Box.IsHandleCreated) Box.BeginInvoke(new Action(AfterChange)); else AfterChange();
        }

        void AfterChange()
        {
            _pending = false;
            if (Box.IsDisposed) return;
            // Undo history: one step per word / line / pause, like Visual Studio
            string now = Box.Text;
            char kind = now.Length > _lastText.Length ? KindAt(now, Box.SelectionStart - 1) : 'd';
            bool newStep = (DateTime.Now - _lastEdit).TotalMilliseconds > 900 || kind != _lastKind || kind == '\n' || Math.Abs(now.Length - _lastText.Length) > 1;
            // What changed: the part between the common start and the common end
            string old = _lastText;
            int pre = 0, max = Math.Min(old.Length, now.Length);
            while (pre < max && old[pre] == now[pre]) pre++;
            int suf = 0;
            while (suf < max - pre && old[old.Length - 1 - suf] == now[now.Length - 1 - suf]) suf++;
            var edit = new Edit { Start = pre, Removed = old.Substring(pre, old.Length - pre - suf), Inserted = now.Substring(pre, now.Length - pre - suf), CaretBefore = _lastSel };
            if (edit.Removed.Length > 0 || edit.Inserted.Length > 0)
            {
                var last = _undo.Count > 0 ? _undo[_undo.Count - 1] : null;
                if (!newStep && last != null && edit.Removed == "" && last.Removed == "" && last.Start + last.Inserted.Length == edit.Start)
                    last.Inserted += edit.Inserted;                                   // typing on
                else if (!newStep && last != null && edit.Inserted == "" && last.Inserted == "" && edit.Start + edit.Removed.Length == last.Start)
                { last.Start = edit.Start; last.Removed = edit.Removed + last.Removed; } // backspacing on
                else if (!newStep && last != null && edit.Inserted == "" && last.Inserted == "" && edit.Start == last.Start)
                    last.Removed += edit.Removed;                                     // deleting forwards
                else
                {
                    _undo.Add(edit);
                    if (_undo.Count > 1000) _undo.RemoveAt(0);
                }
            }
            _redo.Clear();
            _lastText = now; _lastSel = Box.SelectionStart; _lastEdit = DateTime.Now; _lastKind = kind;
            _gutter.UpdateWidth();
            _gutter.Invalidate();
            if (Box.TextLength < 20000) Recolor(false); else { _timer.Stop(); _timer.Start(); }
            if (TextEdited != null) TextEdited(this, EventArgs.Empty);
        }

        static char KindAt(string s, int i)
        {
            if (i < 0 || i >= s.Length) return 'o';
            char c = s[i];
            return c == '\n' ? '\n' : char.IsWhiteSpace(c) ? ' ' : char.IsLetterOrDigit(c) || c == '_' ? 'w' : 'p';
        }

        public bool CanUndo { get { return _undo.Count > 0; } }
        public void Undo() { Step(_undo, _redo, true); }
        public void Redo() { Step(_redo, _undo, false); }

        /// <summary>Applies one step backwards (undo) or forwards (redo) by replacing just that part of the text.</summary>
        void Step(List<Edit> from, List<Edit> to, bool undo)
        {
            if (from.Count == 0) return;
            var e = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            to.Add(e);
            string remove = undo ? e.Inserted : e.Removed, insert = undo ? e.Removed : e.Inserted;
            _busy = true;
            BeginUpdate();
            Box.Select(e.Start, remove.Length);
            Box.SelectedText = insert;
            Box.Select(undo ? Math.Min(e.CaretBefore, Box.TextLength) : e.Start + insert.Length, 0);
            EndUpdate();
            _busy = false;
            _lastText = Box.Text; _lastSel = Box.SelectionStart; _lastKind = 'o'; _lastEdit = DateTime.MinValue;
            Recolor(false);
            _gutter.UpdateWidth(); _gutter.Invalidate();
            Box.ScrollToCaretIfHidden();
            if (TextEdited != null) TextEdited(this, EventArgs.Empty);
        }

        /// <summary>Types text at the caret as one undo step.</summary>
        public void Insert(string s)
        {
            _lastEdit = DateTime.MinValue;
            Box.SelectedText = s;
            _lastEdit = DateTime.MinValue;
        }

        internal bool HandleKey(Keys key)
        {
            switch (key)
            {
                case Keys.Control | Keys.Z: Undo(); return true;
                case Keys.Control | Keys.Y: case Keys.Control | Keys.Shift | Keys.Z: Redo(); return true;
                case Keys.Control | Keys.V: case Keys.Shift | Keys.Insert: PastePlain(); return true;
                case Keys.Control | Keys.OemQuestion: case Keys.Control | Keys.Divide: ToggleComment(); return true;
                case Keys.Tab: Indent(false); return true;
                case Keys.Shift | Keys.Tab: Indent(true); return true;
                case Keys.Enter: NewLine(); return true;
                case Keys.Home: SmartHome(); return true;
            }
            return false;
        }

        void PastePlain()
        {
            string s = null;
            try { if (Clipboard.ContainsText()) s = Clipboard.GetText(); } catch { }
            if (s == null) return;
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            Insert(s);
        }

        int LineStart(int pos) { string t = Box.Text; int i = Math.Min(pos, t.Length); while (i > 0 && t[i - 1] != '\n') i--; return i; }

        void NewLine()
        {
            string t = Box.Text;
            int pos = Box.SelectionStart;
            int ls = LineStart(pos);
            int ws = ls; while (ws < t.Length && ws < pos && (t[ws] == ' ' || t[ws] == '\t')) ws++;
            string indent = t.Substring(ls, ws - ls);
            int before = pos - 1; while (before >= ls && (t[before] == ' ' || t[before] == '\t')) before--;
            bool opened = before >= ls && t[before] == '{';
            int sel = Box.SelectionLength;
            int afterPos = pos + sel;
            bool closes = afterPos < t.Length && t[afterPos] == '}';
            if (opened && closes) Insert("\n" + indent + "    \n" + indent);
            else Insert("\n" + indent + (opened ? "    " : ""));
            if (opened && closes) Box.Select(pos + 1 + indent.Length + 4, 0);
        }

        void Indent(bool outdent)
        {
            string t = Box.Text;
            int s = Box.SelectionStart, len = Box.SelectionLength;
            bool multiLine = len > 0 && t.IndexOf('\n', s, Math.Min(len, t.Length - s)) >= 0;
            if (!multiLine && !outdent)
            {
                int col = s - LineStart(s);
                Insert(new string(' ', 4 - col % 4));
                return;
            }
            int a = LineStart(s), b = s + len;
            if (b > a && b <= t.Length && b > 0 && t[b - 1] == '\n') b--;
            var lines = t.Substring(a, b - a).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (outdent) { int n = 0; while (n < 4 && n < lines[i].Length && lines[i][n] == ' ') n++; if (n == 0 && lines[i].StartsWith("\t")) n = 1; lines[i] = lines[i].Substring(n); }
                else if (lines[i].Length > 0) lines[i] = "    " + lines[i];
            }
            string joined = string.Join("\n", lines);
            Box.Select(a, b - a);
            Insert(joined);
            Box.Select(a, joined.Length);
        }

        void ToggleComment()
        {
            string t = Box.Text;
            int s = Box.SelectionStart, len = Box.SelectionLength;
            int a = LineStart(s), b = s + len;
            if (len > 0 && b > 0 && t[b - 1] == '\n') b--;
            int e = t.IndexOf('\n', b); if (e < 0) e = t.Length;
            var lines = t.Substring(a, e - a).Split('\n');
            bool allCommented = lines.Where(l => l.Trim().Length > 0).All(l => l.TrimStart().StartsWith("//"));
            int minIndent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                if (allCommented) { int k = lines[i].IndexOf("//"); lines[i] = lines[i].Remove(k, k + 2 < lines[i].Length && lines[i][k + 2] == ' ' ? 3 : 2); }
                else lines[i] = lines[i].Insert(minIndent, "// ");
            }
            string joined = string.Join("\n", lines);
            Box.Select(a, e - a);
            Insert(joined);
            Box.Select(a, joined.Length);
        }

        /// <summary>Home goes to the first non-space character, then to the start of the line.</summary>
        void SmartHome()
        {
            string t = Box.Text;
            int caret = Box.SelectionStart;
            int ls = LineStart(caret);
            int first = ls; while (first < t.Length && (t[first] == ' ' || t[first] == '\t')) first++;
            Box.Select(caret == first ? ls : first, 0);
        }

        // ------------------------------------------------------------------ colouring
        public void Recolor(bool full) { Recolor(full, true); }

        bool _hashOnly;

        void Recolor(bool full, bool freeze)
        {
            if (Box.IsDisposed) return;
            // Before the window exists, colours would be lost; HandleCreated colours everything once it does
            if (!Box.IsHandleCreated) { _lineHashes = null; return; }
            string text = Box.Text;
            bool dark = Theme.Dark;
            if (_language == "" || text.Length > MaxHighlight)
            {
                if (full) ApplyColors(0, text.Length, new List<CodeSpan>(), text, dark, freeze);
                _lineHashes = null;
                return;
            }
            var spans = CodeLexer.Lex(text, _language);

            // Hash every line's text and colours so only lines that changed get repainted
            var starts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
            int lines = starts.Count;
            var hashes = new int[lines];
            int si = 0;
            for (int l = 0; l < lines; l++)
            {
                int a = starts[l], b = l + 1 < lines ? starts[l + 1] : text.Length + 1;
                int h = 17;
                for (int i = a; i < b - 1 && i < text.Length; i++) h = h * 31 + text[i];
                while (si < spans.Count && spans[si].Start + spans[si].Length <= a) si++;
                // Positions relative to the line start, so typing on one line doesn't change the lines below it
                for (int k = si; k < spans.Count && spans[k].Start < b; k++) h = h * 131 + (Math.Max(spans[k].Start, a) - a) * 7 + (Math.Min(spans[k].Start + spans[k].Length, b) - a) * 3 + (int)spans[k].Kind;
                hashes[l] = h;
            }

            int from = 0, to = lines; // lines [from, to) need painting
            if (!full && _lineHashes != null)
            {
                var old = _lineHashes;
                while (from < lines && from < old.Length && old[from] == hashes[from]) from++;
                int endNew = lines, endOld = old.Length;
                while (endNew > from && endOld > from && old[endOld - 1] == hashes[endNew - 1]) { endNew--; endOld--; }
                to = endNew;
            }
            _lineHashes = hashes;
            if (_hashOnly) return; // just loaded already coloured: remember the lines, don't repaint
            if (from >= to) return;
            // Many lines to repaint (opening a file, switching theme, a big paste): rebuild it in one go
            if (to - from > 200) { ApplyRtf(text, spans, dark); return; }
            int start = starts[from], end = to < lines ? starts[to] - 1 : text.Length;
            if (end < start) end = start;
            ApplyColors(start, end, spans, text, dark, freeze);
        }

        /// <summary>
        /// Colours the whole text at once by handing the text box one formatted (RTF) document. Setting
        /// colours piece by piece costs a round trip per token, which took minutes on big JSON files.
        /// </summary>
        void ApplyRtf(string text, List<CodeSpan> spans, bool dark)
        {
            string rtf;
            try { rtf = BuildRtf(text, spans, dark); }
            catch { ApplyColors(0, text.Length, spans, text, dark, true); return; }
            bool wasBusy = _busy; _busy = true;
            int selStart = Box.SelectionStart, selLen = Box.SelectionLength;
            var scroll = GetScroll();
            BeginUpdate();
            try
            {
                Box.Rtf = rtf;
                // Some text boxes read RTF line breaks back differently; if the text changed, fall back
                if (Box.TextLength != text.Length) { Box.Text = text; ApplyColors(0, text.Length, spans, text, dark, false); }
                Box.Select(Math.Min(selStart, Box.TextLength), selLen);
            }
            finally { SetScroll(scroll); EndUpdate(); _busy = wasBusy; }
        }

        string BuildRtf(string text, List<CodeSpan> spans, bool dark)
        {
            var kinds = (CodeKind[])Enum.GetValues(typeof(CodeKind));
            var sb = new StringBuilder(text.Length * 2 + 1024);
            int twips = TextRenderer.MeasureText("0000", Box.Font).Width * 1440 / 96;
            sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fmodern\fcharset0 ").Append(Box.Font.Name).Append(@";}}{\colortbl ;");
            foreach (var k in kinds) { var c = CodeColors.Of(k, dark); sb.Append(@"\red").Append(c.R).Append(@"\green").Append(c.G).Append(@"\blue").Append(c.B).Append(';'); }
            sb.Append(@"}\uc1\pard\deftab").Append(twips).Append(@"\f0\fs").Append((int)Math.Round(Box.Font.SizeInPoints * 2)).Append(@"\cf").Append((int)CodeKind.Plain + 1).Append(' ');
            var sorted = spans.Where(x => x.Kind != CodeKind.Plain && x.Length > 0).OrderBy(x => x.Start).ToList();
            int pos = 0, si = 0;
            while (pos < text.Length)
            {
                while (si < sorted.Count && sorted[si].Start + sorted[si].Length <= pos) si++;
                int next = si < sorted.Count ? Math.Max(sorted[si].Start, pos) : text.Length;
                if (next > pos) { AppendRtfText(sb, text, pos, next); pos = next; continue; }
                var sp = sorted[si];
                int end = Math.Min(text.Length, sp.Start + sp.Length);
                sb.Append(@"\cf").Append((int)sp.Kind + 1).Append(' ');
                AppendRtfText(sb, text, pos, end);
                sb.Append(@"\cf").Append((int)CodeKind.Plain + 1).Append(' ');
                pos = end; si++;
            }
            sb.Append('}');
            return sb.ToString();
        }

        static void AppendRtfText(StringBuilder sb, string text, int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '{': sb.Append(@"\{"); break;
                    case '}': sb.Append(@"\}"); break;
                    case '\n': sb.Append(@"\par").Append('\n'); break;
                    case '\r': break;
                    case '\t': sb.Append(@"\tab "); break;
                    default:
                        if (c < 128) sb.Append(c);
                        else sb.Append(@"\u").Append((int)(short)c).Append('?');
                        break;
                }
            }
        }

        void ApplyColors(int start, int end, List<CodeSpan> spans, string text, bool dark, bool freeze)
        {
            bool wasBusy = _busy; _busy = true;
            int selStart = Box.SelectionStart, selLen = Box.SelectionLength;
            Point scroll = Point.Empty;
            if (freeze) { scroll = GetScroll(); BeginUpdate(); }
            try
            {
                var plain = CodeColors.Of(CodeKind.Plain, dark);
                Box.Select(start, Math.Max(0, end - start));
                Box.SelectionColor = plain;
                foreach (var sp in spans)
                {
                    if (sp.Start + sp.Length <= start || sp.Start >= end || sp.Kind == CodeKind.Plain) continue;
                    int a = Math.Max(sp.Start, start), b = Math.Min(sp.Start + sp.Length, end);
                    Box.Select(a, b - a);
                    Box.SelectionColor = CodeColors.Of(sp.Kind, dark);
                }
                Box.Select(selStart, selLen);
            }
            finally
            {
                if (freeze) { SetScroll(scroll); EndUpdate(); }
                _busy = wasBusy;
            }
        }

        // ------------------------------------------------------------------ native helpers (Windows)
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref Point l);
        const int WM_SETREDRAW = 0x0B, EM_GETEVENTMASK = 0x043B, EM_SETEVENTMASK = 0x0445, EM_GETSCROLLPOS = 0x04DD, EM_SETSCROLLPOS = 0x04DE;
        IntPtr _eventMask; int _frozen;

        void BeginUpdate()
        {
            if (_frozen++ > 0 || !Native || !Box.IsHandleCreated) return;
            SendMessage(Box.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            _eventMask = SendMessage(Box.Handle, EM_SETEVENTMASK, IntPtr.Zero, IntPtr.Zero);
        }

        void EndUpdate()
        {
            if (--_frozen > 0 || !Native || !Box.IsHandleCreated) return;
            SendMessage(Box.Handle, EM_SETEVENTMASK, IntPtr.Zero, _eventMask);
            SendMessage(Box.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            Box.Invalidate();
        }

        Point GetScroll()
        {
            var p = Point.Empty;
            if (Native && Box.IsHandleCreated) SendMessage(Box.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref p);
            return p;
        }

        void SetScroll(Point p) { if (Native && Box.IsHandleCreated) SendMessage(Box.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref p); }

        // ------------------------------------------------------------------ inner controls
        public class CodeBox : RichTextBox
        {
            readonly CodeEditor _ed;
            public CodeBox(CodeEditor ed) { _ed = ed; }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (Focused && _ed.HandleKey(keyData)) return true;
                return base.ProcessCmdKey(ref msg, keyData);
            }

            protected override bool IsInputKey(Keys keyData)
            {
                if ((keyData & Keys.KeyCode) == Keys.Tab) return true;
                return base.IsInputKey(keyData);
            }

            protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); _ed._gutter.Invalidate(); }

            public void ScrollToCaretIfHidden()
            {
                var p = GetPositionFromCharIndex(SelectionStart);
                if (p.Y < 0 || p.Y > ClientSize.Height - Font.Height) ScrollToCaret();
            }
        }

        class Gutter : Control
        {
            readonly CodeEditor _ed;
            public Gutter(CodeEditor ed) { _ed = ed; DoubleBuffered = true; Width = 48; Cursor = Cursors.Arrow; }

            public void UpdateWidth()
            {
                int lines = _ed.Box.GetLineFromCharIndex(_ed.Box.TextLength) + 1;
                int digits = Math.Max(3, lines.ToString().Length);
                int w = TextRenderer.MeasureText(new string('9', digits), _ed.Box.Font).Width + 18;
                if (Math.Abs(w - Width) > 2) Width = w;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var box = _ed.Box;
                bool dark = Theme.Dark;
                e.Graphics.Clear(CodeColors.GutterBack(dark));
                if (box.IsDisposed) return;
                int textLen = box.TextLength;
                int first = box.GetLineFromCharIndex(box.GetCharIndexFromPosition(new Point(2, 2)));
                int current = box.GetLineFromCharIndex(box.SelectionStart);
                int lineHeight = Math.Max(1, box.Font.Height);
                int total = box.GetLineFromCharIndex(textLen) + 1;
                // Line numbers are drawn level with the box, which may sit lower than the gutter's top
                int offset = box.Top - Top;
                using (var normal = new SolidBrush(CodeColors.LineNumber(dark)))
                using (var cur = new SolidBrush(CodeColors.CurrentLineNumber(dark)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Far })
                {
                    int lastY = int.MinValue;
                    for (int line = Math.Max(0, first - 1); line < total; line++)
                    {
                        int idx = box.GetFirstCharIndexFromLine(line);
                        if (idx < 0) break;
                        int y = box.GetPositionFromCharIndex(idx).Y;
                        if (line > 0 && y <= lastY) y = lastY + lineHeight; // empty last line reports 0 on some systems
                        lastY = y;
                        if (y + lineHeight < 0) continue;
                        if (y > Height) break;
                        e.Graphics.DrawString((line + 1).ToString(), box.Font, line == current ? cur : normal, new RectangleF(0, y + offset, Width - 10, lineHeight), sf);
                    }
                }
                using (var pen = new Pen(CodeColors.Divider(dark))) e.Graphics.DrawLine(pen, Width - 2, 0, Width - 2, Height);
            }

            // Click a line number to select the line, drag to select several (like Visual Studio)
            int _dragFrom = -1;
            protected override void OnMouseDown(MouseEventArgs e) { var box = _ed.Box; _dragFrom = LineAt(e.Y); SelectLines(_dragFrom, _dragFrom); box.Focus(); }
            protected override void OnMouseMove(MouseEventArgs e) { if (_dragFrom >= 0 && e.Button == MouseButtons.Left) SelectLines(_dragFrom, LineAt(e.Y)); }
            protected override void OnMouseUp(MouseEventArgs e) { _dragFrom = -1; }

            int LineAt(int y)
            {
                var box = _ed.Box;
                int idx = box.GetCharIndexFromPosition(new Point(2, Math.Max(0, y - (box.Top - Top))));
                return box.GetLineFromCharIndex(idx);
            }

            void SelectLines(int a, int b)
            {
                var box = _ed.Box;
                int lo = Math.Min(a, b), hi = Math.Max(a, b);
                int s = box.GetFirstCharIndexFromLine(lo);
                int e = box.GetFirstCharIndexFromLine(hi + 1);
                if (s < 0) return;
                if (e < 0) e = box.TextLength;
                box.Select(s, e - s);
                Invalidate();
            }
        }
    }
}
