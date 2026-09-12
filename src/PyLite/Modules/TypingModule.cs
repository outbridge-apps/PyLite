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
    public static class TypingModule
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
                "Text", "AnyStr", "Generic", "Protocol", "TypedDict", "IO", "TextIO", "BinaryIO",
                "Unpack", "Required", "NotRequired", "Concatenate",
            };
            foreach (string name in forms)
                m[name] = new TypingFormValue(name);

            m["TYPE_CHECKING"] = ctx.Values.Bool(false);

            // cast(typ, val) -> val (the entire point of cast is being a runtime no-op).
            m["cast"] = BuiltinFunctionValue.Make("cast", (self, a, kw, c) =>
            {
                Args.Exactly(c, a, "cast", 2);
                return a[1];
            });
            m["assert_type"] = BuiltinFunctionValue.Make("assert_type", (self, a, kw, c) =>
            {
                Args.Exactly(c, a, "assert_type", 2);
                return a[0];
            });

            // Factories returning inert named forms.
            m["TypeVar"] = NamedFactory("TypeVar");
            m["ParamSpec"] = NamedFactory("ParamSpec");
            m["TypeVarTuple"] = NamedFactory("TypeVarTuple");

            // NewType('X', base) -> an identity callable (calling it returns the argument unchanged).
            m["NewType"] = BuiltinFunctionValue.Make("NewType", (self, a, kw, c) =>
            {
                Args.Exactly(c, a, "NewType", 2);
                string name = a[0].Kind == ValueKind.Str ? ((StrValue)a[0]).Value : "NewType";
                return BuiltinFunctionValue.Make(name, (s2, a2, k2, c2) =>
                {
                    Args.Exactly(c2, a2, name, 1);
                    return a2[0];
                });
            });

            // Identity decorators.
            foreach (string dec in new[] { "overload", "final", "runtime_checkable", "no_type_check", "override" })
                m[dec] = IdentityFn(dec);

            // NamedTuple functional form: NamedTuple('P', [('x', int), ...]) -> a collections.namedtuple
            // type (the annotation part of each pair is ignored). Class-based use needs classes -> v3.
            m["NamedTuple"] = BuiltinFunctionValue.Make("NamedTuple", (self, a, kw, c) =>
            {
                Args.Exactly(c, a, "NamedTuple", 2);
                IScriptIterator it = PyOps.GetIterator(a[1], c);
                ListValue names = c.Values.List(4);
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

            m["get_type_hints"] = BuiltinFunctionValue.Make("get_type_hints", (self, a, kw, c) => GetTypeHints(c, a));
            // Introspection stubs: nothing is stored, so nothing comes back.
            m["get_args"] = BuiltinFunctionValue.Make("get_args", (self, a, kw, c) => ValueFactory.EmptyTuple);
            m["get_origin"] = BuiltinFunctionValue.Make("get_origin", (self, a, kw, c) => c.Values.None);

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

        // TypeVar('T', ...) / ParamSpec('P') -> an inert form named after the first argument.
        private static BuiltinFunctionValue NamedFactory(string factory)
        {
            return BuiltinFunctionValue.Make(factory, (self, a, kw, c) =>
            {
                if (a.Length < 1 || a[0].Kind != ValueKind.Str)
                    throw Raise.TypeError(c, factory + "() requires a string name as the first argument");
                return new TypingFormValue(((StrValue)a[0]).Value);
            });
        }

        private static BuiltinFunctionValue IdentityFn(string name)
        {
            return BuiltinFunctionValue.Make(name, (self, a, kw, c) =>
            {
                Args.Exactly(c, a, name, 1);
                return a[0];
            });
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

        protected internal override ScriptValue GetItemCore(ScriptValue index, EvalContext ctx) { return this; }
        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return _name.GetHashCode(); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("typing." + _name); }
    }
}
