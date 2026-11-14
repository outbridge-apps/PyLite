using System.Numerics;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // The central tree-walker. Dispatch is a flat switch over each node's NodeKind, delegating immediately to a per-node EvalXxx/ExecXxx method so EvalExpr /
    // ExecStmt stay small-frame. The only immutable state is the resolved program; all mutable
    // run state lives in EvalContext + Environment, so a run can re-enter it.
    internal sealed partial class Evaluator
    {
        private readonly ResolvedProgram _program;
        private ScriptValue[] _builtinCache;   // per-run resolved builtins (safe: the script provably never
                                               // assigns a Builtin-classified name, host inputs are fixed pre-run)
        private Environment.GlobalCacheEntry[] _globalCache;   // per-run versioned dict-entry cache (lever E)
        private ExprNode[] _literalNodes;       // per-run str/float/bytes literal and literal-tuple values by CacheSlot;
        private ScriptValue[] _literalValues;   // the node is kept beside its value, so a slot can never answer for another
        private object[] _sites;                // per-run record caches by AttributeNode.SiteSlot: a FieldSite or a MethodSite

        // obj.attr on a record instance: the field's index in the class's slot table
        private sealed class FieldSite
        {
            internal readonly AttributeNode Node;
            internal readonly Dictionary<string, int> SlotIndex;
            internal readonly int Index;
            internal FieldSite(AttributeNode n, Dictionary<string, int> si, int idx) { Node = n; SlotIndex = si; Index = idx; }
        }

        // obj.m(...) on a record instance: the method, while the class is the same, no class attribute has been
        // assigned since (it could shadow the method) and the instance field of that name, if any, is unset
        private sealed class MethodSite
        {
            internal readonly AttributeNode Node;
            internal readonly RecordClassValue Class;
            internal readonly int Version;
            internal readonly int FieldIndex;
            internal readonly FunctionValue Method;
            internal MethodSite(AttributeNode n, RecordClassValue c, int v, int f, FunctionValue m) { Node = n; Class = c; Version = v; FieldIndex = f; Method = m; }
        }

        private object SiteOf(AttributeNode n)
        {
            int s = n.SiteSlot;
            return _sites != null && s >= 0 && s < _sites.Length ? _sites[s] : null;
        }

        private void SetSite(AttributeNode n, object entry)
        {
            int s = n.SiteSlot;
            if (s < 0 || s >= _program.SiteCount)
                return;
            if (_sites == null)
                _sites = new object[_program.SiteCount];
            _sites[s] = entry;
        }

        private Environment.GlobalCacheEntry[] GlobalCache
        {
            get { return _globalCache ?? (_globalCache = new Environment.GlobalCacheEntry[_program.GlobalSlotCount]); }
        }

        public Evaluator(ResolvedProgram program)
        {
            _program = program;
        }
        // The evaluator that owns a function's program, so its body's name bindings resolve against the right
        // binding table. A function defined in one module (e.g. a host prelude) and called from another must
        // execute under its own program, not the caller's. Self for the common single-program case.
        internal Evaluator OwnerOf(ResolvedProgram program, EvalContext ctx)
        {
            return program == null || ReferenceEquals(program, _program) ? this : For(program, ctx);
        }

        // The run's one evaluator per compiled unit: a function executes on its own program's evaluator
        // whichever module calls it, so each program's caches are built once per run.
        internal static Evaluator For(ResolvedProgram program, EvalContext ctx)
        {
            Evaluator ev;
            if (ctx.Evaluators == null)
                ctx.Evaluators = new Dictionary<ResolvedProgram, Evaluator>();
            else if (ctx.Evaluators.TryGetValue(program, out ev))
                return ev;
            ev = new Evaluator(program);
            ctx.Evaluators[program] = ev;
            return ev;
        }

        // The context's callbacks into script code (modules calling a key function, dataclass __init__),
        // bound once per run; the host installs them from the main script's evaluator.
        internal void InstallHooks(EvalContext ctx)
        {
            ctx.CallHook = (callee, a, kw) => Call(callee, a, kw, ctx);
            ctx.CallHook1 = (callee, a0) => callee.Kind == ValueKind.Function
                ? OwnerOf(((FunctionValue)callee).Program, ctx).CallFunctionDirect((FunctionValue)callee, 1, a0, null, ctx)
                : Call(callee, new[] { a0 }, KwArgs.Empty, ctx);
            ctx.DataclassInitHook = (cls, inst, a, kw) => RunDataclassInit(cls, inst, a, kw, ctx);
        }

        public ExecSignal RunModule(Environment moduleEnv, EvalContext ctx)
        {
            if (ctx.Evaluators == null)
                ctx.Evaluators = new Dictionary<ResolvedProgram, Evaluator>();
            if (!ctx.Evaluators.ContainsKey(_program))
                ctx.Evaluators[_program] = this;   // a harness that built this evaluator itself
            if (ctx.CallHook == null)
                InstallHooks(ctx);
            try
            {
                ExecSignal sig = ExecBlock(_program.Module.Body, moduleEnv, ctx);
                if (sig.Kind == ExecSignalKind.Raised)
                {
                    // The module boundary: the host runner expects the ScriptException carrier;
                    // the catch below appends the "<module>" frame like any thrown exception.
                    throw new ScriptException(sig.Raised);
                }
                return sig;
            }
            catch (ScriptException se)
            {
                // The module frame of the traceback; function frames were appended by RunBody on the
                // way up, each with its own line (Call restores CurrentLine per frame). A module-level
                // raise is the first frame — use the recorded raise site (bare-re-raise parity).
                int line = se.Value.TracebackFrames == null && se.Value.RaiseLine > 0
                    ? se.Value.RaiseLine : ctx.CurrentLine;
                TracebackBuilder.AppendFrame(se.Value, "<module>", line, _program.ModuleFile);
                throw;
            }
        }

        public ScriptValue EvalExpr(ExprNode e, Environment env, EvalContext ctx)
        {
            // One counter for both cadences: every 64th node charges the batch, every 16th probes the stack
            // (consecutive within an expression, so a nesting of 16 always passes a probe); one test per node.
            int node = ++ctx.NodeCounter;
            if ((node & 15) == 0)
            {
                if ((node & 63) == 0)
                    ctx.Budget.Step(BudgetCost.NodeBatch);
                StackGuard.Probe(ctx);
            }
            switch (e.NodeKind)   // int switch = jump table, not a sequential isinst chain
            {
                case NodeKind.Literal: return EvalLiteral((LiteralNode)e, ctx);
                case NodeKind.Name: return EvalName((NameNode)e, env, ctx);
                case NodeKind.BinOp: return EvalBinOp((BinOpNode)e, env, ctx);
                case NodeKind.UnaryOp: return EvalUnaryOp((UnaryOpNode)e, env, ctx);
                case NodeKind.BoolOp: return EvalBoolOp((BoolOpNode)e, env, ctx);
                case NodeKind.CompareChain: return EvalCompare((CompareChainNode)e, env, ctx);
                case NodeKind.IfExp: return EvalIfExp((IfExpNode)e, env, ctx);
                case NodeKind.Index: return EvalIndex((IndexNode)e, env, ctx);
                case NodeKind.Attribute: return EvalAttribute((AttributeNode)e, env, ctx);
                case NodeKind.List: return EvalList((ListNode)e, env, ctx);
                case NodeKind.Tuple: return EvalTuple((TupleNode)e, env, ctx);
                case NodeKind.Set: return EvalSet((SetNode)e, env, ctx);
                case NodeKind.Dict: return EvalDict((DictNode)e, env, ctx);
                case NodeKind.Slice: return EvalSlice((SliceNode)e, env, ctx);
                case NodeKind.Call: return EvalCall((CallNode)e, env, ctx);
                case NodeKind.NamedExpr: return EvalNamedExpr((NamedExprNode)e, env, ctx);
                case NodeKind.Lambda: return EvalLambda((LambdaNode)e, env, ctx);
                case NodeKind.Comprehension: return EvalComprehension((ComprehensionNode)e, env, ctx);
                case NodeKind.FString: return EvalFString((FStringNode)e, env, ctx);
                default: throw EngineBug("unexpected expr node: " + e.GetType().Name);
            }
        }

        public ExecSignal ExecStmt(StmtNode s, Environment env, EvalContext ctx)
        {
            if (ctx.Budget.HardStopRequested)
            {
                throw ctx.Budget.PendingAbort;
            }
            ctx.CurrentLine = s.Src.Line;
            if ((++ctx.NodeCounter & 63) == 0)
            {
                ctx.Budget.Step(BudgetCost.NodeBatch);
            }
            switch (s.NodeKind)   // jump table (see EvalExpr)
            {
                case NodeKind.ExprStmt:
                {
                    ExprNode e = ((ExprStmtNode)s).Value;
                    if (e.QuietValue)
                    {
                        ExceptionValue raised;
                        ValueOrRaised(e, env, ctx, out raised);
                        return raised == null ? ExecSignal.Normal : ExecSignal.Raise(raised);
                    }
                    EvalExpr(e, env, ctx);
                    return ExecSignal.Normal;
                }
                case NodeKind.Assign: return ExecAssign((AssignNode)s, env, ctx);
                case NodeKind.AugAssign: return ExecAugAssign((AugAssignNode)s, env, ctx);
                case NodeKind.Del: return ExecDel((DelNode)s, env, ctx);
                case NodeKind.Pass: return ExecSignal.Normal;
                case NodeKind.If: return ExecIf((IfNode)s, env, ctx);
                case NodeKind.While: return ExecWhile((WhileNode)s, env, ctx);
                case NodeKind.For: return ExecFor((ForNode)s, env, ctx);
                case NodeKind.FuncDef: return ExecFuncDef((FuncDefNode)s, env, ctx);
                case NodeKind.Return: return ExecReturn((ReturnNode)s, env, ctx);
                case NodeKind.Break: return ExecSignal.Break;
                case NodeKind.Continue: return ExecSignal.Continue;
                case NodeKind.Assert: return ExecAssert((AssertNode)s, env, ctx);
                case NodeKind.Try: return ExecTry((TryNode)s, env, ctx);
                case NodeKind.Raise: return ExecRaise((RaiseNode)s, env, ctx);
                case NodeKind.Import: return ExecImport((ImportNode)s, env, ctx);
                case NodeKind.FromImport: return ExecFromImport((FromImportNode)s, env, ctx);
                case NodeKind.Global: return ExecSignal.Normal;
                case NodeKind.Nonlocal: return ExecSignal.Normal;
                case NodeKind.Match: return ExecMatch((MatchNode)s, env, ctx);
                case NodeKind.ClassDef: return ExecClassDef((ClassDefNode)s, env, ctx);
                default: throw EngineBug("unexpected stmt node: " + s.GetType().Name);
            }
        }

        public ExecSignal ExecBlock(IReadOnlyList<StmtNode> stmts, Environment env, EvalContext ctx)
        {
            for (int i = 0; i < stmts.Count; i++)
            {
                ExecSignal sig = ExecStmt(stmts[i], env, ctx);
                if (!sig.IsNormal)
                {
                    return sig;
                }
            }
            return ExecSignal.Normal;
        }

        private ScriptValue EvalLiteral(LiteralNode lit, EvalContext ctx)
        {
            int slot = lit.CacheSlot;
            if (slot >= 0 && slot < _program.LiteralCount)
            {
                if (_literalNodes == null)
                {
                    _literalNodes = new ExprNode[_program.LiteralCount];
                    _literalValues = new ScriptValue[_program.LiteralCount];
                }
                if (ReferenceEquals(_literalNodes[slot], lit))
                    return _literalValues[slot];
                ScriptValue v = NewLiteral(lit, ctx);
                _literalValues[slot] = v;
                _literalNodes[slot] = lit;
                return v;
            }
            return NewLiteral(lit, ctx);
        }

        private static ScriptValue NewLiteral(LiteralNode lit, EvalContext ctx)
        {
            switch (lit.Kind)
            {
                case LiteralKind.Int:
                {
                    // Cache-range ints are shared zero-charge singletons — skip the BigInteger unbox+compares.
                    object cached = lit.CachedSmall;
                    if (cached != null)
                        return (ScriptValue)cached;
                    IntValue v = ctx.Values.Int((BigInteger)lit.Value);
                    if (v.IsSmall && v.Small >= IntValue.CacheLow && v.Small <= IntValue.CacheHigh)
                        lit.CachedSmall = v;
                    return v;
                }
                case LiteralKind.Float: return ctx.Values.Float((double)lit.Value);
                case LiteralKind.Str: return ctx.Values.Str((string)lit.Value);
                case LiteralKind.Bytes: return ctx.Values.Bytes((byte[])lit.Value);
                case LiteralKind.Bool: return ctx.Values.Bool((bool)lit.Value);
                case LiteralKind.Ellipsis: return EllipsisValue.Instance;
                default: return ctx.Values.None;   // LiteralKind.None
            }
        }

        private ScriptValue EvalName(NameNode n, Environment env, EvalContext ctx)
        {
            NameBinding b = _program.GetBinding(n);
            if (b == null)
            {
                return env.GetGlobal(n.Id);
            }
            switch (b.Kind)
            {
                case NameKind.Local: return env.GetLocal(b.Slot, n.Id);
                case NameKind.Cell: return env.GetCell(b.Slot, n.Id);
                case NameKind.Free:
                case NameKind.Nonlocal: return env.GetFree(b.Slot, n.Id);
                case NameKind.Builtin:
                {
                    ScriptValue[] cache = _builtinCache ?? (_builtinCache = new ScriptValue[_program.BuiltinSlotCount]);
                    ScriptValue cv = cache[b.Slot];
                    if (cv == null)
                    {
                        cv = env.GetGlobal(n.Id);   // globals-first (host inputs may shadow), then builtins
                        cache[b.Slot] = cv;
                    }
                    return cv;
                }
                default:   // Global
                    return b.Slot >= 0
                        ? env.GetGlobalCached(GlobalCache, b.Slot, n.Id)
                        : env.GetGlobal(n.Id);
            }
        }

        // --- routing helpers shared across the partials ---

        private static PyBinOp MapBin(BinOp op)
        {
            switch (op)
            {
                case BinOp.Add: return PyBinOp.Add;
                case BinOp.Sub: return PyBinOp.Sub;
                case BinOp.Mul: return PyBinOp.Mul;
                case BinOp.Div: return PyBinOp.TrueDiv;
                case BinOp.FloorDiv: return PyBinOp.FloorDiv;
                case BinOp.Mod: return PyBinOp.Mod;
                case BinOp.Pow: return PyBinOp.Pow;
                case BinOp.LShift: return PyBinOp.LShift;
                case BinOp.RShift: return PyBinOp.RShift;
                case BinOp.BitAnd: return PyBinOp.BitAnd;
                case BinOp.BitOr: return PyBinOp.BitOr;
                default: return PyBinOp.BitXor;   // BinOp.BitXor
            }
        }

        private ScriptValue GetItemByKind(ScriptValue obj, ScriptValue idx, EvalContext ctx)
        {
            return PyOps.GetItem(obj, idx, ctx);
        }

        // A statement's whole value (`v = ...`, `v += ...`, `return ...`, an expression statement) whose QuietValue is
        // set: a missing dict key, a list or tuple index out of range and a quiet builtin's failure (int('x'), next()
        // at the end) are handed back as the exception, for the statement to raise as a signal - a .NET throw costs
        // 40-80 times a raise statement. The same Raise.Make, so its text, line, steps and charge are too.
        private ScriptValue ValueOrRaised(ExprNode e, Environment env, EvalContext ctx, out ExceptionValue raised)
        {
            return e.NodeKind == NodeKind.Index
                ? IndexOrRaised((IndexNode)e, env, ctx, out raised)
                : CallOrRaised((CallNode)e, env, ctx, out raised);
        }

        // Any statement's value: ValueOrRaised's when QuietValue is set, else EvalExpr's.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private ScriptValue StatementValue(ExprNode e, Environment env, EvalContext ctx, out ExceptionValue raised)
        {
            if (e.QuietValue)
                return ValueOrRaised(e, env, ctx, out raised);
            raised = null;
            return EvalExpr(e, env, ctx);
        }

        private static void CountNode(EvalContext ctx)
        {
            int node = ++ctx.NodeCounter;   // counted as EvalExpr counts the node
            if ((node & 15) == 0)
            {
                if ((node & 63) == 0)
                    ctx.Budget.Step(BudgetCost.NodeBatch);
                StackGuard.Probe(ctx);
            }
        }

        // int(...), float(...), next(...) as EvalCall runs them, but through the twin while the name holds the builtin.
        private ScriptValue CallOrRaised(CallNode n, Environment env, EvalContext ctx, out ExceptionValue raised)
        {
            raised = null;
            CountNode(ctx);
            ScriptValue callee = EvalExpr(n.Func, env, ctx);
            ValueKind kind = callee.Kind;
            QuietBuiltin quiet;
            switch (kind)
            {
                case ValueKind.BuiltinFunction:
                    quiet = ((BuiltinFunctionValue)callee).Quiet;
                    break;
                case ValueKind.Type:
                    quiet = ((TypeValue)callee).Quiet;
                    break;
                default:
                    quiet = null;
                    break;
            }
            if (quiet == null)
                return DispatchCall(n, callee, env, ctx);
            KwArgs kw;
            ScriptValue[] args = EvalCallArgs(n, env, ctx, out kw);
            return CallQuiet(quiet, kind == ValueKind.Type, args, kw, ctx, out raised);
        }

        private ScriptValue IndexOrRaised(IndexNode n, Environment env, EvalContext ctx, out ExceptionValue raised)
        {
            CountNode(ctx);
            raised = null;
            ScriptValue obj = EvalExpr(n.Value, env, ctx);
            ScriptValue idx = EvalSubscript(n.Index, env, ctx);
            ScriptValue v;
            int pos;
            switch (obj.Kind)
            {
                case ValueKind.Dict:
                    if (((DictValue)obj).TryGetItem(idx, ctx, out v))
                        return v;
                    raised = Raise.KeyError(ctx, idx).Value;
                    return null;
                case ValueKind.List:
                    if (!SequenceOps.IsIndex(idx))
                        break;
                    List<ScriptValue> items = ((ListValue)obj).Items;
                    if (SequenceOps.TryNormIndex(idx, items.Count, out pos))
                        return items[pos];
                    raised = Raise.IndexError(ctx, "list index out of range").Value;
                    return null;
                case ValueKind.Tuple:
                    if (!SequenceOps.IsIndex(idx))
                        break;
                    ScriptValue[] elems = ((TupleValue)obj).Items;
                    if (SequenceOps.TryNormIndex(idx, elems.Length, out pos))
                        return elems[pos];
                    raised = Raise.IndexError(ctx, "tuple index out of range").Value;
                    return null;
            }
            return GetItemByKind(obj, idx, ctx);
        }

        private void SetItemByKind(ScriptValue obj, ScriptValue idx, ScriptValue value, EvalContext ctx)
        {
            PyOps.SetItem(obj, idx, value, ctx);
        }

        private void DelItemByKind(ScriptValue obj, ScriptValue idx, EvalContext ctx)
        {
            PyOps.DelItem(obj, idx, ctx);
        }

        // Evaluate a subscript index: a slice node becomes a SliceValue, everything else an ordinary value.
        private ScriptValue EvalSubscript(ExprNode index, Environment env, EvalContext ctx)
        {
            SliceNode sl = index as SliceNode;
            if (sl != null)
            {
                return EvalSlice(sl, env, ctx);
            }
            return EvalExpr(index, env, ctx);
        }

        private ScriptValue[] Unpack(ScriptValue v, int count, EvalContext ctx)
        {
            // Direct path: tuple/list sources unpack by index — no iterator allocated per unpack, which
            // is the per-element cost of `for k, v in enumerate/zip/items(...)` loops. The Step mirrors
            // the iterator path's count+1 MoveNext charges. Callers only read the returned array.
            TupleValue tv = v as TupleValue;
            if (tv != null)
            {
                if (tv.Items.Length != count)
                {
                    throw UnpackCountError(ctx, count, tv.Items.Length);
                }
                ctx.Budget.Step(count + 1);
                return tv.Items;
            }
            ListValue lv = v as ListValue;
            if (lv != null)
            {
                if (lv.Items.Count != count)
                {
                    throw UnpackCountError(ctx, count, lv.Items.Count);
                }
                ctx.Budget.Step(count + 1);
                var direct = new ScriptValue[count];
                lv.Items.CopyTo(direct);
                return direct;
            }
            IScriptIterator it = PyOps.GetIterator(v, ctx);
            var buf = new ScriptValue[count];
            ScriptValue cur;
            for (int k = 0; k < count; k++)
            {
                if (!it.MoveNext(ctx, out cur))
                {
                    throw Raise.ValueError(ctx, "not enough values to unpack (expected " + count + ", got " + k + ")");
                }
                buf[k] = cur;
            }
            if (it.MoveNext(ctx, out cur))
            {
                throw Raise.ValueError(ctx, "too many values to unpack (expected " + count + ")");
            }
            return buf;
        }

        private static ScriptException UnpackCountError(EvalContext ctx, int expected, int got)
        {
            if (got < expected)
            {
                return Raise.ValueError(ctx, "not enough values to unpack (expected " + expected + ", got " + got + ")");
            }
            return Raise.ValueError(ctx, "too many values to unpack (expected " + expected + ")");
        }

        private static System.Exception EngineBug(string message)
        {
            return new System.InvalidOperationException(message);
        }
    }
}
