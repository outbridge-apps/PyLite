using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // The single call point + script-function argument binding. Call is the ONLY
    // place CallDepth/probe/Step are charged; the decrement is in a finally so it never leaks on a throw.
    internal sealed class SlotCache
    {
        internal readonly ScriptTypeInfo Type;
        internal readonly SlotDescriptor Slot;
        internal SlotCache(ScriptTypeInfo type, SlotDescriptor slot) { Type = type; Slot = slot; }
    }

    internal sealed partial class Evaluator
    {
        private ScriptValue EvalCall(CallNode n, Environment env, EvalContext ctx)
        {
            // Call-site fusion: obj.method(...) invokes the slot descriptor directly — no BoundMethod
            // materialized per call. Bare `m = obj.method` still creates one via GetAttr.
            if (n.Func.NodeKind == NodeKind.Attribute)
            {
                AttributeNode at = (AttributeNode)n.Func;
                ScriptValue self = EvalExpr(at.Value, env, ctx);

                // The #1 script idiom, list.append(x): ListValue is sealed, so Kind==List is exactly
                // the builtin list and append cannot be shadowed — add directly, skipping the slot
                // lookup, the args array and the call prologue. The Call step keeps budget parity;
                // no reentry happens, so the stack probe is not needed.
                if (self.Kind == ValueKind.List && n.Args.Count == 1
                    && n.Args[0].Kind == ArgKind.Positional && at.Attr == "append")
                {
                    ScriptValue arg = EvalExpr(n.Args[0].Value, env, ctx);
                    ctx.Budget.Step(BudgetCost.Call);
                    ((ListValue)self).Add(arg, ctx);
                    return ctx.Values.None;
                }

                // Monomorphic inline cache: one (TypeInfo -> descriptor) pair per call site. A single
                // object write keeps concurrent readers safe (Slots tables are immutable, TypeInfo
                // instances are static per value type).
                SlotDescriptor d;
                SlotCache cache = n.CachedCallSlot as SlotCache;
                if (cache != null && ReferenceEquals(cache.Type, self.TypeInfo))
                {
                    d = cache.Slot;
                }
                else if (self.TypeInfo.Slots.TryGetValue(at.Attr, out d) && d.Kind == SlotKind.Method)
                {
                    n.CachedCallSlot = new SlotCache(self.TypeInfo, d);
                }
                else
                {
                    d = null;
                }
                if (d != null)
                {
                    KwArgs mkw;
                    ScriptValue[] margs = EvalCallArgs(n, env, ctx, out mkw);
                    return CallDescriptor(d, self, margs, mkw, ctx);
                }
                return DispatchCall(n, AttrOnValue(self, at.Attr, ctx), env, ctx);
            }
            return DispatchCall(n, EvalExpr(n.Func, env, ctx), env, ctx);
        }

        private ScriptValue DispatchCall(CallNode n, ScriptValue callee, Environment env, EvalContext ctx)
        {
            // Script-function calls with <= 2 plain positional args skip the args array: the values
            // ride in C# locals straight into the frame slots (reentrancy-safe by construction —
            // nested calls evaluate in their own frames). Callee-then-args order is preserved; any
            // signature the direct path can't bind materializes the array and takes the general path.
            if (callee.Kind == ValueKind.Function && n.Args.Count <= 2
                && (n.Args.Count < 1 || n.Args[0].Kind == ArgKind.Positional)
                && (n.Args.Count < 2 || n.Args[1].Kind == ArgKind.Positional))
            {
                int argc = n.Args.Count;
                ScriptValue a0 = argc >= 1 ? EvalExpr(n.Args[0].Value, env, ctx) : null;
                ScriptValue a1 = argc == 2 ? EvalExpr(n.Args[1].Value, env, ctx) : null;
                FunctionValue dfv = (FunctionValue)callee;
                return OwnerOf(dfv.Program).CallFunctionDirect(dfv, argc, a0, a1, ctx);
            }
            KwArgs kw;
            ScriptValue[] args = EvalCallArgs(n, env, ctx, out kw);
            return Call(callee, args, kw, ctx);
        }

        // Same prologue as Call() (depth/probe/step), then the descriptor body — mirrors CallBound.
        private ScriptValue CallDescriptor(SlotDescriptor d, ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ctx.CallDepth++;
            if (ctx.CallDepth > ctx.Limits.MaxCallDepth)
            {
                ctx.CallDepth--;
                throw ctx.Budget.CreateAbort(EngineAbortKind.Recursion, "MaxCallDepth", ctx.Limits.MaxCallDepth, ctx.CallDepth + 1);
            }
            StackGuard.Probe(ctx);
            ctx.Budget.Step(BudgetCost.Call);
            if (ctx.CallDepth > ctx.Stats.PeakCallDepth)
            {
                ctx.Stats.PeakCallDepth = ctx.CallDepth;
            }
            try
            {
                return d.Method(self, args, kw, ctx);
            }
            catch (System.OverflowException)
            {
                throw OverflowEscaped(ctx);
            }
            finally
            {
                ctx.CallDepth--;
            }
        }

        private ScriptValue[] EvalCallArgs(CallNode n, Environment env, EvalContext ctx, out KwArgs kw)
        {
            // Fast path: no */** at the call site — sizes are static, fill the arrays directly
            // (no List+ToArray). Source order of evaluation is preserved (positionals precede keywords).
            int argc = n.Args.Count;
            int kwCount = 0;
            bool hasStars = false;
            for (int i = 0; i < argc; i++)
            {
                ArgKind kind = n.Args[i].Kind;
                if (kind == ArgKind.Keyword)
                {
                    kwCount++;
                }
                else if (kind != ArgKind.Positional)
                {
                    hasStars = true;
                    break;
                }
            }
            if (!hasStars)
            {
                var direct = new ScriptValue[argc - kwCount];
                string[] names = kwCount > 0 ? new string[kwCount] : null;
                ScriptValue[] vals = kwCount > 0 ? new ScriptValue[kwCount] : null;
                int pi = 0, ki = 0;
                for (int i = 0; i < argc; i++)
                {
                    Arg a = n.Args[i];
                    if (a.Kind == ArgKind.Positional)
                    {
                        direct[pi++] = EvalExpr(a.Value, env, ctx);
                    }
                    else
                    {
                        names[ki] = a.Keyword;
                        vals[ki++] = EvalExpr(a.Value, env, ctx);
                    }
                }
                kw = kwCount == 0 ? KwArgs.Empty : KwArgs.FromArrays(names, vals);
                return direct;
            }
            string fname = CalleeName(n.Func);
            var pos = new List<ScriptValue>(n.Args.Count);
            List<string> kwNames = null;
            List<ScriptValue> kwVals = null;
            HashSet<string> kwSeen = null;   // O(1) dup detection across keywords and ** merges

            for (int i = 0; i < n.Args.Count; i++)
            {
                Arg arg = n.Args[i];
                switch (arg.Kind)
                {
                    case ArgKind.Positional:
                        pos.Add(EvalExpr(arg.Value, env, ctx));
                        break;
                    case ArgKind.Star:
                        ExpandStar(EvalExpr(arg.Value, env, ctx), pos, fname, ctx);
                        break;
                    case ArgKind.Keyword:
                    {
                        EnsureKw(ref kwNames, ref kwVals, ref kwSeen);
                        // CPython order: the value is evaluated before the duplicate check fires.
                        ScriptValue kv = EvalExpr(arg.Value, env, ctx);
                        if (!kwSeen.Add(arg.Keyword))
                        {
                            throw MultipleKwValues(ctx, fname, arg.Keyword);
                        }
                        kwNames.Add(arg.Keyword);
                        kwVals.Add(kv);
                        break;
                    }
                    default:   // ArgKind.DoubleStar
                        EnsureKw(ref kwNames, ref kwVals, ref kwSeen);
                        ExpandDoubleStar(EvalExpr(arg.Value, env, ctx), kwNames, kwVals, kwSeen, fname, ctx);
                        break;
                }
            }

            kw = kwNames == null ? KwArgs.Empty : KwArgs.FromArrays(kwNames.ToArray(), kwVals.ToArray());
            return pos.ToArray();
        }

        private static void EnsureKw(ref List<string> names, ref List<ScriptValue> values, ref HashSet<string> seen)
        {
            if (names == null)
            {
                names = new List<string>();
                values = new List<ScriptValue>();
                seen = new HashSet<string>(System.StringComparer.Ordinal);
            }
        }

        private void ExpandStar(ScriptValue star, List<ScriptValue> pos, string fname, EvalContext ctx)
        {
            IScriptIterator it = PyOps.TryGetIterator(star, ctx);
            if (it == null)
                throw Raise.TypeError(ctx, fname + "() argument after * must be a sequence, not " + star.PyTypeName);
            ScriptValue el;
            while (it.MoveNext(ctx, out el))
            {
                pos.Add(el);
            }
        }

        private void ExpandDoubleStar(ScriptValue mapping, List<string> names, List<ScriptValue> values,
            HashSet<string> seen, string fname, EvalContext ctx)
        {
            DictValue dd = mapping as DictValue;
            if (dd == null)
            {
                throw Raise.TypeError(ctx, fname + "() argument after ** must be a mapping, not " + mapping.PyTypeName);
            }
            IScriptIterator it = PyOps.GetIterator(dd, ctx);
            ScriptValue key;
            while (it.MoveNext(ctx, out key))
            {
                StrValue sk = key as StrValue;
                if (sk == null)
                {
                    throw Raise.TypeError(ctx, "keywords must be strings");
                }
                if (!seen.Add(sk.Value))
                {
                    throw MultipleKwValues(ctx, fname, sk.Value);
                }
                names.Add(sk.Value);
                values.Add(dd.GetItemOrThrow(key, ctx));
            }
        }

        private static string CalleeName(ExprNode func)
        {
            NameNode nm = func as NameNode;
            if (nm != null)
            {
                return nm.Id;
            }
            AttributeNode at = func as AttributeNode;
            if (at != null)
            {
                return at.Attr;
            }
            return "<callable>";
        }

        public ScriptValue Call(ScriptValue callee, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ctx.CallDepth++;
            if (ctx.CallDepth > ctx.Limits.MaxCallDepth)
            {
                ctx.CallDepth--;
                throw ctx.Budget.CreateAbort(EngineAbortKind.Recursion, "MaxCallDepth", ctx.Limits.MaxCallDepth, ctx.CallDepth + 1);
            }
            StackGuard.Probe(ctx);
            ctx.Budget.Step(BudgetCost.Call);
            if (ctx.CallDepth > ctx.Stats.PeakCallDepth)
            {
                ctx.Stats.PeakCallDepth = ctx.CallDepth;
            }
            // The callee's statements advance ctx.CurrentLine; restore the call-site line on the normal
            // return and on the ScriptException unwind (so each frame's traceback entry and any later
            // error in the SAME caller statement report the caller's line). EngineAbort deliberately
            // keeps the innermost line — "which statement burned the budget" is the useful datum.
            int callerLine = ctx.CurrentLine;
            try
            {
                ScriptValue r;
                switch (callee.Kind)
                {
                    case ValueKind.Function:
                        FunctionValue fv = (FunctionValue)callee;
                        r = OwnerOf(fv.Program).CallFunction(fv, args, kw, ctx);
                        break;
                    case ValueKind.BuiltinFunction:
                        r = CallBuiltin((BuiltinFunctionValue)callee, args, kw, ctx);
                        break;
                    case ValueKind.BoundMethod:
                        BoundUserMethodValue bum = callee as BoundUserMethodValue;
                        r = bum != null ? CallUserMethod(bum, args, kw, ctx) : CallBound((BoundMethodValue)callee, args, kw, ctx);
                        break;
                    case ValueKind.Type:
                        r = CallType((TypeValue)callee, args, kw, ctx);
                        break;
                    case ValueKind.RecordClass:
                        r = ConstructRecord((RecordClassValue)callee, args, kw, ctx);
                        break;
                    default:
                        if (callee.IsCallable)
                        {
                            r = callee.CallCore(args, kw, ctx);   // callable opaque (functools.partial)
                            break;
                        }
                        throw Raise.TypeError(ctx, "'" + callee.PyTypeName + "' object is not callable");
                }
                ctx.CurrentLine = callerLine;
                return r;
            }
            catch (ScriptException)
            {
                ctx.CurrentLine = callerLine;
                throw;
            }
            catch (System.OverflowException)
            {
                ctx.CurrentLine = callerLine;
                throw OverflowEscaped(ctx);
            }
            finally
            {
                ctx.CallDepth--;
            }
        }

        // The net under every builtin and slot method: an arithmetic overflow that escapes one (a cast
        // of a script's int past the C range that no guard caught) is the script's OverflowError, with
        // the text CPython's converters give, and never an engine fault the script cannot catch.
        private static ScriptException OverflowEscaped(EvalContext ctx)
        {
            return Raise.Overflow(ctx, "Python int too large to convert to C int");
        }

        private ScriptValue CallBuiltin(BuiltinFunctionValue fn, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (fn.IsHostProvided)
            {
                // A host override: fence it so an arbitrary C# exception becomes HostError, not EngineFault.
                return Outbridge.PyLite.Hosting.HostCallGuard.Invoke(fn.Name, fn.Fn, ctx, args, kw);
            }
            ctx.Budget.CheckDeadlineNow();
            ScriptValue r = fn.Fn(null, args, kw, ctx);   // free builtin: self == null
            ctx.Budget.CheckDeadlineNow();
            if (ctx.Budget.IsTerminating)
            {
                throw ctx.Budget.PendingAbort;
            }
            return r;
        }

        private ScriptValue CallBound(BoundMethodValue bound, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            return bound.Descriptor.Method(bound.Self, args, kw, ctx);   // self is passed separately (not in args)
        }

        // A bound record method: prepend the instance as the first positional (self) and run the function.
        private ScriptValue CallUserMethod(BoundUserMethodValue m, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            var full = new ScriptValue[args.Length + 1];
            full[0] = m.Self;
            System.Array.Copy(args, 0, full, 1, args.Length);
            return OwnerOf(m.Func.Program).CallFunction(m.Func, full, kw, ctx);
        }

        private ScriptValue CallType(TypeValue type, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (type.Constructor == null)
            {
                throw Raise.TypeError(ctx, "cannot create '" + type.Name + "' instances");
            }
            return type.Constructor(null, args, kw, ctx);
        }

        // --------- script function argument binding ----------

        // The P4 fast-bind body shared by the array path and the arrayless direct path: acquire a
        // frame (pooled when cell-free — such an Environment cannot escape the call, and the
        // allocation charge is kept so budget accounting matches the non-pooled path), bind the argc
        // leading params from either the array or the two direct values, fill defaults, run, release.
        private ScriptValue RunFastBind(FunctionValue fn, FunctionInfo info, ScriptValue[] argsOrNull,
            int argc, ScriptValue a0, ScriptValue a1, EvalContext ctx)
        {
            int pc = info.PositionalCount;
            int dl = fn.Defaults != null ? fn.Defaults.Length : 0;
            Environment fastEnv;
            bool pooled = false;
            Cell[] closure = fn.Closure ?? System.Array.Empty<Cell>();
            if (info.CellVars.Count == 0)
            {
                // Freelist acquire: each recursion depth pops its own frame; sibling subtrees reuse
                // frames the unwind pushed back. The allocation charge is kept on a hit so budget
                // accounting matches the non-pooled path.
                pooled = true;
                if (fn.PooledEnvCount > 0)
                {
                    ctx.Budget.ChargeAllocation(64 + 8L * (info.LocalSlotCount + closure.Length));
                    fastEnv = (Environment)fn.PooledEnvs[--fn.PooledEnvCount];
                    fn.PooledEnvs[fn.PooledEnvCount] = null;
                }
                else
                {
                    fastEnv = Environment.CreateFunction(ctx, info.LocalSlotCount, 0,
                        closure, (Environment)fn.GlobalsToken, info);
                }
            }
            else
            {
                fastEnv = Environment.CreateFunction(ctx, info.LocalSlotCount, info.CellVars.Count,
                    closure, (Environment)fn.GlobalsToken, info);
            }
            try
            {
                for (int i = 0; i < argc; i++)
                {
                    BindByIndex(fastEnv, info, i, argsOrNull != null ? argsOrNull[i] : (i == 0 ? a0 : a1));
                }
                int firstDef = pc - dl;
                for (int i = argc; i < pc; i++)
                {
                    BindByIndex(fastEnv, info, i, fn.Defaults[i - firstDef]);
                }
                return RunBody(fn, fastEnv, ctx);
            }
            finally
            {
                if (pooled)
                {
                    // Release: push back while there is room (recursion deeper than the cap
                    // allocates; its unwound frames refill the pool for the next subtree).
                    // ClearLocals keeps unbound-local semantics; a dropped frame just dies. The
                    // pool array itself is tiny and uncharged (the GlobalCacheEntry[] precedent).
                    object[] pool = fn.PooledEnvs;
                    if (pool == null)
                    {
                        fn.PooledEnvs = pool = new object[FramePoolCap];
                    }
                    if (fn.PooledEnvCount < pool.Length)
                    {
                        fastEnv.ClearLocals();
                        pool[fn.PooledEnvCount++] = fastEnv;
                    }
                }
            }
        }

        private const int FramePoolCap = 16;   // covers fib-style tree recursion; deeper levels allocate

        // Arrayless entry for <= 2 plain positional args (DispatchCall). Mirrors Call()'s prologue
        // exactly (depth guard / stack probe / step / peak stat / caller-line restore); an ineligible
        // signature materializes the array once and takes the general path, so errors and *args/kw
        // binding stay byte-identical.
        private ScriptValue CallFunctionDirect(FunctionValue fn, int argc, ScriptValue a0, ScriptValue a1, EvalContext ctx)
        {
            FunctionInfo info = fn.Info;
            int pc = info.PositionalCount;
            int dl = fn.Defaults != null ? fn.Defaults.Length : 0;
            if (info.KwOnlyCount != 0 || info.HasStarArg || info.HasKwArg || argc > pc || argc < pc - dl)
            {
                ScriptValue[] args = argc == 0 ? System.Array.Empty<ScriptValue>()
                    : argc == 1 ? new[] { a0 } : new[] { a0, a1 };
                return Call(fn, args, KwArgs.Empty, ctx);
            }
            ctx.CallDepth++;
            if (ctx.CallDepth > ctx.Limits.MaxCallDepth)
            {
                ctx.CallDepth--;
                throw ctx.Budget.CreateAbort(EngineAbortKind.Recursion, "MaxCallDepth", ctx.Limits.MaxCallDepth, ctx.CallDepth + 1);
            }
            StackGuard.Probe(ctx);
            ctx.Budget.Step(BudgetCost.Call);
            if (ctx.CallDepth > ctx.Stats.PeakCallDepth)
            {
                ctx.Stats.PeakCallDepth = ctx.CallDepth;
            }
            int callerLine = ctx.CurrentLine;
            try
            {
                ScriptValue r = RunFastBind(fn, info, null, argc, a0, a1, ctx);
                ctx.CurrentLine = callerLine;
                return r;
            }
            catch (ScriptException)
            {
                ctx.CurrentLine = callerLine;
                throw;
            }
            finally
            {
                ctx.CallDepth--;
            }
        }

        private ScriptValue CallFunction(FunctionValue fn, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            FunctionInfo info = fn.Info;

            // P4 fast path: plain positional call of a simple signature (no */**, no kw-only, no keywords,
            // every required positional supplied) — bind slots directly, skip the bookkeeping arrays.
            int pc = info.PositionalCount;
            int dl = fn.Defaults != null ? fn.Defaults.Length : 0;
            if (kw.Count == 0 && info.KwOnlyCount == 0 && !info.HasStarArg && !info.HasKwArg
                && args.Length <= pc && args.Length >= pc - dl)
            {
                return RunFastBind(fn, info, args, args.Length, null, null, ctx);
            }

            string fname = fn.Name;
            int p = info.PositionalCount;
            int k = info.KwOnlyCount;
            int starIdx = info.HasStarArg ? p : -1;
            int kwStart = p + (info.HasStarArg ? 1 : 0);
            int kwArgIdx = info.HasKwArg ? kwStart + k : -1;
            int defLen = fn.Defaults != null ? fn.Defaults.Length : 0;

            Environment fenv = Environment.CreateFunction(ctx, info.LocalSlotCount, info.CellVars.Count,
                fn.Closure ?? System.Array.Empty<Cell>(), (Environment)fn.GlobalsToken, info);

            // (2) positionals
            int nPos = args.Length;
            int nToPos = nPos < p ? nPos : p;
            var posFilled = new bool[p];
            for (int i = 0; i < nToPos; i++)
            {
                BindByIndex(fenv, info, i, args[i]);
                posFilled[i] = true;
            }

            // (3) excess positionals -> *args or error
            if (nPos > p)
            {
                if (starIdx >= 0)
                {
                    int extra = nPos - p;
                    var tail = new ScriptValue[extra];
                    System.Array.Copy(args, p, tail, 0, extra);
                    BindByIndex(fenv, info, starIdx, ctx.Values.Tuple(tail));
                }
                else
                {
                    throw TooManyPositional(ctx, fname, p, defLen, nPos);
                }
            }
            else if (starIdx >= 0)
            {
                BindByIndex(fenv, info, starIdx, ctx.Values.Tuple(System.Array.Empty<ScriptValue>()));
            }

            // (4) keywords -> params / kwonly / **kwargs
            DictValue kwargsDict = kwArgIdx >= 0 ? ctx.Values.Dict(kw.Count) : null;
            var kwFilled = new bool[k];
            for (int j = 0; j < kw.Count; j++)
            {
                string name = kw.NameAt(j);
                ScriptValue val = kw.ValueAt(j);
                // PEP 570: keywords cannot bind positional-only params (indices below PositionalOnlyCount).
                int pIdx = IndexOfName(info.ParamNames, name, info.PositionalOnlyCount, p);
                if (pIdx >= 0)
                {
                    if (posFilled[pIdx])
                    {
                        throw MultipleValues(ctx, fname, name);
                    }
                    BindByIndex(fenv, info, pIdx, val);
                    posFilled[pIdx] = true;
                    continue;
                }
                int kIdx = IndexOfName(info.ParamNames, name, kwStart, kwStart + k);
                if (kIdx >= 0)
                {
                    BindByIndex(fenv, info, kIdx, val);
                    kwFilled[kIdx - kwStart] = true;
                    continue;
                }
                if (kwargsDict != null)
                {
                    kwargsDict.SetItem(ctx.Values.Str(name), val, ctx);
                }
                else if (IndexOfName(info.ParamNames, name, 0, info.PositionalOnlyCount) >= 0)
                {
                    throw Raise.TypeError(ctx, fname
                        + "() got some positional-only arguments passed as keyword arguments: '" + name + "'");
                }
                else
                {
                    throw UnexpectedKeyword(ctx, fname, name);
                }
            }
            if (kwArgIdx >= 0)
            {
                BindByIndex(fenv, info, kwArgIdx, kwargsDict);
            }

            // (5) positional defaults (right-aligned)
            int firstDefault = p - defLen;
            List<string> missing = null;
            for (int i = 0; i < p; i++)
            {
                if (posFilled[i])
                {
                    continue;
                }
                if (i >= firstDefault)
                {
                    BindByIndex(fenv, info, i, fn.Defaults[i - firstDefault]);
                }
                else
                {
                    (missing ?? (missing = new List<string>())).Add(info.ParamNames[i]);
                }
            }
            if (missing != null)
            {
                throw MissingRequired(ctx, fname, missing, "positional");
            }

            // (6) keyword-only defaults
            List<string> missingKw = null;
            for (int i = 0; i < k; i++)
            {
                if (kwFilled[i])
                {
                    continue;
                }
                string kname = info.ParamNames[kwStart + i];
                ScriptValue dv = FindKwDefault(fn.KwDefaults, kname);
                if (dv != null)
                {
                    BindByIndex(fenv, info, kwStart + i, dv);
                }
                else
                {
                    (missingKw ?? (missingKw = new List<string>())).Add(kname);
                }
            }
            if (missingKw != null)
            {
                throw MissingRequired(ctx, fname, missingKw, "keyword-only");
            }

            // (7) execute
            return RunBody(fn, fenv, ctx);
        }

        // The single body-execution point (both binding paths). A ScriptException leaving the body gets
        // exactly one traceback frame — this function at the line IT was executing (inner Call catches
        // already restored CurrentLine past deeper callees). Binding errors are raised before RunBody,
        // so they carry no callee frame — CPython parity. EngineAbort is not a ScriptException and
        // collects no frames.
        private ScriptValue RunBody(FunctionValue fn, Environment fenv, EvalContext ctx)
        {
            string callerFn = ctx.CurrentFunctionName;   // names the frame an except handler stamps
            ctx.CurrentFunctionName = fn.Name;
            try
            {
                if (fn.Info.Kind == ScopeKind.Lambda)
                {
                    // An expression-whitelist lambda body rides the closure-compiled form (T2.1
                    // machinery; cache on the shared FunctionInfo), skipping per-node dispatch on
                    // every call — filter/map/sort-key predicates are exactly this shape.
                    CompiledEval bodyFn = GetCompiledExpr(ref fn.Info.CompiledLambdaBody, fn.LambdaBody);
                    return bodyFn != null ? bodyFn(this, fenv, ctx) : EvalExpr(fn.LambdaBody, fenv, ctx);
                }
                ExecSignal sig = ExecBlock(fn.Body, fenv, ctx);
                if (sig.Kind == ExecSignalKind.Raised)
                {
                    // The function boundary: an uncaught Raised signal becomes the carrier the caller's
                    // C# stack expects; the catch below appends this frame like any thrown exception.
                    throw new ScriptException(sig.Raised);
                }
                return sig.Kind == ExecSignalKind.Return ? sig.ReturnValue : ctx.Values.None;
            }
            catch (ScriptException se)
            {
                TracebackBuilder.AppendFrame(se.Value, fn.Name, FrameLine(se.Value, fn, ctx));
                throw;
            }
            finally
            {
                ctx.CurrentFunctionName = callerFn;
            }
        }

        // The line for a frame being appended: a lambda always reports its body's own line (its
        // expression never updates CurrentLine, and a lambda cannot contain a raise statement); the
        // FIRST frame otherwise uses the recorded raise site (survives a bare re-raise in the same
        // function); every later frame is the line that frame was executing.
        private static int FrameLine(ExceptionValue exc, FunctionValue fn, EvalContext ctx)
        {
            if (fn.Info.Kind == ScopeKind.Lambda)
            {
                return fn.LambdaBody.Src.Line;
            }
            if (exc.TracebackFrames == null && exc.RaiseLine > 0)
            {
                return exc.RaiseLine;
            }
            return ctx.CurrentLine;
        }

        private void BindByIndex(Environment fenv, FunctionInfo info, int paramIndex, ScriptValue value)
        {
            if (info.ParamIsCell[paramIndex])
            {
                fenv.SetCell(info.ParamSlots[paramIndex], value);
            }
            else
            {
                fenv.SetLocal(info.ParamSlots[paramIndex], value);
            }
        }

        private static int IndexOfName(IReadOnlyList<string> names, string name, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (names[i] == name)
                {
                    return i;
                }
            }
            return -1;
        }

        private static ScriptValue FindKwDefault(KeyValuePair<string, ScriptValue>[] kwDefaults, string name)
        {
            if (kwDefaults != null)
            {
                for (int i = 0; i < kwDefaults.Length; i++)
                {
                    if (kwDefaults[i].Key == name)
                    {
                        return kwDefaults[i].Value;
                    }
                }
            }
            return null;
        }

        // --------- CPython parity error texts (wording advisory) ----------

        private ScriptException TooManyPositional(EvalContext ctx, string fname, int posCount, int defCount, int got)
        {
            int maxN = posCount;
            int minN = posCount - defCount;
            string were = got == 1 ? "was" : "were";
            string takes = minN == maxN
                ? "takes " + maxN + " positional argument" + (maxN == 1 ? "" : "s")
                : "takes from " + minN + " to " + maxN + " positional arguments";
            return Raise.TypeError(ctx, fname + "() " + takes + " but " + got + " " + were + " given");
        }

        private ScriptException MissingRequired(EvalContext ctx, string fname, List<string> names, string kind)
        {
            string s = names.Count == 1 ? "" : "s";
            return Raise.TypeError(ctx, fname + "() missing " + names.Count + " required " + kind
                + " argument" + s + ": " + FormatNameList(names));
        }

        private ScriptException MultipleValues(EvalContext ctx, string fname, string name)
        {
            return Raise.TypeError(ctx, fname + "() got multiple values for argument '" + name + "'");
        }

        // A collision between keyword sources (** vs explicit / ** vs **) — CPython says "keyword argument"
        // here, vs plain "argument" for a positional-vs-keyword collision.
        private ScriptException MultipleKwValues(EvalContext ctx, string fname, string name)
        {
            return Raise.TypeError(ctx, fname + "() got multiple values for keyword argument '" + name + "'");
        }

        private ScriptException UnexpectedKeyword(EvalContext ctx, string fname, string name)
        {
            return Modules.Support.KwReader.UnexpectedError(ctx, fname, name);
        }

        private static string FormatNameList(List<string> names)
        {
            int n = names.Count;
            if (n == 1)
            {
                return "'" + names[0] + "'";
            }
            if (n == 2)
            {
                return "'" + names[0] + "' and '" + names[1] + "'";
            }
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                if (i == n - 1)
                {
                    sb.Append("and ");
                }
                sb.Append("'").Append(names[i]).Append("'");
            }
            return sb.ToString();
        }
    }
}
