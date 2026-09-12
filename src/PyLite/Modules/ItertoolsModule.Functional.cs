using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // itertools functional iterators: accumulate, chain (+from_iterable), compress
    // dropwhile, takewhile, filterfalse. Predicate/accumulator callables are invoked via ctx.CallHook.
    public static partial class ItertoolsModule
    {
        // chain is a type (CPython parity) so chain.from_iterable resolves via its static slots.
        internal static TypeValue MakeChain(EvalContext ctx)
        {
            var statics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["from_iterable"] = BuiltinFunctionValue.Make("chain.from_iterable", ChainFromIterable),
            };
            return ctx.Values.Type("itertools.chain", ChainCtor, null, statics);
        }

        private static ScriptValue ChainCtor(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            IScriptIterator outer = PyOps.GetIterator(ctx.Values.Tuple((ScriptValue[])args.Clone()), ctx);
            return ctx.Values.Iterator(new ChainIterator(outer), "itertools.chain");
        }

        private static ScriptValue ChainFromIterable(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "from_iterable", 1);
            IScriptIterator outer = IterArg(ctx, args[0]);
            return ctx.Values.Iterator(new ChainIterator(outer), "itertools.chain");
        }

        private static ScriptValue Accumulate(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "accumulate", 1, 2);
            IScriptIterator src = IterArg(ctx, args[0]);
            ScriptValue func = args.Length == 2 && args[1].Kind != ValueKind.None ? args[1] : null;
            ScriptValue fkw;
            if (kw.TryGet("func", out fkw) && fkw.Kind != ValueKind.None)
                func = fkw;
            ScriptValue initial = null;   // 3.8: yielded first when given
            ScriptValue ikw;
            if (kw.TryGet("initial", out ikw) && ikw.Kind != ValueKind.None)
                initial = ikw;
            return ctx.Values.Iterator(new AccumulateIterator(src, func, initial), "itertools.accumulate");
        }

        private static ScriptValue Compress(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "compress", 2);
            IScriptIterator data = IterArg(ctx, args[0]);
            IScriptIterator sel = IterArg(ctx, args[1]);
            return ctx.Values.Iterator(new CompressIterator(data, sel), "itertools.compress");
        }

        private static ScriptValue DropWhile(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length != 2)
                throw Raise.TypeError(ctx, "dropwhile expected 2 arguments, got " + args.Length);
            IScriptIterator src = IterArg(ctx, args[1]);
            return ctx.Values.Iterator(new DropWhileIterator(args[0], src), "itertools.dropwhile");
        }

        private static ScriptValue TakeWhile(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length != 2)
                throw Raise.TypeError(ctx, "takewhile expected 2 arguments, got " + args.Length);
            IScriptIterator src = IterArg(ctx, args[1]);
            return ctx.Values.Iterator(new TakeWhileIterator(args[0], src), "itertools.takewhile");
        }

        private static ScriptValue FilterFalse(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length != 2)
                throw Raise.TypeError(ctx, "filterfalse expected 2 arguments, got " + args.Length);
            ScriptValue pred = args[0].Kind == ValueKind.None ? null : args[0];
            IScriptIterator src = IterArg(ctx, args[1]);
            return ctx.Values.Iterator(new FilterFalseIterator(pred, src), "itertools.filterfalse");
        }
    }

    // accumulate: yields running totals; the first element (or initial=, 3.8) is emitted without calling func.
    internal sealed class AccumulateIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator _src;
        private readonly ScriptValue _func;      // null => the engine '+'
        private readonly ScriptValue _initial;   // null => start from the first element
        private ScriptValue _total;
        private bool _started;

        internal AccumulateIterator(IScriptIterator src, ScriptValue func, ScriptValue initial = null)
        {
            _src = src;
            _func = func;
            _initial = initial;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (!_started)
            {
                if (_initial != null)
                    _total = _initial;
                else if (!_src.MoveNext(ctx, out _total))
                {
                    value = null;
                    return false;
                }
                _started = true;
                value = _total;
                return true;
            }
            ScriptValue nxt;
            if (!_src.MoveNext(ctx, out nxt))
            {
                value = null;
                return false;
            }
            _total = _func == null
                ? PyOps.BinaryOp(PyBinOp.Add, _total, nxt, ctx)
                : ctx.CallHook(_func, new[] { _total, nxt }, KwArgs.Empty);
            value = _total;
            return true;
        }
    }

    // chain: concatenate iterables lazily; the next inner iterable is acquired only when the current ends.
    internal sealed class ChainIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator _outer;
        private IScriptIterator _cur;

        internal ChainIterator(IScriptIterator outer) { _outer = outer; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            while (true)
            {
                if (_cur != null)
                {
                    if (_cur.MoveNext(ctx, out value))
                        return true;
                    _cur = null;
                }
                ScriptValue nextIterable;
                if (!_outer.MoveNext(ctx, out nextIterable))
                {
                    value = null;
                    return false;
                }
                _cur = ItertoolsModule.IterArg(ctx, nextIterable);
            }
        }
    }

    // compress: yields data[i] where selectors[i] is truthy; stops at the shorter of the two.
    internal sealed class CompressIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator _data;
        private readonly IScriptIterator _selectors;

        internal CompressIterator(IScriptIterator data, IScriptIterator selectors) { _data = data; _selectors = selectors; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            while (true)
            {
                ScriptValue d, s;
                if (!_data.MoveNext(ctx, out d) || !_selectors.MoveNext(ctx, out s))
                {
                    value = null;
                    return false;
                }
                if (s.IsTruthy(ctx))
                {
                    value = d;
                    return true;
                }
            }
        }
    }

    // dropwhile: drop leading elements while pred is truthy; the first false element is yielded, then pred
    // is never called again.
    internal sealed class DropWhileIterator : ScriptIteratorBase
    {
        private readonly ScriptValue _pred;
        private readonly IScriptIterator _src;
        private bool _dropping = true;

        internal DropWhileIterator(ScriptValue pred, IScriptIterator src) { _pred = pred; _src = src; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_dropping)
            {
                while (true)
                {
                    ScriptValue v;
                    if (!_src.MoveNext(ctx, out v))
                    {
                        value = null;
                        return false;
                    }
                    if (!ctx.CallHook1(_pred, v).IsTruthy(ctx))
                    {
                        _dropping = false;
                        value = v;
                        return true;
                    }
                }
            }
            if (!_src.MoveNext(ctx, out value))
            {
                value = null;
                return false;
            }
            return true;
        }
    }

    // takewhile: yield while pred is truthy; the first false element consumes the source element and ends.
    internal sealed class TakeWhileIterator : ScriptIteratorBase
    {
        private readonly ScriptValue _pred;
        private readonly IScriptIterator _src;
        private bool _done;

        internal TakeWhileIterator(ScriptValue pred, IScriptIterator src) { _pred = pred; _src = src; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_done)
            {
                value = null;
                return false;
            }
            ScriptValue v;
            if (!_src.MoveNext(ctx, out v))
            {
                _done = true;
                value = null;
                return false;
            }
            if (ctx.CallHook1(_pred, v).IsTruthy(ctx))
            {
                value = v;
                return true;
            }
            _done = true;
            value = null;
            return false;
        }
    }

    // filterfalse: yields elements where pred is falsy; pred=None filters by the elements' own truthiness.
    internal sealed class FilterFalseIterator : ScriptIteratorBase
    {
        private readonly ScriptValue _pred;   // null => filter by falsy
        private readonly IScriptIterator _src;

        internal FilterFalseIterator(ScriptValue pred, IScriptIterator src) { _pred = pred; _src = src; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            ScriptValue v;
            while (_src.MoveNext(ctx, out v))
            {
                bool keep = _pred == null
                    ? !v.IsTruthy(ctx)
                    : !ctx.CallHook1(_pred, v).IsTruthy(ctx);
                if (keep)
                {
                    value = v;
                    return true;
                }
            }
            value = null;
            return false;
        }
    }
}
