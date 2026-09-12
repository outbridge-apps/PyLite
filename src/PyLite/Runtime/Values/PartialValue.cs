using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // functools.partial: a callable that prepends bound args and merges bound keywords.
    // Nested partials are flattened at construction (Func is never itself a PartialValue).
    internal sealed class PartialValue : ScriptValue
    {
        private static readonly ScriptTypeInfo PType = new ScriptTypeInfo("functools.partial", BuildSlots());

        internal readonly ScriptValue Func;
        internal readonly ScriptValue[] Args;
        internal readonly string[] KwNames;
        internal readonly ScriptValue[] KwValues;

        private PartialValue(ScriptValue func, ScriptValue[] args, string[] kwNames, ScriptValue[] kwValues)
        {
            Func = func;
            Args = args;
            KwNames = kwNames;
            KwValues = kwValues;
        }

        public override ScriptTypeInfo TypeInfo { get { return PType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }
        internal override bool IsCallable { get { return true; } }

        internal static PartialValue Create(EvalContext ctx, ScriptValue func, ScriptValue[] newArgs, KwArgs newKw)
        {
            if (!func.IsCallable)
                throw Raise.TypeError(ctx, "the first argument must be callable");
            PartialValue inner = func as PartialValue;
            if (inner != null)
            {
                var args = new ScriptValue[inner.Args.Length + newArgs.Length];
                Array.Copy(inner.Args, args, inner.Args.Length);
                Array.Copy(newArgs, 0, args, inner.Args.Length, newArgs.Length);
                string[] mn;
                ScriptValue[] mv;
                Merge(inner.KwNames, inner.KwValues, newKw, out mn, out mv);
                return new PartialValue(inner.Func, args, mn, mv);
            }
            string[] names = new string[newKw.Count];
            ScriptValue[] vals = new ScriptValue[newKw.Count];
            for (int i = 0; i < newKw.Count; i++)
            {
                names[i] = newKw.NameAt(i);
                vals[i] = newKw.ValueAt(i);
            }
            return new PartialValue(func, newArgs, names, vals);
        }

        protected internal override ScriptValue CallCore(ScriptValue[] callArgs, KwArgs callKw, EvalContext ctx)
        {
            var finalArgs = new ScriptValue[Args.Length + callArgs.Length];
            Array.Copy(Args, finalArgs, Args.Length);
            Array.Copy(callArgs, 0, finalArgs, Args.Length, callArgs.Length);
            KwArgs finalKw;
            if (KwNames.Length == 0)
                finalKw = callKw;
            else
            {
                string[] mn;
                ScriptValue[] mv;
                Merge(KwNames, KwValues, callKw, out mn, out mv);   // the call's keywords override the bound ones
                finalKw = KwArgs.FromArrays(mn, mv);
            }
            return ctx.CallHook(Func, finalArgs, finalKw);
        }

        // base keywords overlaid with `over` (over wins).
        private static void Merge(string[] baseNames, ScriptValue[] baseVals, KwArgs over,
            out string[] names, out ScriptValue[] values)
        {
            var ns = new List<string>(baseNames);
            var vs = new List<ScriptValue>(baseVals);
            for (int i = 0; i < over.Count; i++)
            {
                string n = over.NameAt(i);
                int at = ns.IndexOf(n);
                if (at >= 0)
                    vs[at] = over.ValueAt(i);
                else
                {
                    ns.Add(n);
                    vs.Add(over.ValueAt(i));
                }
            }
            names = ns.ToArray();
            values = vs.ToArray();
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("functools.partial(");
            sb.Append(Func.Repr(ctx, depth + 1));
            for (int i = 0; i < Args.Length; i++)
            {
                sb.Append(", ");
                sb.Append(Args[i].Repr(ctx, depth + 1));
            }
            for (int i = 0; i < KwNames.Length; i++)
            {
                sb.Append(", ");
                sb.Append(KwNames[i]);
                sb.Append("=");
                sb.Append(KwValues[i].Repr(ctx, depth + 1));
            }
            sb.Append(")");
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            s["func"] = SlotDescriptor.MakeProperty("func", (self, ctx) => ((PartialValue)self).Func);
            s["args"] = SlotDescriptor.MakeProperty("args", (self, ctx) => ctx.Values.Tuple((ScriptValue[])((PartialValue)self).Args.Clone()));
            s["keywords"] = SlotDescriptor.MakeProperty("keywords", (self, ctx) => KeywordsDict((PartialValue)self, ctx));
            return s;
        }

        private static ScriptValue KeywordsDict(PartialValue p, EvalContext ctx)
        {
            DictValue d = ctx.Values.Dict(p.KwNames.Length);
            for (int i = 0; i < p.KwNames.Length; i++)
                d.SetItem(ctx.Values.Str(p.KwNames[i]), p.KwValues[i], ctx);
            return d;
        }
    }
}
