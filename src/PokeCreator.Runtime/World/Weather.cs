using System;
using System.Collections.Generic;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// Overworld weather, drawn over the map: rain, storm, snow, blizzard, sandstorm, heavy rain, sun and fog.
    /// Type numbers follow Essentials (used by the Set Weather Effects command, code 236).
    /// </summary>
    public class WeatherSystem
    {
        public static readonly string[] Names = { "None", "Rain", "Storm", "Snow", "Blizzard", "Sandstorm", "HeavyRain", "Sun", "Fog" };
        public const int None = 0, Rain = 1, Storm = 2, Snow = 3, Blizzard = 4, Sandstorm = 5, HeavyRain = 6, Sun = 7, Fog = 8;

        class Particle { public double X, Y, Vx, Vy, Phase; public int Size; }

        public int Type { get; private set; }
        /// <summary>Strength 0-9 the weather is heading to; Current fades toward it.</summary>
        public int Power { get; private set; }
        double _current;
        double _step;
        int _lightning, _nextLightning = 200;
        readonly List<Particle> _parts = new List<Particle>();
        readonly Random _rng = new Random();
        int _lastDx, _lastDy;
        bool _haveLast;

        public static int TypeFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return None;
            int i = Array.FindIndex(Names, n => n.Equals(name.Trim().TrimStart(':'), StringComparison.OrdinalIgnoreCase));
            return i < 0 ? None : i;
        }

        /// <summary>Changes the weather. duration is in RMXP frames (1/40 second); 0 = at once.</summary>
        public void Set(int type, int power, int duration)
        {
            power = Math.Max(0, Math.Min(9, power));
            if (type < 0 || type >= Names.Length) type = None;
            if (type == None) power = 0;
            int frames = Math.Max(0, duration * 3 / 2);
            if (type == Type || Type == None || _current <= 0.01 || frames == 0)
            {
                if (type != Type) { Type = type; _parts.Clear(); _current = frames == 0 ? power : 0; }
                _pendingType = -1; Power = power;
                if (frames == 0) _current = power;
                _step = frames == 0 ? 99 : Math.Max(0.01, Math.Abs(Power - _current) / frames);
            }
            else
            {
                // Another weather is showing: fade it out over half the time, then fade the new one in
                _pendingType = type; _pendingPower = power;
                _step = Math.Max(0.01, _current / Math.Max(1, frames / 2.0));
            }
        }
        int _pendingType = -1, _pendingPower;

        public void Update(int displayX, int displayY, int w, int h)
        {
            // Fading out an old type before switching
            if (_pendingType >= 0)
            {
                _current = Math.Max(0, _current - _step);
                if (_current <= 0) { Type = _pendingType; Power = _pendingPower; _pendingType = -1; _parts.Clear(); }
            }
            else if (_current < Power) _current = Math.Min(Power, _current + _step);
            else if (_current > Power) _current = Math.Max(Power, _current - _step);

            // Particles stay put in the world while the camera scrolls
            if (_haveLast)
            {
                int ddx = displayX - _lastDx, ddy = displayY - _lastDy;
                if (Math.Abs(ddx) < 64 && Math.Abs(ddy) < 64)
                    foreach (var p in _parts) { p.X -= ddx; p.Y -= ddy; }
            }
            _lastDx = displayX; _lastDy = displayY; _haveLast = true;

            int want = (int)Math.Round(MaxParticles() * _current / 9.0);
            while (_parts.Count < want) _parts.Add(NewParticle(w, h, _parts.Count < want / 2 && _parts.Count == 0 ? true : _rng.Next(4) == 0));
            if (_parts.Count > want) _parts.RemoveRange(want, _parts.Count - want);

            foreach (var p in _parts)
            {
                p.Phase += 0.05;
                p.X += p.Vx + (Type == Snow ? Math.Sin(p.Phase) * 0.6 : 0);
                p.Y += p.Vy;
                if (p.Y > h + 16 || p.X < -48 || p.X > w + 48 || p.Y < -64)
                {
                    var n = NewParticle(w, h, false);
                    p.X = n.X; p.Y = n.Y; p.Vx = n.Vx; p.Vy = n.Vy; p.Size = n.Size;
                }
            }

            if (Type == Storm && _current > 2)
            {
                if (_lightning > 0) _lightning--;
                else if (--_nextLightning <= 0) { _lightning = 8; _nextLightning = 120 + _rng.Next(360); }
            }
            else _lightning = 0;
        }

        int MaxParticles()
        {
            switch (Type)
            {
                case Rain: return 60;
                case Storm: return 120;
                case HeavyRain: return 180;
                case Snow: return 100;
                case Blizzard: return 160;
                case Sandstorm: return 100;
                case Fog: return 10;
                default: return 0;
            }
        }

        Particle NewParticle(int w, int h, bool anywhere)
        {
            var p = new Particle { Phase = _rng.NextDouble() * 6.28 };
            switch (Type)
            {
                case Rain: case Storm: case HeavyRain:
                    {
                        double speed = Type == Rain ? 10 : Type == Storm ? 14 : 16;
                        p.Vx = -speed * 0.35; p.Vy = speed; p.Size = Type == Rain ? 12 : 16;
                        p.X = _rng.Next(-16, w + 96); p.Y = anywhere ? _rng.Next(-32, h) : _rng.Next(-64, -8);
                        break;
                    }
                case Snow:
                    p.Vx = -0.4; p.Vy = 1 + _rng.NextDouble() * 1.5; p.Size = 3 + _rng.Next(3);
                    p.X = _rng.Next(-16, w + 32); p.Y = anywhere ? _rng.Next(-16, h) : _rng.Next(-32, -4);
                    break;
                case Blizzard:
                    p.Vx = -5 - _rng.NextDouble() * 3; p.Vy = 3 + _rng.NextDouble() * 2; p.Size = 2 + _rng.Next(3);
                    p.X = anywhere ? _rng.Next(0, w + 48) : (_rng.Next(2) == 0 ? w + 16 : _rng.Next(0, w + 48)); p.Y = anywhere ? _rng.Next(-16, h) : _rng.Next(-32, h / 2);
                    break;
                case Sandstorm:
                    p.Vx = -8 - _rng.NextDouble() * 4; p.Vy = 1 + _rng.NextDouble(); p.Size = 6 + _rng.Next(10);
                    p.X = anywhere ? _rng.Next(0, w + 48) : w + 16 + _rng.Next(32); p.Y = _rng.Next(-16, h);
                    break;
                case Fog:
                    p.Vx = -0.3 - _rng.NextDouble() * 0.3; p.Vy = 0; p.Size = 96 + _rng.Next(96);
                    p.X = anywhere ? _rng.Next(-64, w + 64) : w + p.Size; p.Y = _rng.Next(-32, h);
                    break;
            }
            return p;
        }

        public void Draw(Gfx g)
        {
            if (Type == None || _current <= 0.01) return;
            double k = _current / 9.0;
            // Screen tint
            switch (Type)
            {
                case Rain: g.FillRect(0, 0, g.Width, g.Height, new Color(16, 24, 56, (byte)(40 * k))); break;
                case Storm: g.FillRect(0, 0, g.Width, g.Height, new Color(8, 8, 40, (byte)(90 * k))); break;
                case HeavyRain: g.FillRect(0, 0, g.Width, g.Height, new Color(8, 8, 32, (byte)(110 * k))); break;
                case Snow: g.FillRect(0, 0, g.Width, g.Height, new Color(200, 216, 248, (byte)(24 * k))); break;
                case Blizzard: g.FillRect(0, 0, g.Width, g.Height, new Color(216, 224, 248, (byte)(70 * k))); break;
                case Sandstorm: g.FillRect(0, 0, g.Width, g.Height, new Color(184, 136, 72, (byte)(80 * k))); break;
                case Sun:
                    {
                        double pulse = 0.75 + 0.25 * Math.Sin(Environment.TickCount / 600.0);
                        g.FillRect(0, 0, g.Width, g.Height, new Color(255, 232, 160, (byte)(56 * k * pulse)));
                        break;
                    }
                case Fog: g.FillRect(0, 0, g.Width, g.Height, new Color(232, 232, 240, (byte)(70 * k))); break;
            }
            foreach (var p in _parts)
            {
                int x = (int)p.X, y = (int)p.Y;
                switch (Type)
                {
                    case Rain: case Storm: case HeavyRain:
                        {
                            var c = new Color(200, 216, 248, 170);
                            for (int i = 0; i < p.Size; i += 2) g.FillRect(x - (int)(i * 0.35), y - p.Size + i, 2, 2, c);
                            break;
                        }
                    case Snow: case Blizzard:
                        g.FillRect(x, y, p.Size, p.Size, new Color(248, 248, 255, 230));
                        if (p.Size > 2) { g.FillRect(x - 1, y + 1, p.Size + 2, p.Size - 2, new Color(248, 248, 255, 120)); }
                        break;
                    case Sandstorm:
                        g.FillRect(x, y, p.Size, 2, new Color(216, 168, 96, 180));
                        break;
                    case Fog:
                        g.FillRect(x, y, p.Size, p.Size / 3, new Color(240, 240, 248, 40));
                        g.FillRect(x + p.Size / 6, y - p.Size / 8, p.Size * 2 / 3, p.Size / 2, new Color(240, 240, 248, 30));
                        break;
                }
            }
            if (_lightning > 0) g.FillRect(0, 0, g.Width, g.Height, new Color(255, 255, 255, (byte)(_lightning * 20)));
        }
    }
}
