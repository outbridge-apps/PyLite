using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    internal enum EnumFlavor { Plain, Int, Str }

    // An enumeration (enum.Enum/IntEnum/StrEnum and the classes derived from them): the members in
    // definition order, every name (aliases included) and a value index. A class with members is final.
    // An IntEnum/StrEnum member is a real int/str object, so everything that takes an int or a str takes
    // it; the context's EnumValues map is what knows it is also a member (repr, .name, type()).
    public sealed class EnumClassValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("EnumType", null);

        internal readonly string Name;
        internal readonly EnumFlavor Flavor;
        internal readonly bool MixedIn;                                // class X(str, Enum): str() is 'X.NAME', not the value
        internal readonly EnumClassValue Base;                         // null for enum.Enum itself
        internal readonly Dictionary<string, FunctionValue> Methods;   // own
        internal readonly List<EnumMemberValue> Members = new List<EnumMemberValue>();   // canonical, in order
        internal readonly Dictionary<string, EnumMemberValue> ByName = new Dictionary<string, EnumMemberValue>(StringComparer.Ordinal);
        internal readonly List<string> NameOrder = new List<string>();   // definition order of every name
        internal readonly Dictionary<string, ScriptValue> Extras = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
        internal readonly ScriptTypeInfo MemberType;
        private readonly DictValue _byValue;                           // hashable value -> member
        private List<EnumMemberValue> _unhashable;                     // members whose value has no hash
        private readonly long _identity;

        internal EnumClassValue(string name, EnumFlavor flavor, EnumClassValue baseCls, Dictionary<string, FunctionValue> methods,
            EvalContext ctx, bool mixedIn = false)
        {
            ctx.Values.PreCharge(160);
            Name = name;
            Flavor = flavor;
            MixedIn = mixedIn;
            Base = baseCls;
            Methods = methods ?? new Dictionary<string, FunctionValue>(StringComparer.Ordinal);
            MemberType = new ScriptTypeInfo(name, null);
            _byValue = ctx.Values.Dict(4);
            _identity = SharedIdentitySource.Next();
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }
        internal override bool IsCallable { get { return true; } }

        internal bool IsSubclassOf(EnumClassValue other)
        {
            for (EnumClassValue c = this; c != null; c = c.Base)
                if (ReferenceEquals(c, other))
                    return true;
            return false;
        }

        internal FunctionValue FindMethod(string name)
        {
            for (EnumClassValue c = this; c != null; c = c.Base)
            {
                FunctionValue f;
                if (c.Methods.TryGetValue(name, out f))
                    return f;
            }
            return null;
        }

        // The member whose script-visible object is v (a plain member, or a registered int/str), or null.
        internal static EnumMemberValue MemberOf(ScriptValue v, EvalContext ctx)
        {
            EnumMemberValue m = v as EnumMemberValue;
            if (m != null)
                return m;
            if (ctx.EnumValues == null || (v.Kind != ValueKind.Int && v.Kind != ValueKind.Str))
                return null;
            ctx.EnumValues.TryGetValue(v, out m);
            return m;
        }

        // A member or an alias, in definition order. A value equal to an earlier member's makes an alias.
        internal void AddMember(string name, ScriptValue raw, EvalContext ctx)
        {
            EnumMemberValue prior;
            ScriptValue priorExtra;
            if (ByName.TryGetValue(name, out prior) || Extras.TryGetValue(name, out priorExtra))
                throw Raise.TypeError(ctx, "'" + name + "' already defined as " + (prior != null ? prior.Value : Extras[name]).Repr(ctx));
            if (NameOrder.Count >= ctx.Limits.MaxCollectionItems)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxCollectionItems", ctx.Limits.MaxCollectionItems, NameOrder.Count + 1L);
            ctx.Values.PreCharge(96);
            ScriptValue value = raw is AutoValue ? NextAutoValue(name, ctx) : Coerce(raw, ctx);
            EnumMemberValue existing = FindByValue(value, ctx);
            NameOrder.Add(name);
            if (existing != null)
            {
                ByName[name] = existing;   // an alias: the same member under a second name
                return;
            }
            var m = new EnumMemberValue(this, name, value);
            if (Flavor == EnumFlavor.Int)
            {
                IntValue iv = (IntValue)value;
                m.Self = iv.IsSmall ? new IntValue(iv.Small) : new IntValue(iv.Value);   // never a shared cached int
                Register(m, ctx);
            }
            else if (Flavor == EnumFlavor.Str)
            {
                ctx.Values.PreCharge(24 + 2L * ((StrValue)value).Value.Length);
                m.Self = new StrValue(((StrValue)value).Value);   // never the shared empty string
                Register(m, ctx);
            }
            Members.Add(m);
            ByName[name] = m;
            long h;
            if (TryHash(value, ctx, out h))
                _byValue.SetItem(value, m, ctx);
            else
                (_unhashable ?? (_unhashable = new List<EnumMemberValue>())).Add(m);
        }

        private static void Register(EnumMemberValue m, EvalContext ctx)
        {
            if (ctx.EnumValues == null)
                ctx.EnumValues = new Dictionary<object, EnumMemberValue>(Syntax.ReferenceEqualityComparer.Instance);
            ctx.EnumValues[m.Self] = m;
        }

        // IntEnum takes int(value) and StrEnum only a str, as CPython's do.
        private ScriptValue Coerce(ScriptValue raw, EvalContext ctx)
        {
            if (Flavor == EnumFlavor.Int)
            {
                switch (raw.Kind)
                {
                    case ValueKind.Int: return ctx.Values.Int(((IntValue)raw).Value);
                    case ValueKind.Bool: return ctx.Values.Int(((BoolValue)raw).Value ? 1 : 0);
                    case ValueKind.Float:
                    {
                        double d = ((FloatValue)raw).Value;
                        if (double.IsNaN(d) || double.IsInfinity(d))
                            throw Raise.ValueError(ctx, "cannot convert float " + raw.Repr(ctx) + " to integer");
                        return ctx.Values.Int(new BigInteger(Math.Truncate(d)));
                    }
                    case ValueKind.Str:
                    {
                        BigInteger b;
                        string s = ((StrValue)raw).Value.Trim();
                        if (BigInteger.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out b))
                            return ctx.Values.Int(b);
                        throw Raise.ValueError(ctx, "invalid literal for int() with base 10: " + raw.Repr(ctx));
                    }
                    default:
                        throw Raise.TypeError(ctx, "int() argument must be a string, a bytes-like object or a real number, not '" + raw.PyTypeName + "'");
                }
            }
            if (Flavor != EnumFlavor.Str)
                return raw;
            if (raw.Kind != ValueKind.Str)
                throw Raise.TypeError(ctx, raw.Repr(ctx) + " is not a string");
            return MemberOf(raw, ctx) != null ? ctx.Values.Str(((StrValue)raw).Value) : raw;   // .value is a plain str
        }

        // enum.auto(): the name in lower case for a StrEnum, else one more than the last int value, from 1.
        private ScriptValue NextAutoValue(string name, EvalContext ctx)
        {
            if (Flavor == EnumFlavor.Str)
                return ctx.Values.Str(name.ToLowerInvariant());
            for (int i = NameOrder.Count - 1; i >= 0; i--)
            {
                ScriptValue v = ByName[NameOrder[i]].Value;
                if (v.Kind == ValueKind.Int)
                    return ctx.Values.Int(((IntValue)v).Value + 1);
            }
            return ctx.Values.Int(1);
        }

        private static bool TryHash(ScriptValue v, EvalContext ctx, out long h)
        {
            try
            {
                h = PyOps.Hash(v, ctx, 0);
                return true;
            }
            catch (ScriptException ex) when (ex.Value.ExcType.IsSubtypeOf(PyExceptionTypes.TypeError))
            {
                h = 0;
                return false;
            }
        }

        internal EnumMemberValue FindByValue(ScriptValue value, EvalContext ctx)
        {
            long h;
            if (TryHash(value, ctx, out h))
            {
                ScriptValue m;
                if (_byValue.TryGetItem(value, ctx, out m))
                    return (EnumMemberValue)m;
            }
            if (_unhashable != null)
            {
                foreach (EnumMemberValue u in _unhashable)
                    if (PyOps.Equals(u.Value, value, ctx, 0))
                        return u;
            }
            return null;
        }

        // Color(1), Color(Color.RED); the functional API on a class without members.
        protected internal override ScriptValue CallCore(ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (Members.Count == 0 && (args.Length >= 2 || kw.Count > 0))
                return EnumFunctional.Create(this, args, kw, ctx);
            if (args.Length != 1 || kw.Count > 0)
                throw Raise.TypeError(ctx, "EnumType.__call__() takes 1 positional argument for a lookup by value (" + args.Length + " given)");
            EnumMemberValue em = MemberOf(args[0], ctx);
            if (em != null && em.Class.IsSubclassOf(this))
                return em.Self;
            EnumMemberValue found = FindByValue(args[0], ctx);
            if (found != null)
                return found.Self;
            throw Raise.ValueError(ctx, args[0].Repr(ctx) + " is not a valid " + Name);
        }

        // Color['RED'], aliases included.
        protected internal override ScriptValue GetItemCore(ScriptValue index, EvalContext ctx)
        {
            StrValue s = index as StrValue;
            EnumMemberValue m;
            if (s != null && ByName.TryGetValue(s.Value, out m))
                return m.Self;
            throw Raise.KeyError(ctx, index);
        }

        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx)
        {
            var items = new List<ScriptValue>(Members.Count);
            foreach (EnumMemberValue m in Members)
                items.Add(m.Self);
            return new ListIterator(items);
        }

        protected internal override bool TryLengthCore(out long length) { length = Members.Count; return true; }

        // A member of this enumeration, or (3.12) a value one of them has.
        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            EnumMemberValue em = MemberOf(item, ctx);
            found = em != null && em.Class.IsSubclassOf(this) || em == null && FindByValue(item, ctx) != null;
            return true;
        }

        internal ScriptValue GetAttribute(string name, EvalContext ctx)
        {
            EnumMemberValue m;
            if (ByName.TryGetValue(name, out m))
                return m.Self;
            ScriptValue extra;
            for (EnumClassValue c = this; c != null; c = c.Base)
            {
                if (c.Extras.TryGetValue(name, out extra))
                    return extra;
            }
            FunctionValue f = FindMethod(name);
            if (f != null)
                return f;   // Color.describe(Color.RED)
            switch (name)
            {
                case "__name__":
                case "__qualname__":
                    return ctx.Values.Str(Name);
                case "__members__":
                {
                    DictValue d = ctx.Values.Dict(NameOrder.Count);
                    foreach (string n in NameOrder)
                        d.SetItem(ctx.Values.Str(n), ByName[n].Self, ctx);
                    return d;
                }
            }
            throw Raise.Make(ctx, PyExceptionTypes.AttributeError, "type object '" + Name + "' has no attribute '" + name + "'");
        }

        protected internal override bool TrySetAttrCore(string name, ScriptValue value, EvalContext ctx)
        {
            if (ByName.ContainsKey(name))
                throw Raise.Make(ctx, PyExceptionTypes.AttributeError, "cannot reassign member '" + name + "'");
            if (name.StartsWith("__", StringComparison.Ordinal) && name.EndsWith("__", StringComparison.Ordinal))
                return false;
            ctx.Values.PreCharge(48);
            Extras[name] = value;
            return true;
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return _identity; }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("<enum '" + Name + "'>"); }
    }

    // One member: its class, name and plain value. For a plain Enum this object is the member scripts see;
    // for an IntEnum/StrEnum, Self is the int/str object that stands for it.
    public sealed class EnumMemberValue : ScriptValue
    {
        internal readonly EnumClassValue Class;
        internal readonly string Name;
        internal readonly ScriptValue Value;   // .value: a plain object (an int, not the member, for an IntEnum)
        internal ScriptValue Self;
        private long _hash;
        private bool _hashed;

        internal EnumMemberValue(EnumClassValue cls, string name, ScriptValue value)
        {
            Class = cls;
            Name = name;
            Value = value;
            Self = this;
        }

        public override ScriptTypeInfo TypeInfo { get { return Class.MemberType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        internal ScriptValue GetAttribute(string name, EvalContext ctx)
        {
            switch (name)
            {
                case "name":
                case "_name_":
                    return ctx.Values.Str(Name);
                case "value":
                case "_value_":
                    return Value;
                case "__class__":
                    return Class;
            }
            FunctionValue f = Class.FindMethod(name);
            if (f != null)
                return new BoundUserMethodValue(Self, f);
            for (EnumClassValue c = Class; c != null; c = c.Base)
            {
                ScriptValue extra;
                if (c.Extras.TryGetValue(name, out extra))
                {
                    FunctionValue ef = extra as FunctionValue;
                    return ef != null ? new BoundUserMethodValue(Self, ef) : extra;
                }
            }
            throw Raise.Make(ctx, PyExceptionTypes.AttributeError, "'" + Class.Name + "' object has no attribute '" + name + "'");
        }

        // <Color.RED: 1>, or what a __repr__ the class wrote returns.
        internal void AppendRepr(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            FunctionValue rep = Class.FindMethod("__repr__");
            if (rep != null)
            {
                sb.Append(CallText(rep, "__repr__", ctx));
                return;
            }
            sb.Append("<" + Class.Name + "." + Name + ": ");
            sb.Append(Value.Repr(ctx, depth + 1));
            sb.Append('>');
        }

        // str(): Color.RED for a plain Enum, the value for an IntEnum/StrEnum, or a written __str__.
        internal string StrText(EvalContext ctx)
        {
            FunctionValue s = Class.FindMethod("__str__");
            if (s != null)
                return CallText(s, "__str__", ctx);
            return Class.Name + "." + Name;
        }

        // For an IntEnum/StrEnum member: whether str() is something other than the plain value.
        internal bool HasOwnStr { get { return Class.MixedIn || Class.FindMethod("__str__") != null; } }

        private string CallText(FunctionValue f, string which, EvalContext ctx)
        {
            ScriptValue r = ctx.CallHook1(f, Self);
            StrValue sr = r as StrValue;
            if (sr == null)
                throw Raise.TypeError(ctx, which + " returned non-string (type " + r.PyTypeName + ")");
            return sr.Value;
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { AppendRepr(sb, ctx, depth); }
        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append(StrText(ctx)); }

        protected internal override ScriptValue FormatSpecCore(string spec, EvalContext ctx)
        {
            return ctx.Format.FormatValue(ctx, ctx.Values.Str(StrText(ctx)), spec);   // 3.12: format() is str()'s
        }

        // CPython hashes a member by its name.
        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            if (!_hashed)
            {
                _hash = PyOps.Hash(ctx.Values.Str(Name), ctx, depth + 1);
                _hashed = true;
            }
            return _hash;
        }
    }

    // enum.auto(): replaced by a generated value when the class is created.
    internal sealed class AutoValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("auto", null);
        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("auto(_auto_null)"); }
    }

    // Enum('Color', 'RED GREEN'), Enum('Color', ['RED', 'GREEN']), Enum('Color', [('RED', 1), ...]),
    // Enum('Color', {'RED': 1, ...}).
    internal static class EnumFunctional
    {
        internal static ScriptValue Create(EnumClassValue baseCls, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ScriptValue nameArg = args.Length > 0 ? args[0] : null;
            ScriptValue names = args.Length > 1 ? args[1] : null;
            for (int i = 0; i < kw.Count; i++)
            {
                string k = kw.NameAt(i);
                if (k == "value" && nameArg == null)
                    nameArg = kw.ValueAt(i);
                else if (k == "names" && names == null)
                    names = kw.ValueAt(i);
                else
                    throw Raise.TypeError(ctx, "Enum() got an unexpected keyword argument '" + k + "'");
            }
            StrValue clsName = nameArg as StrValue;
            if (clsName == null || names == null)
                throw Raise.TypeError(ctx, "Enum() takes a class name (str) and the member names");
            var cls = new EnumClassValue(clsName.Value, baseCls.Flavor, baseCls, null, ctx, baseCls.MixedIn);
            StrValue s = names as StrValue;
            if (s != null)
            {
                foreach (string n in s.Value.Replace(',', ' ').Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    cls.AddMember(n, new AutoValue(), ctx);
                return cls;
            }
            DictValue d = names as DictValue;
            IScriptIterator it = PyOps.GetIterator(d != null ? (ScriptValue)d.ItemsView(ctx) : names, ctx);
            ScriptValue item;
            while (it.MoveNext(ctx, out item))
            {
                StrValue one = item as StrValue;
                if (one != null)
                {
                    cls.AddMember(one.Value, new AutoValue(), ctx);
                    continue;
                }
                TupleValue pair = item as TupleValue;
                ListValue lpair = item as ListValue;
                ScriptValue[] parts = pair != null ? pair.Items : lpair != null ? lpair.Items.ToArray() : null;
                StrValue pn = parts != null && parts.Length == 2 ? parts[0] as StrValue : null;
                if (pn == null)
                    throw Raise.TypeError(ctx, "Enum() names must be strings or (name, value) pairs");
                cls.AddMember(pn.Value, parts[1], ctx);
            }
            return cls;
        }
    }
}
