using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // A distinct class (not a subclass of IntValue); the numeric tower is provided by NumericOps.
    public sealed class BoolValue : ScriptValue
    {
        public static readonly BoolValue True = new BoolValue(true);
        public static readonly BoolValue False = new BoolValue(false);
        // bool carries int's methods, as CPython's bool does by being an int subclass: True.bit_length()
        // is 1 and True.real is the int 1. The VALUE model keeps them separate kinds (repr, the ==/hash
        // invariant and the numeric ops already treat a bool as 0/1), so this is the method surface only.
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("bool", IntMethods.BuildSlots());

        public readonly bool Value;
        private BoolValue(bool v) { Value = v; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Bool; } }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return Value; }
        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return NumericHash.HashBool(Value); }

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            if (!NumericOps.IsNumber(other))
                return null;
            return reflected ? NumericOps.BinaryNumeric(op, other, this, ctx) : NumericOps.BinaryNumeric(op, this, other, ctx);
        }

        protected internal override ScriptValue UnaryOpCore(PyUnaryOp op, EvalContext ctx) { return NumericOps.UnaryNumeric(op, this, ctx); }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append(Value ? "True" : "False"); }
    }
}
