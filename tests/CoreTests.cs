using System;
using System.IO;
using System.Linq;
using PokeCreator.Json;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Editors;
using PokeCreator.Pkmn;

// Quick self-checks: run with  mono bin/CoreTests.exe <project folder>
public static class CoreTests
{
    static int _fail;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) _fail++; }

    /// <summary>Where the runtime (GameMap.LoadNeighbors) puts map2 relative to map1.</summary>
    static System.Drawing.Point Neighbour(PokeCreator.Data.MapConnection c, MapData m1, MapData m2)
    {
        Func<string, int, MapData, System.Drawing.Point> edge = (e, o, m) => e == "North" ? new System.Drawing.Point(o, 0) : e == "South" ? new System.Drawing.Point(o, m.Height) : e == "East" ? new System.Drawing.Point(m.Width, o) : new System.Drawing.Point(0, o);
        var p1 = edge(c.Edge1, c.Offset1, m1); var p2 = edge(c.Edge2, c.Offset2, m2);
        return new System.Drawing.Point(p1.X - p2.X, p1.Y - p2.Y);
    }

    public static int Main(string[] args)
    {
        // Autotile shapes
        Check(TileGraphics.PatternFor((dx, dy) => true) == 0, "autotile fully surrounded -> pattern 0");
        Check(TileGraphics.PatternFor((dx, dy) => false) == 46, "autotile isolated -> bordered single tile (46)");
        Check(TileGraphics.PatternFor((dx, dy) => dy >= 0) == 20, "autotile with nothing above -> top edge (20)");
        Check(TileGraphics.PatternFor((dx, dy) => !(dx == -1 && dy == -1)) == 1, "autotile missing top-left diagonal -> inner corner (1)");

        // JSON round trip keeps non-default booleans (e.g. WalkAnime=false)
        var pg = new EventPage { WalkAnime = false, Trigger = 3 };
        var back = Json.Deserialize<EventPage>(Json.Write(pg));
        Check(back.WalkAnime == false && back.Trigger == 3, "JSON keeps WalkAnime=false and Trigger");

        // Growth-rate EXP curves (official values)
        Check(GrowthRates.ExpForLevel("Medium", 100) == 1000000, "Medium Fast L100 = 1,000,000");
        Check(GrowthRates.ExpForLevel("Parabolic", 5) == 135, "Medium Slow L5 = 135");
        Check(GrowthRates.ExpForLevel("Parabolic", 100) == 1059860, "Medium Slow L100 = 1,059,860");
        Check(GrowthRates.ExpForLevel("Erratic", 100) == 600000, "Erratic L100 = 600,000");
        Check(GrowthRates.ExpForLevel("Fluctuating", 100) == 1640000, "Fluctuating L100 = 1,640,000");
        Check(GrowthRates.ExpForLevel("Fast", 100) == 800000 && GrowthRates.ExpForLevel("Slow", 100) == 1250000, "Fast / Slow L100");
        Check(GrowthRates.LevelForExp("Parabolic", 135) == 5 && GrowthRates.LevelForExp("Parabolic", 134) == 4, "level from EXP");
        Check(Natures.Raised("ADAMANT") == Stat.Attack && Natures.Lowered("ADAMANT") == Stat.SpAtk && Natures.Raised("HARDY") == -1, "nature modifiers");

        if (args.Length > 0)
        {
            var p = Project.Load(args[0]);
            // Garchomp L78, Adamant, IVs 24/12/30/5/16/23, EVs 74/190/91/23/48/84 (Bulbapedia example) -> 289/278/193/171/135/171
            var gc = new Pokemon { Species = "GARCHOMP", Level = 78, Nature = "ADAMANT", IV = new System.Collections.Generic.List<int> { 24, 12, 30, 5, 16, 23 }, EV = new System.Collections.Generic.List<int> { 74, 190, 91, 23, 48, 84 } };
            var st = gc.Stats(p);
            Check(st[Stat.HP] == 289 && st[Stat.Attack] == 278 && st[Stat.Defense] == 193 && st[Stat.SpAtk] == 135 && st[Stat.SpDef] == 171 && st[Stat.Speed] == 171,
                  "Garchomp stat formula (" + string.Join(",", st) + ")");
            var made = Pokemon.Create(p, "PIKACHU", 10, new Random(1));
            Check(made.Moves.Count > 0 && made.HP == made.TotalHP(p) && made.Exp == GrowthRates.ExpForLevel("Medium", 10), "Pokemon.Create gives moves, full HP and matching EXP");
            Check(p.Species.Count > 1000, "project loads species (" + p.Species.Count + ")");
            var m = p.LoadMap(2);
            string before = Json.Write(m);
            p.SaveMap(m); p.ForgetMap(2);
            string after = Json.Write(p.LoadMap(2));
            Check(before == after, "map 2 save/load round trip is identical");
            p.Save();
            var p2 = Project.Load(args[0]);
            Check(Json.Write(p2.Species) == Json.Write(p.Species), "species save/load round trip is identical");
            Check(p2.Trainers.Any(t => t.Pokemon.Any(pk => pk.Shiny)), "trainer Pokémon options survive (shiny Onix)");
        }
        // World map: connections made by touching edges must put the neighbour where the game expects it
        foreach (var t in new[] { new[] { 30, 5 }, new[] { -10, 0 }, new[] { 4, -30 } })
        {
            var a = new WorldEditor.Placed { Id = 1, Map = MapData.Create(1, 30, 20, 1), X = 0, Y = 0 };
            var east = new WorldEditor.Placed { Id = 2, Map = MapData.Create(2, 25, 40, 1), X = 30, Y = t[1] };
            var south = new WorldEditor.Placed { Id = 3, Map = MapData.Create(3, 50, 10, 1), X = t[0] - 20, Y = 20 };
            var ce = WorldEditor.Touching(a, east); var cs = WorldEditor.Touching(a, south);
            Check(ce != null && Neighbour(ce, a.Map, east.Map) == new System.Drawing.Point(30, t[1]), "world: east neighbour at offset " + t[1]);
            Check(cs != null && Neighbour(cs, a.Map, south.Map) == new System.Drawing.Point(t[0] - 20, 20), "world: south neighbour at offset " + (t[0] - 20));
        }
        Check(WorldEditor.Touching(new WorldEditor.Placed { Id = 1, Map = MapData.Create(1, 10, 10, 1) }, new WorldEditor.Placed { Id = 2, Map = MapData.Create(2, 10, 10, 1), X = 11 }) == null, "world: a gap means no connection");

        // Tileset import: a 16 px sheet 20 tiles wide becomes 256 px wide, doubled and stacked in strips
        using (var b = new System.Drawing.Bitmap(320, 32))
        {
            b.SetPixel(0, 0, System.Drawing.Color.Red); b.SetPixel(128, 16, System.Drawing.Color.Blue);
            string note;
            using (var r = TilesetImport.ToEngineTileset(b, 16, out note))
            {
                Check(r.Width == 256 && r.Height == 64 * 3, "tileset import: 20 tiles of 16 px -> 3 strips of 8 at 32 px (" + r.Width + "x" + r.Height + ")");
                Check(r.GetPixel(1, 1).ToArgb() == System.Drawing.Color.Red.ToArgb() && r.GetPixel(1, 64 + 33).ToArgb() == System.Drawing.Color.Blue.ToArgb(), "tileset import: pixels land in the right tiles");
            }
        }

        Console.WriteLine(_fail == 0 ? "All tests passed" : _fail + " test(s) failed");
        return _fail;
    }
}
