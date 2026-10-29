using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // collections: OrderedDict, defaultdict, Counter, namedtuple, deque.
    // Each container type is registered as a callable TypeValue whose constructor builds the value.
    internal static partial class CollectionsModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            var orderedStatics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["fromkeys"] = BuiltinFunctionValue.Make("fromkeys", OrderedDictFromKeys),
            };
            m["OrderedDict"] = ctx.Values.Type("collections.OrderedDict", OrderedDictCtor, null, orderedStatics);
            m["defaultdict"] = ctx.Values.Type("collections.defaultdict", DefaultDictCtor, null, null);
            var counterStatics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["fromkeys"] = BuiltinFunctionValue.Make("fromkeys", CounterValue.FromKeys),
            };
            m["Counter"] = ctx.Values.Type("collections.Counter", CounterCtor, null, counterStatics);
            m["namedtuple"] = BuiltinFunctionValue.Make("namedtuple", NamedTuple);
            m["deque"] = ctx.Values.Type("collections.deque", DequeCtor, null, null);
            m["ChainMap"] = ctx.Values.Type("collections.ChainMap", ChainMapCtor, null, null);
            return ctx.Values.Module("collections", m);
        }

        private static ScriptValue OrderedDictCtor(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Unpack(ctx, args, "OrderedDict", 0, 1);
            OrderedDictValue od = ctx.Values.OrderedDict(kw.Count + 4);
            DictMethods.InitFrom(ctx, od, args.Length == 1 ? args[0] : null, kw);
            return od;
        }

        // OrderedDict.fromkeys(iterable[, value]): every key maps to the SAME value object.
        private static ScriptValue OrderedDictFromKeys(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length < 1 || args.Length > 2)
                throw Raise.TypeError(ctx, "fromkeys expected 1 or 2 arguments, got " + args.Length);
            ScriptValue value = args.Length == 2 ? args[1] : ctx.Values.None;
            return DictMethods.FillFromKeys(ctx, ctx.Values.OrderedDict(4), args[0], value);
        }

        // ChainMap(*maps): no maps -> one fresh empty dict (CPython).
        private static ScriptValue ChainMapCtor(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ListValue maps = ctx.Values.List(args.Length > 0 ? args.Length : 1);
            for (int i = 0; i < args.Length; i++)
            {
                if (!(args[i] is DictValue))
                    throw Raise.TypeError(ctx, "ChainMap maps must be dicts, not " + args[i].PyTypeName);
                maps.Add(args[i], ctx);
            }
            if (args.Length == 0)
                maps.Add(ctx.Values.Dict(4), ctx);
            ctx.Values.PreCharge(32);
            return new ChainMapValue(maps);
        }

        private static ScriptValue DequeCtor(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Unpack(ctx, args, "deque", 0, 2);
            ScriptValue iterable = args.Length >= 1 ? args[0] : null;
            ScriptValue maxlenArg = args.Length >= 2 ? args[1] : null;
            ScriptValue mkw;
            if (kw.TryGet("maxlen", out mkw))
                maxlenArg = mkw;
            int maxlen = -1;
            if (maxlenArg != null && maxlenArg.Kind != ValueKind.None)
            {
                if (maxlenArg.Kind != ValueKind.Int && maxlenArg.Kind != ValueKind.Bool)
                    throw Raise.TypeError(ctx, "an integer is required");
                System.Numerics.BigInteger m = Outbridge.PyLite.Runtime.Values.NumericOps.AsBigInteger(maxlenArg);
                if (m < 0)
                    throw Raise.ValueError(ctx, "maxlen must be non-negative");
                if (m > int.MaxValue)
                    throw Raise.Make(ctx, PyExceptionTypes.OverflowError, "maxlen too large to convert to C ssize_t");
                maxlen = (int)m;
            }
            ctx.Values.PreCharge(64);
            DequeValue d = new DequeValue(maxlen);
            if (iterable != null && iterable.Kind != ValueKind.None)
            {
                IScriptIterator it = PyOps.GetIterator(iterable, ctx);
                ScriptValue v;
                while (it.MoveNext(ctx, out v))
                    d.Append(v, ctx);
            }
            return d;
        }

        private static ScriptValue CounterCtor(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Unpack(ctx, args, "Counter", 0, 1);
            CounterValue c = ctx.Values.Counter(kw.Count + 4);
            CounterValue.UpdateFrom(ctx, c, args.Length == 1 ? args[0] : null, kw, +1);
            return c;
        }

        private static ScriptValue DefaultDictCtor(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ScriptValue factory = ctx.Values.None;
            ScriptValue mapping = null;
            if (args.Length >= 1)
            {
                factory = args[0];
                if (factory.Kind != ValueKind.None && !factory.IsCallable)
                    throw Raise.TypeError(ctx, "first argument must be callable or None");
            }
            if (args.Length >= 2)
            {
                if (args.Length > 2)
                    throw Args.UnpackError(ctx, "dict", "at most ", 1, args.Length - 1);
                mapping = args[1];
            }
            DefaultDictValue dd = ctx.Values.DefaultDict(factory, kw.Count + 4);
            DictMethods.InitFrom(ctx, dd, mapping, kw);
            return dd;
        }
    }
}
