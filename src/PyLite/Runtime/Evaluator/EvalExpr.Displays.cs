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
    // value; a starred element is unpacked in place (PEP 448).
    internal sealed partial class Evaluator
    {
        private ScriptValue EvalComprehension(ComprehensionNode n, Environment env, EvalContext ctx)
        {
            FunctionInfo info = _program.GetFunctionInfo(n);
            Cell[] closure = env.CaptureClosure(info);
            Environment scope = Environment.CreateFunction(ctx, info.LocalSlotCount, info.CellVars.Count,
                closure, env.ModuleEnv, info);
            // The first for-iterable is evaluated eagerly in the OUTER scope (CPython parity).
            ScriptValue iterable = EvalExpr(n.Clauses[0].Iter, env, ctx);

            // P3: single-for (+ optional single if) list/set/dict comprehensions drain in a flat loop —
            // no C# yield state machine per element. The odometer stays for genexp (laziness) and
            // nested clauses.
            if (n.Kind != CompKind.Generator && !ctx.InterpretOnly
                && (n.Clauses.Count == 1 || (n.Clauses.Count == 2 && !n.Clauses[1].IsFor)))
            {
                return EvalComprehensionFlat(n, scope, iterable, ctx);
            }

            var odo = new ComprehensionIterator(n, scope, PyOps.GetIterator(iterable, ctx), ctx, this);

            if (n.Kind == CompKind.Generator)
            {
                return ctx.Values.Iterator(odo, "generator");   // lazy, single-use
            }
            var sink = new CompSink(n.Kind == CompKind.List ? ctx.Values.List(4) : null,
                n.Kind == CompKind.Set ? ctx.Values.Set(4) : null, n.Kind == CompKind.Dict ? ctx.Values.Dict(4) : null);
            ScriptValue el;
            while (odo.MoveNext(ctx, out el))
            {
                if (sink.Dict == null)
                {
                    sink.Add(el, ctx);
                }
                else
                {
                    TupleValue t = (TupleValue)el;
                    sink.Dict.SetItem(t.Items[0], t.Items[1], ctx);   // last-wins
                }
            }
            return sink.Result;
        }

        // The container a list/set/dict comprehension fills: exactly one field is set.
        private struct CompSink
        {
            internal readonly ListValue List;
            internal readonly SetValue Set;
            internal readonly DictValue Dict;

            internal CompSink(ListValue list, SetValue set, DictValue dict)
            {
                List = list;
                Set = set;
                Dict = dict;
            }

            internal void Add(ScriptValue el, EvalContext ctx)
            {
                if (List != null)
                    List.Add(el, ctx);
                else
                    Set.AddItem(el, ctx);
            }

            internal ScriptValue Result
            {
                get { return List != null ? (ScriptValue)List : (Set != null ? (ScriptValue)Set : Dict); }
            }
        }

        // PEP 479: a StopIteration escaping a comprehension or generator body is a RuntimeError.
        internal static bool IsStopIteration(ScriptException se)
        {
            return se.Value.ExcType.IsSubtypeOf(PyExceptionTypes.StopIteration);
        }

        internal static ScriptException StopIterationEscaped(EvalContext ctx)
        {
            return Raise.RuntimeError(ctx, "generator raised StopIteration");
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
            long known = -1;   // the source's length when it is known without running script code
            if (iterable.Kind != ValueKind.Range && !iterable.TryLengthCore(out known))
                known = -1;
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
                    known = step > 0
                        ? (cur < stop ? (stop - cur - 1) / step + 1 : 0)
                        : (cur > stop ? (cur - stop - 1) / (-step) + 1 : 0);
                }
            }

            // The result starts at its real size when it is known, so a big one is not rebuilt on every
            // doubling: a list without a filter gets one element per source item; a table past 4096 only
            // when its keys are the source's own distinct items (duplicates would leave a large table
            // nearly empty, charged and kept). A filtered list keeps the bounded 4096 hint (8B/slot).
            int cap = known < 0 ? 4 : (int)System.Math.Min(System.Math.Max(known, 4), 4096);
            int hashCap = known < 0 || cond != null || n.Kind == CompKind.List ? 4
                : KeysAreDistinctItems(n, iterable) ? ctx.Values.PresizeHint(known, 36) : (int)System.Math.Min(System.Math.Max(known, 4), 4096);
            var sink = new CompSink(
                n.Kind != CompKind.List ? null
                    : known >= 0 && cond == null ? ctx.Values.ListForCount(known) : ctx.Values.List(cap),
                n.Kind == CompKind.Set ? ctx.Values.Set(hashCap) : null,
                n.Kind == CompKind.Dict ? ctx.Values.Dict(hashCap) : null);

            // T2.1: the body re-evaluates per element — use the closure-compiled form when the
            // expressions are in the compiler's whitelist (null => tree-walk, identical semantics).
            CompiledEval elemFn = GetCompiledExpr(ref n.CompiledElement, n.Element, ctx);
            CompiledEval valFn = n.ValueElement != null
                ? GetCompiledExpr(ref n.CompiledValueElement, n.ValueElement, ctx) : null;
            CompiledEval condFn = cond != null ? GetCompiledExpr(ref cond.CompiledCond, cond.Cond, ctx) : null;

            // T2.3 superinstruction fusion: a pure-int body/condition over the loop variable runs in
            // C# longs; any per-element edge (overflow, zero divisor) re-runs that element generally.
            // The range drain feeds the raw counter; the iterable drain unboxes machine-int items per
            // element (mixed sources fall back item by item).
            FusedLong fusedElem = null, fusedVal = null;
            FusedCond fusedCond = null;
            if (localSlot >= 0 && FusionAllowed(ctx))
            {
                fusedElem = GetFusedLong(ref n.FusedElement, n.Element, localSlot);
                fusedVal = n.ValueElement != null
                    ? GetFusedLong(ref n.FusedValueElement, n.ValueElement, localSlot) : null;
                fusedCond = cond != null ? GetFusedCond(ref cond.FusedCond, cond.Cond, localSlot) : null;
            }

            if (rangeFast)
            {
                return EvalComprehensionFlatRange(n, scope, localSlot, cond, sink,
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
                            keep = PyOps.Truth(EvalCompiledOr(condFn, cond.Cond, scope, ctx), ctx);
                        if (!keep)
                            continue;
                    }
                    ctx.Budget.Step();
                    long f;
                    if (sink.Dict == null)
                    {
                        sink.Add(fast && fusedElem != null && fusedElem(x, out f)
                            ? ctx.Values.Int(f)
                            : EvalCompiledOr(elemFn, n.Element, scope, ctx), ctx);
                    }
                    else
                    {
                        long fk = 0;
                        bool kFused = fast && fusedElem != null && fusedElem(x, out fk);
                        ScriptValue k = kFused ? ctx.Values.Int(fk) : EvalCompiledOr(elemFn, n.Element, scope, ctx);
                        ScriptValue v = fast && fusedVal != null && fusedVal(x, out f)
                            ? (kFused && f == fk ? k : ctx.Values.Int(f))   // {i: i ...}: one int object, as the interpreted form reads
                            : EvalCompiledOr(valFn, n.ValueElement, scope, ctx);
                        sink.Dict.SetItem(k, v, ctx);   // last-wins
                    }
                }
            }
            catch (ScriptException se) when (IsStopIteration(se))
            {
                throw StopIterationEscaped(ctx);
            }
            ctx.Budget.Step();
            return sink.Result;
        }

        // {x ... for x in <range/set/dict>} and {k: ... for k, v in d.items()}: one new key per item.
        private static bool KeysAreDistinctItems(ComprehensionNode n, ScriptValue iterable)
        {
            NameNode key = n.Element as NameNode;
            if (key == null)
                return false;
            ExprNode target = n.Clauses[0].Target;
            DictViewValue view = iterable as DictViewValue;
            if (view != null && view.View == DictViewValue.ViewKind.Items)
            {
                TupleNode tt = target as TupleNode;
                NameNode first = tt != null && tt.Elts.Count == 2 ? tt.Elts[0] as NameNode : null;
                return first != null && first.Id == key.Id;
            }
            NameNode t = target as NameNode;
            if (t == null || t.Id != key.Id)
                return false;
            switch (iterable.Kind)
            {
                case ValueKind.Range:
                case ValueKind.Set:
                case ValueKind.FrozenSet:
                case ValueKind.Dict:
                    return true;
                default:
                    return view != null && view.View == DictViewValue.ViewKind.Keys;
            }
        }

        // Long-range twin of the flat drain body (budget/PEP-479 semantics identical). The fused
        // delegates (null when not applicable) take the raw counter; the loop variable is boxed into
        // its slot only when something interpreted will read it — the comp scope is private and the
        // fused delegates are the only code running, so a skipped write is unobservable. A false
        // return from a fused delegate materializes the slot and re-runs that element generally.
        private ScriptValue EvalComprehensionFlatRange(ComprehensionNode n, Environment scope, int localSlot,
            CompClause cond, CompSink sink, long cur, long stop, long step,
            CompiledEval elemFn, CompiledEval valFn, CompiledEval condFn,
            FusedLong fusedElem, FusedLong fusedVal, FusedCond fusedCond, EvalContext ctx)
        {
            bool up = step > 0;
            bool interpreted = fusedElem == null || (cond != null && fusedCond == null)
                || (sink.Dict != null && fusedVal == null);
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
                    bool slotSet = false;
                    if (interpreted)
                        MaterializeSlot(ref slotSet, scope, localSlot, x, ctx);
                    if (cond != null)
                    {
                        bool keep;
                        if (fusedCond == null || !fusedCond(x, out keep))
                        {
                            MaterializeSlot(ref slotSet, scope, localSlot, x, ctx);
                            keep = PyOps.Truth(EvalCompiledOr(condFn, cond.Cond, scope, ctx), ctx);
                        }
                        if (!keep)
                            continue;
                    }
                    ctx.Budget.Step();
                    long f;
                    if (sink.Dict == null)
                    {
                        ScriptValue el;
                        if (fusedElem != null && fusedElem(x, out f))
                        {
                            el = ctx.Values.Int(f);
                        }
                        else
                        {
                            MaterializeSlot(ref slotSet, scope, localSlot, x, ctx);
                            el = EvalCompiledOr(elemFn, n.Element, scope, ctx);
                        }
                        sink.Add(el, ctx);
                    }
                    else
                    {
                        ScriptValue k;
                        long fk = 0;
                        bool kFused = fusedElem != null && fusedElem(x, out fk);
                        if (kFused)
                        {
                            k = ctx.Values.Int(fk);
                        }
                        else
                        {
                            MaterializeSlot(ref slotSet, scope, localSlot, x, ctx);
                            k = EvalCompiledOr(elemFn, n.Element, scope, ctx);
                        }
                        ScriptValue v;
                        if (fusedVal != null && fusedVal(x, out f))
                        {
                            v = kFused && f == fk ? k : ctx.Values.Int(f);
                        }
                        else
                        {
                            MaterializeSlot(ref slotSet, scope, localSlot, x, ctx);
                            v = EvalCompiledOr(valFn, n.ValueElement, scope, ctx);
                        }
                        sink.Dict.SetItem(k, v, ctx);   // last-wins
                    }
                }
            }
            catch (ScriptException se) when (IsStopIteration(se))
            {
                throw StopIterationEscaped(ctx);
            }
            ctx.Budget.Step();
            return sink.Result;
        }

        // The range drain boxes the loop variable only when interpreted code is about to read it.
        private static void MaterializeSlot(ref bool slotSet, Environment scope, int slot, long x, EvalContext ctx)
        {
            if (!slotSet)
            {
                scope.SetLocal(slot, ctx.Values.Int(x));
                slotSet = true;
            }
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
            // PEP 448: unpack into a temp list, then copy it into the tuple's array.
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

        // f-strings. Charged builder; !s/!r/!a conversions applied before the spec, nested {width}/{prec}
        // specs evaluated as expressions, the spec itself rendered by the format engine (PyFormat).
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
                    conv = ctx.Values.Str(ctx.Format.AsciiEscape(v.Repr(ctx)));
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
                if (ctx.Format == null)
                    throw Raise.NotImplemented(ctx, "formatting is not available in this context");
                string rendered = ((StrValue)ctx.Format.FormatValue(ctx, conv, spec)).Value;
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
    }
}
