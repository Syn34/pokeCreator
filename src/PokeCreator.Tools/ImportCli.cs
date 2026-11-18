using System;
using System.Collections.Generic;
using System.IO;
using PokeCreator.Data;
using PokeCreator.Import;

namespace PokeCreator.Tools
{
    /// <summary>
    /// Command line:
    ///   PokeCreatorImport.exe "C:\path\to\Essentials" "C:\path\to\NewProject" [--no-assets] [--no-base]
    ///   PokeCreatorImport.exe --normalize "folder with species.json, moves.json ..."   (rewrites them in PokeCreator's format)
    /// </summary>
    public static class ImportCli
    {
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            if (args.Length >= 2 && args[0] == "--normalize") return Normalize(args[1]);
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: PokeCreatorImport.exe <essentials folder> <new project folder> [--no-assets] [--no-base]");
                Console.WriteLine("       PokeCreatorImport.exe --normalize <data folder>");
                return 1;
            }
            var imp = new EssentialsImporter { Log = Console.WriteLine };
            if (Array.IndexOf(args, "--no-assets") >= 0) imp.CopyAssets = false;
            if (Array.IndexOf(args, "--no-base") >= 0) imp.BaseDataFolder = null;
            try { imp.Import(args[0], args[1]); }
            catch (Exception ex) { Console.WriteLine("Import failed: " + ex.Message); return 2; }
            return 0;
        }

        static int Normalize(string dir)
        {
            Rewrite<List<SpeciesData>>(dir, "species.json");
            Rewrite<List<MoveData>>(dir, "moves.json");
            Rewrite<List<AbilityData>>(dir, "abilities.json");
            Rewrite<List<ItemData>>(dir, "items.json");
            Rewrite<List<TypeData>>(dir, "types.json");
            Rewrite<List<TrainerTypeData>>(dir, "trainer_types.json");
            return 0;
        }

        static void Rewrite<T>(string dir, string file)
        {
            string path = Path.Combine(dir, file);
            if (!File.Exists(path)) return;
            var data = Json.Json.FromFile<T>(path);
            Json.Json.ToFile(path, data);
            Console.WriteLine("normalized " + file + " (" + new FileInfo(path).Length / 1024 + " KB)");
        }
    }
}
