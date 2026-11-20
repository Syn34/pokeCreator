using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PokeCreator.Ruby
{
    public class RString
    {
        public byte[] Bytes;
        public RString(byte[] b) { Bytes = b; }
        public override string ToString() { return Encoding.UTF8.GetString(Bytes); }
    }

    public class RSymbol
    {
        public readonly string Name;
        public RSymbol(string n) { Name = n; }
        public override string ToString() { return Name; }
    }

    /// <summary>An Array subclass (Essentials' PBAnimations, PBAnimation...) with its instance variables.</summary>
    public class RUserArray : List<object>
    {
        public string ClassName;
        public Dictionary<string, object> Ivars = new Dictionary<string, object>();
        public RUserArray(string cls) { ClassName = cls; }
        public object Get(string name) { object v; return Ivars.TryGetValue(name, out v) ? v : null; }
        public int Int(string name) { var v = Get(name); return v == null ? 0 : (v is bool ? ((bool)v ? 1 : 0) : Convert.ToInt32(v, CultureInfo.InvariantCulture)); }
        public string Str(string name) { var v = Get(name); return v == null ? "" : v.ToString(); }
    }

    public class RHash
    {
        public List<KeyValuePair<object, object>> Pairs = new List<KeyValuePair<object, object>>();
        public object Default;
    }

    public class RObject
    {
        public string ClassName;
        public Dictionary<string, object> Ivars = new Dictionary<string, object>();
        public RObject(string cls) { ClassName = cls; }

        public object Get(string name) { object v; return Ivars.TryGetValue(name, out v) ? v : null; }
        public int Int(string name) { var v = Get(name); return v == null ? 0 : (v is bool ? ((bool)v ? 1 : 0) : Convert.ToInt32(v, CultureInfo.InvariantCulture)); }
        public bool Bool(string name) { var v = Get(name); return v is bool && (bool)v; }
        public string Str(string name) { var v = Get(name); return v == null ? "" : v.ToString(); }
        public List<object> List(string name) { return Get(name) as List<object> ?? new List<object>(); }
        public RObject Obj(string name) { return Get(name) as RObject; }
        public override string ToString() { return "#<" + ClassName + ">"; }
    }

    /// <summary>Objects stored with a custom _dump (RGSS Table, Color, Tone).</summary>
    public class RUserData
    {
        public string ClassName;
        public byte[] Data;
        public RUserData(string cls, byte[] data) { ClassName = cls; Data = data; }

        /// <summary>Decodes an RGSS Table into a flat int array indexed x + y*xsize + z*xsize*ysize.</summary>
        public RTable AsTable()
        {
            using (var br = new BinaryReader(new MemoryStream(Data)))
            {
                var t = new RTable();
                br.ReadInt32(); // dimensions
                t.XSize = br.ReadInt32(); t.YSize = br.ReadInt32(); t.ZSize = br.ReadInt32();
                int n = br.ReadInt32();
                t.Data = new int[n];
                for (int i = 0; i < n; i++) t.Data[i] = br.ReadInt16();
                return t;
            }
        }

        /// <summary>RGSS Color / Tone: four doubles.</summary>
        public double[] AsDoubles()
        {
            int n = Data.Length / 8; var d = new double[n];
            for (int i = 0; i < n; i++) d[i] = BitConverter.ToDouble(Data, i * 8);
            return d;
        }
    }

    public class RTable
    {
        public int XSize, YSize, ZSize;
        public int[] Data;
        public int this[int x, int y = 0, int z = 0]
        {
            get
            {
                int i = x + y * XSize + z * XSize * YSize;
                return i >= 0 && i < Data.Length ? Data[i] : 0;
            }
        }
    }

    /// <summary>Reader for Ruby's Marshal format 4.8, as used by RPG Maker XP's .rxdata files.</summary>
    public class MarshalReader
    {
        readonly BinaryReader _r;
        readonly List<string> _symbols = new List<string>();
        readonly List<object> _objects = new List<object>();

        MarshalReader(Stream s) { _r = new BinaryReader(s); }

        public static object Load(string path)
        {
            using (var fs = File.OpenRead(path)) return Load(fs);
        }

        public static object Load(Stream s)
        {
            var m = new MarshalReader(s);
            int major = m._r.ReadByte(), minor = m._r.ReadByte();
            if (major != 4 || minor > 8) throw new InvalidDataException("Not a Ruby Marshal file (version " + major + "." + minor + ")");
            return m.ReadValue();
        }

        int ReadFixnum()
        {
            sbyte c = (sbyte)_r.ReadByte();
            if (c == 0) return 0;
            if (c > 0)
            {
                if (c > 4) return c - 5;
                int x = 0;
                for (int i = 0; i < c; i++) x |= _r.ReadByte() << (8 * i);
                return x;
            }
            if (c < -4) return c + 5;
            int n = -c, v = -1;
            for (int i = 0; i < n; i++) { v &= ~(0xff << (8 * i)); v |= _r.ReadByte() << (8 * i); }
            return v;
        }

        int PeekByte() { long p = _r.BaseStream.Position; int b = _r.BaseStream.ReadByte(); _r.BaseStream.Position = p; return b; }

        byte[] ReadBytes() { int n = ReadFixnum(); return _r.ReadBytes(n); }

        string ReadSymbol()
        {
            byte t = _r.ReadByte();
            if (t == ':') { var s = Encoding.UTF8.GetString(ReadBytes()); _symbols.Add(s); return s; }
            if (t == ';') return _symbols[ReadFixnum()];
            if (t == 'I')
            {
                // Symbol with encoding ivars
                var s = ReadSymbol(); int n = ReadFixnum();
                for (int i = 0; i < n; i++) { ReadSymbol(); ReadValue(); }
                return s;
            }
            throw new InvalidDataException("Expected symbol, got '" + (char)t + "'");
        }

        object ReadValue()
        {
            byte t = _r.ReadByte();
            switch ((char)t)
            {
                case '0': return null;
                case 'T': return true;
                case 'F': return false;
                case 'i': return ReadFixnum();
                case ':': { var s = Encoding.UTF8.GetString(ReadBytes()); _symbols.Add(s); return new RSymbol(s); }
                case ';': return new RSymbol(_symbols[ReadFixnum()]);
                case '@': return _objects[ReadFixnum()];
                case 'I':
                    {
                        var obj = ReadValue();
                        int n = ReadFixnum();
                        for (int i = 0; i < n; i++)
                        {
                            string k = ReadSymbol(); var v = ReadValue();
                            var ro = obj as RObject; if (ro != null) ro.Ivars[k.TrimStart('@')] = v;
                            var ua = obj as RUserArray; if (ua != null) ua.Ivars[k.TrimStart('@')] = v;
                        }
                        return obj;
                    }
                case '"': { var s = new RString(ReadBytes()); _objects.Add(s); return s; }
                case '[':
                    {
                        var a = new List<object>(); _objects.Add(a);
                        int n = ReadFixnum();
                        for (int i = 0; i < n; i++) a.Add(ReadValue());
                        return a;
                    }
                case '{':
                case '}':
                    {
                        var h = new RHash(); _objects.Add(h);
                        int n = ReadFixnum();
                        for (int i = 0; i < n; i++) { var k = ReadValue(); var v = ReadValue(); h.Pairs.Add(new KeyValuePair<object, object>(k, v)); }
                        if (t == '}') h.Default = ReadValue();
                        return h;
                    }
                case 'o':
                    {
                        var o = new RObject(ReadSymbol()); _objects.Add(o);
                        int n = ReadFixnum();
                        for (int i = 0; i < n; i++) { string k = ReadSymbol(); o.Ivars[k.TrimStart('@')] = ReadValue(); }
                        return o;
                    }
                case 'S':
                    {
                        var o = new RObject(ReadSymbol()); _objects.Add(o);
                        int n = ReadFixnum();
                        for (int i = 0; i < n; i++) { string k = ReadSymbol(); o.Ivars[k] = ReadValue(); }
                        return o;
                    }
                case 'u': { var cls = ReadSymbol(); var u = new RUserData(cls, ReadBytes()); _objects.Add(u); return u; }
                case 'U': { var o = new RObject(ReadSymbol()); _objects.Add(o); o.Ivars["_data"] = ReadValue(); return o; }
                case 'f':
                    {
                        string s = Encoding.ASCII.GetString(ReadBytes());
                        double d;
                        if (s == "inf") d = double.PositiveInfinity;
                        else if (s == "-inf") d = double.NegativeInfinity;
                        else if (s == "nan") d = double.NaN;
                        else
                        {
                            int nul = s.IndexOf('\0'); if (nul >= 0) s = s.Substring(0, nul);
                            d = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                        }
                        _objects.Add(d); return d;
                    }
                case 'l':
                    {
                        byte sign = _r.ReadByte(); int len = ReadFixnum() * 2;
                        var bytes = _r.ReadBytes(len); long v = 0;
                        for (int i = Math.Min(len, 8) - 1; i >= 0; i--) v = (v << 8) | bytes[i];
                        if (sign == '-') v = -v;
                        object o = v; _objects.Add(o); return o;
                    }
                case 'e': ReadSymbol(); return ReadValue();
                case 'C':
                    {
                        string cls = ReadSymbol();
                        if (_r.BaseStream.CanSeek && PeekByte() == '[')
                        {
                            _r.ReadByte();
                            var a = new RUserArray(cls); _objects.Add(a);
                            int n = ReadFixnum();
                            for (int i = 0; i < n; i++) a.Add(ReadValue());
                            return a;
                        }
                        return ReadValue();
                    }
                case 'c':
                case 'm':
                case 'M': { var s = Encoding.UTF8.GetString(ReadBytes()); var o = new RSymbol(s); _objects.Add(o); return o; }
                case '/': { var s = new RString(ReadBytes()); _r.ReadByte(); _objects.Add(s); return s; }
                default:
                    throw new InvalidDataException("Unsupported Marshal type '" + (char)t + "' at byte " + _r.BaseStream.Position);
            }
        }
    }
}
