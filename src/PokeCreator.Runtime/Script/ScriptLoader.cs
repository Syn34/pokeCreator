using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp;

namespace PokeCreator.Runtime.Script
{
    /// <summary>
    /// Marks a static method in a project script as an event command:
    /// <c>[ScriptCommand("healParty")] public static object Heal(Game game, Interpreter ctx, List&lt;object&gt; args)</c>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ScriptCommandAttribute : Attribute
    {
        public readonly string Name;
        public ScriptCommandAttribute(string name) { Name = name; }
    }

    /// <summary>Compiles the project's Scripts/*.cs files at startup and registers their commands.</summary>
    public static class ScriptLoader
    {
        public static void Load(Game game, string scriptsDir)
        {
            if (!Directory.Exists(scriptsDir)) return;
            var files = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);
            if (files.Length == 0) return;

            var opts = new CompilerParameters { GenerateInMemory = true, GenerateExecutable = false, TreatWarningsAsErrors = false };
            opts.ReferencedAssemblies.Add("System.dll");
            opts.ReferencedAssemblies.Add("System.Core.dll");
            opts.ReferencedAssemblies.Add(typeof(Game).Assembly.Location);
            opts.ReferencedAssemblies.Add(typeof(PokeCreator.Projects.Project).Assembly.Location);

            CompilerResults result;
            using (var provider = new CSharpCodeProvider()) result = provider.CompileAssemblyFromFile(opts, files);

            if (result.Errors.HasErrors)
            {
                var msgs = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning)
                    .Select(e => Path.GetFileName(e.FileName) + " line " + e.Line + ": " + e.ErrorText).Take(10);
                throw new Exception("Your scripts have errors:\n\n" + string.Join("\n", msgs));
            }

            int count = 0;
            foreach (var type in result.CompiledAssembly.GetTypes())
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    foreach (ScriptCommandAttribute attr in m.GetCustomAttributes(typeof(ScriptCommandAttribute), false))
                    {
                        var method = m;
                        var ps = method.GetParameters();
                        if (ps.Length != 3) { Console.WriteLine("[scripts] " + type.Name + "." + m.Name + " must take (Game, Interpreter, List<object>)"); continue; }
                        game.Compat.Register(attr.Name, (ctx, args) => method.Invoke(null, new object[] { game, ctx, args }));
                        count++;
                    }
            Console.WriteLine("[scripts] Loaded " + count + " script command(s) from " + files.Length + " file(s)");
        }
    }
}
