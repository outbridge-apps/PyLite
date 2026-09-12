namespace Outbridge.PyLite.Runtime.Values
{
    // An iterator whose IteratorValue wrapper renders a custom repr (itertools count/repeat show live state,
    // e.g. "count(1)"), instead of the generic "<name object>".
    internal interface IReprIterator
    {
        void AppendRepr(BudgetStringBuilder sb, EvalContext ctx);
    }

    // Result of iter(x); itself iterable with iter(it) is it. Hashable by identity.
    public sealed class IteratorValue : ScriptValue
    {
        // One ScriptTypeInfo per iterator name, so type(iter([])) reports list_iterator (CPython parity).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ScriptTypeInfo> Types =
            new System.Collections.Concurrent.ConcurrentDictionary<string, ScriptTypeInfo>(System.StringComparer.Ordinal);

        private readonly ScriptTypeInfo _type;

        internal readonly IScriptIterator Iterator;
        internal readonly string IterTypeName;   // "list_iterator", "str_iterator", ...
        private readonly long _identity;

        internal IteratorValue(IScriptIterator it, string iterTypeName, long identity)
            : this(it, iterTypeName, identity, null)
        {
        }

        // slots: an iterator that also carries attributes (csv.DictReader.fieldnames). The type cache is
        // keyed by NAME, so every iterator of one name must pass the same table.
        internal IteratorValue(IScriptIterator it, string iterTypeName, long identity,
            System.Collections.Generic.IDictionary<string, SlotDescriptor> slots)
        {
            Iterator = it;
            IterTypeName = iterTypeName;
            _identity = identity;
            _type = Types.GetOrAdd(iterTypeName, n => new ScriptTypeInfo(n, slots));
        }

        public override ScriptTypeInfo TypeInfo { get { return _type; } }
        internal override ValueKind Kind { get { return ValueKind.Iterator; } }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return true; }
        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return Iterator; }
        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return _identity; }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            IReprIterator r = Iterator as IReprIterator;
            if (r != null)
                r.AppendRepr(sb, ctx);
            else
                sb.Append("<" + IterTypeName + " object>");
        }
    }
}
