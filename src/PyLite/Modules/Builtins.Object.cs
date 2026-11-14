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
            Add(d, "ascii", 1, 1, (self, args, kw, c) => c.Values.Str(Support.PyFormat.AsciiEscape(c, Args.One(c, args, "ascii").Repr(c))));
            d["Ellipsis"] = EllipsisValue.Instance;
            Add(d, "callable", 1, 1, (self, args, kw, c) => c.Values.Bool(Args.One(c, args, "callable").IsCallable));
            Add(d, "id", 1, 1, (self, args, kw, c) => c.Values.Int(c.Values.IdOf(Args.One(c, args, "id"))));
            AddType(d, "type", 1, 3, (self, args, kw, c) => TypeBuiltin(c, args));
            Add(d, "super", SuperFn);
            Add(d, "dir", 0, 1, (self, args, kw, c) => DirBuiltin(c, args));
            Add(d, "getattr", (self, args, kw, c) => GetAttrBuiltin(c, args, kw));
            Add(d, "hasattr", (self, args, kw, c) => HasAttrBuiltin(c, args, kw));
            Add(d, "setattr", (self, args, kw, c) => SetAttrBuiltin(c, args, kw));
            Add(d, "iter", 1, 2, (self, args, kw, c) => IterBuiltin(c, args));
            AddQuiet(d, "next", NextOrError);
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
            BytesValue b = x as BytesValue;
            if (b != null)
            {
                if (b.Data.Length == 1)
                    return c.Values.Int(b.Data[0]);
                throw Raise.TypeError(c, "ord() expected a character, but string of length " + b.Data.Length + " found");
            }
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
            if (x.TryLengthCore(out small))   // no BigInteger for the usual length
                return c.Values.Int(small);
            return c.Values.Int(PyOps.Length(x, c));   // a range longer than ssize_t is an OverflowError
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
            if (nt != null)
            {
                TypeValue ntClass = nt.NtInfo.Class ?? c.Values.RunTypeFor(nt.NtInfo.InstanceType);   // urllib's and statistics' have one per run
                if (ntClass != null)
                    return ntClass;
            }
            if (args[0] is RecordClassValue)
                return TypeObjectFor(TypeValue.MetaTypeInfo, c);   // a class is a type, whatever it is named
            return TypeObjectFor(args[0].TypeInfo, c);
        }

        // The builtin, known by its delegate: a call site may run super().m(...) without making the super object
        // (CallMachinery). A static method, so the delegate made from it compares equal wherever it is made.
        internal static ScriptValue SuperFn(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext c)
        {
            return SuperBuiltin(c, args, kw);
        }

        // super(Class, obj). The parser writes super() in a method as super(<its class>, <first parameter>),
        // so a bare super() here is one outside a method, or in a function nested in one: CPython's error.
        private static ScriptValue SuperBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            RecordClassValue k;
            RecordInstanceValue inst;
            ResolveSuper(c, args, kw, out k, out inst);
            c.Values.ChargeObject(2);
            return new SuperValue(k, inst);
        }

        // super(Class, obj)'s checks; k is the class of obj's chain that is Class.
        internal static void ResolveSuper(EvalContext c, ScriptValue[] args, KwArgs kw, out RecordClassValue k, out RecordInstanceValue inst)
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
            inst = args[1] as RecordInstanceValue;
            for (k = inst == null ? null : inst.Class; k != null; k = k.Base)
            {
                if (k.SameClass(cls))
                    return;
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
            ScriptValue v;
            return PyOps.TryGetAttr(args[0], name, c, out v) ? v : args[2];
        }

        private static ScriptValue HasAttrBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            Args.NoKw(c, kw, "hasattr");
            Args.Unpack(c, args, "hasattr", 2, 2);
            string name = AttrName(c, args[1]);
            ScriptValue v;
            return c.Values.Bool(PyOps.TryGetAttr(args[0], name, c, out v));
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
                return c.Values.EngineIterator(new CallableIterator(args[0], args[1]), "callable_iterator");
            }
            ScriptValue x = Args.One(c, args, "iter");
            if (x.Kind == ValueKind.Iterator)
                return x;   // iter(it) is it
            if (x is ILineFile)
                return x;   // an in-memory file is its own iterator
            RecordInstanceValue rec = x as RecordInstanceValue;
            FunctionValue iterFn = rec != null ? rec.Class.Dunder(RecordDunder.Iter) : null;
            if (iterFn != null)
            {
                // What __iter__ returned, itself: iter(r) is r when it returns self.
                ScriptValue made = c.CallHook1(iterFn, rec);
                RecordInstanceValue madeRec = made as RecordInstanceValue;
                if (made.Kind == ValueKind.Iterator || made is ILineFile || (madeRec != null && madeRec.Class.Dunder(RecordDunder.Next) != null))
                    return made;
                throw Raise.TypeError(c, "iter() returned non-iterator of type '" + made.PyTypeName + "'");
            }
            IScriptIterator it = PyOps.GetIterator(x, c);
            return c.Values.EngineIterator(it, IteratorTypeName(x));
        }

        // CPython iterator type names: dict and its views share dict_key/value/itemiterator, frozenset uses set_iterator.
        // A script-named type gets a fixed name: iterator type names key a table that outlives the run.
        private static string IteratorTypeName(ScriptValue x)
        {
            if (x.TypeInfo.IsNamedByScript)
                return x is NamedTupleValue ? "tuple_iterator" : "iterator";
            switch (x.PyTypeName)
            {
                case "dict": case "dict_keys": return "dict_keyiterator";
                case "dict_values": return "dict_valueiterator";
                case "dict_items": return "dict_itemiterator";
                case "frozenset": return "set_iterator";
                default:
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
                ScriptValue v;
                try
                {
                    v = ctx.CallHook(_fn, System.Array.Empty<ScriptValue>(), Runtime.Evaluator.KwArgs.Empty);
                }
                catch (ScriptException e) when (e.Value.ExcType.IsSubtypeOf(PyExceptionTypes.StopIteration))
                {
                    _done = true;   // CPython's calliter drops the callable on a StopIteration
                    return false;
                }
                if (PyOps.Equals(v, _sentinel, ctx, 0))
                {
                    _done = true;   // exhausted for good (never revives), like a StopIteration'd iterator
                    return false;
                }
                value = v;
                return true;
            }
        }

        // next(); the StopIteration of an exhausted iterator without a default comes back in `error`
        private static ScriptValue NextOrError(ScriptValue[] args, KwArgs kw, EvalContext c, out ScriptException error)
        {
            error = null;
            if (kw.Count != 0 || args.Length < 1 || args.Length > 2)
            {
                Args.NoKw(c, kw, "next");
                Args.CheckPositional(c, args, "next", 1, 2);
            }
            IteratorValue it = args[0] as IteratorValue;
            ScriptValue v;
            ILineFile file = args[0] as ILineFile;
            if (file != null)
            {
                if (file.NextLine(c, out v))
                    return v;
                if (args.Length == 2)
                    return args[1];
                error = Raise.StopIteration(c);
                return null;
            }
            RecordInstanceValue rec = args[0] as RecordInstanceValue;
            FunctionValue nextFn = rec != null ? rec.Class.Dunder(RecordDunder.Next) : null;
            if (nextFn != null)
            {
                if (args.Length == 1)
                    return c.CallHook1(nextFn, rec);   // its StopIteration, with its arguments, is the caller's
                try
                {
                    return c.CallHook1(nextFn, rec);
                }
                catch (ScriptException e) when (e.Value.ExcType.IsSubtypeOf(PyExceptionTypes.StopIteration))
                {
                    return args[1];
                }
            }
            if (it == null)
                throw Raise.TypeError(c, "'" + args[0].PyTypeName + "' object is not an iterator");
            c.CaughtStop = null;
            if (it.Iterator.MoveNext(c, out v))
                return v;
            if (args.Length == 2)
                return args[1];
            // From a deeper call it is a loop inside a callback that ended, and the callback went on: not this stop.
            error = c.CaughtStop != null && c.CaughtStopDepth == c.CallDepth ? new ScriptException(c.CaughtStop) : Raise.StopIteration(c);
            return null;
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
