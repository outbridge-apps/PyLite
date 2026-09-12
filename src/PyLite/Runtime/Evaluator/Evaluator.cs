using System.Numerics;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // The central tree-walker. Dispatch is a flat switch over the C# node type (there is no
    // NodeKind discriminator), delegating immediately to a per-node EvalXxx/ExecXxx method so EvalExpr /
    // ExecStmt stay small-frame. The only immutable state is the resolved program; all mutable
    // run state lives in EvalContext + Environment (reentrancy, A8-#5).
    internal sealed partial class Evaluator
    {
        private readonly ResolvedProgram _program;
        private Dictionary<ResolvedProgram, Evaluator> _peers;   // evaluators for other compiled units
        private ScriptValue[] _builtinCache;   // per-run resolved builtins (safe: the script provably never
                                               // assigns a Builtin-classified name, host inputs are fixed pre-run)
        private Environment.GlobalCacheEntry[] _globalCache;   // per-run versioned dict-entry cache (lever E)

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
        internal Evaluator OwnerOf(ResolvedProgram program)
        {
            if (program == null || ReferenceEquals(program, _program))
            {
                return this;
            }
            if (_peers == null)
            {
                _peers = new Dictionary<ResolvedProgram, Evaluator>();
            }
            Evaluator ev;
            if (!_peers.TryGetValue(program, out ev))
            {
                ev = new Evaluator(program);
                _peers[program] = ev;
            }
            return ev;
        }

        public ExecSignal RunModule(Environment moduleEnv, EvalContext ctx)
        {
            if (ctx.CallHook == null)
            {
                ctx.CallHook = (callee, a, kw) => Call(callee, a, kw, ctx);
                ctx.CallHook1 = (callee, a0) => callee.Kind == ValueKind.Function
                    ? OwnerOf(((FunctionValue)callee).Program).CallFunctionDirect((FunctionValue)callee, 1, a0, null, ctx)
                    : Call(callee, new[] { a0 }, KwArgs.Empty, ctx);
            }
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
                TracebackBuilder.AppendFrame(se.Value, "<module>", line);
                throw;
            }
        }

        public ScriptValue EvalExpr(ExprNode e, Environment env, EvalContext ctx)
        {
            if ((++ctx.NodeCounter & 63) == 0)
            {
                ctx.Budget.Step(BudgetCost.NodeBatch);
            }
            if ((++ctx.ExprProbeCounter & 15) == 0)
            {
                StackGuard.Probe(ctx);
            }
            switch (e.NodeKind)   // P2: int switch = jump table, not a sequential isinst chain
            {
                case NodeKind.Literal: return EvalLiteral((LiteralNode)e, ctx);
                case NodeKind.ConstFolded: return EvalLiteral(((ConstFoldedNode)e).Folded, ctx);
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
            switch (s.NodeKind)   // P2: jump table (see EvalExpr)
            {
                case NodeKind.ExprStmt: EvalExpr(((ExprStmtNode)s).Value, env, ctx); return ExecSignal.Normal;
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

        private static ScriptValue EvalLiteral(LiteralNode lit, EvalContext ctx)
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
