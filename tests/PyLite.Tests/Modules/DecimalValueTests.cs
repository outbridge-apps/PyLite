using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/10 T-MNM-08/09: decimal.Decimal (repr/ctor/arith/compare/hash). // truncates and % takes the
    // dividend's sign (real CPython decimal; the doc's floor values were an error).
    [Trait("Category", "Compatibility")]
    public class DecimalValueTests
    {
        private const string Prelude = "from decimal import Decimal, InvalidOperation, DivisionByZero, Overflow, DecimalException\nfrom fractions import Fraction\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(Prelude + body + "\n"));
        }

        [Theory]
        // ---- construction / str (T-MNM-08) ----
        [InlineData("str(Decimal('0'))", "'0'")]
        [InlineData("str(Decimal('1.00'))", "'1.00'")]
        // to-scientific-string: an exponent above zero, or a value below 1e-6, prints in exponent form
        [InlineData("str(Decimal('1E+2'))", "'1E+2'")]
        [InlineData("str(Decimal('100'))", "'100'")]
        [InlineData("str(Decimal('0.0000001'))", "'1E-7'")]
        [InlineData("str(Decimal('0.000001'))", "'0.000001'")]
        [InlineData("str(Decimal('0E+2'))", "'0E+2'")]
        [InlineData("str(Decimal('1E-3'))", "'0.001'")]
        [InlineData("str(Decimal('.5'))", "'0.5'")]
        [InlineData("str(Decimal('  1.5  '))", "'1.5'")]
        [InlineData("str(Decimal(100))", "'100'")]
        [InlineData("repr(Decimal(True))", "\"Decimal('1')\"")]
        [InlineData("str(Decimal('-5.0'))", "'-5.0'")]
        [InlineData("repr(Decimal('1.23'))", "\"Decimal('1.23')\"")]
        // ---- from float (T-MNM-09) ----
        [InlineData("str(Decimal(0.5))", "'0.5'")]
        [InlineData("str(Decimal(0.1))", "'0.1000000000000000055511151231'")]
        [InlineData("repr(Decimal.from_float(1.0))", "\"Decimal('1')\"")]
        // ---- comparisons / hash ----
        [InlineData("Decimal('7') == 7", "True")]
        [InlineData("Decimal('0.1') == 0.1", "False")]                 // Decimal('0.1') is exactly 1/10, float 0.1 is not
        [InlineData("Decimal(0.1) == 0.1", "False")]                   // 28-digit backend rounds off the float tail
        [InlineData("hash(Decimal(7)) == hash(7) == hash(7.0)", "True")]
        [InlineData("Decimal('1.1') < Decimal('1.2')", "True")]
        [InlineData("2 < Decimal('2.5')", "True")]
        [InlineData("Decimal('1.0') == Decimal('1')", "True")]
        [InlineData("len({Decimal(1): 'a', 1: 'b'})", "1")]
        [InlineData("sorted([Decimal(3), 1, Decimal(2)])", "[1, Decimal('2'), Decimal('3')]")]
        // ---- arithmetic (real CPython decimal semantics) ----
        [InlineData("Decimal(1) + 2", "Decimal('3')")]
        [InlineData("Decimal('1.5') + Decimal('2.5')", "Decimal('4.0')")]
        [InlineData("Decimal(-7) // Decimal(2)", "Decimal('-3')")]     // truncates toward zero
        [InlineData("Decimal(-7) % Decimal(2)", "Decimal('-1')")]      // sign of dividend
        [InlineData("Decimal(7) // Decimal(2)", "Decimal('3')")]
        [InlineData("Decimal(2) ** 10", "Decimal('1024')")]
        [InlineData("Decimal(2) ** -1", "Decimal('0.5')")]
        [InlineData("divmod(Decimal(7), Decimal(2))", "(Decimal('3'), Decimal('1'))")]
        [InlineData("-Decimal('5')", "Decimal('-5')")]
        [InlineData("abs(Decimal('-5'))", "Decimal('5')")]
        // ---- Fraction interop ----
        [InlineData("Fraction(Decimal('1.5'))", "Fraction(3, 2)")]
        [InlineData("Fraction(1,2) == Decimal('0.5')", "True")]
        [InlineData("Fraction.from_decimal(Decimal('0.25'))", "Fraction(1, 4)")]
        // ---- long literals: past 60 significant digits the tail is one sticky digit, the parse linear ----
        [InlineData("Decimal('0.' + '9' * 100000)", "Decimal('1.0000000000000000000000000000')")]
        [InlineData("Decimal('0.' + '1' * 27 + '25' + '0' * 60)", "Decimal('0.1111111111111111111111111112')")]       // a tie: even
        [InlineData("Decimal('0.' + '1' * 27 + '25' + '0' * 60 + '1')", "Decimal('0.1111111111111111111111111113')")]   // past the tie
        [InlineData("Decimal('0' * 100000 + '1.5')", "Decimal('1.5')")]
        // past 29 digits: rounded once to the most places whose mantissa fits 96 bits (they used to overflow)
        [InlineData("Decimal('31.4159265358979323846264338327950288')", "Decimal('31.415926535897932384626433833')")]
        [InlineData("Decimal('9.9999999999999999999999999999')", "Decimal('10.000000000000000000000000000')")]
        [InlineData("Decimal('123456789012345678901.123456789')", "Decimal('123456789012345678901.12345679')")]
        [InlineData("Decimal('7922816251426433759354395033.55')", "Decimal('7922816251426433759354395034')")]
        [InlineData("Decimal('79228162514264337593543950335')", "Decimal('79228162514264337593543950335')")]
        // PEP 515 grouping: CPython drops every underscore before parsing
        [InlineData("Decimal('1_000.000_1')", "Decimal('1000.0001')")]
        [InlineData("Decimal(' -1_2_3.4_5 ')", "Decimal('-123.45')")]
        public void DecimalBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("x = Decimal('1E+30')", "Overflow")]
        [InlineData("x = Decimal('9' * 1000000)", "Overflow")]   // without parsing a million digits
        [InlineData("x = Decimal('99999999999999999999999999999')", "Overflow")]     // 29 integer digits past 2^96
        [InlineData("x = Decimal('79228162514264337593543950335.5')", "Overflow")]   // rounds up past 2^96 - 1
        [InlineData("x = Decimal('NaN')", "InvalidOperation")]
        [InlineData("x = Decimal('_')", "InvalidOperation")]
        [InlineData("x = Decimal('1.2.3')", "InvalidOperation")]
        [InlineData("x = Decimal('')", "InvalidOperation")]
        [InlineData("x = Decimal(float('nan'))", "InvalidOperation")]
        [InlineData("x = Decimal(float('inf'))", "Overflow")]
        [InlineData("x = Decimal(1) + 2.0", "TypeError")]
        [InlineData("x = Decimal(1) / 0", "DivisionByZero")]
        [InlineData("x = Decimal(1) % Decimal(0)", "InvalidOperation")]
        [InlineData("x = Decimal(2) ** 0.5", "TypeError")]
        [InlineData("x = Fraction(1,2) + Decimal('0.5')", "TypeError")]
        public void DecimalErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void DecimalExceptionCatchesAll()
        {
            string src = Prelude + "n = 0\nfor op in range(3):\n    try:\n        if op == 0:\n            Decimal(1)/0\n        elif op == 1:\n            Decimal('x')\n        else:\n            Decimal('1E+30')\n    except DecimalException:\n        n += 1\n";
            Assert.Equal("3", Ev.Run(src).Repr("n"));
        }

        [Fact]
        public void DivisionByZeroIsArithmeticError()
        {
            string src = Prelude + "caught = 0\ntry:\n    Decimal(1) / 0\nexcept ArithmeticError:\n    caught = 1\n";
            Assert.Equal("1", Ev.Run(src).Repr("caught"));
        }

        // An exactly zero sum takes the addends' sign when they agree, else +, and - only under ROUND_FLOOR.
        [Theory]
        [InlineData("Decimal('1.30') - Decimal('1.3')", "Decimal('0.00')")]
        [InlineData("Decimal('-1') + Decimal('1')", "Decimal('0')")]
        [InlineData("Decimal('1') + Decimal('-1')", "Decimal('0')")]
        [InlineData("sum([Decimal('0.10'), Decimal('-0.1')])", "Decimal('0.00')")]
        [InlineData("Decimal('-0') + Decimal('-0')", "Decimal('-0')")]
        [InlineData("Decimal('-0') - Decimal('0')", "Decimal('-0')")]
        [InlineData("Decimal('0') - Decimal('-0')", "Decimal('0')")]
        [InlineData("Decimal(0) * -1", "Decimal('-0')")]
        public void ZeroSumSign(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void ZeroSumIsNegativeUnderRoundFloor()
        {
            string src = Prelude + "from decimal import localcontext, ROUND_FLOOR\nwith localcontext() as c:\n"
                + "    c.rounding = ROUND_FLOOR\n    r = (Decimal('1.30') - Decimal('1.3'), Decimal('-1') + Decimal('1'))\n";
            Assert.Equal("(Decimal('-0.00'), Decimal('-0'))", Ev.Run(src).Repr("r"));
        }
    }
}
