using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime
{
    // Per-run decimal context. Both settings are script-settable and drive every arithmetic
    // result; the System.Decimal backend is what caps prec at 28. Not static.
    public sealed class DecimalContext
    {
        public const int MaxPrec = 28;

        public int Prec = MaxPrec;

        private string _rounding = "ROUND_HALF_EVEN";

        // The name is what the script reads and writes; the parsed mode is what every operation uses.
        internal RoundingMode Mode = RoundingMode.HalfEven;

        public string Rounding
        {
            get { return _rounding; }
            set
            {
                _rounding = value;
                Mode = DecimalRound.Parse(value);
            }
        }
    }
}
