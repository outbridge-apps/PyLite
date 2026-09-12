using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    internal struct SliceIndices
    {
        public BigInteger Start, Stop, Step, Length;

        // Step can exceed int range for a huge stride (e.g. [::10**100]); Start/Length are always bounded by
        // the sequence length. When |Step| is that large, Length is <= 1, so the stride is never multiplied
        // by a nonzero index — clamping it to int range is safe and dodges the (int)BigInteger overflow.
        public int StepInt { get { return Step > int.MaxValue ? int.MaxValue : Step < int.MinValue ? int.MinValue : (int)Step; } }
    }

    public sealed class SliceValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("slice", BuildSlots());

        public readonly ScriptValue StartObj, StopObj, StepObj;   // NoneValue or Int/Bool

        private static System.Collections.Generic.IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var d = new System.Collections.Generic.Dictionary<string, SlotDescriptor>(System.StringComparer.Ordinal);
            d["start"] = SlotDescriptor.MakeProperty("start", (self, ctx) => ((SliceValue)self).StartObj);
            d["stop"] = SlotDescriptor.MakeProperty("stop", (self, ctx) => ((SliceValue)self).StopObj);
            d["step"] = SlotDescriptor.MakeProperty("step", (self, ctx) => ((SliceValue)self).StepObj);
            d["indices"] = SlotDescriptor.MakeMethod("indices", (self, a, kw, ctx) => ((SliceValue)self).IndicesMethod(a, ctx));
            return d;
        }

        // slice.indices(len) -> (start, stop, step), the normalised triple used by slicing.
        private ScriptValue IndicesMethod(ScriptValue[] a, EvalContext ctx)
        {
            Args.Exactly(ctx, a, "indices", 1);
            if (a[0].Kind != ValueKind.Int && a[0].Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "'" + a[0].PyTypeName + "' object cannot be interpreted as an integer");
            BigInteger len = NumericOps.AsBigInteger(a[0]);
            if (len.Sign < 0)
                throw Raise.ValueError(ctx, "length should not be negative");
            SliceIndices ind = Indices(len, ctx);
            return ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(ind.Start), ctx.Values.Int(ind.Stop), ctx.Values.Int(ind.Step) });
        }

        internal SliceValue(ScriptValue start, ScriptValue stop, ScriptValue step)
        {
            StartObj = start;
            StopObj = stop;
            StepObj = step;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Slice; } }

        // HashLeafCore inherits the base "unhashable type: 'slice'"; TryCompareLeafCore stays false (unorderable).
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            SliceValue o = other as SliceValue;
            if (o == null)
                return false;
            return PyOps.Equals(StartObj, o.StartObj, ctx, depth + 1)
                && PyOps.Equals(StopObj, o.StopObj, ctx, depth + 1)
                && PyOps.Equals(StepObj, o.StepObj, ctx, depth + 1);
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("slice(");
            sb.Append(StartObj.Repr(ctx, depth + 1)); sb.Append(", ");
            sb.Append(StopObj.Repr(ctx, depth + 1)); sb.Append(", ");
            sb.Append(StepObj.Repr(ctx, depth + 1)); sb.Append(')');
        }

        // Port of Objects/sliceobject.c::PySlice_GetIndicesEx.
        internal SliceIndices Indices(BigInteger len, EvalContext ctx)
        {
            // Fast path: the sequence length fits an int and every present component is a machine-word
            // int (the overwhelmingly common shape). Normalise in long to skip BigInteger arithmetic. A
            // component outside long range (e.g. [::10**100]) or a range longer than int.MaxValue falls
            // through to the exact BigInteger path, which yields a byte-for-byte identical result.
            if (len <= int.MaxValue
                && TryFastComponent(StepObj, out long stepL, out bool stepNone)
                && TryFastComponent(StartObj, out long startL, out bool startNone)
                && TryFastComponent(StopObj, out long stopL, out bool stopNone))
            {
                return IndicesFast((long)len, stepNone ? 1L : stepL, startNone, startL, stopNone, stopL, ctx);
            }
            return IndicesBig(len, ctx);
        }

        // None -> (isNone=true); a small int/bool -> its long value; a big int or wrong type -> false, so the
        // caller drops to IndicesBig (which also raises the TypeError for a non-int component). long.MinValue is
        // rejected because negating it in the length formula would overflow — an absurd stride, handled big.
        private static bool TryFastComponent(ScriptValue v, out long value, out bool isNone)
        {
            value = 0;
            isNone = false;
            switch (v.Kind)
            {
                case ValueKind.None:
                    isNone = true;
                    return true;
                case ValueKind.Bool:
                    value = ((BoolValue)v).Value ? 1L : 0L;
                    return true;
                case ValueKind.Int:
                    IntValue iv = (IntValue)v;
                    if (!iv.IsSmall || iv.Small == long.MinValue)
                        return false;
                    value = iv.Small;
                    return true;
                default:
                    return false;
            }
        }

        // All arithmetic stays in long: start/stop are clamped into [-1, len] and len <= int.MaxValue, so no
        // overflow (step != long.MinValue, so -step is safe). Same algorithm as IndicesBig, machine words.
        private static SliceIndices IndicesFast(long len, long step, bool startNone, long start, bool stopNone, long stop, EvalContext ctx)
        {
            if (step == 0)
                throw Raise.ValueError(ctx, "slice step cannot be zero");
            bool neg = step < 0;

            long s = startNone ? (neg ? len - 1 : 0L) : NormFast(start, len, neg);
            long e = stopNone ? (neg ? -1L : len) : NormFast(stop, len, neg);

            long length;
            if (neg && e < s)
                length = (s - e - 1) / (-step) + 1;
            else if (!neg && s < e)
                length = (e - s - 1) / step + 1;
            else
                length = 0;

            return new SliceIndices { Start = s, Stop = e, Step = step, Length = length };
        }

        private static long NormFast(long x, long len, bool neg)
        {
            if (x < 0)
            {
                x += len;
                if (x < 0)
                    x = neg ? -1 : 0;
            }
            else if (x >= len)
                x = neg ? len - 1 : len;
            return x;
        }

        private SliceIndices IndicesBig(BigInteger len, EvalContext ctx)
        {
            BigInteger step = StepObj.Kind == ValueKind.None ? BigInteger.One : IntComponent(StepObj, ctx);
            if (step.IsZero)
                throw Raise.ValueError(ctx, "slice step cannot be zero");
            bool neg = step.Sign < 0;

            BigInteger defStart = neg ? len - 1 : BigInteger.Zero;
            BigInteger defStop = neg ? BigInteger.MinusOne : len;
            BigInteger start = StartObj.Kind == ValueKind.None ? defStart : Norm(IntComponent(StartObj, ctx), len, neg);
            BigInteger stop = StopObj.Kind == ValueKind.None ? defStop : Norm(IntComponent(StopObj, ctx), len, neg);

            BigInteger length;
            if (neg && stop < start)
                length = (start - stop - 1) / (-step) + 1;
            else if (!neg && start < stop)
                length = (stop - start - 1) / step + 1;
            else
                length = BigInteger.Zero;

            return new SliceIndices { Start = start, Stop = stop, Step = step, Length = length };
        }

        private static BigInteger Norm(BigInteger x, BigInteger len, bool neg)
        {
            if (x < 0)
            {
                x += len;
                if (x < 0)
                    x = neg ? BigInteger.MinusOne : BigInteger.Zero;
            }
            else if (x >= len)
                x = neg ? len - 1 : len;
            return x;
        }

        private static BigInteger IntComponent(ScriptValue v, EvalContext ctx)
        {
            if (v.Kind == ValueKind.Int || v.Kind == ValueKind.Bool)
                return NumericOps.AsBigInteger(v);
            throw Raise.TypeError(ctx, "slice indices must be integers or None or have an __index__ method");
        }
    }
}
