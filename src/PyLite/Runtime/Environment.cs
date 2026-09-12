using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.Runtime
{
    // Slot-based environment. Locals/cells are index-addressed (the Resolver assigns
    // slots); module globals are a DictValue with the seeded hash; builtins are a frozen table. Bound to
    // one EvalContext so the throwing accessors need no ctx parameter.
    internal sealed class Environment
    {
        private readonly EvalContext _ctx;
        private readonly ScriptValue[] _locals;   // null element = unbound slot
        private readonly Cell[] _cells;           // this frame's own cells
        private readonly Cell[] _freeCells;       // captured enclosing-frame cells (closure)
        private readonly DictValue _globals;
        private readonly HostFunctionTable _builtins;

        // Resolver scope this frame belongs to (Module for the module env); used to bind def/class names
        // and to capture closure cells by name. Null in low-level Environment unit tests.
        public FunctionInfo Scope;
        public Environment ModuleEnv;   // the run's module env (self for the module env)

        private Environment(EvalContext ctx, ScriptValue[] locals, Cell[] cells, Cell[] freeCells,
            DictValue globals, HostFunctionTable builtins)
        {
            _ctx = ctx;
            _locals = locals;
            _cells = cells;
            _freeCells = freeCells;
            _globals = globals;
            _builtins = builtins;
        }

        public static Environment CreateModule(EvalContext ctx, DictValue globals, HostFunctionTable builtins,
            FunctionInfo scope = null)
        {
            var e = new Environment(ctx, null, null, null, globals, builtins);
            e.Scope = scope;
            e.ModuleEnv = e;
            return e;
        }

        // The frame skeleton is charged here (A8-#7); the arrays are not script values but must be counted.
        public static Environment CreateFunction(EvalContext ctx, int localCount, int cellCount,
            Cell[] freeCells, Environment moduleEnv, FunctionInfo scope = null)
        {
            ctx.Budget.ChargeAllocation(64 + 8L * (localCount + cellCount + freeCells.Length));
            var locals = new ScriptValue[localCount];
            var cells = new Cell[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                cells[i] = new Cell();
            }
            var e = new Environment(ctx, locals, cells, freeCells, moduleEnv._globals, moduleEnv._builtins);
            e.Scope = scope;
            e.ModuleEnv = moduleEnv;
            return e;
        }

        // Frame-pool release: locals must read as unbound on the next call (UnboundLocalError parity).
        internal void ClearLocals()
        {
            System.Array.Clear(_locals, 0, _locals.Length);
        }

        public ScriptValue GetLocal(int slot, string nameForError)
        {
            ScriptValue v = _locals[slot];
            if (v == null)
            {
                throw Raise.UnboundLocal(_ctx, nameForError);
            }
            return v;
        }

        // Raw slot read for the fused comprehension leaf: null = unbound — the caller falls back to
        // the general path, which raises the canonical UnboundLocalError with the variable's name.
        internal ScriptValue PeekLocal(int slot) { return _locals[slot]; }

        public void SetLocal(int slot, ScriptValue v)
        {
            _locals[slot] = v;
        }

        public void DelLocal(int slot, string nameForError)
        {
            if (_locals[slot] == null)
            {
                throw Raise.UnboundLocal(_ctx, nameForError);
            }
            _locals[slot] = null;
        }

        public ScriptValue GetCell(int idx, string nameForError)
        {
            ScriptValue v = _cells[idx].Value;
            if (v == null)
            {
                throw Raise.UnboundLocal(_ctx, nameForError);
            }
            return v;
        }

        public ScriptValue GetFree(int idx, string nameForError)
        {
            ScriptValue v = _freeCells[idx].Value;
            if (v == null)
            {
                throw Raise.FreeVariable(_ctx, nameForError);
            }
            return v;
        }

        public void SetCell(int idx, ScriptValue v)
        {
            _cells[idx].Value = v;
        }

        public void SetFree(int idx, ScriptValue v)
        {
            _freeCells[idx].Value = v;
        }

        public Cell CellAt(int idx)
        {
            return _cells[idx];
        }

        // Capture the Cell for a nested function's free variable `name` from THIS (the defining) frame:
        // the name is either one of this frame's own cells or one of its own captured free cells.
        public Cell GetClosureCell(string name)
        {
            int ci = IndexOf(Scope.CellVars, name);
            if (ci >= 0)
            {
                return _cells[ci];
            }
            int fi = IndexOf(Scope.FreeVars, name);
            if (fi >= 0)
            {
                return _freeCells[fi];
            }
            throw new System.InvalidOperationException("no closure cell for '" + name + "'");
        }

        // Bind a name defined in THIS scope (a def/class name): module scope -> globals; a captured local
        // -> its cell; otherwise a plain local slot. Slot conventions match the Resolver (local slot =
        // index in LocalNames, cell index = index in CellVars).
        public void BindScopeName(string name, ScriptValue value)
        {
            if (Scope == null || Scope.Kind == ScopeKind.Module)
            {
                SetGlobal(name, value);
                return;
            }
            int ci = IndexOf(Scope.CellVars, name);
            if (ci >= 0)
            {
                SetCell(ci, value);
                return;
            }
            int li = IndexOf(Scope.LocalNames, name);
            if (li >= 0)
            {
                SetLocal(li, value);
                return;
            }
            SetGlobal(name, value);
        }

        // Clear a name bound in THIS scope; an already-unbound name is fine (Py3 `del e` after except).
        public void SafeDelScopeName(string name)
        {
            if (Scope != null && Scope.Kind != ScopeKind.Module)
            {
                int ci = IndexOf(Scope.CellVars, name);
                if (ci >= 0)
                {
                    _cells[ci].Value = null;
                    return;
                }
                int li = IndexOf(Scope.LocalNames, name);
                if (li >= 0)
                {
                    _locals[li] = null;
                    return;
                }
            }
            ScriptValue key = Key(name);
            ScriptValue tmp;
            if (_globals.TryGet(key, _ctx, out tmp))
            {
                _globals.DelItemOrThrow(key, _ctx);
            }
        }

        private static int IndexOf(IReadOnlyList<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == name)
                {
                    return i;
                }
            }
            return -1;
        }

        // Global-name keys intern through a per-run table: the StrValue (with its seeded hash cached
        // inside) is built and budget-charged once per distinct name, so the per-access cost of a global
        // read is one C# dictionary probe — no allocation, no re-hash. Correct for concurrent runs of one
        // CompiledScript because the table lives on the EvalContext, never on shared AST/program state.
        private StrValue Key(string name)
        {
            StrValue k;
            if (!_ctx.GlobalNameKeys.TryGetValue(name, out k))
            {
                k = _ctx.Values.Str(name);
                _ctx.GlobalNameKeys[name] = k;
            }
            return k;
        }

        public ScriptValue GetGlobal(string name)
        {
            ScriptValue v;
            if (_globals.TryGet(Key(name), _ctx, out v))
            {
                return v;
            }
            if (_builtins.TryGet(name, out v))
            {
                return v;
            }
            string unsupported;
            if (HostFunctionTable.TryGetUnsupportedMessage(name, out unsupported))
            {
                throw Raise.Make(_ctx, PyExceptionTypes.NameError, unsupported);
            }
            throw Raise.NameError(_ctx, name);
        }

        // Versioned dict-entry cache for global names (one entry per resolver-assigned global slot,
        // one array per run). A hit is a version compare + positional read: valid because the table
        // Version bumps on every structural change (new key / delete / clear / rebuild) while value
        // updates keep both Version and positions. Pos -1 caches the builtins fallback — sound
        // because shadowing it requires inserting a NEW global key, which bumps Version. Stamp is
        // Version+1 so the default(0) entry never matches. NameError is never cached.
        internal struct GlobalCacheEntry
        {
            public ulong Stamp;
            public int Pos;
            public ScriptValue Fallback;
        }

        public ScriptValue GetGlobalCached(GlobalCacheEntry[] cache, int slot, string name)
        {
            OrderedTable t = _globals.Table;
            ulong stamp = t.Version + 1;
            GlobalCacheEntry e = cache[slot];
            if (e.Stamp == stamp)
            {
                return e.Pos >= 0 ? t.ValueAt(e.Pos) : e.Fallback;
            }
            StrValue key = Key(name);
            ScriptValue v;
            int pos;
            if (t.TryGetValueWithPos(key, PyOps.Hash(key, _ctx, 0), _ctx, 0, out v, out pos))
            {
                cache[slot] = new GlobalCacheEntry { Stamp = stamp, Pos = pos, Fallback = null };
                return v;
            }
            if (_builtins.TryGet(name, out v))
            {
                cache[slot] = new GlobalCacheEntry { Stamp = stamp, Pos = -1, Fallback = v };
                return v;
            }
            string unsupported;
            if (HostFunctionTable.TryGetUnsupportedMessage(name, out unsupported))
            {
                throw Raise.Make(_ctx, PyExceptionTypes.NameError, unsupported);
            }
            throw Raise.NameError(_ctx, name);
        }

        // Write twin: a cached position lets `n = ...` in a loop update the entry in place (the same
        // store InsertOrUpdate's update branch performs). The slow path primes the cache from the
        // write itself (post-op Version), so write-only loop variables hit from iteration two.
        public void SetGlobalCached(GlobalCacheEntry[] cache, int slot, string name, ScriptValue v)
        {
            OrderedTable t = _globals.Table;
            GlobalCacheEntry e = cache[slot];
            if (e.Stamp == t.Version + 1 && e.Pos >= 0)
            {
                t.SetValueAt(e.Pos, v);
                return;
            }
            StrValue key = Key(name);
            int pos;
            t.InsertOrUpdateWithPos(key, PyOps.Hash(key, _ctx, 0), v, _ctx, 0, out pos);
            cache[slot] = new GlobalCacheEntry { Stamp = t.Version + 1, Pos = pos, Fallback = null };
        }

        public void SetGlobal(string name, ScriptValue v)
        {
            _globals.SetItem(Key(name), v, _ctx);
        }

        public void DelGlobal(string name)
        {
            ScriptValue key = Key(name);
            ScriptValue ignored;
            if (!_globals.TryGet(key, _ctx, out ignored))
            {
                throw Raise.NameError(_ctx, name);   // builtins cannot be deleted from a script
            }
            _globals.DelItemOrThrow(key, _ctx);
        }
    }
}
