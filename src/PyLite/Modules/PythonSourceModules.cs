using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.Modules
{
    // Standard-library code written in the dialect itself (Modules/Python/*.py, embedded resources):
    // compiled once per process, run in each run that imports it, under that run's budget like any
    // Python-source module.
    internal static class PythonSourceModules
    {
        private static readonly ConcurrentDictionary<string, Lazy<CompiledScript>> Compiled =
            new ConcurrentDictionary<string, Lazy<CompiledScript>>(StringComparer.Ordinal);

        internal static ModuleValue Load(EvalContext ctx, string resource, string moduleName)
        {
            CompiledScript script = Compiled.GetOrAdd(resource,
                r => new Lazy<CompiledScript>(() => CompileResource(r, moduleName))).Value;
            return Hosting.ScriptEngine.LoadPythonModule(moduleName, script, ctx);
        }

        private static CompiledScript CompileResource(string resource, string moduleName)
        {
            using (Stream s = typeof(PythonSourceModules).Assembly.GetManifestResourceStream(resource))
            {
                if (s == null)
                    throw new InvalidOperationException("embedded module source '" + resource + "' is missing");
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return Compiler.Compile(r.ReadToEnd(), moduleName, SyntaxLimits.Default);
            }
        }
    }
}
