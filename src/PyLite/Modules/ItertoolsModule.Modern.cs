using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // itertools members from CPython 3.10-3.13: pairwise, batched(strict=).
    internal static partial class ItertoolsModule
    {
        private static ScriptValue Pairwise(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "pairwise", 1);
            return ctx.Values.EngineIterator(new PairwiseIterator(IterArg(ctx, args[0])), "itertools.pairwise");
        }

        private static ScriptValue Batched(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "batched", 2);
            BigInteger n = AsCountInt(ctx, args[1]);
            if (n < 1)
                throw Raise.ValueError(ctx, "n must be at least one");
            bool strict = false;
            ScriptValue sv;
            if (kw.TryGet("strict", out sv))
                strict = sv.IsTruthy(ctx);
            return ctx.Values.EngineIterator(new BatchedIterator(IterArg(ctx, args[0]), Coerce.ToSsizeBound(ctx, n), strict), "itertools.batched");
        }
    }

    // pairwise: consecutive overlapping pairs; fewer than two elements -> empty.
    internal sealed class PairwiseIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator _src;
        private ScriptValue _prev;
        private bool _primed;
        private bool _done;   // CPython's pairwise drops its source at its end: a source that resumes is not read

        internal PairwiseIterator(IScriptIterator src) { _src = src; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_done)
            {
                value = null;
                return false;
            }
            _done = true;   // stays so if the source raises: CPython's pairwise drops its source then
            if (!_primed)
            {
                if (!_src.MoveNext(ctx, out _prev))
                {
                    value = null;
                    return false;
                }
                _primed = true;
            }
            ScriptValue cur;
            if (!_src.MoveNext(ctx, out cur))
            {
                _prev = null;
                value = null;
                return false;
            }
            _done = false;
            value = ctx.Values.Tuple(new[] { _prev, cur });
            _prev = cur;
            return true;
        }
    }

    // batched: tuples of up to n elements; strict=True -> ValueError on an incomplete final batch.
    internal sealed class BatchedIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator _src;
        private readonly int _n;
        private readonly bool _strict;
        private bool _done;

        internal BatchedIterator(IScriptIterator src, int n, bool strict)
        {
            _src = src;
            _n = n;
            _strict = strict;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_done)
            {
                value = null;
                return false;
            }
            var batch = new List<ScriptValue>(System.Math.Min(_n, 16));   // grows with the items, not with n
            ScriptValue v;
            _done = true;   // stays so if the source raises: CPython's batched drops its source then
            while (batch.Count < _n && _src.MoveNext(ctx, out v))
            {
                if (batch.Count == batch.Capacity)   // n=30M over repeat(0): charged and capped as it grows
                {
                    ctx.Values.EnsureCollectionSize(batch.Count + 1L);
                    ctx.Values.PreCharge(8L * batch.Count);
                    batch.Capacity = (int)System.Math.Min(System.Math.Max(16L, 2L * batch.Count), System.Math.Min(_n, ctx.Limits.MaxCollectionItems));
                }
                batch.Add(v);
            }
            _done = batch.Count == 0 || (_strict && batch.Count < _n);   // a short batch is no end: CPython asks again
            if (batch.Count == 0)
            {
                value = null;
                return false;
            }
            if (_strict && batch.Count < _n)
                throw Raise.ValueError(ctx, "batched(): incomplete batch");
            value = ctx.Values.Tuple(batch.ToArray());
            return true;
        }
    }
}
