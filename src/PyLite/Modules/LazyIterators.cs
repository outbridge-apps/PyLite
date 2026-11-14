using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // Lazy iterator engines for enumerate/zip/map/filter/reversed. Each extends
    // ScriptIteratorBase so every MoveNext charges one step; they are wrapped in an IteratorValue by the
    // builtins so that iter(it) is it and next(it) work through the existing model.
    internal sealed class EnumerateIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator _src;
        private BigInteger _index;
        private long _indexL;          // long counter while the start fits (quarter-range: ++ never overflows)
        private bool _small;

        internal EnumerateIterator(IScriptIterator src, BigInteger start)
        {
            _src = src;
            if (start >= long.MinValue / 4 && start <= long.MaxValue / 4)
            {
                _small = true;
                _indexL = (long)start;
            }
            else
                _index = start;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            ScriptValue item;
            if (!_src.MoveNext(ctx, out item))
            {
                value = null;
                return false;
            }
            if (_small)
            {
                value = ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(_indexL), item });
                _indexL++;
                return true;
            }
            value = ctx.Values.Tuple(new[] { ctx.Values.Int(_index), item });
            _index += 1;
            return true;
        }
    }

    internal sealed class ZipIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator[] _srcs;
        private readonly bool _strict;   // zip(strict=True), 3.10

        internal ZipIterator(IScriptIterator[] srcs, bool strict = false) { _srcs = srcs; _strict = strict; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_srcs.Length == 0)
            {
                value = null;
                return false;
            }
            var row = new ScriptValue[_srcs.Length];
            for (int i = 0; i < _srcs.Length; i++)
            {
                ScriptValue item;
                if (!_srcs[i].MoveNext(ctx, out item))
                {
                    if (_strict)
                        CheckStrict(ctx, _srcs, i, "zip");
                    value = null;
                    return false;   // first exhausted source ends the zip (values already read are dropped)
                }
                row[i] = item;
            }
            value = ctx.Values.Tuple(row);
            return true;
        }

        // CPython strict diagnostics: source i (0-based) exhausted first mid-row.
        internal static void CheckStrict(EvalContext ctx, IScriptIterator[] _srcs, int i, string f)
        {
            if (i > 0)
            {
                string plural = i == 1 ? "argument 1" : "arguments 1-" + i;
                throw Outbridge.PyLite.Runtime.Errors.Raise.ValueError(ctx,
                    f + "() argument " + (i + 1) + " is shorter than " + plural);
            }
            // Argument 1 ended the row: any later source still holding items is longer.
            for (int j = 1; j < _srcs.Length; j++)
            {
                ScriptValue extra;
                if (_srcs[j].MoveNext(ctx, out extra))
                    throw Outbridge.PyLite.Runtime.Errors.Raise.ValueError(ctx,
                        f + "() argument " + (j + 1) + " is longer than " + (j == 1 ? "argument 1" : "arguments 1-" + j));
            }
        }
    }

    internal sealed class MapIterator : ScriptIteratorBase
    {
        private readonly ScriptValue _fn;
        private readonly IScriptIterator _src;

        internal MapIterator(ScriptValue fn, IScriptIterator src)
        {
            _fn = fn;
            _src = src;
        }

        // one iterable: per-element call via the arrayless hook
        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            try
            {
                ScriptValue one;
                if (!_src.MoveNext(ctx, out one))
                {
                    value = null;
                    return false;
                }
                value = ctx.CallHook1(_fn, one);
                return true;
            }
            catch (Outbridge.PyLite.Runtime.Errors.ScriptException e) when (IsStop(e))
            {
                return EndOnStop(ctx, e, out value);
            }
        }
    }

    // map over several iterables. Its own class: a pull is MoveNext and this MoveNextCore, as for one iterable.
    internal sealed class MapManyIterator : ScriptIteratorBase
    {
        private readonly ScriptValue _fn;
        private readonly IScriptIterator[] _srcs;
        private readonly bool _strict;   // map(strict=True), 3.14

        internal MapManyIterator(ScriptValue fn, IScriptIterator[] srcs, bool strict)
        {
            _fn = fn;
            _srcs = srcs;
            _strict = strict;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            try
            {
                var items = new ScriptValue[_srcs.Length];
                for (int i = 0; i < _srcs.Length; i++)
                {
                    ScriptValue item;
                    if (!_srcs[i].MoveNext(ctx, out item))
                    {
                        if (_strict)
                            ZipIterator.CheckStrict(ctx, _srcs, i, "map");
                        value = null;
                        return false;   // stop at the shortest source
                    }
                    items[i] = item;
                }
                value = ctx.CallHook(_fn, items, Outbridge.PyLite.Runtime.Evaluator.KwArgs.Empty);
                return true;
            }
            catch (Outbridge.PyLite.Runtime.Errors.ScriptException e) when (IsStop(e))
            {
                return EndOnStop(ctx, e, out value);
            }
        }
    }

    internal sealed class FilterIterator : ScriptIteratorBase
    {
        private readonly ScriptValue _fn;
        private readonly IScriptIterator _src;

        private readonly bool _keepWhenTrue;   // false: itertools.filterfalse

        private FilterIterator(ScriptValue fn, IScriptIterator src, bool keepWhenTrue)
        {
            _fn = fn;
            _src = src;
            _keepWhenTrue = keepWhenTrue;
        }

        // fn null: the items' own truth (filter(None, xs)).
        internal static IScriptIterator Of(ScriptValue fn, IScriptIterator src, bool keepWhenTrue)
        {
            return fn == null ? (IScriptIterator)new TruthFilterIterator(src, keepWhenTrue) : new FilterIterator(fn, src, keepWhenTrue);
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            try
            {
                ScriptValue item;
                while (_src.MoveNext(ctx, out item))
                {
                    if (ctx.CallHook1(_fn, item).IsTruthy(ctx) == _keepWhenTrue)
                    {
                        value = item;
                        return true;
                    }
                }
                value = null;
                return false;
            }
            catch (Outbridge.PyLite.Runtime.Errors.ScriptException e) when (IsStop(e))
            {
                return EndOnStop(ctx, e, out value);
            }
        }
    }

    // filter(None, xs) and filterfalse(None, xs). The handler for a StopIteration out of an item's __bool__ is in
    // TruthOrStop, off the frame that nests: in MoveNextCore it brought a chain of these to the stack guard at
    // 160,000 levels, where it went past 200,000 without one (review 12, row 5).
    internal sealed class TruthFilterIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator _src;
        private readonly bool _keepWhenTrue;

        internal TruthFilterIterator(IScriptIterator src, bool keepWhenTrue)
        {
            _src = src;
            _keepWhenTrue = keepWhenTrue;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            ScriptValue item = null;
            bool keep = false, more = true;
            while (more && !keep)
            {
                more = _src.MoveNext(ctx, out item) && Test(ctx, item, out keep);
            }
            value = keep ? item : null;
            return keep;
        }

        // Its own frame: one more local in MoveNextCore brings 200,000 nested filters back to the stack guard.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private bool Test(EvalContext ctx, ScriptValue item, out bool keep)
        {
            bool truthy;
            bool tested = TruthOrStop(ctx, item, out truthy);
            keep = tested && truthy == _keepWhenTrue;
            return tested;
        }
    }

    // reversed() over a live list: LIVE semantics (a shrunk list ends early without error).
    internal sealed class ReversedListIterator : ScriptIteratorBase, ICopyableIterator
    {
        public IScriptIterator CopyPosition() { return (IScriptIterator)MemberwiseClone(); }

        private readonly ListValue _list;
        private int _index;

        internal ReversedListIterator(ListValue list) { _list = list; _index = list.Items.Count - 1; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_index < 0 || _index >= _list.Items.Count)
            {
                value = null;
                return false;
            }
            value = _list.Items[_index];
            _index--;
            return true;
        }
    }

    // reversed() over an immutable snapshot (tuple / str).
    internal sealed class ReversedArrayIterator : ScriptIteratorBase, ICopyableIterator
    {
        public IScriptIterator CopyPosition() { return (IScriptIterator)MemberwiseClone(); }

        private readonly ScriptValue[] _items;
        private int _index;

        internal ReversedArrayIterator(ScriptValue[] items) { _items = items; _index = items.Length - 1; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_index < 0)
            {
                value = null;
                return false;
            }
            value = _items[_index];
            _index--;
            return true;
        }
    }
}
