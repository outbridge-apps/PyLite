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

        public void Append(char c)
        {
            EnsureRoom(1);
            _sb.Append(c);
        }

        private void EnsureRoom(int add)
        {
            long newLen = (long)_sb.Length + add;
            if (newLen > _maxChars)
                throw _budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", _maxChars, newLen);
            if (newLen > _sb.Capacity)
                _budget.ChargeAllocation(2L * (newLen - _sb.Capacity));   // charge BEFORE the real growth
        }

        public override string ToString() { return _sb.ToString(); }
    }
}
