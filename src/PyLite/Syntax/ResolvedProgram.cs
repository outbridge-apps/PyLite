using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    // Immutable resolver output. The resolver bakes NameNode.Binding / Node.ScopeInfo onto the nodes, so
    // the runtime reads those fields and no per-node dictionary is kept with a cached script.
    public sealed class ResolvedProgram
    {
        public ModuleNode Module { get; }
        public FunctionInfo ModuleInfo { get; }

        // Number of distinct builtin-classified names; sizes the Evaluator's per-run resolved cache.
        internal int BuiltinSlotCount { get; }

        // Number of distinct global-classified names; sizes the per-run versioned dict-entry cache.
        internal int GlobalSlotCount { get; }

        // Number of str/float/bytes literals; sizes the per-run literal cache (LiteralNode.CacheSlot).
        internal int LiteralCount { get; }

        internal ResolvedProgram(ModuleNode module, FunctionInfo moduleInfo, int builtinSlotCount, int globalSlotCount,
            int literalCount)
        {
            Module = module;
            ModuleInfo = moduleInfo;
            BuiltinSlotCount = builtinSlotCount;
            GlobalSlotCount = globalSlotCount;
            LiteralCount = literalCount;
        }

        public FunctionInfo GetFunctionInfo(Node scopeNode)
        {
            return scopeNode.ScopeInfo;
        }

        public NameBinding GetBinding(NameNode name)
        {
            return name.Binding;
        }
    }
}
