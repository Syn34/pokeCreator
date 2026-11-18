using System;
using PokeCreator.Import;

namespace PokeCreator.Tools
{
    /// <summary>Command line: PokeCreatorImport.exe "C:\path\to\Essentials" "C:\path\to\NewProject" [--no-assets]</summary>
    public static class ImportCli
    {
        public static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: PokeCreatorImport.exe <essentials folder> <new project folder> [--no-assets]");
                return 1;
            }
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            var imp = new EssentialsImporter { Log = Console.WriteLine };
            if (Array.IndexOf(args, "--no-assets") >= 0) imp.CopyAssets = false;
            try { imp.Import(args[0], args[1]); }
            catch (Exception ex) { Console.WriteLine("Import failed: " + ex.Message); return 2; }
            return 0;
        }
    }
}
