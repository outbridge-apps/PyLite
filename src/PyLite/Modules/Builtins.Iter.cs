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
        static partial void RegisterIter(Dictionary<string, ScriptValue> d)
        {
            AddType(d, "enumerate", (self, args, kw, c) => EnumerateBuiltin(c, args, kw));
            AddType(d, "zip", (self, args, kw, c) => ZipBuiltin(c, args, kw));
            AddType(d, "map", (self, args, kw, c) => MapBuiltin(c, args));
            AddType(d, "filter", (self, args, kw, c) => FilterBuiltin(c, args));
            Add(d, "reversed", (self, args, kw, c) => ReversedBuiltin(c, args));
        }

        private static ScriptValue EnumerateBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            if (args.Length < 1 || args.Length > 2)
                throw Raise.TypeError(c, "enumerate expected 1 to 2 arguments, got " + args.Length);
            BigInteger start = BigInteger.Zero;
            if (args.Length == 2)
                start = Coerce.ToIndex(c, args[1], "enumerate");
            ScriptValue startKw;
            if (kw.TryGet("start", out startKw))
                start = Coerce.ToIndex(c, startKw, "enumerate");
            IScriptIterator src = PyOps.GetIterator(args[0], c);
            return c.Values.Iterator(new EnumerateIterator(src, start), "enumerate");
        }

        private static ScriptValue ZipBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            bool strict = false;
            var r = new KwReader(c, kw, "zip");
            strict = r.Bool("strict", strict);
            r.RejectUnknown();
            var srcs = new IScriptIterator[args.Length];
            for (int i = 0; i < args.Length; i++)
                srcs[i] = IterOrThrow(c, args[i], "zip argument #" + (i + 1) + " must support iteration");
            return c.Values.Iterator(new ZipIterator(srcs, strict), "zip");
        }

        private static ScriptValue MapBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length < 2)
                throw Raise.TypeError(c, "map() must have at least two arguments.");
            var srcs = new IScriptIterator[args.Length - 1];
            for (int i = 1; i < args.Length; i++)
                srcs[i - 1] = PyOps.GetIterator(args[i], c);
            return c.Values.Iterator(new MapIterator(args[0], srcs), "map");
        }

        private static ScriptValue FilterBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length != 2)
                throw Raise.TypeError(c, "filter expected 2 arguments, got " + args.Length);
            ScriptValue fn = args[0].Kind == ValueKind.None ? null : args[0];
            IScriptIterator src = PyOps.GetIterator(args[1], c);
            return c.Values.Iterator(new FilterIterator(fn, src), "filter");
        }

        private static ScriptValue ReversedBuiltin(EvalContext c, ScriptValue[] args)
        {
            ScriptValue x = Args.One(c, args, "reversed");
            if (x.Kind == ValueKind.List)
                return c.Values.Iterator(new ReversedListIterator((ListValue)x), "list_reverseiterator");
            if (x.Kind == ValueKind.Tuple)
                return c.Values.Iterator(new ReversedArrayIterator(((TupleValue)x).Items), "tuple_reverseiterator");
            if (x.Kind == ValueKind.Str)
            {
                string s = ((StrValue)x).Value;
                var arr = new ScriptValue[s.Length];
                for (int i = 0; i < s.Length; i++)
                    arr[i] = c.Values.StrFromChar(s[i]);
                return c.Values.Iterator(new ReversedArrayIterator(arr), "reversed");
            }
            BytesValue bytes = x as BytesValue;
            if (bytes != null)
            {
                var arr = new ScriptValue[bytes.Data.Length];
                for (int i = 0; i < arr.Length; i++)
                    arr[i] = c.Values.Int(bytes.Data[i]);
                return c.Values.Iterator(new ReversedArrayIterator(arr), "reversed");
            }
            if (x.Kind == ValueKind.Range)
            {
                RangeValue r = (RangeValue)x;
                BigInteger len = r.Length;
                if (len.IsZero)
                    return c.Values.Iterator(new ReversedArrayIterator(System.Array.Empty<ScriptValue>()), "range_iterator");
                RangeValue nr = c.Values.Range(r.Start + (len - 1) * r.Step, r.Start - r.Step, -r.Step);
                return c.Values.Iterator(PyOps.GetIterator(nr, c), "range_iterator");
            }
            IScriptIterator rev = x.GetReverseIteratorCore(c);
            if (rev != null)
                return c.Values.Iterator(rev, ReverseIteratorName(x));
            throw Raise.TypeError(c, "argument to reversed() must be a sequence");
        }

        // CPython names: dict_reversekeyiterator / dict_reversevalueiterator / dict_reverseitemiterator.
        private static string ReverseIteratorName(ScriptValue x)
        {
            DictViewValue view = x as DictViewValue;
            if (view == null)
                return x.PyTypeName + "_reverseiterator";
            switch (view.View)
            {
                case DictViewValue.ViewKind.Keys: return "dict_reversekeyiterator";
                case DictViewValue.ViewKind.Values: return "dict_reversevalueiterator";
                default: return "dict_reverseitemiterator";
            }
        }

        private static IScriptIterator IterOrThrow(EvalContext c, ScriptValue x, string msg)
        {
            IScriptIterator it = PyOps.TryGetIterator(x, c);
            if (it == null)
                throw Raise.TypeError(c, msg);
            return it;
        }
    }
}
