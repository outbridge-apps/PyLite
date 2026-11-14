using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.Modules
{
    // Standard-library code written in the dialect itself (Modules/Python/*.py, embedded resources):
    // compiled once per process, run in each run that imports it, under that run's budget like any
    // Python-source module.
    internal static class PythonSourceModules
    {
        private const int CompileStackBytes = 4 * 1024 * 1024;

        // PublicationOnly: a failed compile is not cached, so one run cannot break the module for the process.
        private static readonly ConcurrentDictionary<string, Lazy<CompiledScript>> Compiled =
            new ConcurrentDictionary<string, Lazy<CompiledScript>>(StringComparer.Ordinal);

        internal static ModuleValue Load(EvalContext ctx, string resource, string moduleName)
        {
            CompiledScript script = Compiled.GetOrAdd(resource,
                r => new Lazy<CompiledScript>(() => CompileOnFreshStack(r, moduleName),
                    LazyThreadSafetyMode.PublicationOnly)).Value;
            return Hosting.ScriptEngine.LoadPythonModule(moduleName, script, ctx);
        }

        // The first import can come from deep in a script's recursion, where the parser's stack probe would
        // refuse our own fixed source: compile on a thread of its own instead.
        internal static CompiledScript CompileOnFreshStack(string resource, string moduleName)
        {
            CompiledScript result = null;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = CompileResource(resource, moduleName);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            }, CompileStackBytes);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();
            if (error is OutOfMemoryException)
                throw error;   // a shortage the run reports as one, not a broken module
            if (error != null)
                throw new InvalidOperationException("embedded module '" + moduleName + "' failed to compile", error);
            return result;
        }

        internal static CompiledScript CompileResource(string resource, string moduleName)
        {
            using (Stream s = typeof(PythonSourceModules).Assembly.GetManifestResourceStream(resource))
            {
                if (s == null)
                    throw new InvalidOperationException("embedded module source '" + resource + "' is missing");
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return ScriptEngine.AsModule(Compiler.Compile(r.ReadToEnd(), moduleName, SyntaxLimits.Default), moduleName);
            }
        }
    }
}
