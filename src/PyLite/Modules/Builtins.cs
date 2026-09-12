using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // Free-function builtins. Implemented across partial files Builtins.*.cs; each
    // registers its names into the default template via a Register* method called from RegisterAll.
    internal static partial class Builtins
    {
        // Shared type-object cache so `type(x)`, the `int`/`str`/... names and constructors all resolve to
        // one TypeValue per type name (stable identity for `type(a) == type(b)`).
        private static readonly ConcurrentDictionary<string, TypeValue> TypeCache =
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

        // Reentrant call into a script callable through the single Evaluator.Call point.
        internal static ScriptValue Invoke(Outbridge.PyLite.Runtime.EvalContext c, ScriptValue fn, params ScriptValue[] args)
        {
            return c.CallHook(fn, args, Outbridge.PyLite.Runtime.Evaluator.KwArgs.Empty);
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
            TypeCache[name] = tv;
            return tv;
        }

        // The TypeValue for a runtime type name; synthesizes (and caches) an opaque one for names that
        // have no registered constructor (their instances still answer type()).
        internal static TypeValue TypeObjectFor(string name)
        {
            return TypeCache.GetOrAdd(name, nm => TypeValue.Make(nm, null, null, null, null));
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
