using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // record class object. Callable to construct instances; methods resolve child-first up the
    // single-inheritance chain; reachable statically (ClassName.method) for explicit base calls. Hashable.
    public sealed class RecordClassValue : ScriptValue
    {
        private readonly ScriptTypeInfo _type;
        public readonly string Name;
        public readonly RecordClassValue Base;                       // null => no base
        public readonly IReadOnlyList<string> AllSlots;              // base slots then own new, declaration order
        internal readonly Dictionary<string, int> SlotIndex;
        internal readonly Dictionary<string, FunctionValue> Methods; // own methods only
        public readonly bool Frozen;
        internal readonly string[] MatchArgs;                                 // __match_args__ for class patterns
        internal readonly IReadOnlyDictionary<string, FieldDefault> AnnotationDefaults;   // class-body field defaults
        internal readonly DataclassSpec Dataclass;                            // null => not a @dataclass
        private readonly long _identity;

        internal RecordClassValue(string name, RecordClassValue baseCls, IReadOnlyList<string> allSlots,
            Dictionary<string, int> slotIndex, Dictionary<string, FunctionValue> methods, bool frozen,
            string[] matchArgs, IReadOnlyDictionary<string, FieldDefault> annotationDefaults,
            DataclassSpec dataclass, long identity)
        {
            Name = name;
            Base = baseCls;
            AllSlots = allSlots;
            SlotIndex = slotIndex;
            Methods = methods;
            Frozen = frozen;
            MatchArgs = matchArgs;
            AnnotationDefaults = annotationDefaults;
            Dataclass = dataclass;
            _identity = identity;
            _type = new ScriptTypeInfo(name, null);
        }

        // @dataclass returns a copy with the generated-behavior flags set (fields/methods/slots unchanged).
        internal RecordClassValue WithDataclass(bool frozen, string[] matchArgs, DataclassSpec spec)
        {
            return new RecordClassValue(Name, Base, AllSlots, SlotIndex, Methods, frozen, matchArgs,
                AnnotationDefaults, spec, _identity);
        }

        public override ScriptTypeInfo TypeInfo { get { return _type; } }
        internal override ValueKind Kind { get { return ValueKind.RecordClass; } }
        internal override bool IsCallable { get { return true; } }

        internal FunctionValue FindMethod(string name)
        {
            for (RecordClassValue c = this; c != null; c = c.Base)
            {
                FunctionValue f;
                if (c.Methods.TryGetValue(name, out f))
                    return f;
            }
            return null;
        }

        internal bool IsSubclassOf(RecordClassValue other)
        {
            for (RecordClassValue c = this; c != null; c = c.Base)
                if (ReferenceEquals(c, other))
                    return true;
            return false;
        }

        // __match_args__ as @dataclass writes it: a tuple of the positional __init__ names. A plain
        // class has none (null), which is CPython's AttributeError too.
        internal bool TryMatchArgs(EvalContext ctx, out ScriptValue value)
        {
            value = null;
            if (MatchArgs == null)
                return false;
            var items = new ScriptValue[MatchArgs.Length];
            for (int i = 0; i < items.Length; i++)
                items[i] = ctx.Values.Str(MatchArgs[i]);
            value = ctx.Values.Tuple(items);
            return true;
        }

        internal ScriptValue GetStaticAttr(string name, EvalContext ctx)
        {
            FunctionValue f = FindMethod(name);
            if (f != null)
                return f;   // ClassName.method -> plain function (explicit base calls: Base.__init__(self, ...))
            FieldDefault cv;
            if (AnnotationDefaults != null && AnnotationDefaults.TryGetValue(name, out cv)
                && cv.Kind == FieldKind.ClassVar && cv.HasDefault)
                return cv.Default;   // x: ClassVar[int] = 5 is the class attribute it is in CPython
            ScriptValue ma;
            if (name == "__match_args__" && TryMatchArgs(ctx, out ma))
                return ma;
            throw Raise.Make(ctx, PyExceptionTypes.AttributeError, "type object '" + Name + "' has no attribute '" + name + "'");
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return _identity; }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("<class '" + Name + "'>"); }
    }

    // A fixed-slot record instance (array-backed). Unhashable (default HashLeafCore throws). __repr__/__eq__
    // dispatch to a user override if present, else the auto behavior over slots.
    public sealed class RecordInstanceValue : ScriptValue
    {
        public readonly RecordClassValue Class;
        internal readonly ScriptValue[] Slots;   // null entry => the slot is unset
        internal RecordInstanceValue(RecordClassValue cls, ScriptValue[] slots) { Class = cls; Slots = slots; }

        public override ScriptTypeInfo TypeInfo { get { return Class.TypeInfo; } }
        internal override ValueKind Kind { get { return ValueKind.RecordInstance; } }

        internal ScriptValue GetAttribute(string name, EvalContext ctx)
        {
            int idx;
            if (Class.SlotIndex.TryGetValue(name, out idx))
            {
                ScriptValue v = Slots[idx];
                if (v == null)
                    throw NoAttr(name, ctx);
                return v;
            }
            FunctionValue m = Class.FindMethod(name);
            if (m != null)
                return new BoundUserMethodValue(this, m);
            ScriptValue ma;
            if (name == "__match_args__" && Class.TryMatchArgs(ctx, out ma))
                return ma;
            throw NoAttr(name, ctx);
        }

        // GetAttribute without the exception: for callers that probe several optional attributes on
        // an object, where a raised AttributeError per miss would cost more than the lookup itself.
        internal bool TryGetAttribute(string name, EvalContext ctx, out ScriptValue value)
        {
            int idx;
            if (Class.SlotIndex.TryGetValue(name, out idx))
            {
                value = Slots[idx];
                return value != null;
            }
            FunctionValue m = Class.FindMethod(name);
            if (m != null)
            {
                value = new BoundUserMethodValue(this, m);
                return true;
            }
            if (name == "__match_args__")
                return Class.TryMatchArgs(ctx, out value);
            value = null;
            return false;
        }

        internal void SetAttribute(string name, ScriptValue value, EvalContext ctx)
        {
            int idx;
            if (!Class.SlotIndex.TryGetValue(name, out idx))
                throw NoAttr(name, ctx);
            if (Class.Frozen)
                throw Raise.Make(ctx, PyExceptionTypes.AttributeError, "cannot assign to field '" + name + "'");
            Slots[idx] = value;
        }

        internal void InitSlot(int idx, ScriptValue value) { Slots[idx] = value; }   // bypasses frozen (generated __init__)

        private ScriptException NoAttr(string name, EvalContext ctx)
        {
            return Raise.Make(ctx, PyExceptionTypes.AttributeError, "'" + Class.Name + "' object has no attribute '" + name + "'");
        }

        // CPython's rules, in its order. An explicit __hash__ wins and survives @dataclass. Field equality
        // (@dataclass with eq=True) can only have a structural hash when the fields are frozen, which is
        // why CPython sets __hash__ to None for the rest: the key would be lost on the first assignment.
        // field(compare=False) drops a field from the hash too, unless field(hash=True) puts it back.
        // Everything else compares by identity, and the identity is the hash that agrees with it — unless
        // a written __eq__ took the hash away, which is the data model's rule for any class.
        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            FunctionValue h = Class.FindMethod("__hash__");
            if (h != null)
            {
                ScriptValue r = ctx.CallHook1(h, this);
                if (r.Kind != ValueKind.Int && r.Kind != ValueKind.Bool)
                    throw Raise.TypeError(ctx, "__hash__ method should return an integer");
                return PyOps.Hash(r, ctx, depth + 1);   // the engine's own int reduction, so hash(x) round-trips
            }
            if (Class.Dataclass != null && Class.Dataclass.Eq)
            {
                if (!Class.Frozen)
                    throw Raise.TypeError(ctx, "unhashable type: '" + Class.Name + "'");
                var items = new List<ScriptValue>(Slots.Length);
                for (int i = 0; i < Slots.Length; i++)
                {
                    if (Participates(i, FieldRole.Hash))
                        items.Add(Slots[i] ?? ctx.Values.None);
                }
                // A field that is itself a record re-enters the engine one level down, so a deep chain
                // is a RecursionError at MaxDataDepth and not a walk down the C# stack.
                return PyOps.Hash(ctx.Values.Tuple(items.ToArray()), ctx, depth);   // the tuple is the engine's own frame: its items sit one level down
            }
            if (Class.FindMethod("__eq__") != null)
                throw Raise.TypeError(ctx, "unhashable type: '" + Class.Name + "'");
            return ctx.Values.IdOf(this);
        }

        internal enum FieldRole { Compare, Hash, Repr }

        // Whether slot i takes part in equality / ordering, the frozen hash, or the generated repr.
        // Only @dataclass fields carry knobs; every other slot always participates.
        internal bool Participates(int i, FieldRole role)
        {
            DataclassSpec ds = Class.Dataclass;
            if (ds == null)
                return true;
            FieldDefault fd = ds.ForName(Class.AllSlots[i]);
            if (fd == null)
            {
                // Not in the spec: a plain self.x slot takes part; a ClassVar (excluded from the spec) does not.
                return Class.AnnotationDefaults == null
                    || !Class.AnnotationDefaults.TryGetValue(Class.AllSlots[i], out fd)
                    || fd.Kind == FieldKind.Field;
            }
            if (fd.Kind != FieldKind.Field)   // an InitVar is a parameter, not a field
                return false;
            switch (role)
            {
                case FieldRole.Hash: return fd.InHash;
                case FieldRole.Repr: return fd.Repr;
                default: return fd.Compare;
            }
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            RecordInstanceValue o = other as RecordInstanceValue;
            if (o == null)
                return false;
            FunctionValue eq = Class.FindMethod("__eq__");
            if (eq != null)
                return PyOps.Truth(ctx.CallHook(eq, new ScriptValue[] { this, other }, KwArgs.Empty), ctx);
            // Reflected: only the right side defines __eq__ (CPython would get NotImplemented from
            // the left and try it; when both define one, the left wins — no NotImplemented here).
            FunctionValue req = o.Class.FindMethod("__eq__");
            if (req != null)
                return PyOps.Truth(ctx.CallHook(req, new ScriptValue[] { o, this }, KwArgs.Empty), ctx);
            // Field equality is generated by @dataclass(eq=True) and by nothing else: a bare class
            // compares by identity, as it does in CPython.
            if (Class.Dataclass == null || !Class.Dataclass.Eq)
                return ReferenceEquals(this, other);
            if (!ReferenceEquals(o.Class, Class))
                return false;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Participates(i, FieldRole.Compare) && !PyOps.Equals(Slots[i], o.Slots[i], ctx, depth + 1))
                    return false;
            }
            return true;
        }

        // A user __eq__ is consulted against ANY right-hand type — the Equals engine calls this
        // cross-kind hook on both operands, which also gives the reflected direction (5 == rec).
        // The result coerces via Truth (the engine's bool domain). Without a user __eq__ a record
        // never equals a non-record — decline so the other operand's hook gets its turn.
        protected internal override bool TryEqualsAcrossKindCore(ScriptValue other, EvalContext ctx, int depth, out bool equal)
        {
            FunctionValue eq = Class.FindMethod("__eq__");
            if (eq != null)
            {
                equal = PyOps.Truth(ctx.CallHook(eq, new ScriptValue[] { this, other }, KwArgs.Empty), ctx);
                return true;
            }
            equal = false;
            return false;
        }

        // @dataclass(order=True): instances of the SAME class order by their field tuples (CPython parity).
        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            RecordInstanceValue o = other as RecordInstanceValue;
            if (o == null || !ReferenceEquals(o.Class, Class) || Class.Dataclass == null || !Class.Dataclass.Order)
            {
                cmp = 0;
                return false;
            }
            for (int i = 0; i < Slots.Length; i++)
            {
                if (!Participates(i, FieldRole.Compare))
                    continue;
                ScriptValue a = Slots[i] ?? ctx.Values.None, b = o.Slots[i] ?? ctx.Values.None;
                if (!PyOps.Equals(a, b, ctx, depth + 1))
                {
                    cmp = PyOps.RichCompare(PyCmpOp.Lt, a, b, ctx, depth + 1).IsTruthy(ctx) ? -1 : 1;
                    return true;
                }
            }
            cmp = 0;
            return true;
        }

        // --- user-defined protocol methods (extended 2026-09-11) ----
        // Every one of these dispatches through CallHook, which charges the budget and probes the stack,
        // so script code reached from inside an engine walk is bounded like any other call.

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            FunctionValue f = Class.FindMethod("__str__");
            if (f == null)
            {
                ReprLeafCore(sb, ctx, depth);   // CPython's default __str__ is __repr__
                return;
            }
            ScriptValue r = ctx.CallHook1(f, this);
            StrValue s = r as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "__str__ returned non-string (type " + r.PyTypeName + ")");
            sb.Append(s.Value);
        }

        protected internal override bool IsTruthyCore(EvalContext ctx)
        {
            FunctionValue f = Class.FindMethod("__bool__");
            if (f != null)
            {
                ScriptValue r = ctx.CallHook1(f, this);
                if (r.Kind != ValueKind.Bool)
                    throw Raise.TypeError(ctx, "__bool__ should return bool, returned " + r.PyTypeName);
                return ((BoolValue)r).Value;
            }
            long n;
            return !TryLengthWithContextCore(ctx, out n) || n != 0;   // else __len__ != 0, else always true
        }

        protected internal override bool TryLengthWithContextCore(EvalContext ctx, out long length)
        {
            length = 0;
            FunctionValue f = Class.FindMethod("__len__");
            if (f == null)
                return false;
            ScriptValue r = ctx.CallHook1(f, this);
            if (r.Kind != ValueKind.Int && r.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "'" + r.PyTypeName + "' object cannot be interpreted as an integer");
            System.Numerics.BigInteger n = NumericOps.AsBigInteger(r);
            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "__len__() should return >= 0");
            if (n > long.MaxValue)
                throw Raise.Overflow(ctx, "cannot fit 'int' into an index-sized integer");
            length = (long)n;
            return true;
        }

        internal override bool IsCallable { get { return Class.FindMethod("__call__") != null; } }

        protected internal override ScriptValue CallCore(ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            FunctionValue f = Class.FindMethod("__call__");
            if (f == null)
                return null;
            var a = new ScriptValue[args.Length + 1];
            a[0] = this;
            Array.Copy(args, 0, a, 1, args.Length);
            return ctx.CallHook(f, a, kw);
        }

        // Declining (false) lets PyOps.Contains fall back to iteration, which is CPython's order too.
        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            found = false;
            FunctionValue f = Class.FindMethod("__contains__");
            if (f == null)
                return false;
            found = PyOps.Truth(ctx.CallHook(f, new ScriptValue[] { this, item }, KwArgs.Empty), ctx);
            return true;
        }

        protected internal override ScriptValue GetItemCore(ScriptValue index, EvalContext ctx)
        {
            FunctionValue f = Class.FindMethod("__getitem__");
            return f == null ? null : ctx.CallHook(f, new ScriptValue[] { this, index }, KwArgs.Empty);
        }

        protected internal override bool TrySetItemCore(ScriptValue index, ScriptValue value, EvalContext ctx)
        {
            FunctionValue f = Class.FindMethod("__setitem__");
            if (f == null)
                return false;
            ctx.CallHook(f, new ScriptValue[] { this, index, value }, KwArgs.Empty);
            return true;
        }

        protected internal override bool TryDelItemCore(ScriptValue index, EvalContext ctx)
        {
            FunctionValue f = Class.FindMethod("__delitem__");
            if (f == null)
                return false;
            ctx.CallHook(f, new ScriptValue[] { this, index }, KwArgs.Empty);
            return true;
        }

        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx)
        {
            FunctionValue f = Class.FindMethod("__iter__");
            if (f == null)
            {
                // the legacy sequence protocol: only __getitem__, indexed from 0 until IndexError
                FunctionValue get = Class.FindMethod("__getitem__");
                return get == null ? null : new SeqProtocolIterator(this, get);
            }
            return AsIterator(ctx, ctx.CallHook1(f, this));
        }

        protected internal override IScriptIterator GetReverseIteratorCore(EvalContext ctx)
        {
            FunctionValue f = Class.FindMethod("__reversed__");
            return f == null ? null : AsIterator(ctx, ctx.CallHook1(f, this));
        }

        // What __iter__/__reversed__ returned must BE an iterator, as in CPython: a record carrying
        // __next__, or one of the engine's own. Demanding it here also means a record can never be
        // asked for its iterator again, so a pair of classes returning each other cannot recurse.
        private static IScriptIterator AsIterator(EvalContext ctx, ScriptValue r)
        {
            RecordInstanceValue ri = r as RecordInstanceValue;
            if (ri != null)
            {
                FunctionValue next = ri.Class.FindMethod("__next__");
                if (next != null)
                    return new RecordIterator(ri, next);
            }
            else if (r.Kind == ValueKind.Iterator)
            {
                return r.GetIteratorCore(ctx);
            }
            throw Raise.TypeError(ctx, "iter() returned non-iterator of type '" + r.PyTypeName + "'");
        }

        // __next__ drives this until it raises StopIteration. ScriptIteratorBase charges the step and
        // probes the stack per element.
        private sealed class RecordIterator : ScriptIteratorBase
        {
            private readonly RecordInstanceValue _self;
            private readonly FunctionValue _next;
            private bool _done;

            internal RecordIterator(RecordInstanceValue self, FunctionValue next) { _self = self; _next = next; }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                value = null;
                if (_done)
                    return false;
                try
                {
                    value = ctx.CallHook1(_next, _self);
                    return true;
                }
                catch (ScriptException ex) when (ex.Value.ExcType.IsSubtypeOf(PyExceptionTypes.StopIteration))
                {
                    _done = true;
                    return false;
                }
            }
        }

        private sealed class SeqProtocolIterator : ScriptIteratorBase
        {
            private readonly RecordInstanceValue _self;
            private readonly FunctionValue _get;
            private long _i;
            private bool _done;

            internal SeqProtocolIterator(RecordInstanceValue self, FunctionValue get) { _self = self; _get = get; }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                value = null;
                if (_done)
                    return false;
                try
                {
                    value = ctx.CallHook(_get, new ScriptValue[] { _self, ctx.Values.Int(_i++) }, KwArgs.Empty);
                    return true;
                }
                catch (ScriptException ex) when (ex.Value.ExcType.IsSubtypeOf(PyExceptionTypes.IndexError))
                {
                    _done = true;
                    return false;
                }
            }
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            FunctionValue rep = Class.FindMethod("__repr__");
            if (rep != null)
            {
                ScriptValue r = ctx.CallHook1(rep, this);
                StrValue sr = r as StrValue;
                if (sr == null)
                    throw Raise.TypeError(ctx, "__repr__ returned non-string (type " + r.PyTypeName + ")");
                sb.Append(sr.Value);
                return;
            }
            if (Class.Dataclass != null && !Class.Dataclass.Repr)
            {
                sb.Append("<" + Class.Name + " object>");
                return;
            }
            sb.Append(Class.Name);
            sb.Append('(');
            bool first = true;
            for (int i = 0; i < Class.AllSlots.Count; i++)
            {
                if (!Participates(i, FieldRole.Repr))
                    continue;   // field(repr=False)
                if (!first)
                    sb.Append(", ");
                first = false;
                sb.Append(Class.AllSlots[i]);
                sb.Append('=');
                sb.Append(Slots[i] == null ? "<unset>" : Slots[i].Repr(ctx, depth + 1));
            }
            sb.Append(')');
        }
    }

    // A record method bound to its instance; calling it prepends self (handled by the Call machinery).
    public sealed class BoundUserMethodValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("method", null);
        public readonly ScriptValue Self;
        public readonly FunctionValue Func;
        internal BoundUserMethodValue(ScriptValue self, FunctionValue func) { Self = self; Func = func; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.BoundMethod; } }
        internal override bool IsCallable { get { return true; } }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("<bound method " + Func.Name + " of " + Self.PyTypeName + " object>");
        }
    }

    // The default of a class-body field: a plain value, a default_factory callable, or none.
    // What a class-body annotation declares: a dataclass field, a ClassVar (a class attribute the
    // machinery leaves alone) or an InitVar (an __init__ parameter that goes to __post_init__, never stored).
    internal enum FieldKind { Field, ClassVar, InitVar }

    internal sealed class FieldDefault
    {
        public readonly FieldKind Kind;
        public readonly bool HasDefault;
        public readonly ScriptValue Default;         // plain value (HasDefault && DefaultFactory == null)
        public readonly ScriptValue DefaultFactory;  // callable, invoked per construction; null if none
        public readonly string TypeName;             // plain-name annotation text (dataclasses Field.type), or null
        public readonly bool Init;                   // field(init=False) => never an __init__ parameter
        public readonly bool Repr;                   // field(repr=False) => hidden from the generated repr
        public readonly bool Compare;                // field(compare=False) => out of __eq__ and ordering
        public readonly bool KwOnly;                 // field(kw_only=True) or @dataclass(kw_only=True)
        public readonly bool? Hash;                  // field(hash=); null => follow Compare, as in CPython
        public readonly ScriptValue Metadata;        // field(metadata=...), or null

        internal FieldDefault(bool hasDefault, ScriptValue def, ScriptValue factory, string typeName)
            : this(hasDefault, def, factory, typeName, true, true, true, false, null, null)
        {
        }

        internal FieldDefault(bool hasDefault, ScriptValue def, ScriptValue factory, string typeName,
            bool init, bool repr, bool compare, bool kwOnly, bool? hash, ScriptValue metadata)
            : this(hasDefault, def, factory, typeName, init, repr, compare, kwOnly, hash, metadata, FieldKind.Field)
        {
        }

        internal FieldDefault(bool hasDefault, ScriptValue def, ScriptValue factory, string typeName,
            bool init, bool repr, bool compare, bool kwOnly, bool? hash, ScriptValue metadata, FieldKind kind)
        {
            HasDefault = hasDefault; Default = def; DefaultFactory = factory; TypeName = typeName;
            Init = init; Repr = repr; Compare = compare; KwOnly = kwOnly; Hash = hash; Metadata = metadata;
            Kind = kind;
        }

        // CPython: hash=None (the default) means "whatever compare says".
        public bool InHash { get { return Hash ?? Compare; } }

        // @dataclass(kw_only=True) turns every field keyword-only; the per-field flag already won.
        internal FieldDefault AsKwOnly()
        {
            return KwOnly
                ? this
                : new FieldDefault(HasDefault, Default, DefaultFactory, TypeName, Init, Repr, Compare, true, Hash, Metadata, Kind);
        }

        internal FieldDefault WithKind(FieldKind kind)
        {
            return kind == Kind
                ? this
                : new FieldDefault(HasDefault, Default, DefaultFactory, TypeName, Init, Repr, Compare, KwOnly, Hash, Metadata, kind);
        }

        internal static readonly FieldDefault None = new FieldDefault(false, null, null, null);
    }

    // What @dataclass records so construction can synthesize __init__.
    internal sealed class DataclassSpec
    {
        public readonly string[] FieldNames;     // init params, in order
        public readonly FieldDefault[] Fields;    // parallel to FieldNames
        public readonly bool Init;
        public readonly bool Eq;      // false => identity equality (no generated __eq__)
        public readonly bool Order;   // true => <, <=, >, >= compare the field tuples
        public readonly bool Repr;    // false => "<Name object>"
        internal DataclassSpec(string[] names, FieldDefault[] fields, bool init, bool eq, bool order, bool repr)
        {
            FieldNames = names; Fields = fields; Init = init; Eq = eq; Order = order; Repr = repr;
        }

        // The per-field knobs by slot name; null when the slot is not a dataclass field (a plain
        // self.x = ... assignment in a hand-written __init__ never carries field() options).
        internal FieldDefault ForName(string name)
        {
            for (int i = 0; i < FieldNames.Length; i++)
            {
                if (string.Equals(FieldNames[i], name, StringComparison.Ordinal))
                    return Fields[i];
            }
            return null;
        }
    }

    // dataclasses field(default=, default_factory=, init=, repr=, compare=, kw_only=, hash=, metadata=)
    // marker (opaque), consumed at class creation and folded into a FieldDefault.
    internal sealed class FieldSpecValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("Field", null);
        public readonly bool HasDefault;
        public readonly ScriptValue Default;
        public readonly ScriptValue DefaultFactory;
        public readonly bool Init = true;
        public readonly bool InRepr = true;   // named InRepr: "Repr" would hide ScriptValue.Repr(ctx)
        public readonly bool Compare = true;
        public readonly bool KwOnly;
        public readonly bool? Hash;
        public readonly ScriptValue Metadata;

        internal FieldSpecValue(bool hasDefault, ScriptValue def, ScriptValue factory,
            bool init, bool repr, bool compare, bool kwOnly, bool? hash, ScriptValue metadata)
        {
            HasDefault = hasDefault; Default = def; DefaultFactory = factory;
            Init = init; InRepr = repr; Compare = compare; KwOnly = kwOnly; Hash = hash; Metadata = metadata;
        }

        internal FieldSpecValue(bool hasDefault, ScriptValue def, ScriptValue factory)
        {
            HasDefault = hasDefault; Default = def; DefaultFactory = factory;
        }
        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }
    }
}
