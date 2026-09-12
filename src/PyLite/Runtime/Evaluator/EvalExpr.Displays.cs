using System.Collections.Generic;
using System.Globalization;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // Collection displays, subscription, slice, attribute reads and
    // comprehensions. Elements are evaluated left to right; dict pairs evaluate the key before the
    // value. Starred elements in displays are handled in a later phase.
    internal sealed partial class Evaluator
    {
        private ScriptValue EvalComprehension(ComprehensionNode n, Environment env, EvalContext ctx)
        {
            FunctionInfo info = _program.GetFunctionInfo(n);
            var closure = new Cell[info.FreeVars.Count];
            for (int i = 0; i < closure.Length; i++)
            {
                closure[i] = env.GetClosureCell(info.FreeVars[i]);
            }
            Environment scope = Environment.CreateFunction(ctx, info.LocalSlotCount, info.CellVars.Count,
                closure, env.ModuleEnv, info);
            // The first for-iterable is evaluated eagerly in the OUTER scope (CPython parity).
            ScriptValue iterable = EvalExpr(n.Clauses[0].Iter, env, ctx);

            // P3: single-for (+ optional single if) list/set/dict comprehensions drain in a flat loop —
            // no C# yield state machine per element. The odometer stays for genexp (laziness) and
            // nested clauses.
            if (n.Kind != CompKind.Generator
                && (n.Clauses.Count == 1 || (n.Clauses.Count == 2 && !n.Clauses[1].IsFor)))
            {
                return EvalComprehensionFlat(n, scope, iterable, ctx);
            }

            var odo = new ComprehensionIterator(n, scope, PyOps.GetIterator(iterable, ctx), ctx, this);

            if (n.Kind == CompKind.Generator)
            {
                return ctx.Values.Iterator(odo, "generator");   // lazy, single-use
            }
            ScriptValue el;
            if (n.Kind == CompKind.List)
            {
                ListValue lst = ctx.Values.List(4);
                while (odo.MoveNext(ctx, out el))
                {
                    lst.Add(el, ctx);
                }
                return lst;
            }
            if (n.Kind == CompKind.Set)
            {
                SetValue set = ctx.Values.Set(4);
                while (odo.MoveNext(ctx, out el))
                {
                    set.AddItem(el, ctx);
                }
                return set;
            }
            DictValue dict = ctx.Values.Dict(4);
            while (odo.MoveNext(ctx, out el))
            {
                TupleValue t = (TupleValue)el;
                dict.SetItem(t.Items[0], t.Items[1], ctx);   // last-wins
            }
            return dict;
        }

        // The flat drain (P3). One try implements PEP 479 for the whole loop (the odometer converts a
        // StopIteration escaping the body per MoveNext); the extra Step per produced element + one at
        // exhaustion mirrors the odometer's own MoveNext charges, so budget accounting is unchanged.
        private ScriptValue EvalComprehensionFlat(ComprehensionNode n, Environment scope,
            ScriptValue iterable, EvalContext ctx)
        {
            CompClause cond = n.Clauses.Count == 2 ? n.Clauses[1] : null;
            // Hoist the loop-target slot once: the overwhelmingly common `for <name> in ...` with a
            // Local binding writes straight to the slot, skipping the per-element AssignTo/SetName
            // double-switch. Tuple targets / cell captures fall back to the general AssignTo.
            ExprNode target = n.Clauses[0].Target;
            int localSlot = -1;
            if (target.NodeKind == NodeKind.Name)
            {
                NameBinding tb = ((NameNode)target).Binding;
                if (tb != null && tb.Kind == NameKind.Local)
                    localSlot = tb.Slot;
            }

            // Range source with a slot-bound target: walk a long counter inline (see ExecForRange) —
            // no iterator allocation, no virtual MoveNext; the Step cadence mirrors the iterator path
            // (one per element including the final failed test). The element count is known here too,
            // so the result container starts at its real size (exact without a filter, an upper bound
            // with one) instead of growing through reallocation/rehash — capped so a huge filtered
            // range cannot commit memory ahead of its per-element budget charges.
            bool rangeFast = false;
            long cur = 0, stop = 0, step = 0;
            int cap = 4;
            if (localSlot >= 0 && iterable.Kind == ValueKind.Range)
            {
                RangeValue rng = (RangeValue)iterable;
                const long Lo = long.MinValue / 4, Hi = long.MaxValue / 4;
                if (rng.Start >= Lo && rng.Start <= Hi && rng.Stop >= Lo && rng.Stop <= Hi
                    && rng.Step >= Lo && rng.Step <= Hi)
                {
                    rangeFast = true;
                    cur = (long)rng.Start;
                    stop = (long)rng.Stop;
                    step = (long)rng.Step;
                    long count = step > 0
                        ? (cur < stop ? (stop - cur - 1) / step + 1 : 0)
                        : (cur > stop ? (cur - stop - 1) / (-step) + 1 : 0);
                    cap = (int)System.Math.Min(System.Math.Max(count, 4), 4096);
                }
            }

            // A list holds every produced element, so its hint is right even under a filter (upper
            // bound, 8B/slot). Set/dict hints only make sense without one: a filter of unknown
            // selectivity could charge a large table for a tiny result.
            int hashCap = cond == null ? cap : 4;
            ListValue lst = n.Kind == CompKind.List ? ctx.Values.List(cap) : null;
            SetValue set = n.Kind == CompKind.Set ? ctx.Values.Set(hashCap) : null;
            DictValue dict = n.Kind == CompKind.Dict ? ctx.Values.Dict(hashCap) : null;

            // T2.1: the body re-evaluates per element — use the closure-compiled form when the
            // expressions are in the compiler's whitelist (null => tree-walk, identical semantics).
            CompiledEval elemFn = GetCompiledExpr(ref n.CompiledElement, n.Element);
            CompiledEval valFn = n.ValueElement != null
                ? GetCompiledExpr(ref n.CompiledValueElement, n.ValueElement) : null;
            CompiledEval condFn = cond != null ? GetCompiledExpr(ref cond.CompiledCond, cond.Cond) : null;

            // T2.3 superinstruction fusion: a pure-int body/condition over the loop variable runs in
            // C# longs; any per-element edge (overflow, zero divisor) re-runs that element generally.
            // The range drain feeds the raw counter; the iterable drain unboxes machine-int items per
            // element (mixed sources fall back item by item). Gated to MaxIntBits >= 128 —
            // IntOpSmall's own threshold — so exotic configs keep the exact path's estimate aborts.
            FusedLong fusedElem = null, fusedVal = null;
            FusedCond fusedCond = null;
            if (localSlot >= 0 && ctx.Limits.MaxIntBits >= 128)
            {
                fusedElem = GetFusedLong(ref n.FusedElement, n.Element, localSlot);
                fusedVal = n.ValueElement != null
                    ? GetFusedLong(ref n.FusedValueElement, n.ValueElement, localSlot) : null;
                fusedCond = cond != null ? GetFusedCond(ref cond.FusedCond, cond.Cond, localSlot) : null;
            }

            if (rangeFast)
            {
                return EvalComprehensionFlatRange(n, scope, localSlot, cond, lst, set, dict,
                    cur, stop, step, elemFn, valFn, condFn, fusedElem, fusedVal, fusedCond, ctx);
            }

            IScriptIterator firstIter = PyOps.GetIterator(iterable, ctx);
            try
            {
                ScriptValue item;
                while (firstIter.MoveNext(ctx, out item))
                {
                    if (localSlot >= 0)
                        scope.SetLocal(localSlot, item);
                    else
                        AssignTo(target, item, scope, ctx);
                    // A machine-int item drives the fused body/condition directly. Bools are
                    // EXCLUDED (identity `[x for x in [True]]` must stay bool); floats, strings and
                    // big ints keep the general path for this element. The slot is already written,
                    // so a fused fallback simply re-evaluates the interpreted form.
                    long x = 0;
                    bool fast = false;
                    if ((fusedElem != null || fusedVal != null || fusedCond != null)
                        && item.Kind == ValueKind.Int)
                    {
                        IntValue iv = (IntValue)item;
                        if (iv.IsSmall)
                        {
                            x = iv.Small;
                            fast = true;
                        }
                    }
                    if (cond != null)
                    {
                        bool keep;
                        if (!(fast && fusedCond != null && fusedCond(x, out keep)))
                        {
                            keep = PyOps.Truth(
                                condFn != null ? condFn(this, scope, ctx) : EvalExpr(cond.Cond, scope, ctx), ctx);
                        }
                        if (!keep)
                        {
                            continue;
                        }
                    }
                    ctx.Budget.Step();
                    long f;
                    if (lst != null)
                    {
                        if (fast && fusedElem != null && fusedElem(x, out f))
                            lst.Add(ctx.Values.Int(f), ctx);
                        else
                            lst.Add(elemFn != null ? elemFn(this, scope, ctx) : EvalExpr(n.Element, scope, ctx), ctx);
                    }
                    else if (set != null)
                    {
                        if (fast && fusedElem != null && fusedElem(x, out f))
                            set.AddItem(ctx.Values.Int(f), ctx);
                        else
                            set.AddItem(elemFn != null ? elemFn(this, scope, ctx) : EvalExpr(n.Element, scope, ctx), ctx);
                    }
                    else
                    {
                        ScriptValue k = fast && fusedElem != null && fusedElem(x, out f)
                            ? ctx.Values.Int(f)
                            : (elemFn != null ? elemFn(this, scope, ctx) : EvalExpr(n.Element, scope, ctx));
                        ScriptValue v = fast && fusedVal != null && fusedVal(x, out f)
                            ? ctx.Values.Int(f)
                            : (valFn != null ? valFn(this, scope, ctx) : EvalExpr(n.ValueElement, scope, ctx));
                        dict.SetItem(k, v, ctx);   // last-wins
                    }
                }
            }
            catch (ScriptException se) when (se.Value.ExcType.IsSubtypeOf(PyExceptionTypes.StopIteration))
            {
                // PEP 479: a StopIteration escaping the body is a RuntimeError.
                throw Raise.RuntimeError(ctx, "generator raised StopIteration");
            }
            ctx.Budget.Step();
            return lst != null ? (ScriptValue)lst : (set != null ? (ScriptValue)set : dict);
        }

        // Long-range twin of the flat drain body (budget/PEP-479 semantics identical). The fused
        // delegates (null when not applicable) take the raw counter; the loop variable is boxed into
        // its slot only when something interpreted will read it — the comp scope is private and the
        // fused delegates are the only code running, so a skipped write is unobservable. A false
        // return from a fused delegate materializes the slot and re-runs that element generally.
        private ScriptValue EvalComprehensionFlatRange(ComprehensionNode n, Environment scope, int localSlot,
            CompClause cond, ListValue lst, SetValue set, DictValue dict,
            long cur, long stop, long step,
            CompiledEval elemFn, CompiledEval valFn, CompiledEval condFn,
            FusedLong fusedElem, FusedLong fusedVal, FusedCond fusedCond, EvalContext ctx)
        {
            bool up = step > 0;
            bool interpreted = fusedElem == null || (cond != null && fusedCond == null)
                || (dict != null && fusedVal == null);
            try
            {
                while (true)
                {
                    ctx.Budget.Step();   // mirrors the iterator path's per-MoveNext charge incl the final test
                    if (up ? cur >= stop : cur <= stop)
                    {
                        break;
                    }
                    long x = cur;
                    cur += step;
                    bool slotSet = interpreted;
                    if (slotSet)
                    {
                        scope.SetLocal(localSlot, ctx.Values.Int(x));
                    }
                    if (cond != null)
                    {
                        bool keep;
                        if (fusedCond == null || !fusedCond(x, out keep))
                        {
                            if (!slotSet)
                            {
                                scope.SetLocal(localSlot, ctx.Values.Int(x));
                                slotSet = true;
                            }
                            keep = PyOps.Truth(
                                condFn != null ? condFn(this, scope, ctx) : EvalExpr(cond.Cond, scope, ctx), ctx);
                        }
                        if (!keep)
                        {
                            continue;
                        }
                    }
                    ctx.Budget.Step();
                    long f;
                    if (lst != null)
                    {
                        if (fusedElem != null && fusedElem(x, out f))
                        {
                            lst.Add(ctx.Values.Int(f), ctx);
                        }
                        else
                        {
                            if (!slotSet)
                            {
                                scope.SetLocal(localSlot, ctx.Values.Int(x));
                            }
                            lst.Add(elemFn != null ? elemFn(this, scope, ctx) : EvalExpr(n.Element, scope, ctx), ctx);
                        }
                    }
                    else if (set != null)
                    {
                        if (fusedElem != null && fusedElem(x, out f))
                        {
                            set.AddItem(ctx.Values.Int(f), ctx);
                        }
                        else
                        {
                            if (!slotSet)
                            {
                                scope.SetLocal(localSlot, ctx.Values.Int(x));
                            }
                            set.AddItem(elemFn != null ? elemFn(this, scope, ctx) : EvalExpr(n.Element, scope, ctx), ctx);
                        }
                    }
                    else
                    {
                        ScriptValue k;
                        if (fusedElem != null && fusedElem(x, out f))
                        {
                            k = ctx.Values.Int(f);
                        }
                        else
                        {
                            if (!slotSet)
                            {
                                scope.SetLocal(localSlot, ctx.Values.Int(x));
                                slotSet = true;
                            }
                            k = elemFn != null ? elemFn(this, scope, ctx) : EvalExpr(n.Element, scope, ctx);
                        }
                        ScriptValue v;
                        if (fusedVal != null && fusedVal(x, out f))
                        {
                            v = ctx.Values.Int(f);
                        }
                        else
                        {
                            if (!slotSet)
                            {
                                scope.SetLocal(localSlot, ctx.Values.Int(x));
                            }
                            v = valFn != null ? valFn(this, scope, ctx) : EvalExpr(n.ValueElement, scope, ctx);
                        }
                        dict.SetItem(k, v, ctx);   // last-wins
                    }
                }
            }
            catch (ScriptException se) when (se.Value.ExcType.IsSubtypeOf(PyExceptionTypes.StopIteration))
            {
                // PEP 479: a StopIteration escaping the body is a RuntimeError.
                throw Raise.RuntimeError(ctx, "generator raised StopIteration");
            }
            ctx.Budget.Step();
            return lst != null ? (ScriptValue)lst : (set != null ? (ScriptValue)set : dict);
        }

        // PEP 448: a StarredNode element is unpacked into the display (charged per element via Add).
        private ScriptValue EvalList(ListNode n, Environment env, EvalContext ctx)
        {
            ListValue list = ctx.Values.List(n.Elts.Count);
            for (int i = 0; i < n.Elts.Count; i++)
            {
                if (n.Elts[i] is StarredNode st)
                    list.InPlaceConcat(EvalExpr(st.Value, env, ctx), ctx);
                else
                    list.Add(EvalExpr(n.Elts[i], env, ctx), ctx);
            }
            return list;
        }

        private ScriptValue EvalTuple(TupleNode n, Environment env, EvalContext ctx)
        {
            if (!HasStar(n.Elts))
            {
                var arr = new ScriptValue[n.Elts.Count];
                for (int i = 0; i < n.Elts.Count; i++)
                {
                    arr[i] = EvalExpr(n.Elts[i], env, ctx);
                }
                return ctx.Values.Tuple(arr);
            }
            // PEP 448: unpack into a temp list, then adopt its backing array as the tuple.
            ListValue tmp = ctx.Values.List(n.Elts.Count);
            for (int i = 0; i < n.Elts.Count; i++)
            {
                if (n.Elts[i] is StarredNode st)
                    tmp.InPlaceConcat(EvalExpr(st.Value, env, ctx), ctx);
                else
                    tmp.Add(EvalExpr(n.Elts[i], env, ctx), ctx);
            }
            return ctx.Values.Tuple(tmp.Items.ToArray());
        }

        private ScriptValue EvalSet(SetNode n, Environment env, EvalContext ctx)
        {
            SetValue set = ctx.Values.Set(n.Elts.Count);
            for (int i = 0; i < n.Elts.Count; i++)
            {
                if (n.Elts[i] is StarredNode st)
                {
                    IScriptIterator it = PyOps.GetIterator(EvalExpr(st.Value, env, ctx), ctx);
                    ScriptValue e;
                    while (it.MoveNext(ctx, out e))
                        set.AddItem(e, ctx);
                }
                else
                    set.AddItem(EvalExpr(n.Elts[i], env, ctx), ctx);
            }
            return set;
        }

        private ScriptValue EvalDict(DictNode n, Environment env, EvalContext ctx)
        {
            DictValue dict = ctx.Values.Dict(n.Keys.Count);
            for (int i = 0; i < n.Keys.Count; i++)
            {
                if (n.Keys[i] == null)
                {
                    // PEP 448 '**mapping': merge (last wins). The operand must be a mapping.
                    ScriptValue m = EvalExpr(n.Values[i], env, ctx);
                    DictValue src = m as DictValue;
                    if (src == null)
                        throw Raise.TypeError(ctx, "'" + m.PyTypeName + "' object is not a mapping");
                    DictMethods.Merge(ctx, dict, src);
                    continue;
                }
                ScriptValue k = EvalExpr(n.Keys[i], env, ctx);
                ScriptValue v = EvalExpr(n.Values[i], env, ctx);
                dict.SetItem(k, v, ctx);
            }
            return dict;
        }

        private static bool HasStar(System.Collections.Generic.IReadOnlyList<ExprNode> elts)
        {
            for (int i = 0; i < elts.Count; i++)
                if (elts[i] is StarredNode)
                    return true;
            return false;
        }

        private ScriptValue EvalIndex(IndexNode n, Environment env, EvalContext ctx)
        {
            ScriptValue obj = EvalExpr(n.Value, env, ctx);
            ScriptValue idx = EvalSubscript(n.Index, env, ctx);
            return GetItemByKind(obj, idx, ctx);
        }

        private ScriptValue EvalSlice(SliceNode n, Environment env, EvalContext ctx)
        {
            ScriptValue lo = n.Lower == null ? ctx.Values.None : EvalExpr(n.Lower, env, ctx);
            ScriptValue hi = n.Upper == null ? ctx.Values.None : EvalExpr(n.Upper, env, ctx);
            ScriptValue st = n.Step == null ? ctx.Values.None : EvalExpr(n.Step, env, ctx);
            return ctx.Values.Slice(lo, hi, st);
        }

        private ScriptValue EvalAttribute(AttributeNode n, Environment env, EvalContext ctx)
        {
            return AttrOnValue(EvalExpr(n.Value, env, ctx), n.Attr, ctx);
        }

        // Attribute access on an already-evaluated object (shared with the fused call path, which must
        // not evaluate the object expression twice).
        private ScriptValue AttrOnValue(ScriptValue obj, string attr, EvalContext ctx)
        {
            return PyOps.GetAttr(obj, attr, ctx);   // module members are resolved there too
        }

        // f-strings. Charged builder; !s/!r conversions applied before the spec
        // nested {width}/{prec} specs evaluated as expressions. The format spec uses a MINIMAL formatter
        // here (fill/align/width/precision/f/d) — the full PyFormat engine is PyFormat.
        private ScriptValue EvalFString(FStringNode n, Environment env, EvalContext ctx)
        {
            var sb = ctx.Values.RentBuilder(EstimateFString(n));
            for (int i = 0; i < n.Parts.Count; i++)
            {
                FStringPart part = n.Parts[i];
                if (part.IsLiteral)
                {
                    ctx.Values.ChargeBuilderGrow(sb, part.Literal.Length);
                    sb.Append(part.Literal);
                    continue;
                }
                ScriptValue v = EvalExpr(part.Expr, env, ctx);
                ScriptValue conv;
                if (part.Conversion == 'a')
                {
                    conv = ctx.Values.Str(Modules.Support.PyFormat.AsciiEscape(v.Repr(ctx)));
                }
                else if (part.Conversion == 'r')
                {
                    conv = ctx.Values.Str(v.Repr(ctx));
                }
                else if (part.Conversion == 's')
                {
                    conv = ctx.Values.Str(v.Str(ctx));
                }
                else
                {
                    conv = v;
                }
                string spec = part.FormatSpec == null ? "" : ResolveSpec(part.FormatSpec, env, ctx);
                // The real format engine when installed; MiniFormat is the fallback for low-level
                // contexts that have no IFormatServices.
                string rendered = ctx.Format != null
                    ? ((StrValue)ctx.Format.FormatValue(ctx, conv, spec)).Value
                    : MiniFormat(conv, spec, ctx);
                ctx.Values.ChargeBuilderGrow(sb, rendered.Length);
                sb.Append(rendered);
            }
            return ctx.Values.StrPrecharged(sb.ToString());
        }

        private static int EstimateFString(FStringNode n)
        {
            int est = 0;
            for (int i = 0; i < n.Parts.Count; i++)
            {
                est += n.Parts[i].IsLiteral ? n.Parts[i].Literal.Length : 8;
            }
            return est < 8 ? 8 : est;
        }

        private string ResolveSpec(IReadOnlyList<FStringPart> specParts, Environment env, EvalContext ctx)
        {
            var sb = ctx.Values.RentBuilder(16);
            for (int i = 0; i < specParts.Count; i++)
            {
                FStringPart p = specParts[i];
                if (p.IsLiteral)
                {
                    sb.Append(p.Literal);
                }
                else
                {
                    sb.Append(EvalExpr(p.Expr, env, ctx).Str(ctx));
                }
            }
            return sb.ToString();
        }

        private static bool IsAlign(char c)
        {
            return c == '<' || c == '>' || c == '^' || c == '=';
        }

        private string MiniFormat(ScriptValue v, string spec, EvalContext ctx)
        {
            if (spec.Length == 0)
            {
                return v.Str(ctx);
            }
            int i = 0;
            char fill = ' ';
            char align = '\0';
            if (spec.Length >= 2 && IsAlign(spec[1]))
            {
                fill = spec[0];
                align = spec[1];
                i = 2;
            }
            else if (IsAlign(spec[0]))
            {
                align = spec[0];
                i = 1;
            }
            int width = 0;
            while (i < spec.Length && spec[i] >= '0' && spec[i] <= '9')
            {
                width = width * 10 + (spec[i] - '0');
                i++;
            }
            int prec = -1;
            if (i < spec.Length && spec[i] == '.')
            {
                i++;
                prec = 0;
                while (i < spec.Length && spec[i] >= '0' && spec[i] <= '9')
                {
                    prec = prec * 10 + (spec[i] - '0');
                    i++;
                }
            }
            char type = i < spec.Length ? spec[i] : '\0';

            string baseStr = RenderBase(v, type, prec, ctx);
            if (align == '\0')
            {
                align = (v.Kind == ValueKind.Int || v.Kind == ValueKind.Float || v.Kind == ValueKind.Bool) ? '>' : '<';
            }
            return Pad(baseStr, width, align, fill);
        }

        private string RenderBase(ScriptValue v, char type, int prec, EvalContext ctx)
        {
            switch (type)
            {
                case 'f':
                case 'F':
                    return ToDoubleForFormat(v, ctx).ToString("F" + (prec >= 0 ? prec : 6), CultureInfo.InvariantCulture);
                case 'd':
                    IntValue iv = v as IntValue;
                    if (iv != null)
                    {
                        return iv.Value.ToString(CultureInfo.InvariantCulture);
                    }
                    throw Raise.ValueError(ctx, "Unknown format code 'd' for object of type '" + v.PyTypeName + "'");
                case 's':
                case '\0':
                    return v.Str(ctx);
                default:
                    throw Raise.ValueError(ctx, "Unknown format code '" + type + "' for object of type '" + v.PyTypeName + "'");
            }
        }

        private double ToDoubleForFormat(ScriptValue v, EvalContext ctx)
        {
            FloatValue fv = v as FloatValue;
            if (fv != null)
            {
                return fv.Value;
            }
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                return (double)iv.Value;
            }
            throw Raise.ValueError(ctx, "Unknown format code 'f' for object of type '" + v.PyTypeName + "'");
        }

        private static string Pad(string s, int width, char align, char fill)
        {
            if (s.Length >= width)
            {
                return s;
            }
            int pad = width - s.Length;
            if (align == '<')
            {
                return s + new string(fill, pad);
            }
            if (align == '>' || align == '=')
            {
                return new string(fill, pad) + s;
            }
            int left = pad / 2;
            return new string(fill, left) + s + new string(fill, pad - left);
        }
    }
}
