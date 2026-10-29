using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    internal static partial class Builtins
    {
        static partial void RegisterContainers(Dictionary<string, ScriptValue> d)
        {
            var dictStatics = new Dictionary<string, ScriptValue>(System.StringComparer.Ordinal)
            {
                ["fromkeys"] = BuiltinFunctionValue.Make("fromkeys", Args.Positional("fromkeys", 1, 2, (s, a, kw, c) => DictFromKeys(c, a))),
            };
            AddType(d, "range", 1, 3, (self, args, kw, c) => RangeBuiltin(c, args));
            AddType(d, "list", 0, 1, (self, args, kw, c) => ListBuiltin(c, args), null, ListValue.ListType);
            AddType(d, "tuple", 0, 1, (self, args, kw, c) => TupleBuiltin(c, args), null, TupleValue.TupleType);
            AddType(d, "dict", (self, args, kw, c) => DictBuiltin(c, args, kw), dictStatics, DictValue.DictType);
            AddType(d, "set", 0, 1, (self, args, kw, c) => SetBuiltin(c, args), null, SetValue.SetTypeInfo);
            AddType(d, "frozenset", 0, 1, (self, args, kw, c) => FrozenSetBuiltin(c, args), null, FrozenSetValue.FrozenTypeInfo);
        }

        // dict.fromkeys(iterable[, value]) — every key maps to the SAME value object (CPython parity).
        private static ScriptValue DictFromKeys(EvalContext c, ScriptValue[] args)
        {
            ScriptValue value = args.Length == 2 ? args[1] : c.Values.None;
            return DictMethods.FillFromKeys(c, c.Values.Dict(DistinctHint(c, args[0], 8)), args[0], value);
        }

        private static ScriptValue RangeBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length < 1 || args.Length > 3)
                throw Raise.TypeError(c, "range expected 1 to 3 arguments, got " + args.Length);
            BigInteger start, stop, step;
            if (args.Length == 1)
            {
                start = BigInteger.Zero;
                stop = Coerce.ToIndex(c, args[0], "range");
                step = BigInteger.One;
            }
            else
            {
                start = Coerce.ToIndex(c, args[0], "range");
                stop = Coerce.ToIndex(c, args[1], "range");
                step = args.Length == 3 ? Coerce.ToIndex(c, args[2], "range") : BigInteger.One;
            }
            if (step.IsZero)
                throw Raise.ValueError(c, "range() arg 3 must not be zero");
            return c.Values.Range(start, stop, step);
        }

        private static ScriptValue ListBuiltin(EvalContext c, ScriptValue[] args)
        {
            long known;   // list(sized) starts at the source's length instead of growing from 4
            ListValue list = args.Length == 1 && args[0].TryLengthCore(out known) ? c.Values.ListForCount(known) : c.Values.List(4);
            if (args.Length == 1)
            {
                IScriptIterator it = PyOps.GetIterator(args[0], c);
                ScriptValue v;
                while (it.MoveNext(c, out v))
                    list.Add(v, c);
            }
            return list;
        }

        private static ScriptValue TupleBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length == 0)
                return c.Values.Tuple(System.Array.Empty<ScriptValue>());
            if (args[0].GetType() == typeof(TupleValue))
                return args[0];   // tuple(t) is t for a plain tuple; a namedtuple comes back as a plain one
            return c.Values.Tuple(Support.Iter.ToArray(c, args[0], adoptedByCaller: true));
        }

        private static ScriptValue DictBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            Args.Unpack(c, args, "dict", 0, 1);
            DictValue d = c.Values.Dict(kw.Count + 4);
            DictMethods.InitFrom(c, d, args.Length == 1 ? args[0] : null, kw);
            return d;
        }

        private static ScriptValue SetBuiltin(EvalContext c, ScriptValue[] args)
        {
            SetValue s = c.Values.Set(args.Length == 1 ? DistinctHint(c, args[0], 4) : 4);
            if (args.Length == 1)
                FillSet(c, s, args[0]);
            return s;
        }

        private static ScriptValue FrozenSetBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length == 1 && args[0].Kind == ValueKind.FrozenSet)
                return args[0];   // frozenset(fs) is fs
            SetValue tmp = c.Values.Set(args.Length == 1 ? DistinctHint(c, args[0], 4) : 4);
            if (args.Length == 1)
                FillSet(c, tmp, args[0]);
            return c.Values.FrozenSet(tmp.Table);
        }

        // A source whose items are distinct (a range, a set, a dict or its view) fills a table of its size;
        // any other may repeat items and would leave a presized table mostly empty, so it grows instead.
        private static int DistinctHint(EvalContext c, ScriptValue source, int min)
        {
            long n;
            bool distinct = source.Kind == ValueKind.Range || source.Kind == ValueKind.Set || source.Kind == ValueKind.FrozenSet
                || source.Kind == ValueKind.Dict
                || (source is DictViewValue dv && dv.View != DictViewValue.ViewKind.Values);
            return distinct && source.TryLengthCore(out n) ? System.Math.Max(c.Values.PresizeHint(n, 36), min) : min;
        }

        private static void FillSet(EvalContext c, SetValue s, ScriptValue source)
        {
            IScriptIterator it = PyOps.GetIterator(source, c);
            ScriptValue v;
            while (it.MoveNext(c, out v))
                s.AddItem(v, c);
        }
    }
}
