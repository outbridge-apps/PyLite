using System;
using System.Collections.Generic;

namespace Outbridge.PyLite.Runtime.Values
{
    public enum SlotKind { Method, Property }

    public sealed class SlotDescriptor
    {
        public string Name { get; }
        public SlotKind Kind { get; }
        public BuiltinDelegate Method { get; }                             // Method: args[0] does NOT contain self
        public Func<ScriptValue, EvalContext, ScriptValue> Getter { get; } // Property

        private SlotDescriptor(string name, SlotKind kind, BuiltinDelegate method,
            Func<ScriptValue, EvalContext, ScriptValue> getter)
        {
            Name = name;
            Kind = kind;
            Method = method;
            Getter = getter;
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
        // engine base; object accepts all.
        public bool IsSubtypeOf(string superName)
        {
            for (ScriptTypeInfo t = this; t != null; t = t.Base)
            {
                if (IsSubtypeByName(t.Name, superName))
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
