namespace Outbridge.PyLite.Runtime.Values
{
    public sealed class NoneValue : ScriptValue
    {
        public static readonly NoneValue Instance = new NoneValue();
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("NoneType", null);

        private NoneValue() { }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.None; } }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return false; }
        // Deterministic engine constant (CPython derives it from id; parity not required).
        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return 1_000_000_007L; }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("None"); }
    }
}
