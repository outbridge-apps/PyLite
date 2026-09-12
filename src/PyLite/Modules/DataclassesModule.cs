using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // dataclasses: @dataclass, field(), fields(), asdict(), MISSING. Sugar over
    // record classes — the class-body annotation fields become the slots; @dataclass records how to
    // synthesize __init__ (auto __repr__/__eq__ already come from the record class) and the eq / order /
    // repr knobs. frozen => read-only after init; __post_init__ runs after the generated __init__.
    public static class DataclassesModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["dataclass"] = BuiltinFunctionValue.Make("dataclass", Dataclass);
            m["field"] = BuiltinFunctionValue.Make("field", Field);
            m["fields"] = BuiltinFunctionValue.Make("fields", Fields);
            m["asdict"] = BuiltinFunctionValue.Make("asdict", AsDict);
            m["astuple"] = BuiltinFunctionValue.Make("astuple", AsTuple);
            m["is_dataclass"] = BuiltinFunctionValue.Make("is_dataclass", IsDataclass);
            m["replace"] = BuiltinFunctionValue.Make("replace", Replace);
            m["make_dataclass"] = BuiltinFunctionValue.Make("make_dataclass", MakeDataclass);
            m["InitVar"] = new TypingFormValue("InitVar");   // the annotation head the class body looks for
            m["MISSING"] = MissingValue.Instance;
            return ctx.Values.Module("dataclasses", m);
        }

        // '@dataclass' (bare) or '@dataclass(init=, repr=, eq=, order=, frozen=, kw_only=)'.
        private static ScriptValue Dataclass(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length == 1 && kw.Count == 0 && args[0].Kind == ValueKind.RecordClass)
                return Apply(ctx, (RecordClassValue)args[0], true, false, true, false, true, false);
            if (args.Length > 0)
                throw Raise.TypeError(ctx, "dataclass() should be applied to a class defined in this dialect");
            var r = new KwReader(ctx, kw, "dataclass");
            bool init = r.Bool("init", true);
            bool frozen = r.Bool("frozen", false);
            bool eq = r.Bool("eq", true);
            bool order = r.Bool("order", false);
            bool repr = r.Bool("repr", true);
            bool kwOnly = r.Bool("kw_only", false);
            r.RejectUnknown();
            if (order && !eq)
                throw Raise.ValueError(ctx, "eq must be true if order is true");
            return BuiltinFunctionValue.Make("dataclass", (s2, a2, k2, c2) =>
            {
                if (a2.Length != 1 || a2[0].Kind != ValueKind.RecordClass)
                    throw Raise.TypeError(c2, "dataclass() should be applied to a class defined in this dialect");
                return Apply(c2, (RecordClassValue)a2[0], init, frozen, eq, order, repr, kwOnly);
            });
        }

        private static readonly string[] OrderDunders = { "__lt__", "__le__", "__gt__", "__ge__" };

        private static RecordClassValue Apply(EvalContext ctx, RecordClassValue cls, bool init, bool frozen,
            bool eq, bool order, bool repr, bool kwOnly)
        {
            // order=True generates all four comparisons, so a hand-written one would be silently lost.
            // CPython refuses the combination rather than choosing; an explicit __eq__ it does keep.
            if (order)
            {
                foreach (string name in OrderDunders)
                {
                    if (cls.FindMethod(name) != null)
                        throw Raise.TypeError(ctx, "Cannot overwrite attribute " + name + " in class "
                            + cls.Name + ". Consider using functools.total_ordering");
                }
            }
            var names = new List<string>();
            var fds = new List<FieldDefault>();
            foreach (string slot in cls.AllSlots)
            {
                FieldDefault fd;
                if (cls.AnnotationDefaults == null || !cls.AnnotationDefaults.TryGetValue(slot, out fd))
                    fd = FieldDefault.None;
                if (fd.Kind == FieldKind.ClassVar)   // a class attribute: no parameter, no field
                    continue;
                names.Add(slot);
                fds.Add(kwOnly ? fd.AsKwOnly() : fd);
            }
            // Only the fields that actually become POSITIONAL __init__ parameters have an order to
            // violate: init=False fields are not parameters, and keyword-only ones may follow a default.
            bool sawDefault = false;
            for (int i = 0; i < fds.Count; i++)
            {
                if (!fds[i].Init || fds[i].KwOnly)
                    continue;
                if (fds[i].HasDefault || fds[i].DefaultFactory != null)
                    sawDefault = true;
                else if (sawDefault)
                    throw Raise.TypeError(ctx, "non-default argument '" + names[i] + "' follows default argument");
            }
            var spec = new DataclassSpec(names.ToArray(), fds.ToArray(), init, eq, order, repr);
            // __match_args__ (class patterns) = the positional __init__ parameters, InitVars included.
            var matchArgs = new List<string>();
            for (int i = 0; i < names.Count; i++)
                if (fds[i].Init && !fds[i].KwOnly)
                    matchArgs.Add(names[i]);
            return cls.WithDataclass(frozen, matchArgs.ToArray(), spec);
        }

        // make_dataclass(cls_name, fields, *, bases=(), namespace=None, init=, repr=, eq=, order=, frozen=,
        // kw_only=): a field is a name, (name, type) or (name, type, field(...)); the class is built the
        // way the class statement builds one, then decorated.
        private static ScriptValue MakeDataclass(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "make_dataclass", 2);
            StrValue nameV = args[0] as StrValue;
            if (nameV == null)
                throw Raise.TypeError(ctx, "make_dataclass() cls_name must be a str, not " + args[0].PyTypeName);
            KwReader.RejectUnknownExcept(ctx, kw, "make_dataclass", "bases", "namespace", "init", "repr", "eq", "order", "frozen", "kw_only");

            var names = new List<string>();
            var own = new Dictionary<string, FieldDefault>(StringComparer.Ordinal);
            IScriptIterator it = PyOps.GetIterator(args[1], ctx);
            ScriptValue item;
            while (it.MoveNext(ctx, out item))
            {
                ctx.Budget.Step();
                string fname;
                string tname = null;
                FieldSpecValue spec = null;
                StrValue plain = item as StrValue;
                TupleValue tup = item as TupleValue;
                if (plain != null)
                    fname = plain.Value;
                else if (tup != null && tup.Items.Length >= 1 && tup.Items.Length <= 3 && tup.Items[0] is StrValue)
                {
                    fname = ((StrValue)tup.Items[0]).Value;
                    if (tup.Items.Length >= 2)
                        tname = TypeNameOf(tup.Items[1]);
                    if (tup.Items.Length == 3)
                    {
                        spec = tup.Items[2] as FieldSpecValue;
                        if (spec == null)
                            throw Raise.TypeError(ctx, "Invalid field: " + item.Repr(ctx) + " (the third item must be a field())");
                    }
                }
                else
                    throw Raise.TypeError(ctx, "Invalid field: " + item.Repr(ctx));
                if (fname.Length == 0 || own.ContainsKey(fname))
                    throw Raise.TypeError(ctx, "Field name duplicated or empty: " + fname);
                names.Add(fname);
                own[fname] = spec != null
                    ? new FieldDefault(spec.HasDefault || spec.DefaultFactory != null, spec.Default, spec.DefaultFactory,
                        tname, spec.Init, spec.InRepr, spec.Compare, spec.KwOnly, spec.Hash, spec.Metadata)
                    : new FieldDefault(false, null, null, tname);
            }

            RecordClassValue baseCls = null;
            ScriptValue basesV;
            if (kw.TryGet("bases", out basesV) && basesV.Kind != ValueKind.None)
            {
                TupleValue bt = basesV as TupleValue;
                if (bt == null)
                    throw Raise.TypeError(ctx, "bases must be a tuple");
                if (bt.Items.Length > 1)
                    throw Raise.TypeError(ctx, "make_dataclass() supports single inheritance in this dialect");
                if (bt.Items.Length == 1)
                {
                    baseCls = bt.Items[0] as RecordClassValue;
                    if (baseCls == null)
                        throw Raise.TypeError(ctx, "base must be a class defined in this dialect");
                }
            }

            var methods = new Dictionary<string, FunctionValue>(StringComparer.Ordinal);
            ScriptValue nsV;
            if (kw.TryGet("namespace", out nsV) && nsV.Kind != ValueKind.None)
            {
                DictValue ns = nsV as DictValue;
                if (ns == null)
                    throw Raise.TypeError(ctx, "namespace must be a dict");
                IScriptIterator keys = PyOps.GetIterator(ns, ctx);
                ScriptValue key;
                while (keys.MoveNext(ctx, out key))
                {
                    ScriptValue v;
                    ns.TryGet(key, ctx, out v);
                    StrValue ks = key as StrValue;
                    FunctionValue fv = v as FunctionValue;
                    if (ks == null || fv == null)
                        throw Raise.TypeError(ctx, "namespace entries must map a str to a function defined in this dialect");
                    methods[ks.Value] = fv;
                }
            }

            var allSlots = new List<string>();
            var slotIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var annotations = new Dictionary<string, FieldDefault>(StringComparer.Ordinal);
            if (baseCls != null)
            {
                foreach (string s in baseCls.AllSlots)
                {
                    slotIndex[s] = allSlots.Count;
                    allSlots.Add(s);
                }
                if (baseCls.AnnotationDefaults != null)
                    foreach (var kv in baseCls.AnnotationDefaults)
                        annotations[kv.Key] = kv.Value;
            }
            foreach (string f in names)
            {
                if (!slotIndex.ContainsKey(f))
                {
                    slotIndex[f] = allSlots.Count;
                    allSlots.Add(f);
                }
                annotations[f] = own[f];
            }
            ctx.Values.PreCharge(64 + 16L * allSlots.Count);
            var cls = new RecordClassValue(nameV.Value, baseCls, allSlots, slotIndex, methods, false, null,
                annotations, null, SharedIdentitySource.Next());

            bool eq = KwBool(ctx, kw, "eq", true);
            bool order = KwBool(ctx, kw, "order", false);
            if (order && !eq)
                throw Raise.ValueError(ctx, "eq must be true if order is true");
            return Apply(ctx, cls, KwBool(ctx, kw, "init", true), KwBool(ctx, kw, "frozen", false), eq, order,
                KwBool(ctx, kw, "repr", true), KwBool(ctx, kw, "kw_only", false));
        }

        // The type of a make_dataclass field the way Field.type reports it: a name, when there is one.
        private static string TypeNameOf(ScriptValue t)
        {
            StrValue s = t as StrValue;
            if (s != null)
                return s.Value;
            TypeValue tv = t as TypeValue;
            if (tv != null)
                return tv.Name;
            RecordClassValue rc = t as RecordClassValue;
            return rc != null ? rc.Name : null;
        }

        // field(default=, default_factory=, init=, repr=, compare=, kw_only=, hash=, metadata=)
        // -> a marker consumed at class creation. Unknown keywords are refused rather than ignored.
        private static ScriptValue Field(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length > 0)
                throw Raise.TypeError(ctx, "field() takes no positional arguments");
            var r = new KwReader(ctx, kw, "field");
            ScriptValue def, factory, metadata, hashArg;
            bool hasDef = r.TryGet("default", out def);
            bool hasFac = r.TryGet("default_factory", out factory);
            if (hasDef && hasFac)
                throw Raise.ValueError(ctx, "cannot specify both default and default_factory");
            bool? hash = null;
            if (r.TryGet("hash", out hashArg) && hashArg.Kind != ValueKind.None)
                hash = PyOps.Truth(hashArg, ctx);
            r.TryGet("metadata", out metadata);
            bool init = r.Bool("init", true), repr = r.Bool("repr", true), compare = r.Bool("compare", true);
            bool kwOnly = r.Bool("kw_only", false);
            r.RejectUnknown();
            return new FieldSpecValue(hasDef, hasDef ? def : null, hasFac ? factory : null,
                init, repr, compare, kwOnly, hash, metadata);
        }

        // fields(cls_or_instance) -> tuple of Field objects (name / type / default / default_factory).
        private static ScriptValue Fields(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            RecordClassValue cls = ClassOf(ctx, args);
            if (cls.Dataclass == null)
                throw Raise.TypeError(ctx, "fields() should be called with a dataclass type or instance");
            DataclassSpec ds = cls.Dataclass;
            var arr = new List<ScriptValue>(ds.FieldNames.Length);
            ctx.Values.PreCharge(48L * ds.FieldNames.Length);
            for (int i = 0; i < ds.FieldNames.Length; i++)
                if (ds.Fields[i].Kind == FieldKind.Field)   // an InitVar is a parameter, not a field
                    arr.Add(new DataclassFieldValue(ds.FieldNames[i], ds.Fields[i]));
            return ctx.Values.Tuple(arr.ToArray());
        }

        // asdict(instance) -> dict {field: value}, recursing into nested dataclass instances.
        private static ScriptValue AsDict(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length != 1 || args[0].Kind != ValueKind.RecordInstance)
                throw Raise.TypeError(ctx, "asdict() should be called on dataclass instances");
            RecordInstanceValue inst = (RecordInstanceValue)args[0];
            if (inst.Class.Dataclass == null)
                throw Raise.TypeError(ctx, "asdict() should be called on dataclass instances");
            return AsDictValue(inst, ctx);
        }

        // replace(obj, **changes) -> a NEW instance built through the class, so the generated __init__
        // and __post_init__ run again (CPython semantics). Unnamed fields keep their current value.
        private static ScriptValue Replace(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length != 1 || args[0].Kind != ValueKind.RecordInstance)
                throw Raise.TypeError(ctx, "replace() should be called on dataclass instances");
            RecordInstanceValue inst = (RecordInstanceValue)args[0];
            RecordClassValue cls = inst.Class;
            DataclassSpec ds = cls.Dataclass;
            if (ds == null)
                throw Raise.TypeError(ctx, "replace() should be called on dataclass instances");
            for (int j = 0; j < kw.Count; j++)
            {
                string name = kw.NameAt(j);
                int idx = Array.IndexOf(ds.FieldNames, name);
                if (idx < 0)
                    throw KwReader.UnexpectedError(ctx, "__init__", name);
                if (!ds.Fields[idx].Init)
                    throw Raise.ValueError(ctx, "field " + name
                        + " is declared with init=False, it cannot be specified with replace()");
            }
            var names = new List<string>();
            var values = new List<ScriptValue>();
            for (int i = 0; i < ds.FieldNames.Length; i++)
            {
                if (!ds.Fields[i].Init)
                    continue;
                string name = ds.FieldNames[i];
                ScriptValue v;
                if (!kw.TryGet(name, out v))
                {
                    if (ds.Fields[i].Kind == FieldKind.InitVar)
                    {
                        // never stored, so it cannot be copied: it must be given again unless it defaults
                        if (ds.Fields[i].HasDefault || ds.Fields[i].DefaultFactory != null)
                            continue;
                        throw Raise.ValueError(ctx, "InitVar '" + name + "' must be specified with replace()");
                    }
                    v = inst.Slots[cls.SlotIndex[name]];
                    if (v == null)
                        continue;   // never set: let the generated __init__ apply its own default
                }
                names.Add(name);
                values.Add(v);
            }
            return ctx.CallHook(cls, Array.Empty<ScriptValue>(), KwArgs.FromArrays(names.ToArray(), values.ToArray()));
        }

        // astuple(instance) -> tuple of field values in field order, recursing like asdict.
        private static ScriptValue AsTuple(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length != 1 || args[0].Kind != ValueKind.RecordInstance
                || ((RecordInstanceValue)args[0]).Class.Dataclass == null)
                throw Raise.TypeError(ctx, "astuple() should be called on dataclass instances");
            return AsTupleValue((RecordInstanceValue)args[0], ctx);
        }

        private static ScriptValue AsTupleValue(RecordInstanceValue inst, EvalContext ctx)
        {
            var items = new List<ScriptValue>(inst.Class.AllSlots.Count);
            ctx.Values.PreCharge(8L * inst.Class.AllSlots.Count + 24);
            for (int i = 0; i < inst.Class.AllSlots.Count; i++)
            {
                if (!IsField(inst.Class, i))
                    continue;
                ScriptValue v = inst.Slots[i];
                RecordInstanceValue nested = v as RecordInstanceValue;
                if (nested != null && nested.Class.Dataclass != null)
                    v = AsTupleValue(nested, ctx);
                items.Add(v ?? ctx.Values.None);
            }
            return ctx.Values.Tuple(items.ToArray());
        }

        // asdict / astuple carry the dataclass FIELDS: a ClassVar or InitVar slot is left out.
        private static bool IsField(RecordClassValue cls, int slot)
        {
            FieldDefault fd = cls.Dataclass.ForName(cls.AllSlots[slot]);
            if (fd != null)
                return fd.Kind == FieldKind.Field;
            return cls.AnnotationDefaults == null || !cls.AnnotationDefaults.TryGetValue(cls.AllSlots[slot], out fd)
                || fd.Kind == FieldKind.Field;
        }

        // is_dataclass(x): True for a dataclass CLASS or an instance of one.
        private static ScriptValue IsDataclass(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "is_dataclass", 1);
            ScriptValue x = args[0];
            bool yes = x.Kind == ValueKind.RecordClass
                ? ((RecordClassValue)x).Dataclass != null
                : x.Kind == ValueKind.RecordInstance && ((RecordInstanceValue)x).Class.Dataclass != null;
            return ctx.Values.Bool(yes);
        }

        private static ScriptValue AsDictValue(RecordInstanceValue inst, EvalContext ctx)
        {
            DictValue d = ctx.Values.Dict(inst.Class.AllSlots.Count);
            for (int i = 0; i < inst.Class.AllSlots.Count; i++)
            {
                if (!IsField(inst.Class, i))
                    continue;
                ScriptValue v = inst.Slots[i];
                RecordInstanceValue nested = v as RecordInstanceValue;
                if (nested != null && nested.Class.Dataclass != null)
                    v = AsDictValue(nested, ctx);
                d.SetItem(ctx.Values.Str(inst.Class.AllSlots[i]), v ?? ctx.Values.None, ctx);
            }
            return d;
        }

        private static RecordClassValue ClassOf(EvalContext ctx, ScriptValue[] args)
        {
            if (args.Length != 1)
                throw Raise.TypeError(ctx, "expected exactly one argument");
            if (args[0].Kind == ValueKind.RecordClass)
                return (RecordClassValue)args[0];
            if (args[0].Kind == ValueKind.RecordInstance)
                return ((RecordInstanceValue)args[0]).Class;
            throw Raise.TypeError(ctx, "expected a dataclass type or instance");
        }

        private static bool KwBool(EvalContext ctx, KwArgs kw, string name, bool fallback)
        {
            ScriptValue v;
            return kw.TryGet(name, out v) ? PyOps.Truth(v, ctx) : fallback;
        }
    }

    // dataclasses.MISSING: the "no default" sentinel (identity equality, deterministic hash, repr MISSING).
    internal sealed class MissingValue : ScriptValue
    {
        public static readonly MissingValue Instance = new MissingValue();
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("_MISSING_TYPE", null);

        private MissingValue() { }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return 1_000_000_007L; }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("MISSING"); }
    }

    // One entry of dataclasses.fields(): name, type (the plain-name annotation as a string, PEP 563 style,
    // or None), default and default_factory (MISSING when absent).
    internal sealed class DataclassFieldValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("Field", BuildSlots());
        private readonly string _name;
        private readonly FieldDefault _fd;

        internal DataclassFieldValue(string name, FieldDefault fd) { _name = name; _fd = fd; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        private ScriptValue TypeAttr(EvalContext ctx)
        {
            return _fd.TypeName == null ? (ScriptValue)ctx.Values.None : ctx.Values.Str(_fd.TypeName);
        }

        private ScriptValue DefaultAttr()
        {
            return _fd.HasDefault && _fd.DefaultFactory == null && _fd.Default != null ? _fd.Default : MissingValue.Instance;
        }

        private ScriptValue FactoryAttr()
        {
            return _fd.DefaultFactory ?? (ScriptValue)MissingValue.Instance;
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("Field(name=");
            sb.Append(ctx.Values.Str(_name).Repr(ctx, depth + 1));
            sb.Append(",type=");
            sb.Append(TypeAttr(ctx).Repr(ctx, depth + 1));
            sb.Append(",default=");
            sb.Append(DefaultAttr().Repr(ctx, depth + 1));
            sb.Append(",default_factory=");
            sb.Append(FactoryAttr().Repr(ctx, depth + 1));
            sb.Append(')');
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            s["name"] = SlotDescriptor.MakeProperty("name", (self, c) => c.Values.Str(((DataclassFieldValue)self)._name));
            s["type"] = SlotDescriptor.MakeProperty("type", (self, c) => ((DataclassFieldValue)self).TypeAttr(c));
            s["default"] = SlotDescriptor.MakeProperty("default", (self, c) => ((DataclassFieldValue)self).DefaultAttr());
            s["default_factory"] = SlotDescriptor.MakeProperty("default_factory", (self, c) => ((DataclassFieldValue)self).FactoryAttr());
            s["init"] = SlotDescriptor.MakeProperty("init", (self, c) => c.Values.Bool(((DataclassFieldValue)self)._fd.Init));
            s["repr"] = SlotDescriptor.MakeProperty("repr", (self, c) => c.Values.Bool(((DataclassFieldValue)self)._fd.Repr));
            s["compare"] = SlotDescriptor.MakeProperty("compare", (self, c) => c.Values.Bool(((DataclassFieldValue)self)._fd.Compare));
            s["kw_only"] = SlotDescriptor.MakeProperty("kw_only", (self, c) => c.Values.Bool(((DataclassFieldValue)self)._fd.KwOnly));
            s["hash"] = SlotDescriptor.MakeProperty("hash", (self, c) =>
            {
                bool? h = ((DataclassFieldValue)self)._fd.Hash;
                return h.HasValue ? (ScriptValue)c.Values.Bool(h.Value) : c.Values.None;
            });
            s["metadata"] = SlotDescriptor.MakeProperty("metadata", (self, c) =>
                ((DataclassFieldValue)self)._fd.Metadata ?? (ScriptValue)c.Values.Dict(0));
            return s;
        }
    }
}
