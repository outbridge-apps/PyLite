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
    internal static partial class Builtins
    {
        static partial void RegisterObject(Dictionary<string, ScriptValue> d)
        {
            Add(d, "chr", (self, args, kw, c) => ChrBuiltin(c, args));
            Add(d, "ord", (self, args, kw, c) => OrdBuiltin(c, args));
            Add(d, "hash", (self, args, kw, c) => c.Values.Int(Args.One(c, args, "hash").PyHash(c)));
            Add(d, "len", (self, args, kw, c) => LenBuiltin(c, args));
            Add(d, "repr", (self, args, kw, c) => c.Values.Str(Args.One(c, args, "repr").Repr(c)));
            Add(d, "ascii", (self, args, kw, c) => c.Values.Str(Support.PyFormat.AsciiEscape(Args.One(c, args, "ascii").Repr(c))));
            d["Ellipsis"] = EllipsisValue.Instance;
            Add(d, "callable", (self, args, kw, c) => c.Values.Bool(Args.One(c, args, "callable").IsCallable));
            Add(d, "id", (self, args, kw, c) => c.Values.Int(c.Values.IdOf(Args.One(c, args, "id"))));
            AddType(d, "type", (self, args, kw, c) => TypeBuiltin(c, args));
            Add(d, "dir", (self, args, kw, c) => DirBuiltin(c, args));
            Add(d, "getattr", (self, args, kw, c) => GetAttrBuiltin(c, args, kw));
            Add(d, "hasattr", (self, args, kw, c) => HasAttrBuiltin(c, args, kw));
            Add(d, "setattr", (self, args, kw, c) => SetAttrBuiltin(c, args, kw));
            Add(d, "iter", (self, args, kw, c) => IterBuiltin(c, args));
            Add(d, "next", (self, args, kw, c) => NextBuiltin(c, args));
            AddType(d, "slice", (self, args, kw, c) => SliceBuiltin(c, args));
            Add(d, "format", (self, args, kw, c) => FormatBuiltin(c, args));
        }

        private static ScriptValue ChrBuiltin(EvalContext c, ScriptValue[] args)
        {
            ScriptValue x = Args.One(c, args, "chr");
            if (!IsIntLike(x))
                throw Raise.TypeError(c, "an integer is required (got type " + x.PyTypeName + ")");
            BigInteger i = NumericOps.AsBigInteger(x);
            if (i < 0 || i > 0x10FFFF)
                throw Raise.ValueError(c, "chr() arg not in range(0x110000)");
            int cp = (int)i;
            if (cp <= 0xFFFF)
                return c.Values.StrFromChar((char)cp);   // lone surrogates allowed: a str is UTF-16 code units
            int v = cp - 0x10000;
            char hi = (char)(0xD800 + (v >> 10));
            char lo = (char)(0xDC00 + (v & 0x3FF));
            return c.Values.Str(new string(new[] { hi, lo }));
        }

        private static ScriptValue OrdBuiltin(EvalContext c, ScriptValue[] args)
        {
            ScriptValue x = Args.One(c, args, "ord");
            StrValue s = x as StrValue;
            if (s == null)
                throw Raise.TypeError(c, "ord() expected string of length 1, but " + x.PyTypeName + " found");
            string str = s.Value;
            if (str.Length == 1)
                return c.Values.Int(str[0]);
            if (str.Length == 2 && char.IsHighSurrogate(str[0]) && char.IsLowSurrogate(str[1]))
                return c.Values.Int(char.ConvertToUtf32(str[0], str[1]));
            throw Raise.TypeError(c, "ord() expected a character, but string of length " + str.Length + " found");
        }

        private static ScriptValue LenBuiltin(EvalContext c, ScriptValue[] args)
        {
            ScriptValue x = Args.One(c, args, "len");
            long ctxLen;
            if (x.TryLengthWithContextCore(c, out ctxLen))   // ChainMap key-union
                return c.Values.Int(ctxLen);
            BigInteger len;
            if (x.TryLengthBig(out len))
                return c.Values.Int(len);
            throw Raise.TypeError(c, "object of type '" + x.PyTypeName + "' has no len()");
        }

        private static ScriptValue TypeBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length != 1)
                throw Raise.TypeError(c, "type() takes 1 argument in this environment");
            return TypeObjectFor(args[0].PyTypeName);
        }

        private static ScriptValue DirBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length == 0)
                throw Raise.TypeError(c, "dir() without arguments is not supported in this environment");
            if (args.Length > 1)
                throw Raise.TypeError(c, "dir expected at most 1 argument, got " + args.Length);
            ScriptValue x = args[0];
            var names = new List<string>();
            ModuleValue mod = x as ModuleValue;
            RecordInstanceValue ri = x as RecordInstanceValue;
            RecordClassValue rc = x as RecordClassValue;
            TypeValue tv = x as TypeValue;
            if (mod != null)
            {
                foreach (KeyValuePair<string, ScriptValue> kv in mod.Members)
                    names.Add(kv.Key);
            }
            else if (ri != null || rc != null)
            {
                // a record: its set fields plus every method reachable up the inheritance chain
                RecordClassValue cls = ri != null ? ri.Class : rc;
                if (ri != null)
                {
                    for (int i = 0; i < cls.AllSlots.Count; i++)
                        if (ri.Slots[i] != null)
                            names.Add(cls.AllSlots[i]);
                }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (RecordClassValue k = cls; k != null; k = k.Base)
                    foreach (KeyValuePair<string, FunctionValue> kv in k.Methods)
                        if (seen.Add(kv.Key))
                            names.Add(kv.Key);
            }
            else if (tv != null)
            {
                if (tv.StaticSlots != null)
                    foreach (KeyValuePair<string, ScriptValue> kv in tv.StaticSlots)
                        names.Add(kv.Key);
                if (tv.Describes != null)
                    foreach (KeyValuePair<string, SlotDescriptor> kv in tv.Describes.Slots)
                        names.Add(kv.Key);
            }
            else
            {
                foreach (KeyValuePair<string, SlotDescriptor> kv in x.TypeInfo.Slots)
                    names.Add(kv.Key);
            }
            names.Sort(StringComparer.Ordinal);
            ListValue list = c.Values.List(names.Count);
            for (int i = 0; i < names.Count; i++)
                list.Add(c.Values.Str(names[i]), c);
            return list;
        }

        // getattr/hasattr/setattr reach exactly the surface the dot operator reaches (slot tables plus
        // record fields), so dynamic access by name opens nothing new; none of the three takes kwargs.
        private static string AttrName(EvalContext c, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(c, "attribute name must be string, not '" + v.PyTypeName + "'");
            return s.Value;
        }

        private static void NoKeywords(EvalContext c, KwArgs kw, string name)
        {
            if (kw.Count > 0)
                throw Raise.TypeError(c, name + "() takes no keyword arguments");
        }

        private static ScriptValue GetAttrBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            NoKeywords(c, kw, "getattr");
            if (args.Length < 2)
                throw Raise.TypeError(c, "getattr expected at least 2 arguments, got " + args.Length);
            if (args.Length > 3)
                throw Raise.TypeError(c, "getattr expected at most 3 arguments, got " + args.Length);
            string name = AttrName(c, args[1]);
            if (args.Length == 2)
                return PyOps.GetAttr(args[0], name, c);
            try
            {
                return PyOps.GetAttr(args[0], name, c);
            }
            catch (ScriptException se) when (se.Value.ExcType.IsSubtypeOf(PyExceptionTypes.AttributeError))
            {
                return args[2];
            }
        }

        private static ScriptValue HasAttrBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            NoKeywords(c, kw, "hasattr");
            if (args.Length != 2)
                throw Raise.TypeError(c, "hasattr expected 2 arguments, got " + args.Length);
            string name = AttrName(c, args[1]);
            try
            {
                PyOps.GetAttr(args[0], name, c);
                return c.Values.Bool(true);
            }
            catch (ScriptException se) when (se.Value.ExcType.IsSubtypeOf(PyExceptionTypes.AttributeError))
            {
                return c.Values.Bool(false);
            }
        }

        private static ScriptValue SetAttrBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            NoKeywords(c, kw, "setattr");
            if (args.Length != 3)
                throw Raise.TypeError(c, "setattr expected 3 arguments, got " + args.Length);
            PyOps.SetAttr(args[0], AttrName(c, args[1]), args[2], c);
            return c.Values.None;
        }

        private static ScriptValue IterBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length == 2)
            {
                // iter(callable, sentinel): call until the result equals the sentinel.
                if (!args[0].IsCallable)
                    throw Raise.TypeError(c, "iter(v, w): v must be callable");
                return c.Values.Iterator(new CallableIterator(args[0], args[1]), "callable_iterator");
            }
            ScriptValue x = Args.One(c, args, "iter");
            if (x.Kind == ValueKind.Iterator)
                return x;   // iter(it) is it
            IScriptIterator it = PyOps.GetIterator(x, c);
            return c.Values.Iterator(it, IteratorTypeName(x));
        }

        // CPython iterator type names: dict and its views share dict_key/value/itemiterator, frozenset uses set_iterator.
        private static string IteratorTypeName(ScriptValue x)
        {
            switch (x.PyTypeName)
            {
                case "dict": case "dict_keys": return "dict_keyiterator";
                case "dict_values": return "dict_valueiterator";
                case "dict_items": return "dict_itemiterator";
                case "frozenset": return "set_iterator";
                default: return x.PyTypeName + "_iterator";
            }
        }

        private sealed class CallableIterator : ScriptIteratorBase
        {
            private readonly ScriptValue _fn;
            private readonly ScriptValue _sentinel;
            private bool _done;

            internal CallableIterator(ScriptValue fn, ScriptValue sentinel) { _fn = fn; _sentinel = sentinel; }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                value = null;
                if (_done)
                    return false;
                ScriptValue v = ctx.CallHook(_fn, System.Array.Empty<ScriptValue>(), Runtime.Evaluator.KwArgs.Empty);
                if (PyOps.Equals(v, _sentinel, ctx, 0))
                {
                    _done = true;   // exhausted for good (never revives), like a StopIteration'd iterator
                    return false;
                }
                value = v;
                return true;
            }
        }

        private static ScriptValue NextBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length < 1 || args.Length > 2)
                throw Raise.TypeError(c, "next expected at most 2 arguments, got " + args.Length);
            IteratorValue it = args[0] as IteratorValue;
            if (it == null)
                throw Raise.TypeError(c, "'" + args[0].PyTypeName + "' object is not an iterator");
            ScriptValue v;
            if (it.Iterator.MoveNext(c, out v))
                return v;
            if (args.Length == 2)
                return args[1];
            throw Raise.StopIteration(c);
        }

        private static ScriptValue SliceBuiltin(EvalContext c, ScriptValue[] args)
        {
            ScriptValue none = c.Values.None;
            if (args.Length == 1)
                return c.Values.Slice(none, args[0], none);
            if (args.Length == 2)
                return c.Values.Slice(args[0], args[1], none);
            if (args.Length == 3)
                return c.Values.Slice(args[0], args[1], args[2]);
            throw Raise.TypeError(c, "slice expected 1 to 3 arguments, got " + args.Length);
        }

        private static ScriptValue FormatBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length < 1 || args.Length > 2)
                throw Raise.TypeError(c, "format expected at most 2 arguments, got " + args.Length);
            ScriptValue value = args[0];
            string spec = "";
            if (args.Length == 2)
            {
                StrValue s = args[1] as StrValue;
                if (s == null)
                    throw Raise.TypeError(c, "format() argument 2 must be str, not " + args[1].PyTypeName);
                spec = s.Value;
            }
            return Outbridge.PyLite.Modules.Support.PyFormat.FormatValue(c, value, spec);
        }
    }
}
