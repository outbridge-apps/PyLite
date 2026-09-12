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
    // itertools: the 3.4 iterator toolkit as IScriptIterators wrapped in IteratorValue
    // so iter(it) is it and next(it) work through the existing model. Every MoveNext charges one Budget.Step
    // (ScriptIteratorBase), which makes the infinite count/cycle/repeat safe by construction. All reentrant
    // script-callable invocations go through ctx.CallHook; all materialization is charged before allocation.
    public static partial class ItertoolsModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["count"] = BuiltinFunctionValue.Make("count", Count);
            m["repeat"] = BuiltinFunctionValue.Make("repeat", Repeat);
            m["cycle"] = BuiltinFunctionValue.Make("cycle", Cycle);
            m["islice"] = BuiltinFunctionValue.Make("islice", Islice);
            m["accumulate"] = BuiltinFunctionValue.Make("accumulate", Accumulate);
            m["chain"] = MakeChain(ctx);
            m["compress"] = BuiltinFunctionValue.Make("compress", Compress);
            m["dropwhile"] = BuiltinFunctionValue.Make("dropwhile", DropWhile);
            m["takewhile"] = BuiltinFunctionValue.Make("takewhile", TakeWhile);
            m["filterfalse"] = BuiltinFunctionValue.Make("filterfalse", FilterFalse);
            m["groupby"] = BuiltinFunctionValue.Make("groupby", GroupBy);
            m["starmap"] = BuiltinFunctionValue.Make("starmap", Starmap);
            m["zip_longest"] = BuiltinFunctionValue.Make("zip_longest", ZipLongest);
            m["tee"] = BuiltinFunctionValue.Make("tee", Tee);
            m["product"] = BuiltinFunctionValue.Make("product", Product);
            m["permutations"] = BuiltinFunctionValue.Make("permutations", Permutations);
            m["combinations"] = BuiltinFunctionValue.Make("combinations", Combinations);
            m["combinations_with_replacement"] = BuiltinFunctionValue.Make("combinations_with_replacement", CombinationsWithReplacement);
            m["pairwise"] = BuiltinFunctionValue.Make("pairwise", Pairwise);
            m["batched"] = BuiltinFunctionValue.Make("batched", Batched);
            return ctx.Values.Module("itertools", m);
        }

        // Obtain an argument's iterator, raising the CPython "'<type>' object is not iterable" on failure.
        internal static IScriptIterator IterArg(EvalContext ctx, ScriptValue v)
        {
            return PyOps.GetIterator(v, ctx);
        }

        private static bool IsNumber(ScriptValue v)
        {
            return v.Kind == ValueKind.Int || v.Kind == ValueKind.Float || v.Kind == ValueKind.Bool;
        }

        private static bool IsIntOne(ScriptValue v)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
                return iv.Value.IsOne;
            BoolValue bv = v as BoolValue;
            return bv != null && bv.Value;
        }

        // ---- count(start=0, step=1) ----
        private static ScriptValue Count(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            args = StrMethods.WithKw(ctx, args, kw, "count", "start", "step");
            ScriptValue start = args.Length >= 1 && args[0].Kind != ValueKind.None ? args[0] : ctx.Values.Int(0);
            ScriptValue step = args.Length >= 2 ? args[1] : ctx.Values.Int(1);
            if (!IsNumber(start) || !IsNumber(step))
                throw Raise.TypeError(ctx, "a number is required");
            return ctx.Values.Iterator(new CountIterator(start, step, IsIntOne(step)), "itertools.count");
        }

        // ---- repeat(obj, times=None) ----
        private static ScriptValue Repeat(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length < 1 || args.Length > 2)
                throw Raise.TypeError(ctx, "repeat() takes at most 2 arguments");
            ScriptValue obj = args[0];
            ScriptValue timesVal = args.Length == 2 ? args[1] : null;
            ScriptValue tkw;
            if (kw.TryGet("times", out tkw))
                timesVal = tkw;
            if (timesVal == null)
                return ctx.Values.Iterator(new RepeatIterator(obj), "itertools.repeat");
            if (timesVal.Kind == ValueKind.Float)
                throw Raise.TypeError(ctx, "integer argument expected, got float");
            BigInteger times;
            IntValue iv = timesVal as IntValue;
            if (iv != null)
                times = iv.Value;
            else if (timesVal is BoolValue)
                times = ((BoolValue)timesVal).Value ? BigInteger.One : BigInteger.Zero;
            else
                throw Raise.TypeError(ctx, "'" + timesVal.PyTypeName + "' object cannot be interpreted as an integer");
            if (times < 0)
                times = 0;
            return ctx.Values.Iterator(new RepeatIterator(obj, times), "itertools.repeat");
        }

        // ---- cycle(iterable) ----
        private static ScriptValue Cycle(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "cycle", 1);
            IScriptIterator src = IterArg(ctx, args[0]);
            return ctx.Values.Iterator(new CycleIterator(src), "itertools.cycle");
        }

        // ---- islice(iterable, stop) | islice(iterable, start, stop[, step]) ----
        private static ScriptValue Islice(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "islice", 2, 4);
            IScriptIterator src = IterArg(ctx, args[0]);
            if (args.Length == 2)
            {
                BigInteger? stop1 = SliceIndex(ctx, args[1]);
                return ctx.Values.Iterator(new ISliceIterator(src, 0, stop1, 1), "itertools.islice");
            }
            BigInteger start = SliceIndex(ctx, args[1]) ?? 0;
            BigInteger? stop = SliceIndex(ctx, args[2]);
            BigInteger step = args.Length >= 4 ? SliceStep(ctx, args[3]) : 1;
            return ctx.Values.Iterator(new ISliceIterator(src, start, stop, step), "itertools.islice");
        }

        private static BigInteger? SliceIndex(EvalContext ctx, ScriptValue v)
        {
            if (v.Kind == ValueKind.None)
                return null;
            BigInteger n;
            IntValue iv = v as IntValue;
            if (iv != null)
                n = iv.Value;
            else if (v is BoolValue)
                n = ((BoolValue)v).Value ? BigInteger.One : BigInteger.Zero;
            else
                throw Raise.ValueError(ctx, "Indices for islice() must be None or an integer: 0 <= x <= sys.maxsize.");
            if (n < 0)
                throw Raise.ValueError(ctx, "Indices for islice() must be None or an integer: 0 <= x <= sys.maxsize.");
            return n;
        }

        private static BigInteger SliceStep(EvalContext ctx, ScriptValue v)
        {
            if (v.Kind == ValueKind.None)
                return BigInteger.One;
            BigInteger n;
            IntValue iv = v as IntValue;
            if (iv != null)
                n = iv.Value;
            else if (v is BoolValue)
                n = ((BoolValue)v).Value ? BigInteger.One : BigInteger.Zero;
            else
                throw Raise.ValueError(ctx, "Step for islice() must be a positive integer or None.");
            if (n < 1)
                throw Raise.ValueError(ctx, "Step for islice() must be a positive integer or None.");
            return n;
        }
    }

    // count: infinite, repeated-add via the engine '+' (float count accumulates IEEE error, not start+i*step).
    internal sealed class CountIterator : ScriptIteratorBase, IReprIterator
    {
        private ScriptValue _cur;
        private readonly ScriptValue _step;
        private readonly bool _stepIsOne;

        internal CountIterator(ScriptValue start, ScriptValue step, bool stepIsOne)
        {
            _cur = start;
            _step = step;
            _stepIsOne = stepIsOne;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            value = _cur;
            _cur = PyOps.BinaryOp(PyBinOp.Add, _cur, _step, ctx);
            return true;
        }

        public void AppendRepr(BudgetStringBuilder sb, EvalContext ctx)
        {
            sb.Append("count(");
            sb.Append(_cur.Repr(ctx));
            if (!_stepIsOne)
            {
                sb.Append(", ");
                sb.Append(_step.Repr(ctx));
            }
            sb.Append(")");
        }
    }

    // repeat: obj forever, or times copies (times<0 -> empty).
    internal sealed class RepeatIterator : ScriptIteratorBase, IReprIterator
    {
        private readonly ScriptValue _obj;
        private BigInteger _remaining;
        private readonly bool _infinite;

        internal RepeatIterator(ScriptValue obj)
        {
            _obj = obj;
            _infinite = true;
        }

        internal RepeatIterator(ScriptValue obj, BigInteger times)
        {
            _obj = obj;
            _remaining = times;
            _infinite = false;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (!_infinite && _remaining <= 0)
            {
                value = null;
                return false;
            }
            if (!_infinite)
                _remaining -= 1;
            value = _obj;
            return true;
        }

        public void AppendRepr(BudgetStringBuilder sb, EvalContext ctx)
        {
            sb.Append("repeat(");
            sb.Append(_obj.Repr(ctx));
            if (!_infinite)
            {
                sb.Append(", ");
                sb.Append(_remaining.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.Append(")");
        }
    }

    // cycle: buffers the first pass (charged), then replays it forever; an empty input is instantly exhausted.
    internal sealed class CycleIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator _source;
        private readonly List<ScriptValue> _buffer = new List<ScriptValue>();
        private bool _firstPass = true;
        private int _pos;

        internal CycleIterator(IScriptIterator source) { _source = source; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_firstPass)
            {
                ScriptValue v;
                if (_source.MoveNext(ctx, out v))
                {
                    ctx.Values.PreCharge(16);
                    _buffer.Add(v);
                    value = v;
                    return true;
                }
                _firstPass = false;
            }
            if (_buffer.Count == 0)
            {
                value = null;
                return false;
            }
            value = _buffer[_pos];
            _pos = (_pos + 1) % _buffer.Count;
            return true;
        }
    }

    // islice: skips are charged (each skipped source pull is one Step). stop=null -> unbounded.
    internal sealed class ISliceIterator : ScriptIteratorBase
    {
        private readonly IScriptIterator _src;
        private readonly BigInteger? _stop;
        private readonly BigInteger _step;
        private BigInteger _idx;        // count of source elements already pulled
        private BigInteger _nextEmit;   // source index of the next element to emit

        internal ISliceIterator(IScriptIterator src, BigInteger start, BigInteger? stop, BigInteger step)
        {
            _src = src;
            _stop = stop;
            _step = step;
            _idx = 0;
            _nextEmit = start;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            while (_idx < _nextEmit)
            {
                ScriptValue skip;
                if (!_src.MoveNext(ctx, out skip))
                {
                    value = null;
                    return false;
                }
                _idx += 1;
            }
            if (_stop.HasValue && _idx >= _stop.Value)
            {
                value = null;
                return false;
            }
            if (!_src.MoveNext(ctx, out value))
            {
                value = null;
                return false;
            }
            _idx += 1;
            _nextEmit = _idx + (_step - 1);
            return true;
        }
    }
}
