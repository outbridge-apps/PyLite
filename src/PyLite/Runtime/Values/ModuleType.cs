using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    public sealed class ModuleValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("module", null);

        public readonly string Name;
        internal readonly IReadOnlyDictionary<string, ScriptValue> Members;   // frozen (Ordinal)
        private readonly long _identity;

        internal ModuleValue(string name, IReadOnlyDictionary<string, ScriptValue> members, long identity)
        {
            Name = name;
            Members = members;
            _identity = identity;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Module; } }

        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return _identity; }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("<module '" + Name + "' (built-in)>"); }
    }

    public sealed class TypeValue : ScriptValue
    {
        private static readonly ScriptTypeInfo MetaType = new ScriptTypeInfo("type", null);

        internal static ScriptTypeInfo MetaTypeInfo { get { return MetaType; } }

        public readonly string Name;
        public readonly BuiltinDelegate Constructor;                       // null => cannot instantiate
        public readonly ScriptTypeInfo Describes;
        public readonly IReadOnlyDictionary<string, ScriptValue> StaticSlots;   // null => none
        public readonly PyExceptionType ExcType;                           // non-null only for exception types
        internal QuietBuiltin Quiet;                                       // set while the builtins template is built
        internal string NoInstanceChecks;                                  // isinstance/issubclass refuse it with this text
        internal string ReprModule;                                        // "__main__" for a TypedDict the script made
        private readonly long _identity;

        internal TypeValue(string name, BuiltinDelegate constructor, ScriptTypeInfo describes,
            IReadOnlyDictionary<string, ScriptValue> staticSlots, PyExceptionType excType, long identity)
        {
            Name = name;
            Constructor = constructor;
            Describes = describes;
            StaticSlots = staticSlots;
            ExcType = excType;
            _identity = identity;
        }

        // For the builtins template and the type objects made on demand: hashed by name (identity 0), like
        // BuiltinFunctionValue.Make.
        internal static TypeValue Make(string name, BuiltinDelegate constructor, ScriptTypeInfo describes,
            IReadOnlyDictionary<string, ScriptValue> staticSlots, PyExceptionType excType)
        {
            return new TypeValue(name, constructor, describes, staticSlots, excType, 0);
        }

        // A class a script created and named (namedtuple, make_dataclass, Enum): it never stands for the engine
        // type its name repeats.
        internal bool IsNamedByScript { get { return Describes != null && Describes.IsNamedByScript; } }

        public override ScriptTypeInfo TypeInfo { get { return MetaType; } }
        internal override ValueKind Kind { get { return ValueKind.Type; } }
        internal override bool IsCallable { get { return true; } }

        private System.Collections.Concurrent.ConcurrentDictionary<string, ScriptValue> _unbound;

        // `str.lower` / `dict.get`: an instance slot reached through the type object becomes a plain
        // function taking the instance first (sorted(key=str.lower), map(str.strip, ...)). Null when
        // the type has no slot table or no such slot. One function per slot, kept with the type (the names
        // are the type's own, so the table is bounded): no allocation per access, and str.lower is str.lower.
        internal ScriptValue TryGetUnboundSlot(string name)
        {
            SlotDescriptor sd;
            if (Describes == null || !Describes.Slots.TryGetValue(name, out sd))
                return null;
            var table = System.Threading.Volatile.Read(ref _unbound);
            if (table == null)
            {
                System.Threading.Interlocked.CompareExchange(ref _unbound,
                    new System.Collections.Concurrent.ConcurrentDictionary<string, ScriptValue>(System.StringComparer.Ordinal), null);
                table = _unbound;
            }
            ScriptValue f;
            return table.TryGetValue(name, out f) ? f : table.GetOrAdd(name, MakeUnbound(name, sd));
        }

        private ScriptValue MakeUnbound(string name, SlotDescriptor sd)
        {
            string typeName = Name;
            return BuiltinFunctionValue.Make(name, (self, args, kw, ctx) =>
            {
                if (args.Length == 0)
                    throw Raise.TypeError(ctx, "unbound method " + typeName + "." + name + "() needs an argument");
                ScriptValue target = args[0];
                if (!target.TypeInfo.IsSubtypeOf(typeName))
                    throw Raise.TypeError(ctx, "descriptor '" + name + "' for '" + typeName + "' objects doesn't apply to a '"
                        + target.PyTypeName + "' object");
                if (sd.Kind == SlotKind.Property)
                    return sd.Get(target, ctx);
                var rest = new ScriptValue[args.Length - 1];
                System.Array.Copy(args, 1, rest, 0, rest.Length);
                return sd.Call(target, rest, kw, ctx);
            });
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            return _identity != 0 ? _identity : Modules.Support.NumericHash.HashString(Name, ctx.StrHashSeed ^ 0x6A09E667u);   // not hash(name); equality is identity
        }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("<class '" + (ReprModule != null ? ReprModule + "." : "") + Name + "'>"); }
    }
}
