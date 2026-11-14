using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // itertools grouping/combining iterators: groupby (+_grouper), starmap, zip_longest.
    internal static partial class ItertoolsModule
    {
        private static ScriptValue GroupBy(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "groupby", 1, 2);
            IScriptIterator src = IterArg(ctx, args[0]);
            ScriptValue keyFunc = args.Length == 2 && args[1].Kind != ValueKind.None ? args[1] : null;
            ScriptValue kkw;
            if (kw.TryGet("key", out kkw) && kkw.Kind != ValueKind.None)
                keyFunc = kkw;
            return ctx.Values.EngineIterator(new GroupByIterator(src, keyFunc), "itertools.groupby");
        }

        private static ScriptValue Starmap(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "starmap", 2);
            IScriptIterator src = IterArg(ctx, args[1]);
            return ctx.Values.EngineIterator(new StarmapIterator(args[0], src), "itertools.starmap");
        }

        private static ScriptValue ZipLongest(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ScriptValue fill = ctx.Values.None;
            ScriptValue fkw;
            if (kw.TryGet("fillvalue", out fkw))
                fill = fkw;
            var srcs = new IScriptIterator[args.Length];
            for (int i = 0; i < args.Length; i++)
                srcs[i] = IterArg(ctx, args[i]);
            return ctx.Values.EngineIterator(new ZipLongestIterator(srcs, fill), "itertools.zip_longest");
        }
    }

    // groupby, as itertoolsmodule.c: one shared underlying iterator; an outer step invalidates the previous
    // _grouper via a monotonic group id and reads past any unread tail of the previous group. A value is read
    // when the next one is asked for, not after one is handed out, so a key that raises loses no value.
    internal sealed class GroupByIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator _src;
        private readonly ScriptValue _keyFunc;   // null => identity
        private ScriptValue _tgtKey;

        internal ScriptValue CurValue;   // null once a grouper handed it out (currvalue == NULL)
        internal ScriptValue CurKey;     // null before the first read (currkey == NULL)
        internal long CurrGroupId;

        // With no key function the only script it calls is the keys' __eq__, which EqualOrStop handles off the
        // frame that nests.
        internal GroupByIterator(IScriptIterator src, ScriptValue keyFunc) : base(keyFunc != null) { _src = src; _keyFunc = keyFunc; }

        internal bool HasKeyFunc => _keyFunc != null;

        // groupby_step: false at the end, the state untouched; a raising key leaves it untouched too.
        internal bool Step(EvalContext ctx)
        {
            ScriptValue v;
            if (!_src.MoveNext(ctx, out v))
                return false;
            ScriptValue k = _keyFunc == null ? v : ctx.CallHook1(_keyFunc, v);
            CurValue = v;
            CurKey = k;
            return true;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            CurrGroupId += 1;
            int same = 0;   // the last EqualOrStop: below zero ends the iterator
            while (true)
            {
                if (CurKey != null && (_tgtKey == null || (same = EqualOrStop(ctx, _tgtKey, CurKey)) <= 0))
                    break;
                if (!Step(ctx))
                {
                    value = null;
                    return false;
                }
            }
            if (same < 0)
            {
                value = null;
                return false;
            }
            _tgtKey = CurKey;
            var grouper = new GrouperIterator(this, CurrGroupId, _tgtKey);
            ScriptValue grouperVal = ctx.Values.EngineIterator(grouper, "itertools._grouper");
            value = ctx.Values.Tuple(new[] { _tgtKey, grouperVal });
            return true;
        }
    }

    // _grouper: yields the parent's current run while its key holds and the parent has not stepped past it.
    internal sealed class GrouperIterator : ScriptIteratorBase
    {
        private readonly GroupByIterator _parent;
        private readonly long _id;
        private readonly ScriptValue _key;

        internal GrouperIterator(GroupByIterator parent, long id, ScriptValue key) : base(parent.HasKeyFunc)
        {
            _parent = parent;
            _id = id;
            _key = key;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            value = null;
            if (_parent.CurrGroupId != _id)
                return false;
            if (_parent.CurValue == null && !_parent.Step(ctx))
                return false;
            if (EqualOrStop(ctx, _key, _parent.CurKey) <= 0)   // a stop ends the group as a new key does
                return false;
            value = _parent.CurValue;
            _parent.CurValue = null;
            return true;
        }
    }

    // starmap: func(*element) per element; a non-iterable element raises the CPython iterability TypeError.
    internal sealed class StarmapIterator : ScriptIteratorBase
    {
        private readonly ScriptValue _func;
        private readonly IScriptIterator _src;

        internal StarmapIterator(ScriptValue func, IScriptIterator src) { _func = func; _src = src; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            try
            {
                ScriptValue tup;
                if (!_src.MoveNext(ctx, out tup))
                {
                    value = null;
                    return false;
                }
                ScriptValue[] callArgs = MaterializeArgs(ctx, tup);
                value = ctx.CallHook(_func, callArgs, KwArgs.Empty);
                return true;
            }
            catch (Outbridge.PyLite.Runtime.Errors.ScriptException e) when (IsStop(e))
            {
                return EndOnStop(ctx, e, out value);
            }
        }

        private static ScriptValue[] MaterializeArgs(EvalContext ctx, ScriptValue tup)
        {
            return Iter.ToArray(ctx, tup);
        }
    }

    // zip_longest: pulls every not-yet-exhausted source each row (side effects visible), fills spent ones,
    // and stops only when every source is exhausted (a row with no live source is not yielded).
    internal sealed class ZipLongestIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator[] _srcs;
        private readonly bool[] _done;
        private readonly ScriptValue _fill;
        private int _active;

        internal ZipLongestIterator(IScriptIterator[] srcs, ScriptValue fill)
        {
            _srcs = srcs;
            _done = new bool[srcs.Length];
            _fill = fill;
            _active = srcs.Length;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_active == 0)
            {
                value = null;
                return false;
            }
            var row = new ScriptValue[_srcs.Length];
            int got = 0;
            for (int i = 0; i < _srcs.Length; i++)
            {
                if (_done[i])
                {
                    row[i] = _fill;
                    continue;
                }
                ScriptValue v;
                if (_srcs[i].MoveNext(ctx, out v))
                {
                    row[i] = v;
                    got++;
                }
                else
                {
                    _done[i] = true;
                    _active--;
                    row[i] = _fill;
                }
            }
            if (got == 0)
            {
                value = null;
                return false;
            }
            value = ctx.Values.Tuple(row);
            return true;
        }
    }
}
