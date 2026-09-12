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
    public static partial class ItertoolsModule
    {
        private static ScriptValue Pairwise(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "pairwise", 1);
            return ctx.Values.Iterator(new PairwiseIterator(IterArg(ctx, args[0])), "itertools.pairwise");
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
            return ctx.Values.Iterator(new BatchedIterator(IterArg(ctx, args[0]), ToSsize(ctx, n), strict), "itertools.batched");
        }
    }

    // pairwise: consecutive overlapping pairs; fewer than two elements -> empty.
    internal sealed class PairwiseIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator _src;
        private ScriptValue _prev;
        private bool _primed;

        internal PairwiseIterator(IScriptIterator src) { _src = src; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
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
                value = null;
                return false;
            }
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
            var batch = new List<ScriptValue>(_n);
            ScriptValue v;
            while (batch.Count < _n && _src.MoveNext(ctx, out v))
                batch.Add(v);
            if (batch.Count == 0)
            {
                _done = true;
                value = null;
                return false;
            }
            if (batch.Count < _n)
            {
                _done = true;
                if (_strict)
                    throw Raise.ValueError(ctx, "batched(): incomplete batch");
            }
            value = ctx.Values.Tuple(batch.ToArray());
            return true;
        }
    }
}
