using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // The exponent a value carries, and the four functions that need more than the backend's arithmetic.
    // Every expected string below is CPython's own answer at the default precision of 28.
    [Trait("Category", "Compatibility")]
    public class DecimalMathTests
    {
        private const string Prelude = "from decimal import Decimal, getcontext\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        // The coefficient and the exponent are kept apart, so a value written with an exponent above zero
        // reads back with it — and every operation settles on the exponent the spec calls ideal.
        [Theory]
        [InlineData("str(Decimal('1E+2'))", "'1E+2'")]
        [InlineData("repr(Decimal('1E+2'))", "\"Decimal('1E+2')\"")]
        [InlineData("Decimal('1E+2') == Decimal('100')", "True")]
        [InlineData("hash(Decimal('1E+2')) == hash(Decimal('100'))", "True")]
        [InlineData("int(Decimal('1E+2'))", "100")]
        [InlineData("float(Decimal('1E+2'))", "100.0")]
        [InlineData("str(Decimal('1E+2') + Decimal('1E+1'))", "'1.1E+2'")]
        [InlineData("str(Decimal('1E+2') - Decimal('1E+2'))", "'0E+2'")]
        [InlineData("str(Decimal('1E+2') * Decimal('1E+2'))", "'1E+4'")]
        [InlineData("str(Decimal('1E+2') / Decimal('1'))", "'1E+2'")]
        [InlineData("str(Decimal(100) / Decimal(10))", "'10'")]
        [InlineData("str(Decimal('100.0') / Decimal('10'))", "'10.0'")]
        [InlineData("str(Decimal('1E+2') ** 2)", "'1E+4'")]
        [InlineData("str(Decimal('1E+2') // Decimal('3'))", "'33'")]
        [InlineData("str(-Decimal('1E+2'))", "'-1E+2'")]
        [InlineData("str(abs(Decimal('-1E+2')))", "'1E+2'")]
        [InlineData("tuple(Decimal('1E+2').as_tuple())", "(0, (1,), 2)")]
        [InlineData("Decimal('1E+2').adjusted()", "2")]
        [InlineData("Decimal('1E+2').same_quantum(Decimal('2E+2'))", "True")]
        [InlineData("str(Decimal('1').scaleb(2))", "'1E+2'")]
        [InlineData("str(Decimal('123').quantize(Decimal('1E+2')))", "'1E+2'")]
        [InlineData("str(Decimal('1E+2').to_integral_value())", "'1E+2'")]
        [InlineData("Decimal('1E+2').to_eng_string()", "'100'")]
        [InlineData("Decimal('1E+3').to_eng_string()", "'1E+3'")]
        [InlineData("Decimal('1E-7').to_eng_string()", "'100E-9'")]
        [InlineData("format(Decimal('1E+2'), '')", "'1E+2'")]
        [InlineData("format(Decimal('1E+2'), '>8')", "'    1E+2'")]
        [InlineData("'{:.2f}'.format(Decimal('1E+2'))", "'100.00'")]
        public void ExponentSurvives(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // A result that no longer fits the precision below the point reads as an exponent, the way
        // CPython writes it, instead of being padded back out with zeros.
        [Fact]
        public void PrecisionSpillBecomesAnExponent()
        {
            string src = Prelude + "getcontext().prec = 5\nr = str(+Decimal('123456789'))\n";
            Assert.Equal("'1.2346E+8'", Ev.Run(src).Repr("r"));
        }

        // sqrt is an exact integer square root: the inexact case is correctly rounded and the exact one
        // keeps the ideal exponent.
        [Theory]
        [InlineData("str(Decimal(2).sqrt())", "'1.414213562373095048801688724'")]
        [InlineData("str(Decimal(4).sqrt())", "'2'")]
        [InlineData("str(Decimal('9.00').sqrt())", "'3.0'")]
        [InlineData("str(Decimal(0).sqrt())", "'0'")]
        [InlineData("str(Decimal('1e-8').sqrt())", "'0.0001'")]
        [InlineData("str(Decimal('1E+20').sqrt())", "'1E+10'")]
        [InlineData("str(Decimal(3).sqrt())", "'1.732050807568877293527446342'")]
        [InlineData("Decimal(2).sqrt() ** 2 < Decimal(2)", "True")]
        public void Sqrt(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("str(Decimal(2).ln())", "'0.6931471805599453094172321215'")]
        [InlineData("str(Decimal(10).ln())", "'2.302585092994045684017991455'")]
        [InlineData("str(Decimal(1).ln())", "'0'")]
        [InlineData("str(Decimal('0.5').ln())", "'-0.6931471805599453094172321215'")]
        [InlineData("str(Decimal('1e-10').ln())", "'-23.02585092994045684017991455'")]
        [InlineData("str(Decimal(100).log10())", "'2'")]
        [InlineData("str(Decimal('0.001').log10())", "'-3'")]
        [InlineData("str(Decimal(2).log10())", "'0.3010299956639811952137388947'")]
        [InlineData("str(Decimal(1).exp())", "'2.718281828459045235360287471'")]
        [InlineData("str(Decimal(0).exp())", "'1'")]
        [InlineData("str(Decimal(-1).exp())", "'0.3678794411714423215955237702'")]
        [InlineData("str(Decimal(10).exp())", "'22026.46579480671651695790065'")]
        [InlineData("str(Decimal(2) ** Decimal('0.5'))", "'1.414213562373095048801688724'")]
        [InlineData("str(Decimal(10) ** Decimal('0.5'))", "'3.162277660168379331998893544'")]
        [InlineData("str(2 ** Decimal('0.5'))", "'1.414213562373095048801688724'")]
        [InlineData("str(Decimal(2) ** Decimal(3))", "'8'")]
        [InlineData("str(Decimal(0) ** Decimal('0.5'))", "'0'")]
        [InlineData("Decimal(3).sqrt() == Decimal(3) ** Decimal('0.5')", "True")]
        [InlineData("abs(Decimal(7).ln().exp() - 7) < Decimal('1e-26')", "True")]
        public void LnLogExpAndPower(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // The precision the context asks for is the precision these answer at.
        [Fact]
        public void TranscendentalsHonourThePrecision()
        {
            string src = Prelude + "getcontext().prec = 10\nr = (str(Decimal(2).sqrt()), str(Decimal(2).ln()), str(Decimal(1).exp()))\n";
            Assert.Equal("('1.414213562', '0.6931471806', '2.718281828')", Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("Decimal(-1).sqrt()", "InvalidOperation")]
        [InlineData("Decimal(-1).ln()", "InvalidOperation")]
        [InlineData("Decimal(0).ln()", "InvalidOperation")]
        [InlineData("Decimal(0).log10()", "InvalidOperation")]
        [InlineData("Decimal(-2) ** Decimal('0.5')", "InvalidOperation")]
        [InlineData("Decimal(0) ** Decimal('-0.5')", "InvalidOperation")]
        [InlineData("Decimal(1000).exp()", "Overflow")]
        [InlineData("Decimal(-1000).exp()", "Underflow")]
        public void DomainErrors(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Prelude + "r = (" + expr + ")\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }
    }
}
