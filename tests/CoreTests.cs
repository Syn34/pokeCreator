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
        Console.WriteLine(_fail == 0 ? "All tests passed" : _fail + " test(s) failed");
        return _fail;
    }
}
