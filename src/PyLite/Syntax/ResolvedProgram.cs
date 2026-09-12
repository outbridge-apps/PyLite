using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    // Immutable resolver output; annotations keyed by reference identity. The resolver also
    // bakes NameNode.Binding / Node.ScopeInfo onto the nodes — the runtime reads those fields, the
    // dictionaries stay as the authoritative build artifact.
    public sealed class ResolvedProgram
    {
        public ModuleNode Module { get; }
        public FunctionInfo ModuleInfo { get; }

        private readonly Dictionary<Node, FunctionInfo> _scopeInfos;
        private readonly Dictionary<NameNode, NameBinding> _bindings;
        private readonly Dictionary<ExprNode, ExprNode> _constFolds;

        // Number of distinct builtin-classified names; sizes the Evaluator's per-run resolved cache.
        internal int BuiltinSlotCount { get; }

        // Number of distinct global-classified names; sizes the per-run versioned dict-entry cache.
        internal int GlobalSlotCount { get; }

        internal ResolvedProgram(ModuleNode module, FunctionInfo moduleInfo,
            Dictionary<Node, FunctionInfo> scopeInfos, Dictionary<NameNode, NameBinding> bindings,
            Dictionary<ExprNode, ExprNode> constFolds, int builtinSlotCount, int globalSlotCount)
        {
            Module = module;
            ModuleInfo = moduleInfo;
            _scopeInfos = scopeInfos;
            _bindings = bindings;
            _constFolds = constFolds;
            BuiltinSlotCount = builtinSlotCount;
            GlobalSlotCount = globalSlotCount;
        }

        public FunctionInfo GetFunctionInfo(Node scopeNode)
        {
            return scopeNode.ScopeInfo;
        }

        public NameBinding GetBinding(NameNode name)
        {
            return name.Binding;
        }

        public bool TryGetFold(ExprNode e, out ExprNode folded)
        {
            return _constFolds.TryGetValue(e, out folded);
        }
    }
}
