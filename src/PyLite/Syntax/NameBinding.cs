namespace Outbridge.PyLite.Syntax
{
    public enum NameKind { Local, Global, Nonlocal, Builtin, Cell, Free }

    public sealed class NameBinding
    {
        public NameKind Kind { get; }
        public int Slot { get; }   // index into localSlots/cellVars/freeVars; -1 for Global;
                                   // for Builtin: index into the per-run resolved-builtin cache
        public NameBinding(NameKind kind, int slot) { Kind = kind; Slot = slot; }
    }
}
