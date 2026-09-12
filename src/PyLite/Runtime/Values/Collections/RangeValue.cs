using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // Port of Objects/rangeobject.c. Step != 0 is guaranteed by the caller (the range builtin).
    public sealed class RangeValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("range", BuildSlots());

        public readonly BigInteger Start, Stop, Step;

        private static System.Collections.Generic.IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var d = new System.Collections.Generic.Dictionary<string, SlotDescriptor>(System.StringComparer.Ordinal);
            d["start"] = SlotDescriptor.MakeProperty("start", (self, ctx) => ctx.Values.Int(((RangeValue)self).Start));
            d["stop"] = SlotDescriptor.MakeProperty("stop", (self, ctx) => ctx.Values.Int(((RangeValue)self).Stop));
            d["step"] = SlotDescriptor.MakeProperty("step", (self, ctx) => ctx.Values.Int(((RangeValue)self).Step));
            d["index"] = SlotDescriptor.MakeMethod("index", (self, a, kw, ctx) => ((RangeValue)self).Index(a, ctx));
            d["count"] = SlotDescriptor.MakeMethod("count", (self, a, kw, ctx) => ((RangeValue)self).Count(a, ctx));
            return d;
        }

        // Integral membership without a scan (rangeobject.c range_index/range_count); a float that is
        // integral compares equal to its int, anything else is never a member.
        private bool TryMember(ScriptValue item, out BigInteger x)
        {
            if (item.Kind == ValueKind.Int || item.Kind == ValueKind.Bool)
            {
                x = NumericOps.AsBigInteger(item);
            }
            else if (item.Kind == ValueKind.Float && !double.IsInfinity(((FloatValue)item).Value)
                && !double.IsNaN(((FloatValue)item).Value) && ((FloatValue)item).Value == System.Math.Floor(((FloatValue)item).Value))
            {
                x = new BigInteger(((FloatValue)item).Value);
            }
            else
            {
                x = BigInteger.Zero;
                return false;
            }
            bool inRange = Step.Sign > 0 ? (Start <= x && x < Stop) : (Stop < x && x <= Start);
            return inRange && ((x - Start) % Step).IsZero;
        }

        private ScriptValue Index(ScriptValue[] a, EvalContext ctx)
        {
            Args.Exactly(ctx, a, "index", 1);
            BigInteger x;
            if (!TryMember(a[0], out x))
                throw Raise.ValueError(ctx, a[0].Repr(ctx) + " is not in range");
            return ctx.Values.Int((x - Start) / Step);
        }

        private ScriptValue Count(ScriptValue[] a, EvalContext ctx)
        {
            Args.Exactly(ctx, a, "count", 1);
            BigInteger x;
            return ctx.Values.Int(TryMember(a[0], out x) ? 1 : 0);
        }
        private BigInteger _length = BigInteger.MinusOne;   // lazy sentinel

        internal RangeValue(BigInteger start, BigInteger stop, BigInteger step)
        {
            Start = start; Stop = stop; Step = step;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Range; } }

        public BigInteger Length
        {
            get
            {
                if (_length.Sign < 0)
                    _length = ComputeLength();
                return _length;
            }
        }

        private BigInteger ComputeLength()
        {
            BigInteger lo, hi, step;
            if (Step.Sign > 0)
            {
                lo = Start;
                hi = Stop;
                step = Step;
            }
            else
            {
                lo = Stop;
                hi = Start;
                step = -Step;
            }
            if (lo >= hi)
                return BigInteger.Zero;
            return (hi - lo - 1) / step + 1;
        }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return Length.Sign > 0; }

        protected internal override bool TryLengthCore(out long length)
        {
            BigInteger n = Length;
            if (n <= long.MaxValue)
            {
                length = (long)n;
                return true;
            }
            length = 0;
            return false;
        }

        internal override bool TryLengthBig(out BigInteger length) { length = Length; return true; }

        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new RangeIterator(this); }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            BigInteger len = Length;
            ScriptValue[] parts;
            if (len.IsZero)
                parts = new ScriptValue[]
                {
                    ctx.Values.Int(0),
                    ctx.Values.None,
                    ctx.Values.None
                };
            else if (len == 1)
                parts = new ScriptValue[]
                {
                    ctx.Values.Int(len),
                    ctx.Values.Int(Start),
                    ctx.Values.None
                };
            else
                parts = new ScriptValue[]
                {
                    ctx.Values.Int(len),
                    ctx.Values.Int(Start),
                    ctx.Values.Int(Step)
                };
            return PyOps.Hash(ctx.Values.Tuple(parts), ctx, depth);
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            RangeValue o = other as RangeValue;
            if (o == null)
                return false;
            BigInteger la = Length, lb = o.Length;
            if (la != lb)
                return false;
            if (la.IsZero)
                return true;
            if (Start != o.Start)
                return false;
            if (la == 1)
                return true;
            return Step == o.Step;
        }

        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            if (item.Kind == ValueKind.Int || item.Kind == ValueKind.Bool)
            {
                BigInteger x = NumericOps.AsBigInteger(item);
                bool inRange = Step.Sign > 0 ? (Start <= x && x < Stop) : (Stop < x && x <= Start);
                found = inRange && ((x - Start) % Step).IsZero;
                return true;
            }
            found = false;
            return false;   // unsupported -> facade falls back to a linear scan (rangeobject.c parity)
        }

        internal ScriptValue GetItem(ScriptValue index, EvalContext ctx)
        {
            if (index.Kind == ValueKind.Slice)
                return SliceRange((SliceValue)index, ctx);
            if (index.Kind == ValueKind.Int || index.Kind == ValueKind.Bool)
            {
                BigInteger i = NumericOps.AsBigInteger(index);
                if (i.Sign < 0)
                    i += Length;
                if (i.Sign < 0 || i >= Length)
                    throw Raise.IndexError(ctx, "range object index out of range");
                return ctx.Values.Int(Start + i * Step);
            }
            throw Raise.TypeError(ctx, "range indices must be integers or slices, not " + index.PyTypeName);
        }

        private RangeValue SliceRange(SliceValue sl, EvalContext ctx)
        {
            SliceIndices ind = sl.Indices(Length, ctx);
            BigInteger newStep = Step * ind.Step;
            BigInteger newStart = Start + ind.Start * Step;
            BigInteger newStop = Start + ind.Stop * Step;
            return ctx.Values.Range(newStart, newStop, newStep);
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("range(");
            sb.Append(Start.ToString(CultureInfo.InvariantCulture));
            sb.Append(", ");
            sb.Append(Stop.ToString(CultureInfo.InvariantCulture));
            if (Step != BigInteger.One)
            {
                sb.Append(", ");
                sb.Append(Step.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(')');
        }
    }

    internal sealed class RangeIterator : ScriptIteratorBase
    {
        private readonly BigInteger _stop, _step;
        private readonly bool _up;
        private BigInteger _current;

        // P1: when start/stop/step sit comfortably inside long (quarter-range keeps _curL += _stepL
        // overflow-free), iterate on machine words; huge ranges keep the exact BigInteger walk.
        private readonly bool _small;
        private readonly long _stopL, _stepL;
        private long _curL;

        internal RangeIterator(RangeValue r)
        {
            const long Lo = long.MinValue / 4, Hi = long.MaxValue / 4;
            if (r.Start >= Lo && r.Start <= Hi && r.Stop >= Lo && r.Stop <= Hi && r.Step >= Lo && r.Step <= Hi)
            {
                _small = true;
                _curL = (long)r.Start;
                _stopL = (long)r.Stop;
                _stepL = (long)r.Step;
                _up = _stepL > 0;
                return;
            }
            _current = r.Start;
            _stop = r.Stop;
            _step = r.Step;
            _up = r.Step.Sign > 0;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_small)
            {
                if (_up ? _curL >= _stopL : _curL <= _stopL)
                {
                    value = null;
                    return false;
                }
                value = ctx.Values.Int(_curL);
                _curL += _stepL;
                return true;
            }
            bool has = _up ? _current < _stop : _current > _stop;
            if (!has)
            {
                value = null;
                return false;
            }
            value = ctx.Values.Int(_current);
            _current += _step;
            return true;
        }
    }
}
