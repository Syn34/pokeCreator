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

    /// <summary>
    /// Marks a static method that runs once when the game starts, after every script is loaded:
    /// <c>[ScriptInit] public static void Setup(Game game)</c> (subscribe to GameEvents here).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ScriptInitAttribute : Attribute { }

    /// <summary>Compiles the project's Scripts/*.cs files and the switched-on plugins' scripts at startup and registers their commands.</summary>
    public static class ScriptLoader
    {
        public static void Load(Game game, string scriptsDir)
        {
            var list = PokeCreator.Projects.Plugins.ScriptFiles(game.Project);
            if (Directory.Exists(scriptsDir)) list.AddRange(Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories));
            var files = list.ToArray();
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
            var inits = new List<MethodInfo>();
            foreach (var type in result.CompiledAssembly.GetTypes())
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    if (m.GetCustomAttributes(typeof(ScriptInitAttribute), false).Length > 0) inits.Add(m);
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
            foreach (var m in inits)
            {
                try { m.Invoke(null, m.GetParameters().Length == 1 ? new object[] { game } : new object[0]); }
                catch (TargetInvocationException e) { Console.WriteLine("[scripts] " + m.DeclaringType.Name + "." + m.Name + " failed: " + e.InnerException.Message); }
            }
        }
    }
}
