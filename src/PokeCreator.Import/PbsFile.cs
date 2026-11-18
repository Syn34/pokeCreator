using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PokeCreator.Import
{
    public class PbsLine
    {
        public string Key;      // null for lines without '='
        public string Value;    // text after '=' (or the whole line)
        public bool Indented;   // line began with whitespace (trainer Pokémon properties, encounter slots)
        public int LineNumber;
    }

    public class PbsSection
    {
        public string Header;   // text inside [ ]
        public string Comment;  // trailing "# comment" after the header
        public List<PbsLine> Lines = new List<PbsLine>();

        public string Get(string key)
        {
            foreach (var l in Lines) if (l.Key != null && string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase)) return l.Value;
            return null;
        }
    }

    /// <summary>Reads Essentials PBS text files ([Section] blocks of Key = Value lines).</summary>
    public static class PbsFile
    {
        public static List<PbsSection> Read(string path, out List<PbsLine> loose)
        {
            var sections = new List<PbsSection>();
            loose = new List<PbsLine>();
            PbsSection cur = null;
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int n = 0; n < lines.Length; n++)
            {
                string raw = lines[n];
                if (n == 0 && raw.Length > 0 && raw[0] == '﻿') raw = raw.Substring(1);
                string trimmed = raw.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

                if (trimmed.StartsWith("["))
                {
                    int close = trimmed.IndexOf(']');
                    if (close > 0)
                    {
                        cur = new PbsSection { Header = trimmed.Substring(1, close - 1).Trim() };
                        string rest = trimmed.Substring(close + 1).Trim();
                        if (rest.StartsWith("#")) cur.Comment = rest.Substring(1).Trim();
                        sections.Add(cur);
                        continue;
                    }
                }

                var line = new PbsLine { LineNumber = n + 1, Indented = raw.Length > 0 && char.IsWhiteSpace(raw[0]) };
                int eq = trimmed.IndexOf('=');
                if (eq > 0 && IsKey(trimmed.Substring(0, eq).Trim()))
                {
                    line.Key = trimmed.Substring(0, eq).Trim();
                    line.Value = trimmed.Substring(eq + 1).Trim();
                }
                else line.Value = trimmed;
                if (cur != null) cur.Lines.Add(line); else loose.Add(line);
            }
            return sections;
        }

        static bool IsKey(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        /// <summary>Splits a comma separated value list, trimming each entry.</summary>
        public static List<string> Csv(string v)
        {
            var r = new List<string>();
            if (string.IsNullOrEmpty(v)) return r;
            foreach (var p in v.Split(',')) r.Add(p.Trim());
            return r;
        }
    }
}
