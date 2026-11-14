using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // itertools tee + product. tee shares one divergence buffer capped at
    // TeeBufferCap (a module constant rather than a ResourceLimits field, to avoid churning that ctor);
    // product materializes all pools up front (an infinite input aborts in the constructor).
    internal static partial class ItertoolsModule
    {
        internal const int TeeBufferCap = 10000;

        private static ScriptValue Tee(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.NoKw(ctx, kw, "itertools.tee");
            Args.Unpack(ctx, args, "tee", 1, 2);
            int n = 2;
            if (args.Length == 2)
            {
                BigInteger nb = AsCountInt(ctx, args[1]);
                if (nb < 0)
                    throw Raise.ValueError(ctx, "n must be >= 0");
                n = ToSsize(ctx, nb);
            }
            IScriptIterator src = IterArg(ctx, args[0]);
            ctx.Values.EnsureCollectionSize(n);   // n comes from the script: cap it before the array
            ctx.Values.PreCharge(96L * n);         // a clone and its iterator object each
            var buf = new TeeBuffer(src);
            var tuple = new ScriptValue[n];
            for (int i = 0; i < n; i++)
            {
                if ((i & 4095) == 4095)
                    ctx.Budget.CheckDeadlineNow();   // ~1.5 us per clone: 1.5M of them took 2-3 s
                var t = new TeeIterator(buf);
                buf.Register(t);
                tuple[i] = ctx.Values.EngineIterator(t, "itertools._tee");
            }
            return ctx.Values.Tuple(tuple);
        }

        private static ScriptValue Product(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            int repeat = 1;
            ScriptValue rkw;
            if (kw.TryGet("repeat", out rkw))
            {
                BigInteger rb = AsCountInt(ctx, rkw);
                if (rb < 0)
                    throw Raise.ValueError(ctx, "repeat argument cannot be negative");
                repeat = ToSsize(ctx, rb);   // OverflowError (not a fault) on an oversized repeat
            }
            var basePools = new ScriptValue[args.Length][];
            for (int i = 0; i < args.Length; i++)
            {
                basePools[i] = Support.Iter.ToArray(ctx, args[i]);   // an infinite input stops on MaxCollectionItems here
            }
            long poolCount = (long)args.Length * repeat;
            ctx.Values.EnsureCollectionSize(poolCount);   // every output is a poolCount-tuple
            ctx.Values.PreCharge(poolCount * 8 + 16);   // bounds poolCount before the (int) cast + alloc
            var pools = new ScriptValue[(int)poolCount][];
            int idx = 0;
            for (int r = 0; r < repeat; r++)
                for (int i = 0; i < args.Length; i++)
                    pools[idx++] = basePools[i];
            return ctx.Values.EngineIterator(new ProductIterator(pools), "itertools.product");
        }

        private static BigInteger AsCountInt(EvalContext ctx, ScriptValue v) { return Coerce.ToIndex(ctx, v, "count"); }
    }

    // tee's shared divergence buffer: a ring holding elements still needed by at least one clone.
    internal sealed class TeeBuffer
    {
        private readonly IScriptIterator _src;
        private readonly List<ScriptValue> _ring = new List<ScriptValue>();
        private readonly List<TeeIterator> _clones = new List<TeeIterator>();
        private int _head;          // _ring[_head] is the element at _baseIndex; the slots before it are spent
        private long _baseIndex;
        private long _producedTo;   // total elements pulled from the source
        // The lowest clone position and how many clones are there. The clones are scanned only when the
        // last of them leaves it, and every clone has moved past it by then: O(1) per element amortized
        // for any clone count (the scan per element made tee(it, 1500000) quadratic).
        private long _min;
        private int _atMin;

        internal TeeBuffer(IScriptIterator src) { _src = src; }

        internal void Register(TeeIterator t)
        {
            _clones.Add(t);
            _atMin++;   // every clone starts at position 0, which is _min until a clone moves
        }

        internal bool Get(EvalContext ctx, long absIdx, out ScriptValue v)
        {
            // the source is asked again after its end, as CPython's tee asks it: one that resumes is read on
            bool more = true;
            while (absIdx >= _producedTo && more)
            {
                ScriptValue nv;
                more = _src.MoveNext(ctx, out nv);
                if (more)
                {
                    ctx.Values.PreCharge(16);
                    _ring.Add(nv);
                    _producedTo++;
                    int live = _ring.Count - _head;
                    if (live > ItertoolsModule.TeeBufferCap)
                        throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "TeeBufferCap", ItertoolsModule.TeeBufferCap, live);
                }
            }
            if (absIdx >= _producedTo)
            {
                v = null;
                return false;
            }
            v = _ring[_head + (int)(absIdx - _baseIndex)];
            return true;
        }

        // A clone moved from `from` to from + 1.
        internal void Moved(long from)
        {
            if (from != _min || --_atMin > 0)
                return;
            _min = long.MaxValue;
            for (int i = 0; i < _clones.Count; i++)
            {
                long p = _clones[i].Pos;
                if (p < _min)
                {
                    _min = p;
                    _atMin = 1;
                }
                else if (p == _min)
                {
                    _atMin++;
                }
            }
            while (_baseIndex < _min && _head < _ring.Count)
            {
                _ring[_head++] = null;
                _baseIndex++;
            }
            if (_head > 1024 && 2 * _head > _ring.Count)
            {
                _ring.RemoveRange(0, _head);
                _head = 0;
            }
        }
    }

    // tee clone: reads by absolute position from the shared buffer; synchronized clones keep O(1) memory.
    internal sealed class TeeIterator : ScriptIteratorBase
    {
        private readonly TeeBuffer _buf;
        internal long Pos;

        internal TeeIterator(TeeBuffer buf) { _buf = buf; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (!_buf.Get(ctx, Pos, out value))
                return false;
            Pos += 1;
            _buf.Moved(Pos - 1);
            return true;
        }
    }

    // product: an index odometer over pre-materialized pools; an empty pool yields no output.
    internal sealed class ProductIterator : ScriptIteratorBase
    {
        private readonly ScriptValue[][] _pools;
        private readonly int[] _indices;
        private bool _started;
        private bool _exhausted;

        internal ProductIterator(ScriptValue[][] pools)
        {
            _pools = pools;
            _indices = new int[pools.Length];
            for (int i = 0; i < pools.Length; i++)
                if (pools[i].Length == 0)
                    _exhausted = true;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_exhausted)
            {
                value = null;
                return false;
            }
            if (!_started)
            {
                _started = true;
                value = BuildTuple(ctx);
                return true;
            }
            for (int i = _pools.Length - 1; i >= 0; i--)
            {
                _indices[i]++;
                if (_indices[i] < _pools[i].Length)
                {
                    value = BuildTuple(ctx);
                    return true;
                }
                _indices[i] = 0;
            }
            _exhausted = true;
            value = null;
            return false;
        }

        private ScriptValue BuildTuple(EvalContext ctx)
        {
            var row = new ScriptValue[_pools.Length];
            for (int i = 0; i < _pools.Length; i++)
                row[i] = _pools[i][_indices[i]];
            return ctx.Values.Tuple(row);
        }
    }
}
