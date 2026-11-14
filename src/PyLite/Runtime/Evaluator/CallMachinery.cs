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
                ScriptValue superResult;
                if (at.Value.NodeKind == NodeKind.Call && TrySuperCall(n, at, (CallNode)at.Value, env, ctx, out superResult))
                    return superResult;
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

                // obj.m() / obj.m(x) on a record instance runs the method with self as the first argument:
                // no bound method, no argument arrays. The method is looked up before the argument runs.
                if (self.Kind == ValueKind.RecordInstance)
                {
                    FunctionValue method = FindRecordMethod(at, (RecordInstanceValue)self, ctx);
                    if (method != null)
                    {
                        if (n.Args.Count <= 1 && (n.Args.Count == 0 || n.Args[0].Kind == ArgKind.Positional))
                        {
                            ScriptValue a1 = n.Args.Count == 1 ? EvalExpr(n.Args[0].Value, env, ctx) : null;
                            return OwnerOf(method.Program, ctx).CallFunctionDirect(method, n.Args.Count + 1, self, a1, ctx);
                        }
                        // any other argument list: self first, still without a bound method per call (a 2-argument
                        // method called 1M times outgrew the default MaxAllocBytes that 1.1.0 fitted)
                        KwArgs rkw;
                        ScriptValue[] rargs = EvalCallArgs(n, env, ctx, out rkw);
                        var full = new ScriptValue[rargs.Length + 1];
                        full[0] = self;
                        System.Array.Copy(rargs, 0, full, 1, rargs.Length);
                        return Call(method, full, rkw, ctx);
                    }
                }

                // Monomorphic inline cache: one (TypeInfo -> descriptor) pair per call site. A single
                // object write keeps concurrent readers safe (Slots tables are immutable, TypeInfo
                // instances are static per value type, or per namedtuple class and released at run end).
                SlotDescriptor d;
                SlotCache cache = n.CachedCallSlot as SlotCache;
                if (cache != null && ReferenceEquals(cache.Type, self.TypeInfo))
                {
                    d = cache.Slot;
                }
                else if (self.TypeInfo.Slots.TryGetValue(at.Attr, out d) && d.Kind == SlotKind.Method)
                {
                    var entry = new SlotCache(self.TypeInfo, d);
                    n.CachedCallSlot = entry;
                    // A type this run created: whatever another run wrote here before, this run clears its
                    // own last entry when it ends (a site skipped because it held another run's type kept
                    // a namedtuple of 21 MB after both runs ended).
                    if (self.TypeInfo.IsScriptDefined)
                        (ctx.RunTypedCallSites ?? (ctx.RunTypedCallSites = new Dictionary<CallNode, object>()))[n] = entry;
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

        private static readonly BuiltinDelegate SuperDelegate = Modules.Builtins.SuperFn;

        // super().m(...) as the parser writes it, super(<class>, <self>).m(...), with the builtin super: the method runs
        // with self first, and neither the super object nor a bound method is made (1M super().__init__ calls no longer
        // outgrew the default MaxAllocBytes that 1.1.0 fitted). Anything else is false, and the call site goes on as before.
        private bool TrySuperCall(CallNode n, AttributeNode at, CallNode inner, Environment env, EvalContext ctx, out ScriptValue result)
        {
            result = null;
            if (inner.Func.NodeKind != NodeKind.Name || ((NameNode)inner.Func).Id != "super" || inner.Args.Count != 2
                || inner.Args[0].Kind != ArgKind.Positional || inner.Args[1].Kind != ArgKind.Positional)
                return false;
            BuiltinFunctionValue bf = EvalExpr(inner.Func, env, ctx) as BuiltinFunctionValue;
            if (bf == null || bf.IsHostProvided || bf.Fn != SuperDelegate)
                return false;
            ScriptValue cls = EvalExpr(inner.Args[0].Value, env, ctx);
            ScriptValue obj = EvalExpr(inner.Args[1].Value, env, ctx);
            ctx.Budget.Step(BudgetCost.Call);   // the super() call this replaces
            RecordClassValue k;
            RecordInstanceValue inst;
            Modules.Builtins.ResolveSuper(ctx, new[] { cls, obj }, KwArgs.Empty, out k, out inst);
            FunctionValue fn = SuperValue.PlainMethod(k, at.Attr);
            if (fn == null)
            {
                ctx.Values.ChargeObject(2);
                result = DispatchCall(n, AttrOnValue(new SuperValue(k, inst), at.Attr, ctx), env, ctx);
                return true;
            }
            if (n.Args.Count <= 1 && (n.Args.Count == 0 || n.Args[0].Kind == ArgKind.Positional))
            {
                ScriptValue a1 = n.Args.Count == 1 ? EvalExpr(n.Args[0].Value, env, ctx) : null;
                result = OwnerOf(fn.Program, ctx).CallFunctionDirect(fn, n.Args.Count + 1, inst, a1, ctx);
                return true;
            }
            KwArgs kw;
            ScriptValue[] args = EvalCallArgs(n, env, ctx, out kw);
            var full = new ScriptValue[args.Length + 1];
            full[0] = inst;
            System.Array.Copy(args, 0, full, 1, args.Length);
            result = Call(fn, full, kw, ctx);   // the call prologue: depth, stack probe, the caller's line on return
            return true;
        }

        // FindMethod, kept per call site: the same class, no class attribute assigned since, the instance field of
        // that name (if the class has one) still unset.
        private FunctionValue FindRecordMethod(AttributeNode at, RecordInstanceValue ri, EvalContext ctx)
        {
            MethodSite m = SiteOf(at) as MethodSite;
            if (m != null && ReferenceEquals(m.Node, at) && ReferenceEquals(m.Class, ri.Class) && m.Version == ctx.ClassAttrVersion
                && (m.FieldIndex < 0 || ri.Slots[m.FieldIndex] == null))
                return m.Method;
            FunctionValue method = ri.FindMethod(at.Attr);
            if (method != null)
            {
                int idx;
                SetSite(at, new MethodSite(at, ri.Class, ctx.ClassAttrVersion,
                    ri.Class.SlotIndex.TryGetValue(at.Attr, out idx) ? idx : -1, method));
            }
            return method;
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
                return OwnerOf(dfv.Program, ctx).CallFunctionDirect(dfv, argc, a0, a1, ctx);
            }
            KwArgs kw;
            ScriptValue[] args = EvalCallArgs(n, env, ctx, out kw);
            return Call(callee, args, kw, ctx);
        }

        // Same prologue as Call() (depth/probe/step), then the descriptor body — mirrors CallBound.
        private ScriptValue CallDescriptor(SlotDescriptor d, ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            EnterCall(ctx);
            try
            {
                return d.Call(self, args, kw, ctx);
            }
            catch (System.OverflowException)
            {
                throw OverflowEscaped(ctx);
            }
            finally
            {
                ctx.CallDepth--;
                ctx.Budget.CheckDeadlineAfterCall();   // a raising call in a loop is work too
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

        // The copy lives as long as its call. Calls at one depth follow each other, so a depth pays its largest copy
        // once (a loop of f(*big) pays one); copies alive together sit at different depths (min(*a, key=...) recursing)
        // and each is paid, but the shallowest: a lone copy beside its charged list was free in 1.1.0 and stays so.
        private static void PayStarCopy(EvalContext ctx, long bytes)
        {
            int d = ctx.CallDepth;
            if (d <= ctx.StarCopyFreeDepth)
            {
                ctx.StarCopyFreeDepth = d;
                return;
            }
            if (ctx.StarCopyPaid == null)
                ctx.StarCopyPaid = new long[System.Math.Max(ctx.Limits.MaxCallDepth, 0) + 2];
            if (d < 0 || d >= ctx.StarCopyPaid.Length)
            {
                ctx.Values.PreCharge(bytes);
                return;
            }
            if (bytes <= ctx.StarCopyPaid[d])
                return;
            ctx.Values.PreCharge(bytes - ctx.StarCopyPaid[d]);
            ctx.StarCopyPaid[d] = bytes;
        }

        private void ExpandStar(ScriptValue star, List<ScriptValue> pos, string fname, EvalContext ctx)
        {
            IScriptIterator it = PyOps.TryGetIterator(star, ctx);
            if (it == null)
                throw Raise.TypeError(ctx, fname + "() argument after * must be a sequence, not " + star.PyTypeName);
            // A list or tuple: its passing copy is sized once (no doubling) and paid once per call depth.
            ListValue sl = star as ListValue;
            TupleValue st = star as TupleValue;
            long known = sl != null ? sl.Items.Count : st != null ? st.Items.Length : 0;
            if (known > 0)
            {
                ctx.Values.EnsureCollectionSize(pos.Count + known);
                if (pos.Capacity < pos.Count + known)
                {
                    PayStarCopy(ctx, 8L * (pos.Count + known));
                    pos.Capacity = (int)(pos.Count + known);
                }
            }
            ScriptValue el;
            while (it.MoveNext(ctx, out el))
            {
                // f(*itertools.repeat(x)) grew this list without a charge or a cap (1.6 GB at default limits)
                if (pos.Count == pos.Capacity)
                {
                    ctx.Values.EnsureCollectionSize(pos.Count + 1L);
                    ctx.Values.PreCharge(8L * System.Math.Max(4, pos.Count));
                    pos.Capacity = (int)System.Math.Min(System.Math.Max(4L, 2L * pos.Count), ctx.Limits.MaxCollectionItems);   // doubling must not jump the cap
                }
                pos.Add(el);
            }
        }

        // CPython's _PyEval_FormatKwargsError: a KeyError with one argument out of the merge (keys(), its iteration,
        // __getitem__) reads as a repeated keyword, named by that argument.
        private void ExpandDoubleStar(ScriptValue mapping, List<string> names, List<ScriptValue> values,
            HashSet<string> seen, string fname, EvalContext ctx)
        {
            try
            {
                MergeDoubleStar(mapping, names, values, seen, fname, ctx);
            }
            catch (ScriptException e) when (e.Value.ExcType.IsSubtypeOf(PyExceptionTypes.KeyError) && e.Value.Args.Length == 1)
            {
                throw MultipleKwValues(ctx, fname, e.Value.Args[0].Str(ctx));
            }
        }

        private void MergeDoubleStar(ScriptValue mapping, List<string> names, List<ScriptValue> values,
            HashSet<string> seen, string fname, EvalContext ctx)
        {
            // CPython: anything with keys() is a mapping, read through its __getitem__ (what {**x} takes as well)
            ChainMapValue chain = mapping as ChainMapValue;
            if (chain != null)
            {
                mapping = chain.Merged(ctx);
            }
            DictValue dd = mapping as DictValue;
            ScriptValue keysFn = null;
            if (dd == null && !(mapping is RecordInstanceValue && PyOps.TryGetAttr(mapping, "keys", ctx, out keysFn)))
            {
                throw Raise.TypeError(ctx, fname + "() argument after ** must be a mapping, not " + mapping.PyTypeName);
            }
            IScriptIterator it;
            if (dd != null)
            {
                it = PyOps.GetIterator(dd, ctx);
            }
            else
            {
                ScriptValue keys = ctx.CallHook(keysFn, System.Array.Empty<ScriptValue>(), KwArgs.Empty);
                try
                {
                    it = PyOps.TryGetIterator(keys, ctx);
                }
                catch (ScriptException e) when (e.Value.ExcType.IsSubtypeOf(PyExceptionTypes.TypeError))
                {
                    it = null;   // PyMapping_Keys: a TypeError out of iter(keys()) is the non-iterable error too
                }
                if (it == null)
                {
                    throw Raise.TypeError(ctx, mapping.PyTypeName + ".keys() returned a non-iterable (type " + keys.PyTypeName + ")");
                }
            }
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
                values.Add(dd != null ? dd.GetItemOrThrow(key, ctx) : PyOps.GetItem(mapping, key, ctx));
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

        // The prologue every call path shares (Call, CallDescriptor, the arrayless fast call): the depth cap,
        // the C# stack probe, the call step and the peak-depth statistic. The caller decrements CallDepth. The depth is
        // raised only once nothing here can throw: a throw comes before the caller's finally, and a host that swallowed
        // such an abort left the depth one high (its hang then read as script code, waited for 2 minutes).
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void EnterCall(EvalContext ctx)
        {
            int depth = ctx.CallDepth + 1;
            if (depth > ctx.Limits.MaxCallDepth)
                throw DepthAbort(ctx, depth);
            StackGuard.Probe(ctx, depth);
            ctx.Budget.Step(BudgetCost.Call);
            ctx.CallDepth = depth;
            if (depth > ctx.Stats.PeakCallDepth)
                ctx.Stats.PeakCallDepth = depth;
        }

        private static EngineAbort DepthAbort(EvalContext ctx, int depth)
        {
            return ctx.Budget.CreateAbort(EngineAbortKind.Recursion, "MaxCallDepth", ctx.Limits.MaxCallDepth, depth);
        }

        // A plain positional call of a simple signature: no keyword-only, *args or **kwargs parameter, and
        // every required positional supplied. RunFastBind binds such a call into the slots directly.
        private static bool CanFastBind(FunctionValue fn, FunctionInfo info, int argc)
        {
            int defaults = fn.Defaults != null ? fn.Defaults.Length : 0;
            return info.KwOnlyCount == 0 && !info.HasStarArg && !info.HasKwArg
                && argc <= info.PositionalCount && argc >= info.PositionalCount - defaults;
        }

        public ScriptValue Call(ScriptValue callee, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            EnterCall(ctx);
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
                        r = OwnerOf(fv.Program, ctx).CallFunction(fv, args, kw, ctx);
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
                            // callable opaque (functools.partial, lru_cache, an Enum class, a record's __call__):
                            // one call can do seconds of work on few steps, so the deadline is read after it, as
                            // after a builtin, also when it raises (a loop of failing calls leaked its runner)
                            try
                            {
                                r = callee.CallCore(args, kw, ctx);
                            }
                            finally
                            {
                                ctx.Budget.CheckDeadlineAfterCall();
                            }
                            break;
                        }
                        throw Raise.TypeError(ctx, "'" + callee.PyTypeName + "' object is not callable");
                }
                ctx.CurrentLine = callerLine;
                return r;
            }
            catch (ScriptException) when (RestoreLine(ctx, callerLine))
            {
                throw;   // never taken: the filter restores the line and declines
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

        // Call() for a builtin's quiet twin: the same prologue, line restore and deadline reads, and a failure
        // handed back is read as the throw it replaces.
        private ScriptValue CallQuiet(QuietBuiltin quiet, bool isType, ScriptValue[] args, KwArgs kw, EvalContext ctx,
            out ExceptionValue raised)
        {
            EnterCall(ctx);
            int callerLine = ctx.CurrentLine;
            bool returned = false;
            try
            {
                ScriptException error;
                ScriptValue r = quiet(args, kw, ctx, out error);
                returned = true;
                if (error != null)
                {
                    ctx.CurrentLine = callerLine;
                    ctx.Budget.CheckDeadlineAfterCall();
                    raised = error.Value;
                    return null;
                }
                if (isType)
                {
                    ctx.Budget.CheckDeadlineAfterCall();   // as CallType
                }
                else
                {
                    ctx.Budget.CheckDeadlineNow();   // as CallBuiltin
                    if (ctx.Budget.IsTerminating)
                        throw ctx.Budget.PendingAbort;
                }
                ctx.CurrentLine = callerLine;
                raised = null;
                return r;
            }
            catch (ScriptException) when (RestoreLine(ctx, callerLine))
            {
                throw;   // never taken: the filter restores the line and declines
            }
            catch (System.OverflowException)
            {
                ctx.CurrentLine = callerLine;
                throw OverflowEscaped(ctx);
            }
            finally
            {
                if (!returned)
                    ctx.Budget.CheckDeadlineAfterCall();
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
            ScriptValue r;
            bool returned = false;
            try
            {
                r = fn.Fn(null, args, kw, ctx);   // free builtin: self == null
                returned = true;
            }
            finally
            {
                if (!returned)
                    ctx.Budget.CheckDeadlineAfterCall();   // a loop of raising calls is seen too
            }
            ctx.Budget.CheckDeadlineNow();   // after only: the statements before it already checked
            if (ctx.Budget.IsTerminating)
            {
                throw ctx.Budget.PendingAbort;
            }
            return r;
        }

        private ScriptValue CallBound(BoundMethodValue bound, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            try
            {
                return bound.Descriptor.Call(bound.Self, args, kw, ctx);   // self is passed separately (not in args)
            }
            finally
            {
                ctx.Budget.CheckDeadlineAfterCall();   // also when it raises: a loop of raising calls
            }
        }

        // A bound record method: prepend the instance as the first positional (self) and run the function.
        private ScriptValue CallUserMethod(BoundUserMethodValue m, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            var full = new ScriptValue[args.Length + 1];
            full[0] = m.Self;
            System.Array.Copy(args, 0, full, 1, args.Length);
            return OwnerOf(m.Func.Program, ctx).CallFunction(m.Func, full, kw, ctx);
        }

        private ScriptValue CallType(TypeValue type, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (type.Constructor == null)
            {
                throw Raise.TypeError(ctx, "cannot create '" + type.Name + "' instances");
            }
            try
            {
                return type.Constructor(null, args, kw, ctx);
            }
            finally
            {
                ctx.Budget.CheckDeadlineAfterCall();   // also when it raises: Decimal(bad) in a try loop
            }
        }

        // --------- script function argument binding ----------

        // The fast-bind body shared by the array path and the arrayless direct path: acquire a
        // frame (pooled when cell-free — such an Environment cannot escape the call, and the
        // allocation charge is kept so budget accounting matches the non-pooled path), bind the argc
        // leading params from either the array or the two direct values, fill defaults, run, release.
        private ScriptValue RunFastBind(FunctionValue fn, FunctionInfo info, ScriptValue[] argsOrNull,
            int argc, ScriptValue a0, ScriptValue a1, EvalContext ctx)
        {
            ChargeFrame(info, ctx);
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
                    ctx.Budget.ChargeAllocation(Environment.FrameCharge(info.LocalSlotCount, 0, closure.Length));
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

        // A frame of n slots is n slots to clear and bind on every call: a NodeBatch per 64, as the tree walk
        // charges its nodes (100 calls of a 45,000-default function were 1,000 steps for 4.5M bindings).
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ChargeFrame(FunctionInfo info, EvalContext ctx)
        {
            if (info.LocalSlotCount >= 64)
                ctx.Budget.Step(info.LocalSlotCount >> 6);
        }

        // Arrayless entry for <= 2 plain positional args (DispatchCall). Mirrors Call()'s prologue
        // exactly (depth guard / stack probe / step / peak stat / caller-line restore); an ineligible
        // signature materializes the array once and takes the general path, so errors and *args/kw
        // binding stay byte-identical.
        private ScriptValue CallFunctionDirect(FunctionValue fn, int argc, ScriptValue a0, ScriptValue a1, EvalContext ctx)
        {
            FunctionInfo info = fn.Info;
            if (!CanFastBind(fn, info, argc))
            {
                ScriptValue[] args = argc == 0 ? System.Array.Empty<ScriptValue>()
                    : argc == 1 ? new[] { a0 } : new[] { a0, a1 };
                return Call(fn, args, KwArgs.Empty, ctx);
            }
            EnterCall(ctx);
            int callerLine = ctx.CurrentLine;
            try
            {
                ScriptValue r = RunFastBind(fn, info, null, argc, a0, a1, ctx);
                ctx.CurrentLine = callerLine;
                return r;
            }
            catch (ScriptException) when (RestoreLine(ctx, callerLine))
            {
                throw;   // never taken: the filter restores the line and declines
            }
            finally
            {
                ctx.CallDepth--;
            }
        }

        private ScriptValue CallFunction(FunctionValue fn, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            FunctionInfo info = fn.Info;

            // Fast path: no keywords and CanFastBind - bind slots directly, skip the bookkeeping arrays.
            if (kw.Count == 0 && CanFastBind(fn, info, args.Length))
            {
                return RunFastBind(fn, info, args, args.Length, null, null, ctx);
            }

            ChargeFrame(info, ctx);
            Environment fenv = Environment.CreateFunction(ctx, info.LocalSlotCount, info.CellVars.Count,
                fn.Closure ?? System.Array.Empty<Cell>(), (Environment)fn.GlobalsToken, info);
            var posFilled = new bool[info.PositionalCount];
            var kwFilled = new bool[info.KwOnlyCount];
            BindPositionals(fn, fenv, args, posFilled, ctx);
            BindKeywords(fn, fenv, kw, posFilled, kwFilled, ctx);
            BindDefaults(fn, fenv, posFilled, kwFilled, ctx);
            return RunBody(fn, fenv, ctx);
        }

        // Positionals fill the leading params; the excess goes to *args or is an error.
        private void BindPositionals(FunctionValue fn, Environment fenv, ScriptValue[] args, bool[] posFilled,
            EvalContext ctx)
        {
            FunctionInfo info = fn.Info;
            int p = info.PositionalCount;
            int nPos = args.Length;
            int nToPos = nPos < p ? nPos : p;
            for (int i = 0; i < nToPos; i++)
            {
                BindByIndex(fenv, info, i, args[i]);
                posFilled[i] = true;
            }

            if (nPos > p)
            {
                if (!info.HasStarArg)
                {
                    throw TooManyPositional(ctx, fn.Name, p, fn.Defaults != null ? fn.Defaults.Length : 0, nPos);
                }
                var tail = new ScriptValue[nPos - p];
                System.Array.Copy(args, p, tail, 0, tail.Length);
                BindByIndex(fenv, info, p, ctx.Values.Tuple(tail));
            }
            else if (info.HasStarArg)
            {
                BindByIndex(fenv, info, p, ctx.Values.Tuple(System.Array.Empty<ScriptValue>()));
            }
        }

        // Keywords bind a named param, a keyword-only param, or go to **kwargs.
        private void BindKeywords(FunctionValue fn, Environment fenv, KwArgs kw, bool[] posFilled, bool[] kwFilled,
            EvalContext ctx)
        {
            FunctionInfo info = fn.Info;
            int p = info.PositionalCount;
            int k = info.KwOnlyCount;
            int kwStart = p + (info.HasStarArg ? 1 : 0);
            DictValue kwargsDict = info.HasKwArg ? ctx.Values.Dict(kw.Count) : null;
            for (int j = 0; j < kw.Count; j++)
            {
                string name = kw.NameAt(j);
                ScriptValue val = kw.ValueAt(j);
                // PEP 570: keywords cannot bind positional-only params (indices below PositionalOnlyCount).
                int pIdx = IndexOfName(info, name, info.PositionalOnlyCount, p);
                if (pIdx >= 0)
                {
                    if (posFilled[pIdx])
                    {
                        throw MultipleValues(ctx, fn.Name, name);
                    }
                    BindByIndex(fenv, info, pIdx, val);
                    posFilled[pIdx] = true;
                    continue;
                }
                int kIdx = IndexOfName(info, name, kwStart, kwStart + k);
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
                else if (IndexOfName(info, name, 0, info.PositionalOnlyCount) >= 0)
                {
                    throw Raise.TypeError(ctx, fn.Name
                        + "() got some positional-only arguments passed as keyword arguments: '" + name + "'");
                }
                else
                {
                    throw UnexpectedKeyword(ctx, fn.Name, name);
                }
            }
            if (kwargsDict != null)
            {
                BindByIndex(fenv, info, kwStart + k, kwargsDict);
            }
        }

        // Unfilled params take their defaults (positional ones right-aligned); the rest are reported missing,
        // positional before keyword-only.
        private void BindDefaults(FunctionValue fn, Environment fenv, bool[] posFilled, bool[] kwFilled,
            EvalContext ctx)
        {
            FunctionInfo info = fn.Info;
            int p = info.PositionalCount;
            int firstDefault = p - (fn.Defaults != null ? fn.Defaults.Length : 0);
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
                throw MissingRequired(ctx, fn.Name, missing, "positional");
            }

            int kwStart = p + (info.HasStarArg ? 1 : 0);
            // KwDefaults follow the keyword-only parameters' order (ExecStmt.Def): one pass over both, where a
            // search per parameter made 45,000 of them quadratic.
            KeyValuePair<string, ScriptValue>[] kwDefaults = fn.KwDefaults;
            int d = 0;
            for (int i = 0; i < kwFilled.Length; i++)
            {
                string kname = info.ParamNames[kwStart + i];
                ScriptValue dv = null;
                if (kwDefaults != null && d < kwDefaults.Length && kwDefaults[d].Key == kname)
                    dv = kwDefaults[d++].Value;
                if (kwFilled[i])
                {
                    continue;
                }
                if (dv != null)
                {
                    BindByIndex(fenv, info, kwStart + i, dv);
                }
                else
                {
                    (missing ?? (missing = new List<string>())).Add(kname);
                }
            }
            if (missing != null)
            {
                throw MissingRequired(ctx, fn.Name, missing, "keyword-only");
            }
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
                    // An expression-whitelist lambda body rides the closure-compiled form (cached on
                    // the shared FunctionInfo), skipping per-node dispatch on
                    // every call — filter/map/sort-key predicates are exactly this shape.
                    CompiledEval bodyFn = GetCompiledExpr(ref fn.Info.CompiledLambdaBody, fn.LambdaBody, ctx);
                    return EvalCompiledOr(bodyFn, fn.LambdaBody, fenv, ctx);
                }
                CompiledEval oneLine = OneLineReturn(fn, ctx);
                if (oneLine != null)
                {
                    // `def f(x): return <expr>` takes the lambda's compiled form. The return statement's
                    // prologue stays, so a traceback names its line and the budget sees the statement.
                    if (ctx.Budget.HardStopRequested)
                        throw ctx.Budget.PendingAbort;
                    ctx.CurrentLine = fn.Body[fn.Body.Count - 1].Src.Line;
                    if ((++ctx.NodeCounter & 63) == 0)
                        ctx.Budget.Step(BudgetCost.NodeBatch);
                    return oneLine(this, fenv, ctx);
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
            catch (ScriptException se) when (AppendFrame(se, fn, ctx))
            {
                throw;   // never taken: the filter appends this frame and declines
            }
            finally
            {
                ctx.CurrentFunctionName = callerFn;
            }
        }

        // The compiled `return <expr>` of a def whose body is just that (after an optional docstring), or
        // null: another body shape, a bare return, or an expression outside the compiler's whitelist.
        private static CompiledEval OneLineReturn(FunctionValue fn, EvalContext ctx)
        {
            if (ctx.InterpretOnly)
                return null;
            object c = fn.Info.CompiledReturnBody;
            if (c != null)
                return c as CompiledEval;
            IReadOnlyList<StmtNode> body = fn.Body;
            ReturnNode ret = body.Count == 1 ? body[0] as ReturnNode
                : body.Count == 2 && IsDocstring(body[0]) ? body[1] as ReturnNode : null;
            return Store(ref fn.Info.CompiledReturnBody, ret != null && ret.Value != null ? Charged(TryCompileExpr(ret.Value), ret.Value) : null);
        }

        private static bool IsDocstring(StmtNode s)
        {
            ExprStmtNode e = s as ExprStmtNode;
            LiteralNode lit = e == null ? null : e.Value as LiteralNode;
            return lit != null && lit.Kind == LiteralKind.Str;
        }

        // The line for a frame being appended: a lambda always reports its body's own line (its
        // expression never updates CurrentLine, and a lambda cannot contain a raise statement); the
        // FIRST frame otherwise uses the recorded raise site (survives a bare re-raise in the same
        // function); every later frame is the line that frame was executing.
        // Exception filters that always decline: they run on the unwind's first pass in the same
        // inner-to-outer order the catch-and-rethrow chain had, without a rethrow per frame.
        private static bool RestoreLine(EvalContext ctx, int line)
        {
            ctx.CurrentLine = line;
            return false;
        }

        private static bool AppendFrame(ScriptException se, FunctionValue fn, EvalContext ctx)
        {
            TracebackBuilder.AppendFrame(se.Value, fn.Name, FrameLine(se.Value, fn, ctx), fn.Program == null ? null : fn.Program.ModuleFile);
            return false;
        }

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

        private const int ManyParams = 16;

        // A short range is searched in place; a long one through the function's name index, so binding
        // stays linear in the keywords.
        private static int IndexOfName(FunctionInfo info, string name, int start, int end)
        {
            if (end - start > ManyParams)
            {
                int at;
                return info.ParamIndex.TryGetValue(name, out at) && at >= start && at < end ? at : -1;
            }
            IReadOnlyList<string> names = info.ParamNames;
            for (int i = start; i < end; i++)
            {
                if (names[i] == name)
                {
                    return i;
                }
            }
            return -1;
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
