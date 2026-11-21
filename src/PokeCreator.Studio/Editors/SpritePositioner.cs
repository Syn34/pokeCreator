using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Database → Sprite Positions (Essentials' sprite positioner): shows a Pokémon in a battle scene the way the game
    /// draws it and moves its front / back sprite (arrow keys) and altitude (Page Up / Down), saved as pokemon_metrics.
    /// </summary>
    public class SpritePositioner : Form
    {
        readonly Project _p;
        readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        readonly TextBox _search = new TextBox { Dock = DockStyle.Top };
        readonly PictureBox _view = new PictureBox { Width = 512, Height = 288, SizeMode = PictureBoxSizeMode.Normal };
        readonly Label _info = new Label { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(6) };
        readonly RadioButton _front = new RadioButton { Text = "Front (opposing)", Checked = true, AutoSize = true };
        readonly RadioButton _back = new RadioButton { Text = "Back (player's)", AutoSize = true };
        List<SpeciesData> _all;
        bool _dirty;

        public SpritePositioner(Project p)
        {
            _p = p;
            Text = "Sprite Positions";
            Width = 900; Height = 520; StartPosition = FormStartPosition.CenterParent; KeyPreview = true;
            _all = p.Species.OrderBy(s => s.Id.Split('_')[0]).ThenBy(s => s.Form).ToList();
            var left = new Panel { Dock = DockStyle.Left, Width = 260 };
            left.Controls.Add(_list); left.Controls.Add(_search);
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32 };
            var save = new Button { Text = "Save", Width = 90 };
            var reset = new Button { Text = "Clear offsets", Width = 110 };
            top.Controls.Add(_front); top.Controls.Add(_back); top.Controls.Add(reset); top.Controls.Add(save);
            _view.Location = new Point(8, 40);
            right.Controls.Add(_view); right.Controls.Add(top); right.Controls.Add(_info);
            Controls.Add(right); Controls.Add(left);
            _search.TextChanged += (s, e) => Fill();
            _list.SelectedIndexChanged += (s, e) => Redraw();
            _front.CheckedChanged += (s, e) => Redraw();
            save.Click += (s, e) => Save();
            reset.Click += (s, e) => { var d = Current; if (d == null) return; if (_front.Checked) { d.FrontSprite = new List<int> { 0, 0 }; d.FrontSpriteAltitude = 0; } else d.BackSprite = new List<int> { 0, 0 }; _dirty = true; Redraw(); };
            FormClosing += (s, e) =>
            {
                if (!_dirty) return;
                var r = MessageBox.Show(this, "Save the sprite positions?", Text, MessageBoxButtons.YesNoCancel);
                if (r == DialogResult.Cancel) e.Cancel = true; else if (r == DialogResult.Yes) Save();
            };
            Fill();
        }

        SpeciesData Current { get { return _list.SelectedItem as SpeciesItem != null ? ((SpeciesItem)_list.SelectedItem).D : null; } }

        class SpeciesItem
        {
            public SpeciesData D;
            public override string ToString() { return (D.Name ?? D.Id) + (D.Form > 0 ? " (" + (string.IsNullOrEmpty(D.FormName) ? "form " + D.Form : D.FormName) + ")" : "") + "  " + D.Id; }
        }

        void Fill()
        {
            string q = _search.Text.Trim();
            _list.BeginUpdate(); _list.Items.Clear();
            foreach (var d in _all.Where(d => q.Length == 0 || (d.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || d.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                _list.Items.Add(new SpeciesItem { D = d });
            _list.EndUpdate();
            if (_list.Items.Count > 0 && _list.SelectedIndex < 0) _list.SelectedIndex = 0;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_search.Focused) return base.ProcessCmdKey(ref msg, keyData);
            var d = Current;
            if (d == null) return base.ProcessCmdKey(ref msg, keyData);
            int dx = 0, dy = 0, dalt = 0;
            switch (keyData)
            {
                case Keys.Left: dx = -1; break;
                case Keys.Right: dx = 1; break;
                case Keys.Up: dy = -1; break;
                case Keys.Down: dy = 1; break;
                case Keys.PageUp: dalt = 1; break;
                case Keys.PageDown: dalt = -1; break;
                default: return base.ProcessCmdKey(ref msg, keyData);
            }
            var off = _front.Checked ? (d.FrontSprite ?? (d.FrontSprite = new List<int>())) : (d.BackSprite ?? (d.BackSprite = new List<int>()));
            while (off.Count < 2) off.Add(0);
            off[0] += dx; off[1] += dy;
            if (_front.Checked) d.FrontSpriteAltitude = Math.Max(0, d.FrontSpriteAltitude + dalt);
            _dirty = true;
            Redraw();
            return true;
        }

        Image Asset(string folder, string name) { return Theme.LoadImage(_p.FindAsset(folder, name)); }

        Image Sprite(SpeciesData d, bool back)
        {
            string folder = back ? "Graphics/Pokemon/Back" : "Graphics/Pokemon/Front";
            var img = Asset(folder, d.Form > 0 ? d.Species + "_" + d.Form : d.Species) ?? Asset(folder, d.Species);
            if (img == null) return null;
            // Animated strips: the first frame
            if (img.Width > img.Height * 3 / 2)
            {
                var first = new Bitmap(img.Height, img.Height);
                using (var g = Graphics.FromImage(first)) g.DrawImage(img, new Rectangle(0, 0, img.Height, img.Height), new Rectangle(0, 0, img.Height, img.Height), GraphicsUnit.Pixel);
                img.Dispose();
                return first;
            }
            return img;
        }

        void Redraw()
        {
            var d = Current;
            var bmp = new Bitmap(512, 288);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                var bg = Asset("Graphics/Battlebacks", "field_bg");
                if (bg != null) { g.DrawImage(bg, 0, 0, bg.Width, bg.Height); bg.Dispose(); } else g.Clear(Color.FromArgb(160, 216, 248));
                var b1 = Asset("Graphics/Battlebacks", "field_base1");
                if (b1 != null) { g.DrawImage(b1, 384 - b1.Width / 2, 176 - b1.Height / 2, b1.Width, b1.Height); b1.Dispose(); }
                var b0 = Asset("Graphics/Battlebacks", "field_base0");
                if (b0 != null) { g.DrawImage(b0, 128 - b0.Width / 2, 288 - b0.Height, b0.Width, b0.Height); b0.Dispose(); }
                // Guides: the bases' centres
                using (var pen = new Pen(Color.FromArgb(120, 255, 0, 0))) { g.DrawLine(pen, 384, 168, 384, 184); g.DrawLine(pen, 376, 176, 392, 176); }
                if (d != null)
                {
                    var front = Sprite(d, false);
                    if (front != null)
                    {
                        var o = d.FrontSprite != null && d.FrontSprite.Count >= 2 ? d.FrontSprite : new List<int> { 0, 0 };
                        int bottom = 176 + o[1] * 2 - d.FrontSpriteAltitude * 2, x = 384 + o[0] * 2;
                        if (d.FrontSpriteAltitude > 0) using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillEllipse(sh, x + d.ShadowX * 2 - 30, 176 - 6, 60, 12);
                        g.DrawImage(front, x - front.Width / 2, bottom - front.Height, front.Width, front.Height);
                        front.Dispose();
                    }
                    var back = Sprite(d, true);
                    if (back != null)
                    {
                        var o = d.BackSprite != null && d.BackSprite.Count >= 2 ? d.BackSprite : new List<int> { 0, 0 };
                        int bottom = 304 + o[1] * 2, x = 128 + o[0] * 2;
                        g.DrawImage(back, x - back.Width / 2, bottom - back.Height, back.Width, back.Height);
                        back.Dispose();
                    }
                }
            }
            var old = _view.Image; _view.Image = bmp; if (old != null) old.Dispose();
            if (d != null)
            {
                Func<List<int>, string> s = l => l != null && l.Count >= 2 ? l[0] + ", " + l[1] : "0, 0";
                _info.Text = "Front offset " + s(d.FrontSprite) + "   altitude " + d.FrontSpriteAltitude + "   Back offset " + s(d.BackSprite) +
                             "\nArrow keys move the " + (_front.Checked ? "front" : "back") + " sprite (1 = 2 pixels); Page Up / Down change the altitude of floating Pokémon.";
            }
        }

        void Save()
        {
            Json.Json.ToFile(_p.DataFile("species.json"), _p.Species);
            _dirty = false;
        }
    }
}
