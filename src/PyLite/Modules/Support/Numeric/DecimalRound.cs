using System.Numerics;

namespace Outbridge.PyLite.Modules.Support
{
    // The 8 decimal rounding modes. Applied to a nonnegative floor-quotient q with a
    // positive remainder r out of divisor; neg is the number's sign.
    internal enum RoundingMode { Ceiling, Floor, Up, Down, HalfUp, HalfDown, HalfEven, ZeroFiveUp }

    internal static class DecimalRound
    {
        public static RoundingMode Parse(string s)
        {
            switch (s)
            {
                case "ROUND_CEILING": return RoundingMode.Ceiling;
                case "ROUND_FLOOR": return RoundingMode.Floor;
                case "ROUND_UP": return RoundingMode.Up;
                case "ROUND_DOWN": return RoundingMode.Down;
                case "ROUND_HALF_UP": return RoundingMode.HalfUp;
                case "ROUND_HALF_DOWN": return RoundingMode.HalfDown;
                case "ROUND_05UP": return RoundingMode.ZeroFiveUp;
                default: return RoundingMode.HalfEven;
            }
        }

        public static BigInteger Apply(BigInteger q, BigInteger r, BigInteger divisor, bool neg, RoundingMode mode)
        {
            if (r.IsZero)
                return q;
            BigInteger twice = r * 2;
            switch (mode)
            {
                case RoundingMode.Down:
                    return q;
                case RoundingMode.Up:
                    return q + 1;
                case RoundingMode.Floor:
                    return neg ? q + 1 : q;
                case RoundingMode.Ceiling:
                    return neg ? q : q + 1;
                case RoundingMode.HalfUp:
                    return twice >= divisor ? q + 1 : q;
                case RoundingMode.HalfDown:
                    return twice > divisor ? q + 1 : q;
                case RoundingMode.ZeroFiveUp:
                    BigInteger last = q % 10;
                    return last == 0 || last == 5 ? q + 1 : q;
                default:   // HalfEven
                    if (twice > divisor)
                        return q + 1;
                    if (twice < divisor)
                        return q;
                    return q.IsEven ? q : q + 1;
            }
        }
    }
}
