using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // Shared, immutable metadata for one namedtuple type. All instances of a given
    // namedtuple point at the same NamedTupleInfo; the instance slot table lives in InstanceType.
    internal sealed class NamedTupleInfo
    {
        public readonly string TypeName;
        public readonly string[] Fields;
        public readonly Dictionary<string, int> FieldIndex;   // Ordinal
        public readonly ScriptTypeInfo InstanceType;
        public readonly ScriptValue[] Defaults;   // per field, null => required (defaults= fills from the right)

        internal NamedTupleInfo(string typeName, string[] fields, ScriptTypeInfo instanceType, ScriptValue[] defaults = null)
        {
            TypeName = typeName;
            Fields = fields;
            InstanceType = instanceType;
            Defaults = defaults ?? new ScriptValue[fields.Length];
            FieldIndex = new Dictionary<string, int>(System.StringComparer.Ordinal);
            for (int i = 0; i < fields.Length; i++)
                FieldIndex[fields[i]] = i;
        }
    }

    // A namedtuple instance: an immutable TupleValue (Kind==Tuple, so == plain tuple, indexing, hashing all
    // work) plus by-name field access and _make/_replace/_asdict/_fields via its InstanceType slots.
    internal sealed class NamedTupleValue : TupleValue
    {
        internal readonly NamedTupleInfo NtInfo;

        internal NamedTupleValue(NamedTupleInfo info, ScriptValue[] items) : base(items) { NtInfo = info; }

        public override ScriptTypeInfo TypeInfo { get { return NtInfo.InstanceType; } }

        // Build the per-type instance slot table: base tuple methods (count/index) + one property per field
        // + _replace/_asdict/_fields. Delegates read NtInfo from the instance, so no cycle with NamedTupleInfo.
        // extra: further slots for an engine-defined namedtuple (urllib.parse's results carry hostname/port/geturl).
        internal static ScriptTypeInfo BuildInstanceType(string typeName, string[] fields, IDictionary<string, SlotDescriptor> extra = null)
        {
            IDictionary<string, SlotDescriptor> slots = ListMethods.BuildTupleSlots();
            for (int i = 0; i < fields.Length; i++)
            {
                int idx = i;
                slots[fields[i]] = SlotDescriptor.MakeProperty(fields[i], (self, ctx) => ((NamedTupleValue)self).Items[idx]);
            }
            slots["_replace"] = SlotDescriptor.MakeMethod("_replace", (self, a, kw, ctx) => Replace((NamedTupleValue)self, a, kw, ctx));
            slots["_asdict"] = SlotDescriptor.MakeMethod("_asdict", (self, a, kw, ctx) => AsDict((NamedTupleValue)self, ctx));
            slots["_fields"] = SlotDescriptor.MakeProperty("_fields", (self, ctx) => FieldsTuple(((NamedTupleValue)self).NtInfo, ctx));
            if (extra != null)
                foreach (KeyValuePair<string, SlotDescriptor> kv in extra)
                    slots[kv.Key] = kv.Value;
            return new ScriptTypeInfo(typeName, slots, TupleType);
        }

        // The type constructor: Point(1, 2) / Point(x=1, y=2).
        internal static NamedTupleValue Construct(NamedTupleInfo info, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            string[] fields = info.Fields;
            var vals = new ScriptValue[fields.Length];
            var filled = new bool[fields.Length];
            if (args.Length > fields.Length)
                throw Raise.TypeError(ctx, "__new__() takes " + (fields.Length + 1)
                    + " positional arguments but " + (args.Length + 1) + " were given");
            for (int i = 0; i < args.Length; i++)
            {
                vals[i] = args[i];
                filled[i] = true;
            }
            for (int j = 0; j < kw.Count; j++)
            {
                string name = kw.NameAt(j);
                int idx;
                if (!info.FieldIndex.TryGetValue(name, out idx))
                    throw Modules.Support.KwReader.UnexpectedError(ctx, "__new__", name);
                if (filled[idx])
                    throw Raise.TypeError(ctx, "__new__() got multiple values for argument '" + name + "'");
                vals[idx] = kw.ValueAt(j);
                filled[idx] = true;
            }
            var missing = new List<string>();
            for (int i = 0; i < fields.Length; i++)
            {
                if (filled[i])
                    continue;
                if (info.Defaults[i] != null)
                {
                    vals[i] = info.Defaults[i];
                    filled[i] = true;
                }
                else
                    missing.Add(fields[i]);
            }
            if (missing.Count > 0)
                throw Raise.TypeError(ctx, "__new__() missing " + missing.Count + " required positional argument"
                    + (missing.Count > 1 ? "s" : "") + ": " + NameList(missing));
            ctx.Values.PreCharge(24 + 8L * fields.Length);
            return new NamedTupleValue(info, vals);
        }

        // Type._make(iterable).
        internal static NamedTupleValue Make(NamedTupleInfo info, ScriptValue iterable, EvalContext ctx)
        {
            IScriptIterator it = PyOps.GetIterator(iterable, ctx);
            var vals = new List<ScriptValue>();
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                vals.Add(v);
            if (vals.Count != info.Fields.Length)
                throw Raise.TypeError(ctx, "Expected " + info.Fields.Length + " arguments, got " + vals.Count);
            return new NamedTupleValue(info, vals.ToArray());
        }

        internal static ScriptValue FieldsTuple(NamedTupleInfo info, EvalContext ctx)
        {
            var arr = new ScriptValue[info.Fields.Length];
            for (int i = 0; i < info.Fields.Length; i++)
                arr[i] = ctx.Values.Str(info.Fields[i]);
            return ctx.Values.Tuple(arr);
        }

        private static ScriptValue Replace(NamedTupleValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length > 0)
                throw Raise.TypeError(ctx, "_replace() takes no positional arguments");
            NamedTupleInfo info = self.NtInfo;
            var vals = (ScriptValue[])self.Items.Clone();
            var unexpected = new List<string>();
            for (int j = 0; j < kw.Count; j++)
            {
                string name = kw.NameAt(j);
                int idx;
                if (info.FieldIndex.TryGetValue(name, out idx))
                    vals[idx] = kw.ValueAt(j);
                else
                    unexpected.Add(name);
            }
            if (unexpected.Count > 0)
                throw Raise.ValueError(ctx, "Got unexpected field names: " + ReprNameList(unexpected, ctx));
            return new NamedTupleValue(info, vals);
        }

        private static ScriptValue AsDict(NamedTupleValue self, EvalContext ctx)
        {
            NamedTupleInfo info = self.NtInfo;
            DictValue d = ctx.Values.Dict(info.Fields.Length);   // plain dict, 3.8+
            for (int i = 0; i < info.Fields.Length; i++)
                d.SetItem(ctx.Values.Str(info.Fields[i]), self.Items[i], ctx);
            return d;
        }

        private static string NameList(List<string> names)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append('\'').Append(names[i]).Append('\'');
            }
            return sb.ToString();
        }

        // A Python list-of-strings repr: ['a', 'b'].
        private static string ReprNameList(List<string> names, EvalContext ctx)
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(ctx.Values.Str(names[i]).Repr(ctx));
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
