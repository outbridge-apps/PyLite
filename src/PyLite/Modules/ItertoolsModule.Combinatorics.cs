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
    public static partial class ItertoolsModule
    {
        internal static ScriptValue[] MaterializePool(EvalContext ctx, ScriptValue iterable)
        {
            IScriptIterator it = IterArg(ctx, iterable);
            var pool = new List<ScriptValue>();
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
            {
                ctx.Values.PreCharge(16);
                pool.Add(v);
            }
            return pool.ToArray();
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

    // permutations: port of itertoolsmodule.c::permutations_next (positional uniqueness, not by value).
    internal sealed class PermutationsIterator : ScriptIteratorBase
    {
        private readonly ScriptValue[] _pool;
        private readonly int _n;
        private readonly int _r;
        private readonly int[] _indices;
        private readonly int[] _cycles;
        private bool _started;
        private bool _exhausted;

        internal PermutationsIterator(ScriptValue[] pool, int r)
        {
            _pool = pool;
            _n = pool.Length;
            _r = r;
            if (r > _n)
            {
                _exhausted = true;
                _indices = Array.Empty<int>();
                _cycles = Array.Empty<int>();
                return;
            }
            _indices = new int[_n];
            for (int i = 0; i < _n; i++)
                _indices[i] = i;
            _cycles = new int[_r];
            for (int i = 0; i < _r; i++)
                _cycles[i] = _n - i;
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
            for (int i = _r - 1; i >= 0; i--)
            {
                _cycles[i]--;
                if (_cycles[i] == 0)
                {
                    int tmp = _indices[i];
                    for (int k = i; k < _n - 1; k++)
                        _indices[k] = _indices[k + 1];
                    _indices[_n - 1] = tmp;
                    _cycles[i] = _n - i;
                }
                else
                {
                    int j = _n - _cycles[i];
                    int t = _indices[i];
                    _indices[i] = _indices[j];
                    _indices[j] = t;
                    value = BuildTuple(ctx);
                    return true;
                }
            }
            _exhausted = true;
            value = null;
            return false;
        }

        private ScriptValue BuildTuple(EvalContext ctx)
        {
            var row = new ScriptValue[_r];
            for (int i = 0; i < _r; i++)
                row[i] = _pool[_indices[i]];
            return ctx.Values.Tuple(row);
        }
    }

    // combinations: port of combinations_next (strictly increasing indices).
    internal sealed class CombinationsIterator : ScriptIteratorBase
    {
        private readonly ScriptValue[] _pool;
        private readonly int _n;
        private readonly int _r;
        private readonly int[] _indices;
        private bool _started;
        private bool _exhausted;

        internal CombinationsIterator(ScriptValue[] pool, int r)
        {
            _pool = pool;
            _n = pool.Length;
            _r = r;
            if (r > _n)
            {
                _exhausted = true;
                _indices = Array.Empty<int>();
                return;
            }
            _indices = new int[r];
            for (int i = 0; i < r; i++)
                _indices[i] = i;
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
            int i = _r - 1;
            while (i >= 0 && _indices[i] == i + _n - _r)
                i--;
            if (i < 0)
            {
                _exhausted = true;
                value = null;
                return false;
            }
            _indices[i]++;
            for (int j = i + 1; j < _r; j++)
                _indices[j] = _indices[j - 1] + 1;
            value = BuildTuple(ctx);
            return true;
        }

        private ScriptValue BuildTuple(EvalContext ctx)
        {
            var row = new ScriptValue[_r];
            for (int i = 0; i < _r; i++)
                row[i] = _pool[_indices[i]];
            return ctx.Values.Tuple(row);
        }
    }

    // combinations_with_replacement: port of cwr_next (non-decreasing indices).
    internal sealed class CwrIterator : ScriptIteratorBase
    {
        private readonly ScriptValue[] _pool;
        private readonly int _n;
        private readonly int _r;
        private readonly int[] _indices;
        private bool _started;
        private bool _exhausted;

        internal CwrIterator(ScriptValue[] pool, int r)
        {
            _pool = pool;
            _n = pool.Length;
            _r = r;
            _indices = new int[r];   // all zeros
            if (_n == 0 && _r > 0)
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
            int i = _r - 1;
            while (i >= 0 && _indices[i] == _n - 1)
                i--;
            if (i < 0)
            {
                _exhausted = true;
                value = null;
                return false;
            }
            int newval = _indices[i] + 1;
            for (int j = i; j < _r; j++)
                _indices[j] = newval;
            value = BuildTuple(ctx);
            return true;
        }

        private ScriptValue BuildTuple(EvalContext ctx)
        {
            var row = new ScriptValue[_r];
            for (int i = 0; i < _r; i++)
                row[i] = _pool[_indices[i]];
            return ctx.Values.Tuple(row);
        }
    }
}
