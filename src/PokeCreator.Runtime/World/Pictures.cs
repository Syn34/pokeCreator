using System;
using System.Collections.Generic;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// RMXP pictures (Show / Move / Rotate / Tint / Erase Picture): images from Graphics/Pictures drawn over the map,
    /// used for intros and cutscenes. Times are in RMXP frames (1/40 s).
    /// </summary>
    public class Picture
    {
        public string Name;
        public int Origin;                   // 0 top-left, 1 centre
        public double X, Y, ZoomX = 100, ZoomY = 100, Opacity = 255;
        public int Blend;
        public double Angle, RotateSpeed;
        double _tx, _ty, _tzx, _tzy, _top;
        int _frames;
        /// <summary>Change Picture Tone: red, green, blue (-255..255), gray (ignored), changing over a duration.</summary>
        public double[] Tone = new double[4];
        double[] _toneTarget = new double[4];
        int _toneFrames;

        public void SetTone(double[] tone, int frames)
        {
            for (int i = 0; i < 4 && i < tone.Length; i++) _toneTarget[i] = tone[i];
            _toneFrames = Math.Max(0, frames);
            if (_toneFrames == 0) Array.Copy(_toneTarget, Tone, 4);
        }

        public void Show(string name, int origin, double x, double y, double zx, double zy, double opacity, int blend)
        {
            Name = name; Origin = origin; X = x; Y = y; ZoomX = zx; ZoomY = zy; Opacity = opacity; Blend = blend;
            Angle = 0; RotateSpeed = 0; _frames = 0;
        }

        public void Move(int duration, int origin, double x, double y, double zx, double zy, double opacity, int blend)
        {
            Origin = origin; Blend = blend;
            _tx = x; _ty = y; _tzx = zx; _tzy = zy; _top = opacity;
            _frames = Math.Max(1, duration * 3 / 2);
            if (duration <= 0) { X = x; Y = y; ZoomX = zx; ZoomY = zy; Opacity = opacity; _frames = 0; }
        }

        public void Update()
        {
            if (_frames > 0)
            {
                X = (X * (_frames - 1) + _tx) / _frames;
                Y = (Y * (_frames - 1) + _ty) / _frames;
                ZoomX = (ZoomX * (_frames - 1) + _tzx) / _frames;
                ZoomY = (ZoomY * (_frames - 1) + _tzy) / _frames;
                Opacity = (Opacity * (_frames - 1) + _top) / _frames;
                _frames--;
            }
            if (RotateSpeed != 0) Angle = (Angle + RotateSpeed / 2.0) % 360;
            if (_toneFrames > 0)
            {
                for (int i = 0; i < 4; i++) Tone[i] = (Tone[i] * (_toneFrames - 1) + _toneTarget[i]) / _toneFrames;
                _toneFrames--;
            }
        }

        public void Draw(Gfx g, Game game)
        {
            if (string.IsNullOrEmpty(Name) || Opacity <= 0) return;
            var tex = g.Load(game.Project.FindAsset("Graphics/Pictures", Name));
            if (tex == null) return;
            double w = tex.Width * ZoomX / 100, h = tex.Height * ZoomY / 100;
            // Centre point (rotation happens around it); origin 0 = X/Y is the top-left corner
            double cx = Origin == 1 ? X : X + w / 2, cy = Origin == 1 ? Y : Y + h / 2;
            g.DrawToned(tex, 0, 0, tex.Width, tex.Height, cx, cy, ZoomX / 100, ZoomY / 100, Angle, (int)Math.Min(255, Opacity), Blend, (int)Tone[0], (int)Tone[1], (int)Tone[2]);
        }
    }

    public class PictureSet
    {
        readonly Dictionary<int, Picture> _pics = new Dictionary<int, Picture>();

        public Picture Get(int n) { Picture p; if (!_pics.TryGetValue(n, out p)) _pics[n] = p = new Picture(); return p; }
        public void Erase(int n) { _pics.Remove(n); }
        public void Clear() { _pics.Clear(); }
        public void Update() { foreach (var p in _pics.Values) p.Update(); }
        public void Draw(Gfx g, Game game)
        {
            var keys = new List<int>(_pics.Keys); keys.Sort();
            foreach (var k in keys) _pics[k].Draw(g, game);
        }
    }
}
