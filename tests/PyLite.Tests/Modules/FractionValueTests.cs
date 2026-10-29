using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/10 T-MNM-12/13/14: fractions.Fraction. (Decimal interop tests land with DecimalValue.)
    [Trait("Category", "Compatibility")]
    public class FractionValueTests
    {
        private const string Prelude = "from fractions import Fraction\nimport math\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(Prelude + body + "\n"));
        }

        [Theory]
        // ---- construction (T-MNM-12) ----
        [InlineData("Fraction(4, 6)", "Fraction(2, 3)")]
        [InlineData("Fraction(4, -6)", "Fraction(-2, 3)")]
        [InlineData("Fraction('3/7')", "Fraction(3, 7)")]
        [InlineData("Fraction(' -1.5 ')", "Fraction(-3, 2)")]
        [InlineData("Fraction('1e-3')", "Fraction(1, 1000)")]
        [InlineData("Fraction('.5')", "Fraction(1, 2)")]
        [InlineData("Fraction('1.1')", "Fraction(11, 10)")]
        [InlineData("Fraction('1_000')", "Fraction(1000, 1)")]   // PEP 515 underscores (3.11)
        [InlineData("Fraction('1_0/3_0')", "Fraction(1, 3)")]
        [InlineData("Fraction('-1_0.2_5e1_0')", "Fraction(-102500000000, 1)")]
        [InlineData("Fraction(1.1)", "Fraction(2476979795053773, 2251799813685248)")]
        [InlineData("Fraction('1.1') == Fraction(1.1)", "False")]
        [InlineData("Fraction(0.5)", "Fraction(1, 2)")]
        [InlineData("Fraction(0, 5)", "Fraction(0, 1)")]
        [InlineData("(Fraction(7).numerator, Fraction(7).denominator)", "(7, 1)")]
        [InlineData("Fraction.from_float(0.5)", "Fraction(1, 2)")]
        [InlineData("str(Fraction(3, 7))", "'3/7'")]
        [InlineData("str(Fraction(7))", "'7'")]
        // ---- arithmetic (T-MNM-13) ----
        [InlineData("Fraction(1,2) + Fraction(1,3)", "Fraction(5, 6)")]
        [InlineData("Fraction(1,2) + 1", "Fraction(3, 2)")]
        [InlineData("1 + Fraction(1,2)", "Fraction(3, 2)")]
        [InlineData("Fraction(1,2) + 0.5", "1.0")]
        [InlineData("Fraction(1,2) * Fraction(2,3)", "Fraction(1, 3)")]
        [InlineData("Fraction(-7,2) // 1", "-4")]
        [InlineData("Fraction(-7,2) % 1", "Fraction(1, 2)")]
        [InlineData("Fraction(2,3) ** 2", "Fraction(4, 9)")]
        [InlineData("Fraction(2,3) ** -1", "Fraction(3, 2)")]
        [InlineData("Fraction(4) ** 0.5", "2.0")]
        [InlineData("2 - Fraction(1,2)", "Fraction(3, 2)")]
        [InlineData("1 / Fraction(1,2)", "Fraction(2, 1)")]
        [InlineData("-Fraction(1,2)", "Fraction(-1, 2)")]
        [InlineData("abs(Fraction(-3,4))", "Fraction(3, 4)")]
        // ---- comparisons / hash / rounding ----
        [InlineData("Fraction(1,3) < 0.34", "True")]
        [InlineData("2 < Fraction(5,2)", "True")]
        [InlineData("Fraction(1,2) == 0.5", "True")]
        [InlineData("Fraction(2,1) == 2", "True")]
        [InlineData("hash(Fraction(1,2)) == hash(0.5)", "True")]
        [InlineData("hash(Fraction(7)) == hash(7)", "True")]
        [InlineData("hash(Fraction(7)) == hash(7.0)", "True")]
        [InlineData("math.floor(Fraction(7,2))", "3")]
        [InlineData("math.ceil(Fraction(7,2))", "4")]
        [InlineData("math.trunc(Fraction(-7,2))", "-3")]
        [InlineData("round(Fraction(7,2))", "4")]
        [InlineData("round(Fraction(5,2))", "2")]
        [InlineData("round(Fraction(1,3), 2)", "Fraction(33, 100)")]
        [InlineData("sorted([Fraction(1,2), Fraction(1,3), Fraction(2,3)])", "[Fraction(1, 3), Fraction(1, 2), Fraction(2, 3)]")]
        [InlineData("bool(Fraction(0))", "False")]
        [InlineData("bool(Fraction(1,2))", "True")]
        // ---- limit_denominator (T-MNM-14) ----
        [InlineData("Fraction('3.141592653589793').limit_denominator(10)", "Fraction(22, 7)")]
        [InlineData("Fraction('3.141592653589793').limit_denominator(100)", "Fraction(311, 99)")]
        [InlineData("Fraction(1.1).limit_denominator()", "Fraction(11, 10)")]
        [InlineData("Fraction(3,7).limit_denominator(5)", "Fraction(2, 5)")]
        [InlineData("Fraction(1,3).limit_denominator(2)", "Fraction(1, 2)")]
        [InlineData("Fraction(5).limit_denominator(1)", "Fraction(5, 1)")]
        [InlineData("Fraction(7).limit_denominator(3)", "Fraction(7, 1)")]
        public void FractionBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("x = Fraction(5, 0)", "ZeroDivisionError")]
        [InlineData("x = Fraction('3/0')", "ZeroDivisionError")]
        [InlineData("x = Fraction('abc')", "ValueError")]
        [InlineData("x = Fraction('  ')", "ValueError")]
        // PEP 515: a single underscore, between two digits only
        [InlineData("x = Fraction('1__0')", "ValueError")]
        [InlineData("x = Fraction('_1')", "ValueError")]
        [InlineData("x = Fraction('1_')", "ValueError")]
        [InlineData("x = Fraction('1_/2')", "ValueError")]
        [InlineData("x = Fraction('1._5')", "ValueError")]
        [InlineData("x = Fraction('1e_5')", "ValueError")]
        [InlineData("x = Fraction(float('nan'))", "ValueError")]
        [InlineData("x = Fraction(float('inf'))", "OverflowError")]
        [InlineData("x = Fraction(0) ** -1", "ZeroDivisionError")]
        public void FractionErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        // A literal whose integers would pass MaxIntBits is refused before it is built (it ran a quadratic
        // BigInteger.Parse or a 10**1e9 past the deadline); a zero mantissa never builds its power.
        [Theory]
        [InlineData("Fraction('9' * 1000000)")]
        [InlineData("Fraction('1/' + '9' * 1000000)")]
        [InlineData("Fraction('1e999999999')")]
        [InlineData("Fraction('1e-999999')")]
        // parts built by arithmetic are ints too: gated before the product or power is built
        [InlineData("Fraction(3, 7) ** 1000000")]
        [InlineData("Fraction(1 << 600000, 3) * Fraction(1 << 500000, 5)")]
        [InlineData("round(Fraction(1, 3), 400000)")]
        public void LongLiteralAbortsOnIntBits(string expr)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(Prelude + "x = " + expr + "\n"));
            Assert.Equal("MaxIntBits", ex.LimitName);
        }

        [Theory]
        [InlineData("Fraction('0e999999999')", "Fraction(0, 1)")]
        [InlineData("Fraction('-' + '0' * 100000 + '25e-2')", "Fraction(-1, 4)")]
        [InlineData("Fraction('12/' + '0' * 1000 + '8')", "Fraction(3, 2)")]
        // round to a power of ten past twice |self| is 0 without building the power (as round(int, -n))
        [InlineData("round(Fraction(1, 3), -400000)", "Fraction(0, 1)")]
        [InlineData("round(Fraction(10 ** 6, 3), -5)", "Fraction(300000, 1)")]
        [InlineData("(round(Fraction(5), -1), round(Fraction(15), -1), round(Fraction(7), -1))", "(Fraction(0, 1), Fraction(20, 1), Fraction(10, 1))")]
        public void LongLiteralWithinIntBits(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void InvalidLiteralMessage()
        {
            string src = Prelude + "msg = ''\ntry:\n    Fraction('abc')\nexcept ValueError as e:\n    msg = str(e)\n";
            Assert.Equal("\"Invalid literal for Fraction: 'abc'\"", Ev.Run(src).Repr("msg"));
        }

        [Fact]
        public void LimitDenominatorZeroIsValueError()
        {
            Assert.Equal("ValueError", Raises("x = Fraction(1,2).limit_denominator(0)").Value.ExcType.Name);
        }
    }
}
