using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    public sealed class IntValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("int", IntMethods.BuildSlots());
        internal static ScriptTypeInfo IntType { get { return Type; } }

        // P1 small-int fast path: values that fit in a long are stored as one (canonical — a long-fitting
        // value is NEVER stored big, the BigInteger ctor normalizes), so arithmetic/compare/hash stay in
        // machine words. Big ints keep the exact BigInteger path.
        internal readonly bool IsSmall;
        internal readonly long Small;
        private readonly BigInteger _big;
        private int _bitsCache;   // big path only: exact bit length, 0 = not yet computed (big => >= 64)

        public BigInteger Value { get { return IsSmall ? new BigInteger(Small) : _big; } }

        internal IntValue(long v) { IsSmall = true; Small = v; }

        internal IntValue(BigInteger v)
        {
            if (v >= LongMin && v <= LongMax)
            {
                IsSmall = true;
                Small = (long)v;
            }
            else
                _big = v;
        }

        private static readonly BigInteger LongMin = long.MinValue;
        private static readonly BigInteger LongMax = long.MaxValue;

        // Small-int cache: values -5..4096 (CPython caches -5..256; we widen — loop counters, indices
        // and lengths overwhelmingly land here, and every hit skips an alloc + budget charge). ~4.1k
        // singletons x ~56B = ~230KB static, shared across runs. Int identity (`is`) is
        // implementation-defined in Python, so the wider range is observable but legal.
        internal const int CacheLow = -5;
        internal const int CacheHigh = 4096;
        private static readonly IntValue[] SmallCache = BuildCache();

        private static IntValue[] BuildCache()
        {
            var arr = new IntValue[CacheHigh - CacheLow + 1];
            for (int v = CacheLow; v <= CacheHigh; v++)
                arr[v - CacheLow] = new IntValue((long)v);
            return arr;
        }

        internal static IntValue Cached(long v) { return SmallCache[(int)v - CacheLow]; }

        internal static bool TryGetCached(BigInteger v, out IntValue cached)
        {
            if (v >= CacheLow && v <= CacheHigh)
            {
                cached = SmallCache[(int)v - CacheLow];
                return true;
            }
            cached = null;
            return false;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Int; } }

        // Exact bit length of |value|, cached for big values (immutable — computed at most once; the
        // benign race writes the same number). Multiplies/charges recompute this constantly otherwise.
        internal long BitLength()
        {
            if (IsSmall)
                return NumericOps.BitLengthLong(Small);
            int c = _bitsCache;
            if (c > 0)
                return c;
            long bits = NumericOps.BitLength(_big);
            if (bits <= int.MaxValue)
                _bitsCache = (int)bits;
            return bits;
        }

        internal void PrefillBits(long bits)
        {
            if (!IsSmall && bits > 0 && bits <= int.MaxValue)
                _bitsCache = (int)bits;
        }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return IsSmall ? Small != 0 : !_big.IsZero; }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            return IsSmall ? NumericHash.HashLong(Small) : NumericHash.HashBigInteger(_big);
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            IntValue o = other as IntValue;
            if (o == null)
                return false;
            // Canonical storage: same value => same representation.
            if (IsSmall != o.IsSmall)
                return false;
            return IsSmall ? Small == o.Small : _big == o._big;
        }

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            if (!NumericOps.IsNumber(other))
                return null;
            return reflected ? NumericOps.BinaryNumeric(op, other, this, ctx) : NumericOps.BinaryNumeric(op, this, other, ctx);
        }

        protected internal override ScriptValue UnaryOpCore(PyUnaryOp op, EvalContext ctx) { return NumericOps.UnaryNumeric(op, this, ctx); }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            if (IsSmall)
            {
                NumericOps.ChargeIntToStr(Small, ctx);
                sb.Append(Small.ToString(CultureInfo.InvariantCulture));
                return;
            }
            NumericOps.ChargeIntToStr(_big, ctx);
            sb.Append(_big.ToString(CultureInfo.InvariantCulture));
        }
    }
}
