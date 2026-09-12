using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // Assignment, augmented assignment and del. AssignTo recurses over the
    // TARGET structure (depth <= 64, static) — legal C# recursion. The RHS is evaluated once.
    internal sealed partial class Evaluator
    {
        private ExecSignal ExecAssign(AssignNode n, Environment env, EvalContext ctx)
        {
            ScriptValue v = EvalExpr(n.Value, env, ctx);
            for (int i = 0; i < n.Targets.Count; i++)
            {
                AssignTo(n.Targets[i], v, env, ctx);
            }
            return ExecSignal.Normal;
        }

        internal void AssignTo(ExprNode target, ScriptValue value, Environment env, EvalContext ctx)
        {
            switch (target.NodeKind)   // P2: jump table (see EvalExpr)
            {
                case NodeKind.Name:
                    SetName((NameNode)target, value, env);
                    return;
                case NodeKind.Index:
                    IndexNode ix = (IndexNode)target;
                    ScriptValue obj = EvalExpr(ix.Value, env, ctx);
                    ScriptValue idx = EvalSubscript(ix.Index, env, ctx);
                    SetItemByKind(obj, idx, value, ctx);
                    return;
                case NodeKind.Tuple:
                    UnpackInto(((TupleNode)target).Elts, value, env, ctx);
                    return;
                case NodeKind.List:
                    UnpackInto(((ListNode)target).Elts, value, env, ctx);
                    return;
                case NodeKind.Attribute:
                    AttributeNode at = (AttributeNode)target;
                    StoreAttrValue(EvalExpr(at.Value, env, ctx), at.Attr, value, ctx);
                    return;
                default:
                    throw EngineBug("cannot assign to " + target.GetType().Name);
            }
        }

        // Store `value` into obj.attr, or raise the read-only AttributeError. Shared by tree-walk assign
        // and the bytecode VM's STORE_ATTR so both are byte-identical.
        internal void StoreAttrValue(ScriptValue aobj, string attr, ScriptValue value, EvalContext ctx)
        {
            PyOps.SetAttr(aobj, attr, value, ctx);
        }

        private void UnpackInto(System.Collections.Generic.IReadOnlyList<ExprNode> targets, ScriptValue value, Environment env, EvalContext ctx)
        {
            int star = -1;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] is StarredNode)
                {
                    star = i;
                    break;
                }
            }

            if (star < 0)
            {
                ScriptValue[] items = Unpack(value, targets.Count, ctx);
                for (int i = 0; i < targets.Count; i++)
                {
                    AssignTo(targets[i], items[i], env, ctx);
                }
                return;
            }

            // a, *b, c = value : before (star elements) / star (rest) / after
            int total = targets.Count;
            int nBefore = star;
            int nAfter = total - star - 1;
            int atLeast = nBefore + nAfter;
            IScriptIterator it = PyOps.GetIterator(value, ctx);
            ScriptValue el;
            for (int i = 0; i < nBefore; i++)
            {
                if (!it.MoveNext(ctx, out el))
                {
                    throw Raise.ValueError(ctx, "not enough values to unpack (expected at least " + atLeast + ", got " + i + ")");
                }
                AssignTo(targets[i], el, env, ctx);
            }
            ListValue rest = ctx.Values.List(4);
            while (it.MoveNext(ctx, out el))
            {
                rest.Add(el, ctx);
            }
            if (rest.Items.Count < nAfter)
            {
                throw Raise.ValueError(ctx, "not enough values to unpack (expected at least " + atLeast + ", got " + (nBefore + rest.Items.Count) + ")");
            }
            int starLen = rest.Items.Count - nAfter;
            ListValue starVal = ctx.Values.List(starLen);
            for (int i = 0; i < starLen; i++)
            {
                starVal.Add(rest.Items[i], ctx);
            }
            AssignTo(((StarredNode)targets[star]).Value, starVal, env, ctx);   // Py3: *b is always a list
            for (int j = 0; j < nAfter; j++)
            {
                AssignTo(targets[star + 1 + j], rest.Items[starLen + j], env, ctx);
            }
        }

        // PEP 572 walrus: evaluate, bind the target name in the enclosing scope, and yield the value.
        internal ScriptValue EvalNamedExpr(NamedExprNode n, Environment env, EvalContext ctx)
        {
            ScriptValue v = EvalExpr(n.Value, env, ctx);
            SetName(n.Target, v, env);
            return v;
        }

        private void SetName(NameNode n, ScriptValue value, Environment env)
        {
            NameBinding b = _program.GetBinding(n);
            if (b == null)
            {
                env.SetGlobal(n.Id, value);
                return;
            }
            switch (b.Kind)
            {
                case NameKind.Local: env.SetLocal(b.Slot, value); return;
                case NameKind.Cell: env.SetCell(b.Slot, value); return;
                case NameKind.Free:
                case NameKind.Nonlocal: env.SetFree(b.Slot, value); return;
                case NameKind.Global when b.Slot >= 0:
                    env.SetGlobalCached(GlobalCache, b.Slot, n.Id, value); return;
                default: env.SetGlobal(n.Id, value); return;   // Global(unslotted) | Builtin
            }
        }

        private ExecSignal ExecAugAssign(AugAssignNode n, Environment env, EvalContext ctx)
        {
            PyBinOp op = MapBin(n.Op);
            switch (n.Target)
            {
                case NameNode nm:
                    ScriptValue cur = EvalName(nm, env, ctx);
                    ScriptValue rhs = EvalExpr(n.Value, env, ctx);
                    ScriptValue result = InplaceOp(op, cur, rhs, ctx);
                    SetName(nm, result, env);   // list += rebinds the SAME (mutated) object
                    return ExecSignal.Normal;
                case IndexNode ix:
                    ScriptValue obj = EvalExpr(ix.Value, env, ctx);   // evaluated once
                    ScriptValue idx = EvalSubscript(ix.Index, env, ctx);
                    ScriptValue got = GetItemByKind(obj, idx, ctx);
                    ScriptValue res = InplaceOp(op, got, EvalExpr(n.Value, env, ctx), ctx);
                    SetItemByKind(obj, idx, res, ctx);
                    return ExecSignal.Normal;
                case AttributeNode at:
                    ScriptValue aobj = EvalExpr(at.Value, env, ctx);   // evaluated once
                    if (aobj.Kind == ValueKind.RecordInstance)
                    {
                        RecordInstanceValue ri = (RecordInstanceValue)aobj;
                        ScriptValue cur2 = ri.GetAttribute(at.Attr, ctx);
                        ri.SetAttribute(at.Attr, InplaceOp(op, cur2, EvalExpr(n.Value, env, ctx), ctx), ctx);
                        return ExecSignal.Normal;
                    }
                    throw ReadOnlyAttrOf(aobj.PyTypeName, at.Attr, ctx);
                default:
                    throw EngineBug("cannot augment " + n.Target.GetType().Name);
            }
        }

        // Inplace semantics: list += iterable extends the same object, list *= n repeats it in place;
        // everything else is an ordinary BinOp. The TypeError symbol is rewritten to the augmented form.
        private ScriptValue InplaceOp(PyBinOp op, ScriptValue cur, ScriptValue rhs, EvalContext ctx)
        {
            ListValue lst = cur as ListValue;
            if (lst != null)
            {
                if (op == PyBinOp.Add)
                {
                    lst.InPlaceConcat(rhs, ctx);
                    return lst;
                }
                if (op == PyBinOp.Mul && rhs is IntValue iv)
                {
                    lst.InPlaceRepeat(iv.Value, ctx);
                    return lst;
                }
            }
            // PEP 584: d |= other updates the SAME dict in place (accepts a mapping or an
            // iterable of key/value pairs, like dict.update; right wins).
            if (op == PyBinOp.BitOr && cur is DictValue dcur)
            {
                DictMethods.Merge(ctx, dcur, rhs);
                return dcur;
            }
            DequeValue dq = cur as DequeValue;   // d += iterable extends, d *= n repeats, both in place
            if (dq != null && op == PyBinOp.Add)
            {
                dq.ExtendFrom(rhs, ctx);
                return dq;
            }
            if (dq != null && op == PyBinOp.Mul && rhs is IntValue dqn)
            {
                dq.RepeatInPlace(dqn.Value, ctx);
                return dq;
            }
            // s |= t, &=, -=, ^= update the SAME set in place; the operand must be a set (CPython parity).
            if (cur.Kind == ValueKind.Set && rhs is SetValue
                && (op == PyBinOp.BitOr || op == PyBinOp.BitAnd || op == PyBinOp.Sub || op == PyBinOp.BitXor))
            {
                SetValue scur = (SetValue)cur;
                SetMethods.ReplaceWith(ctx, scur, SetOps.Apply(op, scur, rhs, ctx, false).Table);
                return scur;
            }
            // Small-int shortcut (n += 1 loops): no TypeError to re-augment on this path — IntOpSmall
            // raises only ZeroDivision/ValueError, which ReAugment passes through unchanged anyway.
            ScriptValue fastAug = PyOps.TrySmallIntBinOp(op, cur, rhs, ctx);
            if (fastAug != null)
            {
                return fastAug;
            }
            try
            {
                return PyOps.BinaryOp(op, cur, rhs, ctx);
            }
            catch (ScriptException se)
            {
                throw ReAugment(se, op, ctx);
            }
        }

        private ScriptException ReAugment(ScriptException se, PyBinOp op, EvalContext ctx)
        {
            if (ReferenceEquals(se.Value.ExcType, PyExceptionTypes.TypeError))
            {
                string sym = PyOps.Sym(op);
                string msg = se.Value.GetMessageText(ctx);
                string needle = " for " + sym + ":";
                if (msg.Contains(needle))
                {
                    return Raise.TypeError(ctx, msg.Replace(needle, " for " + sym + "=:"));
                }
            }
            return se;
        }

        // Builtin script types are attribute-immutable; only record instances have settable slots.
        private ScriptException ReadOnlyAttr(AttributeNode at, Environment env, EvalContext ctx)
        {
            return ReadOnlyAttrOf(EvalExpr(at.Value, env, ctx).PyTypeName, at.Attr, ctx);
        }

        private ScriptException ReadOnlyAttrOf(string typeName, string attr, EvalContext ctx)
        {
            return Raise.Make(ctx, PyExceptionTypes.AttributeError,
                "'" + typeName + "' object attribute '" + attr + "' is read-only");
        }

        private ExecSignal ExecDel(DelNode n, Environment env, EvalContext ctx)
        {
            for (int i = 0; i < n.Targets.Count; i++)
            {
                DelOne(n.Targets[i], env, ctx);
            }
            return ExecSignal.Normal;
        }

        private void DelOne(ExprNode target, Environment env, EvalContext ctx)
        {
            switch (target)
            {
                case NameNode nm:
                    NameBinding b = _program.GetBinding(nm);
                    if (b != null && b.Kind == NameKind.Local)
                    {
                        env.DelLocal(b.Slot, nm.Id);
                    }
                    else
                    {
                        env.DelGlobal(nm.Id);
                    }
                    return;
                case IndexNode ix:
                    ScriptValue obj = EvalExpr(ix.Value, env, ctx);
                    ScriptValue idx = EvalSubscript(ix.Index, env, ctx);
                    DelItemByKind(obj, idx, ctx);
                    return;
                default:
                    throw ReadOnlyAttr((AttributeNode)target, env, ctx);
            }
        }
    }
}
