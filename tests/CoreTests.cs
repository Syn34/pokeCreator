using System;
using System.IO;
using System.Linq;
using PokeCreator.Json;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Editors;

// Quick self-checks: run with  mono bin/CoreTests.exe <project folder>
public static class CoreTests
{
    static int _fail;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) _fail++; }

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

        if (args.Length > 0)
        {
            var p = Project.Load(args[0]);
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
        Console.WriteLine(_fail == 0 ? "All tests passed" : _fail + " test(s) failed");
        return _fail;
    }
}
