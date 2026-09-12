using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // Base of every script value. Leaf virtual hooks are called ONLY by the PyOps/ReprEngine engines
    // and never traverse container children themselves. Non-virtual facades are the
    // Evaluator's entry points.
    public abstract class ScriptValue
    {
        public abstract ScriptTypeInfo TypeInfo { get; }
        internal abstract ValueKind Kind { get; }
        public string PyTypeName { get { return TypeInfo.Name; } }

        // ---- Leaf virtual hooks ----
        protected internal virtual bool IsTruthyCore(EvalContext ctx) { return true; }
        protected internal virtual bool TryLengthCore(out long length) { length = 0; return false; }
        // len() for values whose length needs engine services (ChainMap key-union hashing).
        // Consulted by PyOps.Length BEFORE TryLengthCore; default: not applicable.
        protected internal virtual bool TryLengthWithContextCore(EvalContext ctx, out long length) { length = 0; return false; }
        protected internal virtual IScriptIterator GetIteratorCore(EvalContext ctx) { return null; }
        // reversed(obj): dicts (insertion order, 3.8+) and OrderedDict provide one. null => not reversible.
        protected internal virtual IScriptIterator GetReverseIteratorCore(EvalContext ctx) { return null; }
        protected internal virtual bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        { found = false; return false; }
        protected internal virtual long HashLeafCore(EvalContext ctx, int depth)
        { throw Raise.TypeError(ctx, "unhashable type: '" + PyTypeName + "'"); }
        protected internal virtual bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        { return ReferenceEquals(this, other); }
        protected internal virtual bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        { cmp = 0; return false; }
        // Cross-kind equality for opaque numerics vs int/float (Fraction/Decimal). Consulted by the
        // Equals engine BEFORE the kind-mismatch fast-path. Default: not applicable.
        protected internal virtual bool TryEqualsAcrossKindCore(ScriptValue other, EvalContext ctx, int depth, out bool equal)
        { equal = false; return false; }
        // Attribute assignment obj.attr = value for opaque values (decimal getcontext().rounding). false => read-only.
        protected internal virtual bool TrySetAttrCore(string name, ScriptValue value, EvalContext ctx) { return false; }
        protected internal virtual ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        { return null; }
        protected internal virtual ScriptValue UnaryOpCore(PyUnaryOp op, EvalContext ctx) { return null; }
        // abs()/divmod() builtins consult these before their generic TypeError (timedelta).
        protected internal virtual ScriptValue AbsCore(EvalContext ctx) { return null; }
        protected internal virtual ScriptValue DivmodCore(ScriptValue other, bool reflected, EvalContext ctx) { return null; }
        // Subscript obj[index] for opaque sequence-like values (struct_time). null => not subscriptable.
        protected internal virtual ScriptValue GetItemCore(ScriptValue index, EvalContext ctx) { return null; }
        // obj[index] = value / del obj[index] for opaque mutable sequences (deque). false => unsupported.
        protected internal virtual bool TrySetItemCore(ScriptValue index, ScriptValue value, EvalContext ctx) { return false; }
        protected internal virtual bool TryDelItemCore(ScriptValue index, EvalContext ctx) { return false; }
        // __format__(spec) with a non-empty spec (date/time/datetime -> strftime). null => unsupported.
        protected internal virtual ScriptValue FormatSpecCore(string spec, EvalContext ctx) { return null; }
        // math.floor/ceil/trunc and round() consult these on opaque numerics (Fraction). null => unsupported.
        protected internal virtual ScriptValue FloorCore(EvalContext ctx) { return null; }
        protected internal virtual ScriptValue CeilCore(EvalContext ctx) { return null; }
        protected internal virtual ScriptValue TruncCore(EvalContext ctx) { return null; }
        protected internal virtual ScriptValue RoundCore(ScriptValue ndigits, EvalContext ctx) { return null; }
        protected internal virtual void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        { sb.Append("<" + PyTypeName + " object>"); }
        protected internal virtual void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        { ReprLeafCore(sb, ctx, depth); }

        // ---- Markers used by later packages (callable()/len over a big range) ----
        internal virtual bool IsCallable { get { return false; } }
        // A callable opaque value's dispatch (functools.partial); CallMachinery consults
        // this when IsCallable is true but the Kind is not a built-in callable.
        protected internal virtual ScriptValue CallCore(ScriptValue[] args, Evaluator.KwArgs kw, EvalContext ctx) { return null; }
        internal virtual bool TryLengthBig(out BigInteger length)
        {
            long l;
            if (TryLengthCore(out l))
            {
                length = l;
                return true;
            }
            length = BigInteger.Zero;
            return false;
        }

        // ---- Non-virtual facades ----
        public bool IsTruthy(EvalContext ctx) { return IsTruthyCore(ctx); }
        public string Repr(EvalContext ctx) { return ReprEngine.Render(this, ctx, useStrAtTop: false); }
        public string Str(EvalContext ctx) { return ReprEngine.Render(this, ctx, useStrAtTop: true); }
        // For a leaf hook rendering its children: pass the depth it was called at, plus one.
        internal string Repr(EvalContext ctx, int depth) { return ReprEngine.Render(this, ctx, false, depth); }
        internal string Str(EvalContext ctx, int depth) { return ReprEngine.Render(this, ctx, true, depth); }
        public long PyHash(EvalContext ctx) { return PyOps.Hash(this, ctx, 0); }
    }
}
