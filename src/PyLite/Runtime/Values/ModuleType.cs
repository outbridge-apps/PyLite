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

        public readonly string Name;
        public readonly BuiltinDelegate Constructor;                       // null => cannot instantiate
        public readonly ScriptTypeInfo Describes;
        public readonly IReadOnlyDictionary<string, ScriptValue> StaticSlots;   // null => none
        public readonly PyExceptionType ExcType;                           // non-null only for exception types
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

        // Shared-identity instance for the immutable builtins template.
        internal static TypeValue Make(string name, BuiltinDelegate constructor, ScriptTypeInfo describes,
            IReadOnlyDictionary<string, ScriptValue> staticSlots, PyExceptionType excType)
        {
            return new TypeValue(name, constructor, describes, staticSlots, excType, SharedIdentitySource.Next());
        }

        public override ScriptTypeInfo TypeInfo { get { return MetaType; } }
        internal override ValueKind Kind { get { return ValueKind.Type; } }
        internal override bool IsCallable { get { return true; } }

        // `str.lower` / `dict.get`: an instance slot reached through the type object becomes a plain
        // function taking the instance first (sorted(key=str.lower), map(str.strip, ...)). Null when
        // the type has no slot table or no such slot.
        internal ScriptValue TryGetUnboundSlot(string name)
        {
            SlotDescriptor sd;
            if (Describes == null || !Describes.Slots.TryGetValue(name, out sd))
                return null;
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
                    return sd.Getter(target, ctx);
                var rest = new ScriptValue[args.Length - 1];
                System.Array.Copy(args, 1, rest, 0, rest.Length);
                return sd.Method(target, rest, kw, ctx);
            });
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return _identity; }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("<class '" + Name + "'>"); }
    }
}
