using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // typing: a runtime STUB so 3.14-era scripts using annotations and `from typing import ...`
    // just run. Annotations themselves are parsed-and-discarded by the frontend (PEP 649 spirit); this
    // module only has to make runtime uses inert: subscription (Optional[int]) returns the form itself,
    // cast(t, v) returns v, decorators are identity, TYPE_CHECKING is False. No type checking happens.
    internal static class TypingModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);

            // Special forms and generic aliases — inert, subscriptable, not callable.
            string[] forms =
            {
                "Any", "Union", "Optional", "Literal", "Final", "ClassVar", "Annotated", "Callable",
                "Type", "List", "Dict", "Tuple", "Set", "FrozenSet", "Deque", "DefaultDict", "OrderedDict",
                "Counter", "ChainMap", "Iterable", "Iterator", "Generator", "Reversible", "Sequence",
                "MutableSequence", "Mapping", "MutableMapping", "MutableSet", "AbstractSet", "Collection",
                "Container", "Hashable", "Sized", "NoReturn", "Never", "Self", "LiteralString", "TypeAlias",
                "Text", "AnyStr", "Generic", "Protocol", "IO", "TextIO", "BinaryIO",
                "Unpack", "Required", "NotRequired", "Concatenate",
            };
            foreach (string name in forms)
                m[name] = new TypingFormValue(name);

            m["TYPE_CHECKING"] = ctx.Values.Bool(false);

            // cast(typ, val) -> val (the entire point of cast is being a runtime no-op).
            m["cast"] = BuiltinFunctionValue.Make("cast", Args.Positional("cast", 2, 2, (self, a, kw, c) =>
            {
                Args.Exactly(c, a, "cast", 2);
                return a[1];
            }));
            m["assert_type"] = BuiltinFunctionValue.Make("assert_type", Args.Positional("assert_type", 2, 2, (self, a, kw, c) =>
            {
                Args.Exactly(c, a, "assert_type", 2);
                return a[0];
            }));

            // Factories returning inert named forms.
            m["TypeVar"] = NamedFactory("TypeVar");
            m["ParamSpec"] = NamedFactory("ParamSpec");
            m["TypeVarTuple"] = NamedFactory("TypeVarTuple");

            // NewType('X', base) -> an identity callable (calling it returns the argument unchanged).
            m["NewType"] = BuiltinFunctionValue.Make("NewType", Args.Positional("NewType", 2, 2, (self, a, kw, c) =>
            {
                Args.Exactly(c, a, "NewType", 2);
                string name = a[0].Kind == ValueKind.Str ? ((StrValue)a[0]).Value : "NewType";
                return c.Values.Closure(name, (s2, a2, k2, c2) =>
                {
                    Args.Exactly(c2, a2, name, 1);
                    return a2[0];
                });
            }));

            // Identity decorators.
            foreach (string dec in new[] { "overload", "final", "runtime_checkable", "no_type_check", "override" })
                m[dec] = IdentityFn(dec);

            // NamedTuple functional form: NamedTuple('P', [('x', int), ...]) -> a collections.namedtuple
            // type (the annotation part of each pair is ignored). Class-based use needs classes -> v3.
            m["NamedTuple"] = BuiltinFunctionValue.Make("NamedTuple", (self, a, kw, c) =>
            {
                // NamedTuple(typename, fields) or NamedTuple(typename, x=int, y=str): the keyword form names the
                // fields in order (the annotations are not kept).
                Args.Between(c, a, "NamedTuple", 1, 2);
                ListValue names = c.Values.List(4);
                if (kw.Count > 0)
                {
                    if (a.Length == 2)
                        throw Raise.TypeError(c, "Either list of fields or keywords can be provided to NamedTuple, not both");
                    for (int i = 0; i < kw.Count; i++)
                        names.Add(c.Values.Str(kw.NameAt(i)), c);
                    return CollectionsModule.NamedTuple(null, new[] { a[0], (ScriptValue)names }, KwArgs.Empty, c);
                }
                if (a.Length < 2)
                    throw Raise.TypeError(c, "NamedTuple() missing required argument 'fields'");
                IScriptIterator it = PyOps.GetIterator(a[1], c);
                ScriptValue item;
                while (it.MoveNext(c, out item))
                {
                    // each item is 'name' or ('name', annotation)
                    if (item.Kind == ValueKind.Str)
                        names.Add(item, c);
                    else if (item.Kind == ValueKind.Tuple && ((TupleValue)item).Items.Length >= 1)
                        names.Add(((TupleValue)item).Items[0], c);
                    else
                        throw Raise.TypeError(c, "NamedTuple() field must be a string or a (name, type) tuple");
                }
                return CollectionsModule.NamedTuple(null, new[] { a[0], (ScriptValue)names }, KwArgs.Empty, c);
            });

            m["TypedDict"] = BuiltinFunctionValue.Make("TypedDict", (self, a, kw, c) => TypedDict(c, a, kw));

            m["get_type_hints"] = BuiltinFunctionValue.Make("get_type_hints", (self, a, kw, c) =>
                GetTypeHints(c, Args.Named(c, a, kw, "get_type_hints", "obj", "globalns", "localns", "include_extras")));
            // Introspection stubs: nothing is stored, so nothing comes back.
            m["get_args"] = BuiltinFunctionValue.Make("get_args", Args.Positional("get_args", 1, 1, (self, a, kw, c) => ValueFactory.EmptyTuple));
            m["get_origin"] = BuiltinFunctionValue.Make("get_origin", Args.Positional("get_origin", 1, 1, (self, a, kw, c) => c.Values.None));

            return ctx.Values.Module("typing", m);
        }

        // A record class keeps each field's annotation, and a function each parameter's and its return
        // annotation, as the plain NAME it was written with (only a bare identifier is retained), so the
        // hints come back as strings — the shape CPython produces under `from __future__ import
        // annotations`. Anything else gets {}.
        private static ScriptValue GetTypeHints(EvalContext ctx, ScriptValue[] args)
        {
            if (args.Length < 1)
                throw Raise.TypeError(ctx, "get_type_hints() missing 1 required positional argument: 'obj'");
            FunctionValue fn = args[0] as FunctionValue;
            if (fn != null)
            {
                DictValue hints = ctx.Values.Dict(fn.Annotations.Length);
                foreach (var kv in fn.Annotations)
                    hints.SetItem(ctx.Values.Str(kv.Key), ctx.Values.Str(kv.Value), ctx);
                return hints;
            }
            RecordClassValue cls = args[0] as RecordClassValue;
            RecordInstanceValue inst = args[0] as RecordInstanceValue;
            if (cls == null && inst != null)
                cls = inst.Class;
            if (cls == null || cls.AnnotationDefaults == null)
                return ctx.Values.Dict(0);
            DictValue d = ctx.Values.Dict(cls.AllSlots.Count);
            for (int i = 0; i < cls.AllSlots.Count; i++)
            {
                string slot = cls.AllSlots[i];
                FieldDefault fd;
                if (cls.AnnotationDefaults.TryGetValue(slot, out fd) && fd.TypeName != null)
                    d.SetItem(ctx.Values.Str(slot), ctx.Values.Str(fd.TypeName), ctx);
            }
            return d;
        }

        // TypedDict('TD', {'a': int}, total=True), the functional form (review 6, t1-03): a class whose call is dict(...),
        // as CPython's; the value types are kept in __annotations__ and not checked.
        private static ScriptValue TypedDict(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            bool total = true;
            for (int i = 0; i < kw.Count; i++)
            {
                string k = kw.NameAt(i);
                if (k == "total")
                    total = kw.ValueAt(i).IsTruthy(c);
                else if (k != "closed" && k != "extra_items")
                    throw Raise.TypeError(c, "TypedDict() got an unexpected keyword argument '" + k + "'");
            }
            Args.Between(c, a, "TypedDict", 1, 2);
            StrValue name = a[0] as StrValue;
            if (name == null)
                throw Raise.TypeError(c, "TypedDict() argument 'typename' must be str, not " + a[0].PyTypeName);
            DictValue fields = a.Length > 1 ? a[1] as DictValue : null;
            if (a.Length > 1 && fields == null && a[1].Kind != ValueKind.None)
                throw Raise.TypeError(c, "TypedDict() argument 'fields' must be a dict, not " + a[1].PyTypeName);
            DictValue annotations = c.Values.Dict(fields == null ? 0 : (int)fields.Count);
            ListValue keys = c.Values.List(4);
            if (fields != null)
            {
                IScriptIterator it = PyOps.GetIterator(fields, c);
                ScriptValue key;
                while (it.MoveNext(c, out key))
                {
                    annotations.SetItem(key, fields.GetItemOrThrow(key, c), c);
                    keys.Add(key, c);
                }
            }
            ScriptValue none = Builtins.FrozenSetBuiltin(c, new ScriptValue[0]);
            ScriptValue all = Builtins.FrozenSetBuiltin(c, new ScriptValue[] { keys });
            var statics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["__total__"] = c.Values.Bool(total),
                ["__required_keys__"] = total ? all : none,
                ["__optional_keys__"] = total ? none : all,
                ["__annotations__"] = annotations,
            };
            c.Values.ChargeObject(1);
            TypeValue td = c.Values.ScriptMadeType(name.Value, (s, args, k2, c2) => Builtins.DictBuiltin(c2, args, k2),
                new ScriptTypeInfo(name.Value, true), statics);
            td.NoInstanceChecks = "TypedDict does not support instance and class checks";
            td.ReprModule = "__main__";
            return td;
        }

        // TypeVar('T', ...) / ParamSpec('P') -> an inert form named after the first argument.
        private static BuiltinFunctionValue NamedFactory(string factory)
        {
            return BuiltinFunctionValue.Make(factory, (self, a, kw, c) =>
            {
                if (a.Length < 1 || a[0].Kind != ValueKind.Str)
                    throw Raise.TypeError(c, factory + "() requires a string name as the first argument");
                // CPython's keywords for each (the C type names itself in lower case in the refusal)
                string[] known = factory == "TypeVar" ? new[] { "bound", "covariant", "contravariant", "infer_variance", "default" }
                    : factory == "ParamSpec" ? new[] { "bound", "covariant", "contravariant", "infer_variance", "default" }
                    : new[] { "default" };
                for (int i = 0; i < kw.Count; i++)
                {
                    if (Array.IndexOf(known, kw.NameAt(i)) < 0)
                        throw Raise.TypeError(c, factory.ToLowerInvariant() + "() got an unexpected keyword argument '" + kw.NameAt(i) + "'");
                }
                c.Values.ChargeObject(1);
                return new TypingFormValue(((StrValue)a[0]).Value);
            });
        }

        private static BuiltinFunctionValue IdentityFn(string name)
        {
            return BuiltinFunctionValue.Make(name, Args.Positional(name, 1, 1, (self, a, kw, c) => a[0]));
        }
    }

    // An inert typing form: subscription returns itself (Optional[int] is Optional), never callable.
    internal sealed class TypingFormValue : ScriptValue
    {
        private static readonly ScriptTypeInfo FormType = new ScriptTypeInfo("typing._SpecialForm", null);

        private readonly string _name;
        internal TypingFormValue(string name) { _name = name; }

        public override ScriptTypeInfo TypeInfo { get { return FormType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        // Optional[X] and Union[...] are the union of their members (a class union is the `X | Y` value, as in 3.14);
        // any other form keeps its arguments for repr and ==, as CPython's _GenericAlias does. Nothing checks them.
        protected internal override ScriptValue GetItemCore(ScriptValue index, EvalContext ctx)
        {
            TupleValue tuple = index as TupleValue;
            ScriptValue[] args = tuple != null ? tuple.Items : new[] { index };
            ctx.Budget.ChargeLinear(args.Length);
            ctx.Values.PreCharge(16L + 8L * args.Length);
            if (_name == "Optional")
                return TypingAliasValue.Union(new[] { index, (ScriptValue)ctx.Values.None }, ctx);
            if (_name == "Union")
                return TypingAliasValue.Union(args, ctx);
            return new TypingAliasValue(_name, args, tuple != null && tuple.Items.Length == 0);
        }

        // Hashed once: the name is the script's and may be 16M chars long.
        private long _hash;
        private bool _hashed;

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            if (!_hashed)
            {
                ctx.Budget.ChargeLinear(_name.Length);
                _hash = NumericHash.HashString(_name, ctx.StrHashSeed);   // string.GetHashCode differs per process
                _hashed = true;
            }
            return _hash;
        }
        // a bare Optional or Union is no type argument (CPython: "Plain typing.Optional is not valid as type argument")
        internal bool CanBeUnionMember { get { return _name != "Optional" && _name != "Union"; } }

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            return CanBeUnionMember ? TypingAliasValue.Or(this, op, other, reflected, ctx) : null;
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("typing." + _name); }
    }

    // A subscripted typing form (List[int], Callable[[int], str], Literal['a']), or a union of members that are not all
    // classes (Optional[List[int]]). Inert like the form; repr and == as CPython 3.14's.
    internal sealed class TypingAliasValue : ScriptValue
    {
        private static readonly ScriptTypeInfo AliasType = new ScriptTypeInfo("typing._GenericAlias", null);

        private readonly string _name;            // the form; null for a union
        private readonly ScriptValue[] _args;
        private readonly bool _emptyTuple;        // Tuple[()]

        internal TypingAliasValue(string name, ScriptValue[] args, bool emptyTuple)
        {
            _name = name;
            _args = args;
            _emptyTuple = emptyTuple;
        }

        public override ScriptTypeInfo TypeInfo { get { return AliasType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        // Union[...]: nested unions flattened, repeats dropped, one member is that member; all classes or None make
        // the `X | Y` value isinstance takes.
        internal static ScriptValue Union(ScriptValue[] args, EvalContext ctx)
        {
            var members = new List<ScriptValue>();
            foreach (ScriptValue a in args)
                AddMember(members, a, ctx);
            if (members.Count == 0)
                throw Raise.TypeError(ctx, "Cannot take a Union of no types.");   // Union[()]
            if (members.Count == 1)
                return members[0];
            ScriptValue folded = members[0];
            for (int i = 1; i < members.Count && folded != null; i++)
                folded = UnionTypeValue.TryMake(folded, members[i], ctx);
            return folded ?? new TypingAliasValue(null, members.ToArray(), false);
        }

        // `X | Y` with a typing object on either side (List[int] | None, Any | None, int | List[int]): typing.Union of the two.
        internal static ScriptValue Or(ScriptValue self, PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            if (op != PyBinOp.BitOr || !IsUnionArg(other))
                return null;
            return Union(reflected ? new[] { other, self } : new[] { self, other }, ctx);
        }

        private static bool IsUnionArg(ScriptValue v)
        {
            TypingFormValue f = v as TypingFormValue;
            if (f != null)
                return f.CanBeUnionMember;
            return v.Kind == ValueKind.Type || v.Kind == ValueKind.RecordClass || v is EnumClassValue || v.Kind == ValueKind.None
                || v is UnionTypeValue || v is TypingAliasValue
                || v is StrValue;   // a forward reference
        }

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            return Or(this, op, other, reflected, ctx);
        }

        private static void AddMember(List<ScriptValue> members, ScriptValue a, EvalContext ctx)
        {
            TypingAliasValue u = a as TypingAliasValue;
            UnionTypeValue cu = a as UnionTypeValue;
            if (u != null && u._name == null)
            {
                foreach (ScriptValue m in u._args)
                    AddMember(members, m, ctx);
                return;
            }
            if (cu != null)
            {
                foreach (ScriptValue m in cu.Args)
                    AddMember(members, m, ctx);
                return;
            }
            foreach (ScriptValue m in members)
            {
                if ((m.Kind == ValueKind.None && a.Kind == ValueKind.None) || PyOps.Equals(m, a, ctx, 0))
                    return;
            }
            members.Add(a);
        }

        protected internal override ScriptValue GetItemCore(ScriptValue index, EvalContext ctx) { return this; }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            TypingAliasValue o = other as TypingAliasValue;
            if (o == null || o._name != _name || o._emptyTuple != _emptyTuple)
                return false;
            if (_name == "Literal")   // CPython: a set of (value, type), so Literal[1] != Literal[True], in any order
            {
                if (AllPlain(_args) && AllPlain(o._args))
                    return CoversByHash(o, this, ctx, depth) && CoversByHash(this, o, ctx, depth);
                return HasLiterals(o, this, ctx, depth) && HasLiterals(this, o, ctx, depth);
            }
            if (o._args.Length != _args.Length)
                return false;
            if (_name == null)   // a union compares as a set of its members, as CPython's
            {
                foreach (ScriptValue a in _args)
                {
                    if (!System.Array.Exists(o._args, m => PyOps.Equals(a, m, ctx, depth + 1)))
                        return false;
                }
                return true;
            }
            for (int i = 0; i < _args.Length; i++)
            {
                if (!PyOps.Equals(_args[i], o._args[i], ctx, depth + 1))
                    return false;
            }
            return true;
        }

        // Values whose hash and == run no script code.
        private static bool AllPlain(ScriptValue[] args)
        {
            foreach (ScriptValue a in args)
            {
                ValueKind k = a.Kind;
                if (k != ValueKind.None && k != ValueKind.Bool && k != ValueKind.Int && k != ValueKind.Str && k != ValueKind.Bytes)
                    return false;
            }
            return true;
        }

        // HasLiterals by hash buckets: linear, where the member-by-member search was quadratic; a bucket of
        // colliding values pays for each value it is searched through.
        private static bool CoversByHash(TypingAliasValue all, TypingAliasValue of, EvalContext ctx, int depth)
        {
            ctx.Budget.ChargeLinear(all._args.Length + of._args.Length);
            var buckets = new Dictionary<long, List<ScriptValue>>();
            foreach (ScriptValue m in all._args)
            {
                long h = PyOps.Hash(m, ctx, depth + 1);
                List<ScriptValue> b;
                if (!buckets.TryGetValue(h, out b))
                    buckets[h] = b = new List<ScriptValue>(1);
                b.Add(m);
            }
            foreach (ScriptValue a in of._args)
            {
                List<ScriptValue> b;
                if (!buckets.TryGetValue(PyOps.Hash(a, ctx, depth + 1), out b))
                    return false;
                ctx.Budget.ChargeLinear(b.Count);
                if (!b.Exists(m => ReferenceEquals(m.TypeInfo, a.TypeInfo) && PyOps.Equals(a, m, ctx, depth + 1)))
                    return false;
            }
            return true;
        }

        private static bool HasLiterals(TypingAliasValue all, TypingAliasValue of, EvalContext ctx, int depth)
        {
            foreach (ScriptValue a in of._args)
            {
                if (!System.Array.Exists(all._args, m => ReferenceEquals(m.TypeInfo, a.TypeInfo) && PyOps.Equals(a, m, ctx, depth + 1)))
                    return false;
            }
            return true;
        }

        // Equal aliases hash alike; an argument that cannot be hashed (Callable's list) counts by its kind only.
        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            long h = _name == null ? 0x7A11 : NumericHash.HashString(_name, ctx.StrHashSeed);
            if (_name == "Literal")
                return h;   // its equality ignores order and repeats: the form alone hashes alike
            if (_name == null)
            {
                foreach (ScriptValue a in _args)
                    h ^= PyOps.Hash(a, ctx, depth + 1);   // order-free, as the equality
                return h;
            }
            foreach (ScriptValue a in _args)
                h = h * 31 + (a.Kind == ValueKind.List ? 0x4C : PyOps.Hash(a, ctx, depth + 1));
            return h;
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            if (_name == null)
            {
                for (int i = 0; i < _args.Length; i++)
                {
                    if (i > 0)
                        sb.Append(" | ");
                    AppendType(sb, _args[i], ctx, depth);
                }
                return;
            }
            sb.Append("typing.");
            sb.Append(_name);
            sb.Append('[');
            if (_emptyTuple)
                sb.Append("()");
            for (int i = 0; i < _args.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                bool value = _name == "Literal" || (_name == "Annotated" && i > 0);
                if (value)
                    sb.Append(_args[i].Repr(ctx, depth + 1));
                else
                    AppendType(sb, _args[i], ctx, depth);
            }
            sb.Append(']');
        }

        // typing._type_repr: a class by its (qualified) name, ... and None as written, a string as a forward reference,
        // Callable's parameter list in brackets, anything else by repr.
        // Into the charged builder, a nested list a C# frame a level: capped at MaxDataDepth and probed, as ReprEngine's leaves.
        private static void AppendType(BudgetStringBuilder sb, ScriptValue a, EvalContext ctx, int depth)
        {
            if (a is EllipsisValue)
            {
                sb.Append("...");
                return;
            }
            StrValue s = a as StrValue;
            if (s != null)
            {
                sb.Append("ForwardRef(");
                sb.Append(s.Repr(ctx, depth + 1));
                sb.Append(')');
                return;
            }
            ListValue l = a as ListValue;
            if (l != null)
            {
                if (depth > ctx.Limits.MaxDataDepth)
                    throw Raise.RecursionError(ctx, "maximum recursion depth exceeded while getting the repr of an object");
                StackGuard.ProbeData(ctx);
                sb.Append('[');
                for (int i = 0; i < l.Items.Count; i++)
                {
                    if (i > 0)
                        sb.Append(", ");
                    AppendType(sb, l.Items[i], ctx, depth + 1);
                }
                sb.Append(']');
                return;
            }
            if (a is TypingFormValue || a is TypingAliasValue || a is UnionTypeValue)
                sb.Append(a.Repr(ctx, depth + 1));
            else
                sb.Append(UnionTypeValue.MemberText(a, ctx, depth));
        }
    }
}
