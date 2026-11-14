using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // Free-function builtins. Implemented across partial files Builtins.*.cs; each
    // registers its names into the default template via a Register* method called from RegisterAll.
    internal static partial class Builtins
    {
        // The builtin type objects by name, so `type(1) is int`. Written only while the builtins template is
        // built: a name a run could add would outlive the run and change what type() answers in every other.
        private static readonly ConcurrentDictionary<string, TypeValue> RegisteredTypes =
            new ConcurrentDictionary<string, TypeValue>(StringComparer.Ordinal);

        internal static void RegisterAll(Dictionary<string, ScriptValue> d)
        {
            RegisterConvert(d);
            RegisterNumeric(d);
            RegisterObject(d);
            RegisterContainers(d);
            RegisterPrint(d);
            RegisterTypes(d);
            RegisterIter(d);
            RegisterAggregate(d);
        }

        // Wrap a free-function delegate as a shared-identity BuiltinFunctionValue for the template.
        internal static void Add(Dictionary<string, ScriptValue> d, string name, BuiltinDelegate fn)
        {
            d[name] = BuiltinFunctionValue.Make(name, fn);
        }

        // A builtin of min..max positional arguments and no keywords (CPython's METH_O and PyArg_UnpackTuple
        // builtins): both are checked before the body, so neither a keyword nor an extra argument is dropped
        // silently (len(x, y=1) used to be len(x)).
        internal static void Add(Dictionary<string, ScriptValue> d, string name, int min, int max, BuiltinDelegate fn)
        {
            Add(d, name, Positional(name, min, max, fn));
        }

        internal static TypeValue AddType(Dictionary<string, ScriptValue> d, string name, int min, int max, BuiltinDelegate ctor,
            IReadOnlyDictionary<string, ScriptValue> staticSlots = null, ScriptTypeInfo describes = null)
        {
            return AddType(d, name, Positional(name, min, max, ctor), staticSlots, describes);
        }

        private static BuiltinDelegate Positional(string name, int min, int max, BuiltinDelegate fn)
        {
            return (self, args, kw, c) =>
            {
                if (kw.Count != 0 || args.Length < min || args.Length > max)
                {
                    Support.Args.NoKw(c, kw, name);
                    Support.Args.CheckPositional(c, args, name, min, max);
                }
                return fn(self, args, kw, c);
            };
        }

        // The builtins registered with a quiet twin: only a call by one of these names looks for one.
        internal static bool HasQuietTwin(string name)
        {
            return name == "int" || name == "float" || name == "next";
        }

        // A builtin with a quiet twin: the plain call throws the failure the twin hands back.
        internal static void AddQuiet(Dictionary<string, ScriptValue> d, string name, QuietBuiltin quiet)
        {
            BuiltinFunctionValue fn = BuiltinFunctionValue.Make(name, Throwing(quiet));
            fn.Quiet = quiet;
            d[name] = fn;
        }

        internal static void AddQuietType(Dictionary<string, ScriptValue> d, string name, QuietBuiltin quiet,
            IReadOnlyDictionary<string, ScriptValue> staticSlots, ScriptTypeInfo describes)
        {
            AddType(d, name, Throwing(quiet), staticSlots, describes).Quiet = quiet;
        }

        private static BuiltinDelegate Throwing(QuietBuiltin quiet)
        {
            return (self, args, kw, c) =>
            {
                ScriptException error;
                ScriptValue v = quiet(args, kw, c, out error);
                if (error != null)
                    throw error;
                return v;
            };
        }

        // Register a type name (int/str/...) as a callable TypeValue and cache it for type().
        internal static TypeValue AddType(Dictionary<string, ScriptValue> d, string name, BuiltinDelegate ctor)
        {
            return AddType(d, name, ctor, null);
        }

        internal static TypeValue AddType(Dictionary<string, ScriptValue> d, string name, BuiltinDelegate ctor,
            IReadOnlyDictionary<string, ScriptValue> staticSlots)
        {
            return AddType(d, name, ctor, staticSlots, null);
        }

        // describes: the instance slot table, which makes `str.lower` etc. reachable as unbound methods.
        internal static TypeValue AddType(Dictionary<string, ScriptValue> d, string name, BuiltinDelegate ctor,
            IReadOnlyDictionary<string, ScriptValue> staticSlots, ScriptTypeInfo describes)
        {
            TypeValue tv = TypeValue.Make(name, ctor, describes, staticSlots, null);
            d[name] = tv;
            RegisteredTypes[name] = tv;
            return tv;
        }

        // type(v) for a value with no class object of its own: the class this run's module made for it, the
        // builtin registered under its type's name, else the type's own object. A type a script named never
        // looks a name up.
        internal static TypeValue TypeObjectFor(ScriptTypeInfo ti, Runtime.EvalContext ctx)
        {
            TypeValue tv;
            if (!ti.IsNamedByScript)
            {
                tv = ctx.Values.RunTypeFor(ti);
                if (tv != null || RegisteredTypes.TryGetValue(ti.Name, out tv))
                    return tv;
            }
            return ti.TypeObject;
        }

        // partial Register* declared in the sibling files
        static partial void RegisterConvert(Dictionary<string, ScriptValue> d);
        static partial void RegisterNumeric(Dictionary<string, ScriptValue> d);
        static partial void RegisterObject(Dictionary<string, ScriptValue> d);
        static partial void RegisterContainers(Dictionary<string, ScriptValue> d);
        static partial void RegisterPrint(Dictionary<string, ScriptValue> d);
        static partial void RegisterTypes(Dictionary<string, ScriptValue> d);
        static partial void RegisterIter(Dictionary<string, ScriptValue> d);
        static partial void RegisterAggregate(Dictionary<string, ScriptValue> d);
    }
}
