using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    public enum ScopeKind { Module, Function, Comprehension, Lambda }

    // Internal working structure of the resolver (not part of the output).
    internal sealed class ScopeInfo
    {
        public ScopeKind Kind;
        public ScopeInfo Parent;
        public string Name;
        public Node Node;                 // FuncDef/Lambda/Comprehension node; null for module
        public readonly List<ScopeInfo> Children = new List<ScopeInfo>();

        public readonly HashSet<string> Bound = new HashSet<string>(System.StringComparer.Ordinal);
        public readonly List<string> BoundOrder = new List<string>();
        public readonly HashSet<string> Globals = new HashSet<string>(System.StringComparer.Ordinal);
        public readonly HashSet<string> Nonlocals = new HashSet<string>(System.StringComparer.Ordinal);
        public readonly HashSet<string> Referenced = new HashSet<string>(System.StringComparer.Ordinal);

        public readonly HashSet<string> CellVars = new HashSet<string>(System.StringComparer.Ordinal);
        public readonly List<string> CellOrder = new List<string>();
        public readonly HashSet<string> FreeVars = new HashSet<string>(System.StringComparer.Ordinal);
        public readonly List<string> FreeOrder = new List<string>();

        // Parameter metadata (function/lambda scopes)
        public readonly List<string> ParamNames = new List<string>();
        public int PositionalCount;
        public int PositionalOnlyCount;
        public int KwOnlyCount;
        public bool HasStarArg;
        public bool HasKwArg;

        // Slot maps (assigned after cell/free analysis)
        public readonly Dictionary<string, int> LocalSlot = new Dictionary<string, int>(System.StringComparer.Ordinal);
        public readonly Dictionary<string, int> CellSlot = new Dictionary<string, int>(System.StringComparer.Ordinal);
        public readonly Dictionary<string, int> FreeSlot = new Dictionary<string, int>(System.StringComparer.Ordinal);
        public readonly List<string> LocalNames = new List<string>();

        public void Bind(string n) { if (Bound.Add(n))
            BoundOrder.Add(n); }
        public void AddCell(string n) { if (CellVars.Add(n))
            CellOrder.Add(n); }
        public void AddFree(string n) { if (FreeVars.Add(n))
            FreeOrder.Add(n); }

        public bool IsFunctionLike { get { return Kind != ScopeKind.Module; } }
    }
}
