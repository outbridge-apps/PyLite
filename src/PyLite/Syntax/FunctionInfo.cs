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
