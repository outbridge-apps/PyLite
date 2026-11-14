using System.Collections.Generic;

namespace Outbridge.PyLite.Runtime.Values
{
    // PEP 604's `int | str` (typing.Union since 3.14): what isinstance and issubclass take as a tuple of its members.
    // Made by `|` between classes and None only, when no operand defines `|` itself.
    public sealed class UnionTypeValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("typing.Union", null);
        private static readonly object NoneKey = new object();

        internal readonly ScriptValue[] Args;   // classes, and None for NoneType; no repeats

        private UnionTypeValue(ScriptValue[] args) { Args = args; }

        // __args__: PyOps.GetAttr answers it, a slot may not have a dunder name
        internal ScriptValue ArgsTuple(EvalContext ctx)
        {
            var args = new ScriptValue[Args.Length];
            for (int i = 0; i < args.Length; i++)
                args[i] = Args[i].Kind == ValueKind.None ? Modules.Builtins.TypeObjectFor(Args[i].TypeInfo, ctx) : Args[i];   // NoneType
            return ctx.Values.Tuple(args);
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        private static bool IsMember(ScriptValue v)
        {
            return v.Kind == ValueKind.Type || v.Kind == ValueKind.RecordClass || v is EnumClassValue || v.Kind == ValueKind.None
                || v is UnionTypeValue;
        }

        // a | b, or null when either side is no class
        internal static ScriptValue TryMake(ScriptValue a, ScriptValue b, EvalContext ctx)
        {
            if (!IsMember(a) || !IsMember(b) || (a.Kind == ValueKind.None && b.Kind == ValueKind.None))
                return null;
            var args = new List<ScriptValue>();
            var seen = new HashSet<object>(Syntax.ReferenceEqualityComparer.Instance);
            Add(args, seen, a);
            Add(args, seen, b);
            ctx.Budget.ChargeLinear(args.Count);
            ctx.Values.PreCharge(16L + 8L * args.Count);
            return args.Count == 1 ? args[0] : new UnionTypeValue(args.ToArray());   // int | int is int
        }

        private static void Add(List<ScriptValue> args, HashSet<object> seen, ScriptValue v)
        {
            UnionTypeValue u = v as UnionTypeValue;
            if (u != null)
            {
                foreach (ScriptValue a in u.Args)
                    Add(args, seen, a);
                return;
            }
            if (seen.Add(v.Kind == ValueKind.None ? NoneKey : v))
                args.Add(v);
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            UnionTypeValue o = other as UnionTypeValue;
            if (o == null || o.Args.Length != Args.Length)
                return false;
            ctx.Budget.ChargeLinear(2L * Args.Length);   // a set compare, linear: members never repeat
            var theirs = new HashSet<object>(Syntax.ReferenceEqualityComparer.Instance);
            foreach (ScriptValue b in o.Args)
                theirs.Add(b.Kind == ValueKind.None ? NoneKey : b);
            foreach (ScriptValue a in Args)
            {
                if (!theirs.Contains(a.Kind == ValueKind.None ? NoneKey : a))
                    return false;
            }
            return true;
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            ctx.Budget.ChargeLinear(Args.Length);
            long h = 0x2F0A;
            foreach (ScriptValue a in Args)
                h ^= PyOps.Hash(a, ctx, depth + 1);   // order-free, as the frozenset CPython hashes
            return h;
        }

        // a builtin by its name, a class the script made with its module, as CPython's __main__.A
        internal static string MemberText(ScriptValue a, EvalContext ctx, int depth)
        {
            if (a.Kind == ValueKind.None)
                return "None";
            TypeValue t = a as TypeValue;
            if (t != null)
                return t.IsNamedByScript ? "__main__." + t.Name : t.Name;
            RecordClassValue rc = a as RecordClassValue;
            if (rc != null)
                return "__main__." + rc.Name;
            EnumClassValue ec = a as EnumClassValue;
            return ec != null ? "__main__." + ec.Name : a.Repr(ctx, depth + 1);
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            for (int i = 0; i < Args.Length; i++)
            {
                if (i > 0)
                    sb.Append(" | ");
                sb.Append(MemberText(Args[i], ctx, depth));
            }
        }
    }
}
