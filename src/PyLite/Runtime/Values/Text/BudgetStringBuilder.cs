using System;
using System.Text;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // A StringBuilder wrapper that charges before growth and hard-caps at MaxStrChars.
    // The single string-assembly buffer used by ReprEngine and string operations.
    public sealed class BudgetStringBuilder
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Budget _budget;
        private readonly int _maxChars;

        public BudgetStringBuilder(EvalContext ctx)
        {
            _budget = ctx.Budget;
            _maxChars = ctx.Limits.MaxStrChars;
        }

        public int Length { get { return _sb.Length; } }

        public void Append(string s)
        {
            if (string.IsNullOrEmpty(s))
                return;
            EnsureRoom(s.Length);
            _sb.Append(s);
        }

        private char[] _digits;

        // A machine int's decimal text, without a string in between.
        internal void AppendDecimal(long v)
        {
            char[] buf = _digits ?? (_digits = new char[20]);
            int p = IntValue.WriteDecimal(v, buf);
            EnsureRoom(buf.Length - p);
            _sb.Append(buf, p, buf.Length - p);
        }

        public void Append(char c)
        {
            EnsureRoom(1);
            _sb.Append(c);
        }

        // Charged so far, ahead of the content by at most a block. The first FreeChars are not: a small builder
        // becomes a str that charges itself, so a million f"{i}" paid twice for the same text.
        private const int FreeChars = 256;
        private long _chargedChars = FreeChars;

        // Charges the content before it is appended, a block at a time as the builder itself grows (doubling,
        // from its own 16 chars, at most 8,000 a block). Charging only what overshot the capacity was 2 bytes per
        // growth, so a 15M-char csv row was charged nothing; a fixed large block made every small repr costly.
        private void EnsureRoom(int add)
        {
            long newLen = (long)_sb.Length + add;
            if (newLen > _maxChars)
                throw _budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", _maxChars, newLen);
            if (newLen > _chargedChars)
            {
                long block = Math.Max(newLen - _chargedChars, Math.Min(Math.Max(_chargedChars, 16), 8000));
                _budget.ChargeAllocation(2L * block);
                _chargedChars += block;
            }
        }

        public override string ToString() { return _sb.ToString(); }
    }
}
