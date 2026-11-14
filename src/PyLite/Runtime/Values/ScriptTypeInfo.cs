using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    public enum SlotKind { Method, Property }

    public sealed class SlotDescriptor
    {
        public string Name { get; }
        public SlotKind Kind { get; }
        public BuiltinDelegate Method { get; }                             // Method: args[0] does NOT contain self
        public Func<ScriptValue, EvalContext, ScriptValue> Getter { get; } // Property

        // A delegate declared outside this assembly is the host's: it is called through HostCallGuard, like a host
        // function (a hang in it was not seen as host code, and its exceptions were engine faults).
        internal bool IsHostProvided { get; }

        private SlotDescriptor(string name, SlotKind kind, BuiltinDelegate method,
            Func<ScriptValue, EvalContext, ScriptValue> getter)
        {
            Name = name;
            Kind = kind;
            Method = method;
            Getter = getter;
            Delegate fn = (Delegate)method ?? getter;
            Type owner = fn != null ? fn.Method.DeclaringType : null;
            IsHostProvided = fn != null && (owner == null || owner.Assembly != typeof(SlotDescriptor).Assembly);
        }

        internal ScriptValue Call(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            return IsHostProvided ? Hosting.HostCallGuard.InvokeOn(Name, Method, self, ctx, args, kw) : Method(self, args, kw, ctx);
        }

        internal ScriptValue Get(ScriptValue self, EvalContext ctx)
        {
            return IsHostProvided ? Hosting.HostCallGuard.Get(Name, Getter, self, ctx) : Getter(self, ctx);
        }

        public static SlotDescriptor MakeMethod(string name, BuiltinDelegate d)
            => new SlotDescriptor(name, SlotKind.Method, d, null);

        public static SlotDescriptor MakeProperty(string name, Func<ScriptValue, EvalContext, ScriptValue> g)
            => new SlotDescriptor(name, SlotKind.Property, null, g);
    }

    public sealed class ScriptTypeInfo
    {
        public string Name { get; }
        public IReadOnlyDictionary<string, SlotDescriptor> Slots { get; }  // StringComparer.Ordinal
        public ScriptTypeInfo Base { get; }   // for script-created types (namedtuple -> tuple); engine types use BaseNameOf

        // Copies the slot table; a dunder slot name (__x__) is an engine bug and throws.
        public ScriptTypeInfo(string name, IDictionary<string, SlotDescriptor> slots) : this(name, slots, null) { }

        // A record class or an enum: the script chose the name.
        internal ScriptTypeInfo(string name, bool namedByScript) : this(name, null, null)
        {
            _namedByScript = namedByScript;
        }

        // An exception instance's slots, its class a script's or the engine's.
        internal ScriptTypeInfo(string name, IDictionary<string, SlotDescriptor> slots, bool namedByScript) : this(name, slots, null)
        {
            _namedByScript = namedByScript;
        }

        public ScriptTypeInfo(string name, IDictionary<string, SlotDescriptor> slots, ScriptTypeInfo baseType)
        {
            Name = name;
            Base = baseType;
            var copy = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            if (slots != null)
            {
                foreach (KeyValuePair<string, SlotDescriptor> kv in slots)
                {
                    if (IsDunder(kv.Key))
                        throw new ArgumentException("slot name must not be a dunder: " + kv.Key, nameof(slots));
                    copy[kv.Key] = kv.Value;
                }
            }
            Slots = copy;
        }

        // A script-created type (namedtuple) has a user-chosen name that may shadow a builtin, so it only
        // ever matches by identity along the Base chain.
        public bool IsScriptDefined { get { return Base != null; } }

        private readonly bool _namedByScript;

        // A name a script chose may repeat or shadow a builtin, and there is no end to them: such a type is
        // never looked up by name in a table that outlives the run.
        internal bool IsNamedByScript { get { return _namedByScript || Base != null; } }

        private TypeValue _typeObject;

        // What type() answers for a value of this type when no class object stands for it: one object per
        // type, kept here so that it lives as long as the type (a run's own types take it with them).
        internal TypeValue TypeObject
        {
            get
            {
                TypeValue tv = System.Threading.Volatile.Read(ref _typeObject);
                if (tv != null)
                    return tv;
                System.Threading.Interlocked.CompareExchange(ref _typeObject, TypeValue.Make(Name, null, null, null, null), null);
                return _typeObject;
            }
        }

        public bool IsSubtypeOf(ScriptTypeInfo target)
        {
            for (ScriptTypeInfo t = this; t != null; t = t.Base)
            {
                if (ReferenceEquals(t, target))
                    return true;
            }
            return false;
        }

        // isinstance semantics against an engine type name: this type, any Base up the chain, or a named
        // engine base; object accepts all. A type a script named never stands for the engine type of that
        // name (`class str` passed str's checks, and str's methods then cast it: an engine fault).
        public bool IsSubtypeOf(string superName)
        {
            if (superName == "object")
                return true;
            for (ScriptTypeInfo t = this; t != null; t = t.Base)
            {
                if (!t.IsNamedByScript && IsSubtypeByName(t.Name, superName))
                    return true;
            }
            return false;
        }

        public static bool IsSubtypeByName(string name, string superName)
        {
            if (superName == "object")
                return true;
            for (string n = name; n != null; n = BaseNameOf(n))
            {
                if (n == superName)
                    return true;
            }
            return false;
        }

        // The dialect's fixed hierarchy: every engine-defined subtype names its base here.
        private static string BaseNameOf(string name)
        {
            switch (name)
            {
                case "bool": return "int";
                case "collections.Counter":
                case "collections.defaultdict":
                case "collections.OrderedDict": return "dict";
                case "datetime.datetime": return "datetime.date";
                case "datetime.timezone":
                case "zoneinfo.ZoneInfo": return "datetime.tzinfo";
                default: return null;
            }
        }

        private static bool IsDunder(string n)
            => n != null && n.Length >= 4
               && n.StartsWith("__", StringComparison.Ordinal) && n.EndsWith("__", StringComparison.Ordinal);
    }
}
