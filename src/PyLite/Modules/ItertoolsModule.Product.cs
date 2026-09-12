using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // itertools tee + product. tee shares one divergence buffer capped at
    // TeeBufferCap (a module constant rather than a ResourceLimits field, to avoid churning that ctor);
    // product materializes all pools up front (an infinite input aborts in the constructor).
    public static partial class ItertoolsModule
    {
        internal const int TeeBufferCap = 10000;

        private static ScriptValue Tee(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length < 1 || args.Length > 2)
                throw Raise.TypeError(ctx, "tee expected at most 2 arguments, got " + args.Length);
            int n = 2;
            if (args.Length == 2)
            {
                BigInteger nb = AsCountInt(ctx, args[1]);
                if (nb < 0)
                    throw Raise.ValueError(ctx, "n must be >= 0");
                n = ToSsize(ctx, nb);
            }
            IScriptIterator src = IterArg(ctx, args[0]);
            var buf = new TeeBuffer(src);
            var tuple = new ScriptValue[n];
            for (int i = 0; i < n; i++)
            {
                var t = new TeeIterator(buf);
                buf.Register(t);
                tuple[i] = ctx.Values.Iterator(t, "itertools._tee");
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
                IScriptIterator it = IterArg(ctx, args[i]);
                var pool = new List<ScriptValue>();
                ScriptValue v;
                while (it.MoveNext(ctx, out v))   // an infinite input never returns -> EngineAbort here (constructor)
                {
                    ctx.Values.PreCharge(16);
                    pool.Add(v);
                }
                basePools[i] = pool.ToArray();
            }
            long poolCount = (long)args.Length * repeat;
            ctx.Values.PreCharge(poolCount * 8 + 16);   // bounds poolCount before the (int) cast + alloc
            var pools = new ScriptValue[(int)poolCount][];
            int idx = 0;
            for (int r = 0; r < repeat; r++)
                for (int i = 0; i < args.Length; i++)
                    pools[idx++] = basePools[i];
            return ctx.Values.Iterator(new ProductIterator(pools), "itertools.product");
        }

        // Interpret a value as a count/index int (int or bool); float/other -> TypeError.
        private static BigInteger AsCountInt(EvalContext ctx, ScriptValue v)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
                return iv.Value;
            if (v is BoolValue)
                return ((BoolValue)v).Value ? BigInteger.One : BigInteger.Zero;
            throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
        }
    }

    // tee's shared divergence buffer: a ring holding elements still needed by at least one clone.
    internal sealed class TeeBuffer
    {
        private readonly IScriptIterator _src;
        private readonly List<ScriptValue> _ring = new List<ScriptValue>();
        private readonly List<TeeIterator> _clones = new List<TeeIterator>();
        private long _baseIndex;    // absolute index of _ring[0]
        private long _producedTo;   // total elements pulled from the source
        private bool _srcDone;

        internal TeeBuffer(IScriptIterator src) { _src = src; }

        internal void Register(TeeIterator t) { _clones.Add(t); }

        internal bool Get(EvalContext ctx, long absIdx, out ScriptValue v)
        {
            while (absIdx >= _producedTo && !_srcDone)
            {
                ScriptValue nv;
                if (_src.MoveNext(ctx, out nv))
                {
                    ctx.Values.PreCharge(16);
                    _ring.Add(nv);
                    _producedTo++;
                    if (_ring.Count > ItertoolsModule.TeeBufferCap)
                        throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "TeeBufferCap", ItertoolsModule.TeeBufferCap, _ring.Count);
                }
                else
                {
                    _srcDone = true;
                }
            }
            if (absIdx >= _producedTo)
            {
                v = null;
                return false;
            }
            v = _ring[(int)(absIdx - _baseIndex)];
            return true;
        }

        internal void RecomputeMinAndTrim()
        {
            long min = long.MaxValue;
            for (int i = 0; i < _clones.Count; i++)
                if (_clones[i].Pos < min)
                    min = _clones[i].Pos;
            while (_baseIndex < min && _ring.Count > 0)
            {
                _ring.RemoveAt(0);
                _baseIndex++;
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
            _buf.RecomputeMinAndTrim();
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
