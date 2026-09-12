namespace Outbridge.PyLite.Runtime.Values
{
    // The `...` singleton (repr "Ellipsis", truthy, hashable). Kind is Opaque like the other leaf
    // values; identity equality; deterministic engine hash constant (same policy as NoneValue).
    public sealed class EllipsisValue : ScriptValue
    {
        public static readonly EllipsisValue Instance = new EllipsisValue();
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("ellipsis", null);

        private EllipsisValue() { }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return 998_244_353L; }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("Ellipsis"); }
    }
}
