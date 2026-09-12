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
                ["fromkeys"] = BuiltinFunctionValue.Make("fromkeys", (s, a, kw, c) => DictFromKeys(c, a)),
            };
            AddType(d, "range", (self, args, kw, c) => RangeBuiltin(c, args));
            AddType(d, "list", (self, args, kw, c) => ListBuiltin(c, args), null, ListValue.ListType);
            AddType(d, "tuple", (self, args, kw, c) => TupleBuiltin(c, args), null, TupleValue.TupleType);
            AddType(d, "dict", (self, args, kw, c) => DictBuiltin(c, args, kw), dictStatics, DictValue.DictType);
            AddType(d, "set", (self, args, kw, c) => SetBuiltin(c, args), null, SetValue.SetTypeInfo);
            AddType(d, "frozenset", (self, args, kw, c) => FrozenSetBuiltin(c, args), null, FrozenSetValue.FrozenTypeInfo);
        }

        // dict.fromkeys(iterable[, value]) — every key maps to the SAME value object (CPython parity).
        private static ScriptValue DictFromKeys(EvalContext c, ScriptValue[] args)
        {
            if (args.Length < 1 || args.Length > 2)
                throw Raise.TypeError(c, "fromkeys expected at most 2 arguments, got " + args.Length);
            ScriptValue value = args.Length == 2 ? args[1] : c.Values.None;
            DictValue result = c.Values.Dict(8);
            IScriptIterator it = PyOps.GetIterator(args[0], c);
            ScriptValue key;
            while (it.MoveNext(c, out key))
                result.SetItem(key, value, c);
            return result;
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
            if (args.Length > 1)
                throw Raise.TypeError(c, "list expected at most 1 argument, got " + args.Length);
            ListValue list = c.Values.List(4);
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
            if (args.Length > 1)
                throw Raise.TypeError(c, "tuple expected at most 1 argument, got " + args.Length);
            if (args.Length == 0)
                return c.Values.Tuple(System.Array.Empty<ScriptValue>());
            if (args[0].GetType() == typeof(TupleValue))
                return args[0];   // tuple(t) is t for a plain tuple; a namedtuple comes back as a plain one
            var items = new List<ScriptValue>();
            IScriptIterator it = PyOps.GetIterator(args[0], c);
            ScriptValue v;
            while (it.MoveNext(c, out v))
            {
                c.Budget.Step();
                items.Add(v);
            }
            return c.Values.Tuple(items.ToArray());
        }

        private static ScriptValue DictBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            if (args.Length > 1)
                throw Raise.TypeError(c, "dict expected at most 1 arguments, got " + args.Length);
            DictValue d = c.Values.Dict(kw.Count + 4);
            if (args.Length == 1)
                DictMethods.Merge(c, d, args[0]);
            for (int j = 0; j < kw.Count; j++)
                d.SetItem(c.Values.Str(kw.NameAt(j)), kw.ValueAt(j), c);
            return d;
        }

        private static ScriptValue SetBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length > 1)
                throw Raise.TypeError(c, "set expected at most 1 argument, got " + args.Length);
            SetValue s = c.Values.Set(4);
            if (args.Length == 1)
                FillSet(c, s, args[0]);
            return s;
        }

        private static ScriptValue FrozenSetBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length > 1)
                throw Raise.TypeError(c, "frozenset expected at most 1 argument, got " + args.Length);
            if (args.Length == 1 && args[0].Kind == ValueKind.FrozenSet)
                return args[0];   // frozenset(fs) is fs
            SetValue tmp = c.Values.Set(4);
            if (args.Length == 1)
                FillSet(c, tmp, args[0]);
            return c.Values.FrozenSet(tmp.Table);
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
