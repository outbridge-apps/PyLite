using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // PEP 634 structural pattern matching. The subject is evaluated once; cases are tried
    // top to bottom. A pattern binds its captures as it succeeds (CPython does not revert bindings on a
    // later failure). A guard runs only after its pattern matches; if it is false the next case is tried.
    // No case matching is not an error (match falls through). Class patterns are rejected by the parser.
    internal sealed partial class Evaluator
    {
        private ExecSignal ExecMatch(MatchNode n, Environment env, EvalContext ctx)
        {
            ScriptValue subject = EvalExpr(n.Subject, env, ctx);
            for (int i = 0; i < n.Cases.Count; i++)
            {
                MatchCase c = n.Cases[i];
                ctx.Budget.Step(BudgetCost.BackEdge);
                if (!TryMatch(c.Pattern, subject, env, ctx))
                    continue;
                if (c.Guard != null && !PyOps.Truth(EvalExpr(c.Guard, env, ctx), ctx))
                    continue;
                return ExecBlock(c.Body, env, ctx);
            }
            return ExecSignal.Normal;
        }

        private bool TryMatch(PatternNode p, ScriptValue subject, Environment env, EvalContext ctx)
        {
            switch (p)
            {
                case CapturePattern c:
                    if (c.Target != null)
                        SetName(c.Target, subject, env);
                    return true;   // wildcard and capture always match
                case LiteralPattern lit:
                    return MatchLiteral(lit.Value, subject, env, ctx);
                case ValuePattern v:
                    return PyOps.Equals(subject, EvalExpr(v.Value, env, ctx), ctx, 0);
                case AsPattern a:
                    if (!TryMatch(a.Inner, subject, env, ctx))
                        return false;
                    SetName(a.Target, subject, env);
                    return true;
                case OrPattern o:
                    for (int i = 0; i < o.Alternatives.Count; i++)
                        if (TryMatch(o.Alternatives[i], subject, env, ctx))
                            return true;
                    return false;
                case SequencePattern seq:
                    return MatchSequence(seq, subject, env, ctx);
                case MappingPattern m:
                    return MatchMapping(m, subject, env, ctx);
                case ClassPattern cp:
                    return MatchClass(cp, subject, env, ctx);
                default:
                    return false;
            }
        }

        private bool MatchClass(ClassPattern cp, ScriptValue subject, Environment env, EvalContext ctx)
        {
            ScriptValue clsVal = EvalExpr(cp.Cls, env, ctx);
            TypeValue tv = clsVal as TypeValue;
            if (tv != null)
                return MatchBuiltinClass(cp, tv, subject, env, ctx);
            RecordClassValue rc = clsVal as RecordClassValue;
            if (rc == null)
                throw Raise.TypeError(ctx, "called match pattern must be a class");
            RecordInstanceValue inst = subject as RecordInstanceValue;
            if (inst == null || !inst.Class.IsSubclassOf(rc))
                return false;

            if (cp.Positional.Count > 0)
            {
                string[] ma = rc.MatchArgs;
                if (ma == null)
                    throw Raise.TypeError(ctx, rc.Name + "() accepts no positional sub-patterns");
                if (cp.Positional.Count > ma.Length)
                    throw Raise.TypeError(ctx, rc.Name + "() accepts " + ma.Length + " positional sub-patterns (" + cp.Positional.Count + " given)");
                for (int i = 0; i < cp.Positional.Count; i++)
                {
                    ScriptValue attr;
                    if (!TryGetAttr(inst, ma[i], ctx, out attr) || !TryMatch(cp.Positional[i], attr, env, ctx))
                        return false;
                }
            }
            for (int i = 0; i < cp.KwNames.Count; i++)
            {
                ScriptValue attr;
                if (!TryGetAttr(inst, cp.KwNames[i], ctx, out attr) || !TryMatch(cp.KwPatterns[i], attr, env, ctx))
                    return false;
            }
            return true;
        }

        // Builtin class pattern (PEP 634): isinstance check, then the self-matching types (int, str, list, ...)
        // take one positional sub-pattern that matches the subject itself; keyword sub-patterns read attributes.
        private bool MatchBuiltinClass(ClassPattern cp, TypeValue tv, ScriptValue subject, Environment env, EvalContext ctx)
        {
            if (!Builtins.IsInstanceOf(subject, tv))
                return false;
            if (cp.Positional.Count > 0)
            {
                if (!IsSelfMatching(tv.Name))
                    throw Raise.TypeError(ctx, tv.Name + "() accepts 0 positional sub-patterns (" + cp.Positional.Count + " given)");
                if (cp.Positional.Count > 1)
                    throw Raise.TypeError(ctx, tv.Name + "() accepts 1 positional sub-pattern (" + cp.Positional.Count + " given)");
                if (!TryMatch(cp.Positional[0], subject, env, ctx))
                    return false;
            }
            for (int i = 0; i < cp.KwNames.Count; i++)
            {
                ScriptValue attr;
                try
                {
                    attr = PyOps.GetAttr(subject, cp.KwNames[i], ctx);
                }
                catch (ScriptException se) when (ReferenceEquals(se.Value.ExcType, PyExceptionTypes.AttributeError))
                {
                    return false;
                }
                if (!TryMatch(cp.KwPatterns[i], attr, env, ctx))
                    return false;
            }
            return true;
        }

        private static bool IsSelfMatching(string typeName)
        {
            switch (typeName)
            {
                case "bool": case "bytearray": case "bytes": case "dict": case "float": case "frozenset":
                case "int": case "list": case "set": case "str": case "tuple":
                    return true;
                default:
                    return false;
            }
        }

        // A missing/unset attribute makes the class pattern fail (PEP 634), not raise.
        private static bool TryGetAttr(RecordInstanceValue inst, string name, EvalContext ctx, out ScriptValue value)
        {
            try
            {
                value = inst.GetAttribute(name, ctx);
                return true;
            }
            catch (ScriptException se) when (ReferenceEquals(se.Value.ExcType, PyExceptionTypes.AttributeError))
            {
                value = null;
                return false;
            }
        }

        private bool MatchLiteral(ExprNode literal, ScriptValue subject, Environment env, EvalContext ctx)
        {
            ScriptValue pv = EvalExpr(literal, env, ctx);
            // None/True/False match by identity of kind; numeric/string literals match by equality.
            if (pv.Kind == ValueKind.None)
                return subject.Kind == ValueKind.None;
            if (pv.Kind == ValueKind.Bool)
                return subject.Kind == ValueKind.Bool && ((BoolValue)subject).Value == ((BoolValue)pv).Value;
            return PyOps.Equals(subject, pv, ctx, 0);
        }

        private bool MatchSequence(SequencePattern seq, ScriptValue subject, Environment env, EvalContext ctx)
        {
            // Only list/tuple/range match a sequence pattern (str/dict/set never do).
            if (subject.Kind != ValueKind.List && subject.Kind != ValueKind.Tuple && subject.Kind != ValueKind.Range)
                return false;
            long len = PyOps.Length(subject, ctx);
            int count = seq.Elements.Count;

            if (seq.StarIndex < 0)
            {
                if (len != count)
                    return false;
                for (int i = 0; i < count; i++)
                    if (!TryMatch(seq.Elements[i], SeqItem(subject, i, ctx), env, ctx))
                        return false;
                return true;
            }

            int nBefore = seq.StarIndex;
            int nAfter = count - seq.StarIndex - 1;
            if (len < nBefore + nAfter)
                return false;
            for (int i = 0; i < nBefore; i++)
                if (!TryMatch(seq.Elements[i], SeqItem(subject, i, ctx), env, ctx))
                    return false;
            for (int j = 0; j < nAfter; j++)
                if (!TryMatch(seq.Elements[seq.StarIndex + 1 + j], SeqItem(subject, len - nAfter + j, ctx), env, ctx))
                    return false;
            NameNode starTarget = ((StarPattern)seq.Elements[seq.StarIndex]).Target;
            if (starTarget != null)
            {
                long midLen = len - nBefore - nAfter;
                ListValue mid = ctx.Values.List((int)(midLen < 0 ? 0 : midLen));
                for (long i = nBefore; i < len - nAfter; i++)
                    mid.Add(SeqItem(subject, i, ctx), ctx);
                SetName(starTarget, mid, env);
            }
            return true;
        }

        private bool MatchMapping(MappingPattern m, ScriptValue subject, Environment env, EvalContext ctx)
        {
            DictValue dict = subject as DictValue;
            if (dict == null)
                return false;
            var matchedKeys = new List<ScriptValue>(m.Keys.Count);
            for (int i = 0; i < m.Keys.Count; i++)
            {
                ScriptValue key = EvalExpr(m.Keys[i], env, ctx);
                ScriptValue val;
                if (!dict.TryGet(key, ctx, out val))
                    return false;
                if (!TryMatch(m.Values[i], val, env, ctx))
                    return false;
                matchedKeys.Add(key);
            }
            if (m.Rest != null)
            {
                DictValue rest = ctx.Values.Dict(dict.Count);
                IScriptIterator it = PyOps.GetIterator(dict, ctx);
                ScriptValue k;
                while (it.MoveNext(ctx, out k))
                {
                    if (ContainsEqual(matchedKeys, k, ctx))
                        continue;
                    rest.SetItem(k, dict.GetItemOrThrow(k, ctx), ctx);
                }
                SetName(m.Rest, rest, env);
            }
            return true;
        }

        private static bool ContainsEqual(List<ScriptValue> keys, ScriptValue k, EvalContext ctx)
        {
            for (int i = 0; i < keys.Count; i++)
                if (PyOps.Equals(keys[i], k, ctx, 0))
                    return true;
            return false;
        }

        private ScriptValue SeqItem(ScriptValue seq, long i, EvalContext ctx)
        {
            switch (seq.Kind)
            {
                case ValueKind.List: return ((ListValue)seq).Items[(int)i];
                case ValueKind.Tuple: return ((TupleValue)seq).Items[(int)i];
                default: return GetItemByKind(seq, ctx.Values.Int(new BigInteger(i)), ctx);   // Range
            }
        }
    }
}
