using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // time.struct_time. A 9-element immutable "named tuple": indexing/slicing/iteration/
    // unpacking/in behave as a tuple, and it compares/​hashes equal to a plain 9-tuple (CPython structseq is a
    // tuple subclass — see PyOps tuple-like handling). Named slots tm_year..tm_isdst read the same items.
    internal sealed class StructTimeValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        internal readonly ScriptValue[] Items;   // exactly 9 IntValues

        private StructTimeValue(ScriptValue[] items) { Items = items; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        internal static StructTimeValue Create(EvalContext ctx, int y, int mon, int mday, int h, int mi, int s, int wday, int yday, int isdst)
        {
            ctx.Values.PreCharge(160);
            var items = new ScriptValue[9]
            {
                ctx.Values.Int(y), ctx.Values.Int(mon), ctx.Values.Int(mday),
                ctx.Values.Int(h), ctx.Values.Int(mi), ctx.Values.Int(s),
                ctx.Values.Int(wday), ctx.Values.Int(yday), ctx.Values.Int(isdst),
            };
            return new StructTimeValue(items);
        }

        // time.struct_time(iterable): exactly 9 int elements (bool counts as int).
        internal static StructTimeValue Construct(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            ctx.Budget.Step();
            Args.Exactly(ctx, args, "struct_time", 1);
            var collected = new List<ScriptValue>(9);
            IScriptIterator it = PyOps.GetIterator(args[0], ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
            {
                ctx.Budget.Step();
                if (v is IntValue)
                    collected.Add(v);
                else if (v is BoolValue)
                    collected.Add(ctx.Values.Int(((BoolValue)v).Value ? 1 : 0));
                else
                    throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
                if (collected.Count > 9)
                    break;
            }
            if (collected.Count != 9)
                throw Raise.TypeError(ctx, "time.struct_time() takes a 9-sequence (" + collected.Count + "-sequence given)");
            ctx.Values.PreCharge(160);
            return new StructTimeValue(collected.ToArray());
        }

        // ---- sequence protocol ----

        protected internal override bool IsTruthyCore(EvalContext ctx) { return true; }   // 9 elements, always truthy
        protected internal override bool TryLengthCore(out long length) { length = 9; return true; }
        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new TupleIterator(Items); }

        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            found = SequenceOps.Contains(new ArraySeq(Items), item, ctx);
            return true;
        }

        protected internal override ScriptValue GetItemCore(ScriptValue index, EvalContext ctx)
        {
            return TupleValue.GetItem(Items, index, ctx);
        }

        // ---- hash (tuple hash of the 9 items) ----

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            return PyOps.Hash(ctx.Values.Tuple((ScriptValue[])Items.Clone()), ctx, depth);
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            string[] names = { "tm_year", "tm_mon", "tm_mday", "tm_hour", "tm_min", "tm_sec", "tm_wday", "tm_yday", "tm_isdst" };
            sb.Append("time.struct_time(");
            for (int i = 0; i < 9; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(names[i]);
                sb.Append("=");
                sb.Append(Items[i].Repr(ctx, depth + 1));
            }
            sb.Append(")");
        }

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            string[] names = { "tm_year", "tm_mon", "tm_mday", "tm_hour", "tm_min", "tm_sec", "tm_wday", "tm_yday", "tm_isdst" };
            for (int i = 0; i < 9; i++)
            {
                int k = i;   // capture
                d[names[i]] = SlotDescriptor.MakeProperty(names[i], (self, c) => ((StructTimeValue)self).Items[k]);
            }
            return new ScriptTypeInfo("time.struct_time", d);
        }
    }
}
