using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    public sealed class FloatValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("float", FloatMethods.BuildSlots());
        internal static ScriptTypeInfo FloatType { get { return Type; } }

        public readonly double Value;
        internal FloatValue(double v) { Value = v; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Float; } }

        // NaN -> true (Python), -0.0 -> false.
        protected internal override bool IsTruthyCore(EvalContext ctx) { return Value != 0.0; }
        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return NumericHash.HashDouble(Value); }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            FloatValue o = other as FloatValue;
            return o != null && Value.Equals(o.Value);
        }
        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            if (!NumericOps.IsNumber(other))
                return null;
            return reflected ? NumericOps.BinaryNumeric(op, other, this, ctx) : NumericOps.BinaryNumeric(op, this, other, ctx);
        }

        protected internal override ScriptValue UnaryOpCore(PyUnaryOp op, EvalContext ctx) { return NumericOps.UnaryNumeric(op, this, ctx); }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append(FloatRepr.Repr(Value)); }
    }
}
