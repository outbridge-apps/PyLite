using System.Collections.Generic;

namespace Outbridge.PyLite.Syntax
{
    // Immutable slot layout for a function/lambda/comprehension scope. Consumed by the
    // runtime Environment. Module scope uses a globals dictionary and has an empty slot layout.
    public sealed class FunctionInfo
    {
        public string Name { get; }
        public ScopeKind Kind { get; }
        public int ParamCount { get; }              // positional + keyword-only (for argument binding)
        public int PositionalCount { get; }
        public int PositionalOnlyCount { get; }     // PEP 570: leading positionals not bindable by keyword
        public int KwOnlyCount { get; }
        public bool HasStarArg { get; }
        public bool HasKwArg { get; }
        public IReadOnlyList<string> ParamNames { get; }   // positional, [*args], kwonly, [**kwargs]
        public IReadOnlyList<int> ParamSlots { get; }
        public IReadOnlyList<bool> ParamIsCell { get; }
        public IReadOnlyList<string> LocalNames { get; }
        public int LocalSlotCount { get; }
        public IReadOnlyList<string> CellVars { get; }
        public IReadOnlyList<string> FreeVars { get; }

        // Runtime cache (the benign-race pattern of ComprehensionNode.CompiledElement): the
        // closure-compiled lambda-body delegate, or its not-compilable sentinel. One FunctionInfo
        // per lambda syntax node, shared across runs — compiled closures capture only immutable
        // AST/binding data.
        public object CompiledLambdaBody;

        // Parameter index by name for keyword binding, the same benign-race runtime cache: a linear search
        // per keyword made a call with 300K keywords against 10K parameters run for minutes.
        private Dictionary<string, int> _paramIndex;

        internal Dictionary<string, int> ParamIndex
        {
            get
            {
                Dictionary<string, int> d = _paramIndex;
                if (d == null)
                {
                    d = new Dictionary<string, int>(ParamNames.Count, System.StringComparer.Ordinal);
                    for (int i = 0; i < ParamNames.Count; i++)
                        d[ParamNames[i]] = i;
                    _paramIndex = d;
                }
                return d;
            }
        }

        private Dictionary<string, int> _cellIndex, _freeIndex, _localIndex;

        // The position of a name among CellVars / FreeVars / LocalNames, or -1; past a few names through an
        // index built once, the same benign-race cache: a scan per captured variable made a closure over
        // 28,000 names quadratic, and one per def or import statement in a big scope was uncharged work.
        internal int CellIndexOf(string name) { return IndexOf(CellVars, ref _cellIndex, name); }
        internal int FreeIndexOf(string name) { return IndexOf(FreeVars, ref _freeIndex, name); }
        internal int LocalIndexOf(string name) { return IndexOf(LocalNames, ref _localIndex, name); }

        private static int IndexOf(IReadOnlyList<string> names, ref Dictionary<string, int> index, string name)
        {
            if (names.Count <= 16)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    if (names[i] == name)
                        return i;
                }
                return -1;
            }
            Dictionary<string, int> d = index;
            if (d == null)
            {
                d = new Dictionary<string, int>(names.Count, System.StringComparer.Ordinal);
                for (int i = names.Count - 1; i >= 0; i--)
                    d[names[i]] = i;   // the first occurrence wins, as the scan's did
                index = d;
            }
            int at;
            return d.TryGetValue(name, out at) ? at : -1;
        }

        // The same for a def whose body is one `return <expr>` (a docstring before it allowed): the compiled
        // expression, or the not-compilable sentinel, which also stands for any other body shape.
        public object CompiledReturnBody;

        // Where this scope's free variables sit in the defining frame (Environment.CaptureClosure).
        internal int[] CaptureMap;
        internal FunctionInfo CaptureScope;

        // The function a list, set or dict comprehension runs inside in CPython (PEP 709); null for any other scope.
        internal FunctionInfo InlinedInto;

        public FunctionInfo(string name, ScopeKind kind, int paramCount, int positionalCount, int kwOnlyCount,
            bool hasStarArg, bool hasKwArg, IReadOnlyList<string> paramNames, IReadOnlyList<int> paramSlots,
            IReadOnlyList<bool> paramIsCell, IReadOnlyList<string> localNames, int localSlotCount,
            IReadOnlyList<string> cellVars, IReadOnlyList<string> freeVars, int positionalOnlyCount = 0)
        {
            Name = name;
            Kind = kind;
            ParamCount = paramCount;
            PositionalCount = positionalCount;
            PositionalOnlyCount = positionalOnlyCount;
            KwOnlyCount = kwOnlyCount;
            HasStarArg = hasStarArg;
            HasKwArg = hasKwArg;
            ParamNames = paramNames;
            ParamSlots = paramSlots;
            ParamIsCell = paramIsCell;
            LocalNames = localNames;
            LocalSlotCount = localSlotCount;
            CellVars = cellVars;
            FreeVars = freeVars;
        }
    }
}
