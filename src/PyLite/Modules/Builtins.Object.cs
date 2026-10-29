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
            Add(d, "chr", 1, 1, (self, args, kw, c) => ChrBuiltin(c, args));
            Add(d, "ord", 1, 1, (self, args, kw, c) => OrdBuiltin(c, args));
            Add(d, "hash", 1, 1, (self, args, kw, c) => c.Values.Int(Args.One(c, args, "hash").PyHash(c)));
            Add(d, "len", 1, 1, (self, args, kw, c) => LenBuiltin(c, args));
            Add(d, "repr", 1, 1, (self, args, kw, c) => c.Values.Str(Args.One(c, args, "repr").Repr(c)));
            Add(d, "ascii", 1, 1, (self, args, kw, c) => c.Values.Str(Support.PyFormat.AsciiEscape(Args.One(c, args, "ascii").Repr(c))));
            d["Ellipsis"] = EllipsisValue.Instance;
            Add(d, "callable", 1, 1, (self, args, kw, c) => c.Values.Bool(Args.One(c, args, "callable").IsCallable));
            Add(d, "id", 1, 1, (self, args, kw, c) => c.Values.Int(c.Values.IdOf(Args.One(c, args, "id"))));
            AddType(d, "type", 1, 3, (self, args, kw, c) => TypeBuiltin(c, args));
            Add(d, "super", (self, args, kw, c) => SuperBuiltin(c, args, kw));
            Add(d, "dir", 0, 1, (self, args, kw, c) => DirBuiltin(c, args));
            Add(d, "getattr", (self, args, kw, c) => GetAttrBuiltin(c, args, kw));
            Add(d, "hasattr", (self, args, kw, c) => HasAttrBuiltin(c, args, kw));
            Add(d, "setattr", (self, args, kw, c) => SetAttrBuiltin(c, args, kw));
            Add(d, "iter", 1, 2, (self, args, kw, c) => IterBuiltin(c, args));
            Add(d, "next", 1, 2, (self, args, kw, c) => NextBuiltin(c, args));
            AddType(d, "slice", 1, 3, (self, args, kw, c) => SliceBuiltin(c, args));
            Add(d, "format", 1, 2, (self, args, kw, c) => FormatBuiltin(c, args));
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
            long small;
            if (x.TryLengthCore(out small))   // no BigInteger for the usual length (P8)
                return c.Values.Int(small);
            BigInteger len;
            if (x.TryLengthBig(out len))
                return c.Values.Int(len);
            throw Raise.TypeError(c, "object of type '" + x.PyTypeName + "' has no len()");
        }

        private static ScriptValue TypeBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length != 1)
                throw Raise.TypeError(c, "type() takes 1 argument in this environment");
            ExceptionValue ev = args[0] as ExceptionValue;
            if (ev != null && ev.ExcType.ClassValue != null)
                return ev.ExcType.ClassValue;   // the class itself, so `type(e) is ValueError` and issubclass hold
            RecordInstanceValue ri = args[0] as RecordInstanceValue;
            if (ri != null)
                return ri.Class;   // so type(self).NAME and super(type(self), self) work
            EnumMemberValue em = EnumClassValue.MemberOf(args[0], c);
            if (em != null)
                return em.Class;
            NamedTupleValue nt = args[0] as NamedTupleValue;
            if (nt != null && nt.NtInfo.Class != null)
                return nt.NtInfo.Class;   // a script-named type: never through the process-wide name cache
            return TypeObjectFor(args[0].PyTypeName);
        }

        // super(Class, obj). The parser writes super() in a method as super(<its class>, <first parameter>),
        // so a bare super() here is one outside a method, or in a function nested in one: CPython's error.
        private static ScriptValue SuperBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            if (kw.Count > 0)
                throw Raise.TypeError(c, "super() takes no keyword arguments");
            if (args.Length == 0)
                throw Raise.Make(c, PyExceptionTypes.RuntimeError, "super(): no arguments");
            if (args.Length > 2)
                throw Raise.TypeError(c, "super() takes at most 2 arguments (" + args.Length + " given)");
            RecordClassValue cls = args[0] as RecordClassValue;
            if (cls == null)
            {
                throw Raise.TypeError(c, args[0].Kind == ValueKind.Type
                    ? "super() argument 1 must be a class defined in this dialect"
                    : "super() argument 1 must be a type, not " + args[0].PyTypeName);
            }
            if (args.Length == 1)
                throw Raise.TypeError(c, "super() with one argument is not supported in this dialect; use super() or super(Class, self)");
            if (args[1].Kind == ValueKind.RecordClass)
                throw Raise.TypeError(c, "super(type, type2) is not supported in this dialect (no classmethods)");
            RecordInstanceValue inst = args[1] as RecordInstanceValue;
            for (RecordClassValue k = inst == null ? null : inst.Class; k != null; k = k.Base)
            {
                if (k.SameClass(cls))
                    return new SuperValue(k, inst);
            }
            throw Raise.TypeError(c, "super(type, obj): obj must be an instance or subtype of type");
        }

        private static ScriptValue DirBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length == 0)
                throw Raise.TypeError(c, "dir() without arguments is not supported in this environment");
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
                // a record: its set fields plus every method and class attribute up the inheritance chain
                RecordClassValue cls = ri != null ? ri.Class : rc;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                if (ri != null)
                {
                    for (int i = 0; i < cls.AllSlots.Count; i++)
                        if (ri.Slots[i] != null && seen.Add(cls.AllSlots[i]))
                            names.Add(cls.AllSlots[i]);
                }
                for (RecordClassValue k = cls; k != null; k = k.Base)
                {
                    foreach (KeyValuePair<string, FunctionValue> kv in k.Methods)
                        if (seen.Add(kv.Key))
                            names.Add(kv.Key);
                    foreach (KeyValuePair<string, ScriptValue> kv in k.Constants)
                        if (seen.Add(kv.Key))
                            names.Add(kv.Key);
                }
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

        private static ScriptValue GetAttrBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            Args.NoKw(c, kw, "getattr");
            Args.Unpack(c, args, "getattr", 2, 3);
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
            Args.NoKw(c, kw, "hasattr");
            Args.Unpack(c, args, "hasattr", 2, 2);
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
            Args.NoKw(c, kw, "setattr");
            Args.Unpack(c, args, "setattr", 3, 3);
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
            if (x is ILineFile)
                return x;   // an in-memory file is its own iterator
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
                default:
                    if (x is NamedTupleValue)
                        return "tuple_iterator";
                    if (x is RecordInstanceValue)
                        return "iterator";   // a script-chosen class name must not key the static type cache
                    return x.PyTypeName + "_iterator";
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
            IteratorValue it = args[0] as IteratorValue;
            ScriptValue v;
            ILineFile file = args[0] as ILineFile;
            if (file != null)
            {
                if (file.NextLine(c, out v))
                    return v;
                if (args.Length == 2)
                    return args[1];
                throw Raise.StopIteration(c);
            }
            if (it == null)
                throw Raise.TypeError(c, "'" + args[0].PyTypeName + "' object is not an iterator");
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
