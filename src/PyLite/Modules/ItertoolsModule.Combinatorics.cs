using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // itertools combinatorics: permutations, combinations, combinations_with_replacement.
    // Exact ports of the CPython indices/cycles algorithms (no recursion); pools materialized+charged.
    internal static partial class ItertoolsModule
    {
        internal static ScriptValue[] MaterializePool(EvalContext ctx, ScriptValue iterable)
        {
            return Iter.ToArray(ctx, iterable);   // charged and capped at MaxCollectionItems
        }

        private static ScriptValue Permutations(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "permutations", 1, 2);
            ScriptValue[] pool = MaterializePool(ctx, args[0]);
            int r = pool.Length;
            ScriptValue rArg = args.Length == 2 ? args[1] : null;
            ScriptValue rkw;
            if (kw.TryGet("r", out rkw))
                rArg = rkw;
            if (rArg != null && rArg.Kind != ValueKind.None)
            {
                BigInteger rb = AsCountInt(ctx, rArg);
                if (rb < 0)
                    throw Raise.ValueError(ctx, "r must be non-negative");
                r = ToSsize(ctx, rb);   // OverflowError (not a fault) when r cannot fit an int, like CPython
            }
            return ctx.Values.Iterator(new PermutationsIterator(pool, r), "itertools.permutations");
        }

        private static ScriptValue Combinations(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            int r = RequiredR(ctx, args, kw, "combinations", out ScriptValue[] pool);
            return ctx.Values.Iterator(new CombinationsIterator(pool, r), "itertools.combinations");
        }

        private static ScriptValue CombinationsWithReplacement(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            int r = RequiredR(ctx, args, kw, "combinations_with_replacement", out ScriptValue[] pool);
            ctx.Values.PreCharge((long)r * 4 + 16);   // cwr (unlike combinations) keeps an int[r] index vector
            return ctx.Values.Iterator(new CwrIterator(pool, r), "itertools.combinations_with_replacement");
        }

        private static int RequiredR(EvalContext ctx, ScriptValue[] args, KwArgs kw, string name, out ScriptValue[] pool)
        {
            Args.Between(ctx, args, name, 1, 2);
            pool = MaterializePool(ctx, args[0]);
            ScriptValue rArg = args.Length == 2 ? args[1] : null;
            ScriptValue rkw;
            if (kw.TryGet("r", out rkw))
                rArg = rkw;
            if (rArg == null)
                throw Raise.TypeError(ctx, name + "() missing required argument 'r' (pos 2)");
            BigInteger rb = AsCountInt(ctx, rArg);
            if (rb < 0)
                throw Raise.ValueError(ctx, "r must be non-negative");
            return ToSsize(ctx, rb);
        }

        // A validated non-negative count as an int, raising OverflowError (never an unchecked-cast fault)
        // when it exceeds int range — CPython's "too large to convert to C ssize_t" for r / repeat.
        internal static int ToSsize(EvalContext ctx, BigInteger n)
        {
            return Coerce.ToSsize(ctx, n);
        }
    }

    // The shape permutations, combinations and combinations_with_replacement share: a pool, r and an index
    // vector. The first row is the initial indices; each later row is Advance(), false when exhausted.
    internal abstract class IndexTupleIterator : ScriptIteratorBase
    {
        protected readonly ScriptValue[] Pool;
        protected readonly int N;
        protected readonly int R;
        protected int[] Indices;
        protected bool Exhausted;
        private bool _started;

        protected IndexTupleIterator(ScriptValue[] pool, int r)
        {
            Pool = pool;
            N = pool.Length;
            R = r;
        }

        protected abstract bool Advance();

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (!Exhausted && (!_started || Advance()))
            {
                _started = true;
                var row = new ScriptValue[R];
                for (int i = 0; i < R; i++)
                    row[i] = Pool[Indices[i]];
                value = ctx.Values.Tuple(row);
                return true;
            }
            Exhausted = true;
            value = null;
            return false;
        }
    }

    // permutations: port of itertoolsmodule.c::permutations_next (positional uniqueness, not by value).
    internal sealed class PermutationsIterator : IndexTupleIterator
    {
        private readonly int[] _cycles;

        internal PermutationsIterator(ScriptValue[] pool, int r) : base(pool, r)
        {
            if (r > N)
            {
                Exhausted = true;
                Indices = Array.Empty<int>();
                _cycles = Array.Empty<int>();
                return;
            }
            Indices = new int[N];
            for (int i = 0; i < N; i++)
                Indices[i] = i;
            _cycles = new int[R];
            for (int i = 0; i < R; i++)
                _cycles[i] = N - i;
        }

        protected override bool Advance()
        {
            for (int i = R - 1; i >= 0; i--)
            {
                _cycles[i]--;
                if (_cycles[i] == 0)
                {
                    int tmp = Indices[i];
                    for (int k = i; k < N - 1; k++)
                        Indices[k] = Indices[k + 1];
                    Indices[N - 1] = tmp;
                    _cycles[i] = N - i;
                }
                else
                {
                    int j = N - _cycles[i];
                    int t = Indices[i];
                    Indices[i] = Indices[j];
                    Indices[j] = t;
                    return true;
                }
            }
            return false;
        }
    }

    // combinations: port of combinations_next (strictly increasing indices).
    internal sealed class CombinationsIterator : IndexTupleIterator
    {
        internal CombinationsIterator(ScriptValue[] pool, int r) : base(pool, r)
        {
            if (r > N)
            {
                Exhausted = true;
                Indices = Array.Empty<int>();
                return;
            }
            Indices = new int[r];
            for (int i = 0; i < r; i++)
                Indices[i] = i;
        }

        protected override bool Advance()
        {
            int i = R - 1;
            while (i >= 0 && Indices[i] == i + N - R)
                i--;
            if (i < 0)
                return false;
            Indices[i]++;
            for (int j = i + 1; j < R; j++)
                Indices[j] = Indices[j - 1] + 1;
            return true;
        }
    }

    // combinations_with_replacement: port of cwr_next (non-decreasing indices).
    internal sealed class CwrIterator : IndexTupleIterator
    {
        internal CwrIterator(ScriptValue[] pool, int r) : base(pool, r)
        {
            Indices = new int[r];   // all zeros
            if (N == 0 && R > 0)
                Exhausted = true;
        }

        protected override bool Advance()
        {
            int i = R - 1;
            while (i >= 0 && Indices[i] == N - 1)
                i--;
            if (i < 0)
                return false;
            int newval = Indices[i] + 1;
            for (int j = i; j < R; j++)
                Indices[j] = newval;
            return true;
        }
    }
}
