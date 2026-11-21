using System;
using PokeCreator.Maps;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// RMXP's panorama (a picture behind the map, scrolling at half speed) and fog (a picture tiled over the map that can
    /// drift, with its own opacity, blend and zoom). They come from the map's tileset and can be changed by event
    /// commands (Change Map Settings, Change Fog Opacity).
    /// </summary>
    public class MapPlanes
    {
        public string Panorama = "", Fog = "";
        public int PanoramaHue, FogHue, FogBlend, FogZoom = 100, FogSx, FogSy;
        public double FogOpacity = 64;
        double _fogOx, _fogOy;
        double _opacityTarget; int _opacityFrames;

        public void FromTileset(TilesetData ts)
        {
            Panorama = ts != null ? ts.PanoramaName ?? "" : ""; PanoramaHue = ts != null ? ts.PanoramaHue : 0;
            Fog = ts != null ? ts.FogName ?? "" : ""; FogHue = ts != null ? ts.FogHue : 0;
            FogOpacity = ts != null ? ts.FogOpacity : 64; FogBlend = ts != null ? ts.FogBlend : 0;
            FogZoom = ts != null && ts.FogZoom > 0 ? ts.FogZoom : 100; FogSx = ts != null ? ts.FogSx : 0; FogSy = ts != null ? ts.FogSy : 0;
            _fogOx = _fogOy = 0; _opacityFrames = 0;
        }

        /// <summary>Fog colour tone (Change Fog Color Tone): red, green, blue -255..255.</summary>
        public double[] FogTone = new double[4];
        double[] _toneTarget = new double[4]; int _toneFrames;
        public void ToneFog(double r, double gr, double b, double gray, int frames)
        {
            _toneTarget = new[] { r, gr, b, gray };
            if (frames <= 0) { FogTone = (double[])_toneTarget.Clone(); _toneFrames = 0; } else _toneFrames = frames;
        }

        /// <summary>Change Fog Opacity over a number of frames.</summary>
        public void FadeFog(int opacity, int frames)
        {
            if (frames <= 0) { FogOpacity = opacity; _opacityFrames = 0; return; }
            _opacityTarget = opacity; _opacityFrames = frames;
        }

        public void Update()
        {
            _fogOx -= FogSx / 8.0; _fogOy -= FogSy / 8.0;
            if (_opacityFrames > 0) { FogOpacity = (FogOpacity * (_opacityFrames - 1) + _opacityTarget) / _opacityFrames; _opacityFrames--; }
            if (_toneFrames > 0) { for (int i = 0; i < 4; i++) FogTone[i] = (FogTone[i] * (_toneFrames - 1) + _toneTarget[i]) / _toneFrames; _toneFrames--; }
        }

        public void DrawPanorama(Game g, int displayX, int displayY)
        {
            if (string.IsNullOrEmpty(Panorama)) return;
            var tex = g.Gfx.LoadHued(g.Project.FindAsset("Graphics/Panoramas", Panorama), PanoramaHue);
            if (tex == null) return;
            DrawTiled(g.Gfx, tex, displayX / 2.0, displayY / 2.0, 1, 255, 0);
        }

        public void DrawFog(Game g, int displayX, int displayY)
        {
            if (string.IsNullOrEmpty(Fog) || FogOpacity <= 0) return;
            var tex = g.Gfx.LoadHued(g.Project.FindAsset("Graphics/Fogs", Fog), FogHue);
            if (tex == null) return;
            // A tone pulls the fog's colour toward the tone's (positive channels)
            double r = Math.Max(0, FogTone[0]), gr = Math.Max(0, FogTone[1]), b = Math.Max(0, FogTone[2]);
            double strength = Math.Max(r, Math.Max(gr, b));
            var tint = strength > 0 ? new Color((byte)Math.Min(255, r), (byte)Math.Min(255, gr), (byte)Math.Min(255, b), (byte)Math.Min(255, strength)) : default(Color);
            DrawTiled(g.Gfx, tex, displayX - _fogOx, displayY - _fogOy, FogZoom / 100.0, (int)FogOpacity, FogBlend, tint);
        }

        /// <summary>Repeats the picture across the screen, offset by (ox, oy) screen pixels.</summary>
        static void DrawTiled(Gfx gfx, Texture tex, double ox, double oy, double zoom, int opacity, int blend, Color tint = default(Color))
        {
            double w = tex.Width * zoom, h = tex.Height * zoom;
            if (w < 1 || h < 1) return;
            double sx = -Mod(ox, w), sy = -Mod(oy, h);
            for (double y = sy; y < gfx.Height; y += h)
                for (double x = sx; x < gfx.Width; x += w)
                    gfx.DrawEx(tex, 0, 0, tex.Width, tex.Height, x + w / 2, y + h / 2, zoom, zoom, 0, false, opacity, blend, tint);
        }

        static double Mod(double a, double m) { double r = a % m; return r < 0 ? r + m : r; }
    }
}
