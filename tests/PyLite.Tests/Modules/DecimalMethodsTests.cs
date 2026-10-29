using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/10 T-MNM-10/11: Decimal quantize / rounding modes / normalize / as_tuple / predicates / __format__ /
    // getcontext.
    [Trait("Category", "Compatibility")]
    public class DecimalMethodsTests
    {
        private const string Prelude =
            "from decimal import Decimal, getcontext, ROUND_CEILING, ROUND_FLOOR, ROUND_UP, ROUND_DOWN, "
            + "ROUND_HALF_UP, ROUND_HALF_DOWN, ROUND_HALF_EVEN, ROUND_05UP\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        [Theory]
        // ---- quantize (8 modes) ----
        [InlineData("Decimal('1.234').quantize(Decimal('0.01'))", "Decimal('1.23')")]
        [InlineData("Decimal('1.235').quantize(Decimal('0.01'))", "Decimal('1.24')")]
        [InlineData("Decimal('1.245').quantize(Decimal('0.01'))", "Decimal('1.24')")]
        [InlineData("Decimal('2.5').quantize(Decimal('1'))", "Decimal('2')")]
        [InlineData("Decimal('3.5').quantize(Decimal('1'))", "Decimal('4')")]
        [InlineData("Decimal('-1.5').quantize(Decimal('1'), ROUND_FLOOR)", "Decimal('-2')")]
        [InlineData("Decimal('1.5').quantize(Decimal('1'), ROUND_CEILING)", "Decimal('2')")]
        [InlineData("Decimal('1.25').quantize(Decimal('0.1'), ROUND_HALF_UP)", "Decimal('1.3')")]
        [InlineData("Decimal('1.25').quantize(Decimal('0.1'), ROUND_HALF_DOWN)", "Decimal('1.2')")]
        [InlineData("Decimal('1.7').quantize(Decimal('1'), ROUND_DOWN)", "Decimal('1')")]
        [InlineData("Decimal('1.2').quantize(Decimal('1'), ROUND_UP)", "Decimal('2')")]
        [InlineData("Decimal('1.10').quantize(Decimal('1'), ROUND_05UP)", "Decimal('1')")]
        [InlineData("Decimal('1.234').quantize(Decimal('1.00'))", "Decimal('1.23')")]
        // ---- normalize / to_integral / as_tuple / adjusted ----
        [InlineData("Decimal('1.2300').normalize()", "Decimal('1.23')")]
        [InlineData("Decimal('120').normalize()", "Decimal('1.2E+2')")]       // a trailing zero moves into the exponent
        [InlineData("Decimal('100').normalize()", "Decimal('1E+2')")]
        [InlineData("Decimal('0.00').normalize()", "Decimal('0')")]
        [InlineData("Decimal('5.00').to_integral_value()", "Decimal('5')")]
        [InlineData("Decimal('1.23').as_tuple()", "DecimalTuple(sign=0, digits=(1, 2, 3), exponent=-2)")]
        [InlineData("Decimal('-0.5').as_tuple()", "DecimalTuple(sign=1, digits=(5,), exponent=-1)")]
        [InlineData("Decimal('-0.5').as_tuple() == (1, (5,), -1)", "True")]
        [InlineData("Decimal('-0.5').as_tuple().exponent", "-1")]
        [InlineData("Decimal('1.23').adjusted()", "0")]
        // ---- __format__ ----
        [InlineData("format(Decimal('1.5'), '.2f')", "'1.50'")]
        [InlineData("f'{Decimal(\"1234.5\"):,.2f}'", "'1,234.50'")]
        [InlineData("format(Decimal('0.125'), '.2f')", "'0.12'")]
        [InlineData("format(Decimal('1'), '.0%')", "'100%'")]
        [InlineData("format(Decimal('1.23'), '')", "'1.23'")]
        // ---- predicates / min-max / compare ----
        [InlineData("Decimal('2').max(Decimal('3'))", "Decimal('3')")]
        [InlineData("Decimal('2').min(Decimal('3'))", "Decimal('2')")]
        [InlineData("Decimal('-5').copy_abs()", "Decimal('5')")]
        [InlineData("Decimal('5').copy_negate()", "Decimal('-5')")]
        [InlineData("Decimal('0').is_zero()", "True")]
        [InlineData("Decimal('1').is_finite()", "True")]
        [InlineData("Decimal('1').is_nan()", "False")]
        [InlineData("Decimal('1').number_class()", "'+Normal'")]
        [InlineData("Decimal('0').number_class()", "'+Zero'")]
        [InlineData("Decimal('-0').number_class()", "'-Zero'")]
        [InlineData("Decimal('-1').number_class()", "'-Normal'")]
        [InlineData("Decimal('1.50').same_quantum(Decimal('2.71'))", "True")]
        [InlineData("Decimal('1.5').same_quantum(Decimal('2'))", "False")]
        [InlineData("Decimal('1').compare(Decimal('2'))", "Decimal('-1')")]
        // ---- getcontext ----
        [InlineData("getcontext().prec", "28")]
        [InlineData("getcontext().rounding", "'ROUND_HALF_EVEN'")]
        public void DecimalMethodBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void ContextRoundingAffectsQuantize()
        {
            string src = Prelude + "getcontext().rounding = ROUND_DOWN\nr = Decimal('1.7').quantize(Decimal('1'))\n";
            Assert.Equal("Decimal('1')", Ev.Run(src).Repr("r"));
        }

        // prec is what every arithmetic result is rounded to, as in CPython; a literal stays exact.
        [Theory]
        [InlineData("Decimal(1) / Decimal(7)", "Decimal('0.14286')")]
        [InlineData("Decimal('1.23456789') + 0", "Decimal('1.2346')")]
        [InlineData("+Decimal('1.23456789')", "Decimal('1.2346')")]
        [InlineData("-Decimal('1.23456789')", "Decimal('-1.2346')")]
        [InlineData("abs(Decimal('-1.23456789'))", "Decimal('1.2346')")]
        [InlineData("Decimal('1.23456789')", "Decimal('1.23456789')")]
        [InlineData("Decimal('1.1111') * Decimal('1.1111')", "Decimal('1.2345')")]
        [InlineData("Decimal('9999.95') + 0", "Decimal('10000')")]
        [InlineData("Decimal('1.234567800').normalize()", "Decimal('1.2346')")]
        [InlineData("Decimal('1.5') ** 3", "Decimal('3.375')")]
        [InlineData("getcontext().prec", "5")]
        [InlineData("repr(getcontext())", "'Context(prec=5, rounding=ROUND_HALF_EVEN)'")]
        public void PrecRoundsEveryResult(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(Prelude + "getcontext().prec = 5\nr = (" + expr + ")\n").Repr("r"));
        }

        // // and % are exact: a result the precision cannot hold is refused, not rounded.
        [Theory]
        [InlineData("Decimal(123456789) // Decimal(1)", "InvalidOperation")]
        [InlineData("Decimal(123456789) % Decimal(7)", "InvalidOperation")]
        [InlineData("Decimal('1234.5').quantize(Decimal('0.01'))", "InvalidOperation")]
        public void PrecRefusesWhatItCannotHold(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(
                () => Ev.Run(Prelude + "getcontext().prec = 5\nr = (" + expr + ")\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }

        [Theory]
        [InlineData("50")]
        [InlineData("0")]
        [InlineData("-1")]
        public void PrecOutOfRangeIsRefused(string value)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Prelude + "getcontext().prec = " + value + "\n"));
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
            Assert.Contains("valid range for prec is [1, 28]", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void PrecMustBeAnInteger()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Prelude + "getcontext().prec = '5'\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        // round() goes through the context mode too, and round(d, n) quantizes, so it pads.
        [Theory]
        [InlineData("ROUND_HALF_EVEN", "round(Decimal('1.5'), 3)", "Decimal('1.500')")]
        [InlineData("ROUND_HALF_EVEN", "round(Decimal('1234.5'), -2)", "Decimal('1.2E+3')")]
        [InlineData("ROUND_UP", "round(Decimal('1.111'), 2)", "Decimal('1.12')")]
        [InlineData("ROUND_UP", "round(Decimal('1.1'))", "2")]
        [InlineData("ROUND_DOWN", "round(Decimal('1.9'))", "1")]
        public void RoundUsesTheContextMode(string mode, string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(Prelude + "getcontext().rounding = " + mode + "\nr = (" + expr + ")\n").Repr("r"));
        }

        // Nothing below 1e-28 fits the backend, and answering 0 would be silently wrong, so it raises.
        [Theory]
        [InlineData("Decimal('1e-30')")]
        [InlineData("Decimal('0.00000000000000000000000000005')")]
        [InlineData("Decimal(1e-30)")]
        [InlineData("Decimal('1e-20') * Decimal('1e-20')")]
        [InlineData("Decimal('1e-20') / Decimal('1e20')")]
        [InlineData("Decimal('0.1') ** 30")]
        public void UnderflowIsLoud(string expr)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Prelude + "r = (" + expr + ")\n"));
            Assert.Equal("Underflow", ex.Value.ExcType.Name);
        }

        // A zero that is genuinely the answer stays an answer.
        [Theory]
        [InlineData("Decimal('1e-28')", "Decimal('1E-28')")]
        [InlineData("Decimal('0e-30')", "Decimal('0E-30')")]
        [InlineData("Decimal('0') * Decimal('5')", "Decimal('0')")]
        [InlineData("Decimal('1.5') - Decimal('1.5')", "Decimal('0.0')")]
        [InlineData("Decimal(1) // Decimal(2)", "Decimal('0')")]
        [InlineData("Decimal('0.001').quantize(Decimal('0.1'))", "Decimal('0.0')")]
        public void ZeroThatIsNotUnderflow(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void UnderflowIsInTheDecimalTree()
        {
            string src = Prelude + "from decimal import DecimalException\n"
                + "try:\n    Decimal('1e-30')\n    r = 'no error'\nexcept DecimalException as e:\n"
                + "    r = type(e).__name__\n";
            Assert.Equal("'Underflow'", Ev.Run(src).Repr("r"));
        }

        // scaleb moves the exponent, shift moves the coefficient's digits inside the precision and
        // leaves the exponent alone, and as_integer_ratio is exact because a scaled decimal always is.
        [Theory]
        [InlineData("Decimal('1.5').scaleb(2)", "Decimal('1.5E+2')")]
        [InlineData("Decimal('150').scaleb(-2)", "Decimal('1.50')")]
        [InlineData("Decimal('1.5').scaleb(0)", "Decimal('1.5')")]
        [InlineData("Decimal('-2').scaleb(3)", "Decimal('-2E+3')")]
        [InlineData("Decimal('123').shift(1)", "Decimal('1230')")]
        [InlineData("Decimal('123').shift(-1)", "Decimal('12')")]
        [InlineData("Decimal('123').shift(0)", "Decimal('123')")]
        [InlineData("Decimal('1.23').shift(1)", "Decimal('12.30')")]
        [InlineData("Decimal('0.5').as_integer_ratio()", "(1, 2)")]
        [InlineData("Decimal('3').as_integer_ratio()", "(3, 1)")]
        [InlineData("Decimal('19.99').as_integer_ratio()", "(1999, 100)")]
        [InlineData("Decimal('-0.25').as_integer_ratio()", "(-1, 4)")]
        [InlineData("Decimal('0').as_integer_ratio()", "(0, 1)")]
        public void ScalebShiftAndRatio(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void ShiftBeyondPrecisionIsInvalidOperation()
        {
            var ex = Assert.Throws<ScriptException>(
                () => Ev.Run(Prelude + "r = Decimal('1').shift(40)\n"));
            Assert.Equal("InvalidOperation", ex.Value.ExcType.Name);
        }

        [Fact]
        public void DefaultContextIsAttributeError()
        {
            // setcontext/localcontext/Context exist since 1.1.0; the module-level template contexts do not
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import decimal\nx = decimal.DefaultContext\n"));
            Assert.Equal("AttributeError", ex.Value.ExcType.Name);
        }
    }
}
