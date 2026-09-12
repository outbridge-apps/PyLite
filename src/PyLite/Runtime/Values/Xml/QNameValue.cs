using System.Collections.Generic;

namespace Outbridge.PyLite.Runtime.Values
{
    // xml.etree.ElementTree.QName: a carrier for the '{uri}local' text and nothing else. It compares,
    // orders and hashes by that text, so it serves as a dict key and sorts beside plain strings, and
    // str() gives the text back. A tag or attribute name written as one is stored as its text, so the
    // rest of the tree machinery never has to know about the type.
    internal sealed class QNameValue : ScriptValue
    {
        internal static readonly ScriptTypeInfo QNameType =
            new ScriptTypeInfo("xml.etree.ElementTree.QName", BuildSlots());

        internal readonly StrValue Text;

        internal QNameValue(StrValue text) { Text = text; }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            return new Dictionary<string, SlotDescriptor>(System.StringComparer.Ordinal)
            {
                ["text"] = SlotDescriptor.MakeProperty("text", (self, ctx) => ((QNameValue)self).Text),
            };
        }

        public override ScriptTypeInfo TypeInfo { get { return QNameType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return PyOps.Hash(Text, ctx, depth + 1); }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            QNameValue q = other as QNameValue;
            return q != null && PyOps.Equals(Text, q.Text, ctx, depth + 1);
        }

        // Equal to the plain string it carries, and ordered against one, which is how CPython's QName
        // behaves through its __eq__/__lt__ over self.text.
        protected internal override bool TryEqualsAcrossKindCore(ScriptValue other, EvalContext ctx, int depth, out bool equal)
        {
            StrValue s = other as StrValue;
            equal = s != null && PyOps.Equals(Text, s, ctx, depth + 1);
            return s != null;
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            QNameValue q = other as QNameValue;
            StrValue s = q != null ? q.Text : other as StrValue;
            if (s == null)
            {
                cmp = 0;
                return false;
            }
            cmp = string.CompareOrdinal(Text.Value, s.Value);
            return true;
        }

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append(Text.Value); }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("<QName ");
            sb.Append(Text.Repr(ctx, depth + 1));
            sb.Append('>');
        }
    }
}
