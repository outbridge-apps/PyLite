using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Builtins
{
    // impl/06 T-BLT-04/05/06: conversions, numeric builtins, object builtins (end-to-end via the harness).
    [Trait("Category", "Compatibility")]
    public class BuiltinsPhaseCTests
    {
        private static string R(string expr)
        {
            return Ev.ReprOf(expr);
        }

        private static string Str(string src, string name)
        {
            var r = Ev.Run(src);
            return r.Get(name).Str(r.Ctx);
        }

        // ---- int() / float() / str() / bool() (T-BLT-04) ----

        [Theory]
        [InlineData("int('1101', 2)", "13")]
        [InlineData("int('-0o17', 0)", "-15")]
        [InlineData("int(3.999)", "3")]
        [InlineData("int(-3.999)", "-3")]
        [InlineData("int('ff', 16)", "255")]
        [InlineData("int('0XFF', 16)", "255")]
        [InlineData("int('  42  ')", "42")]
        [InlineData("int('z', 36)", "35")]
        [InlineData("int('1_000')", "1000")]
        [InlineData("float('1_0')", "10.0")]
        [InlineData("int(True)", "1")]
        [InlineData("int()", "0")]
        [InlineData("float('  +1.5e3 ')", "1500.0")]
        [InlineData("float('1e400')", "inf")]
        [InlineData("float('-0.0')", "-0.0")]   // net472's double.Parse drops this sign (B13)
        [InlineData("float('-1e400')", "-inf")]
        [InlineData("float('.5')", "0.5")]
        [InlineData("float(10)", "10.0")]
        [InlineData("bool(0)", "False")]
        [InlineData("bool([1])", "True")]
        public void ConvertValues(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void StrIdentityAndFloat()
        {
            Assert.Equal("0.1", Str("r = str(0.1)\n", "r"));
            Assert.Equal("2.0", Str("r = str(2.0)\n", "r"));
            Assert.Equal("None", Str("r = str(None)\n", "r"));
        }

        [Theory]
        [InlineData("int('12', 1)", "int() base must be >= 2 and <= 36")]
        [InlineData("int(float('nan'))", "cannot convert float NaN to integer")]
        [InlineData("int(float('inf'))", "cannot convert float infinity to integer")]
        [InlineData("int([1])", "int() argument must be a string, a bytes-like object or a real number, not 'list'")]
        public void ConvertErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // ---- round / divmod / pow / abs / hex / oct / bin (T-BLT-05) ----

        [Theory]
        [InlineData("round(2.675, 2)", "2.67")]
        [InlineData("round(2.5)", "2")]
        [InlineData("round(3.5)", "4")]
        [InlineData("round(-2.5)", "-2")]
        [InlineData("round(25, -1)", "20")]
        [InlineData("round(35, -1)", "40")]
        [InlineData("divmod(-7, 2)", "(-4, 1)")]
        [InlineData("divmod(7, -2)", "(-4, -1)")]
        [InlineData("divmod(7.0, 2)", "(3.0, 1.0)")]
        [InlineData("pow(3, 4)", "81")]
        [InlineData("pow(3, 4, 5)", "1")]
        [InlineData("pow(2, -2)", "0.25")]
        [InlineData("pow(2, 3, -5)", "-2")]
        [InlineData("abs(-0.0)", "0.0")]
        [InlineData("abs(-5)", "5")]
        [InlineData("hex(-26)", "'-0x1a'")]
        [InlineData("oct(-8)", "'-0o10'")]
        [InlineData("bin(-5)", "'-0b101'")]
        [InlineData("hex(0)", "'0x0'")]
        [InlineData("bin(True)", "'0b1'")]
        public void NumericValues(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("round('a')", "type str doesn't define __round__ method")]
        [InlineData("divmod(1, 0)", "integer division or modulo by zero")]
        [InlineData("divmod(1.0, 0.0)", "float divmod()")]
        [InlineData("pow(2, -1, 4)", "base is not invertible for the given modulus")]
        [InlineData("pow(2, 3, 0)", "pow() 3rd argument cannot be 0")]
        [InlineData("hex(3.0)", "'float' object cannot be interpreted as an integer")]
        public void NumericErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // Past ModPowChunkWork the exponent is taken in chunks (r^(2^len) * b^chunk): the same value as one
        // ModPow call, here with 1 chunk boundary, many, and a zero chunk.
        [Theory]
        [InlineData(3, 5000, 12345, 4001, 7)]
        [InlineData(7, 400, 99, 20000, 1)]
        [InlineData(5, 9000, 0, 4100, 3)]
        public void ChunkedModPowMatchesModPow(int b, int eShift, int eAdd, int mShift, int mAdd)
        {
            System.Numerics.BigInteger e = (System.Numerics.BigInteger.One << eShift) + eAdd;
            System.Numerics.BigInteger m = (System.Numerics.BigInteger.One << mShift) + mAdd;
            string expected = System.Numerics.BigInteger.ModPow(b, e, m).ToString();
            string src = "r = pow(" + b + ", (1 << " + eShift + ") + " + eAdd + ", (1 << " + mShift + ") + " + mAdd + ")\n";
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void ModPowIsFast()
        {
            Assert.Equal("0", R("pow(123456789, 12345678901234567890, 1000000007) % 1000000007 - pow(123456789, 12345678901234567890, 1000000007) % 1000000007"));
        }

        // ---- chr / ord / hash / len / type / callable / iter / next (T-BLT-06) ----

        [Theory]
        [InlineData("chr(97)", "'a'")]
        [InlineData("ord('a')", "97")]
        [InlineData("ord(chr(119070))", "119070")]
        [InlineData("type(1) == type(2)", "True")]
        [InlineData("type(1) == type(1.0)", "False")]
        [InlineData("callable(len)", "True")]
        [InlineData("callable(3)", "False")]
        [InlineData("hash(1) == hash(1.0)", "True")]
        [InlineData("next(iter([1]))", "1")]
        [InlineData("next(iter([]), 9)", "9")]
        public void ObjectValues(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void TypeRepr()
        {
            Assert.Equal("<class 'int'>", Str("r = repr(type(1))\n", "r"));
        }

        [Theory]
        [InlineData("chr(0x110000)", "chr() arg not in range(0x110000)")]
        [InlineData("len(5)", "object of type 'int' has no len()")]
        [InlineData("next([1])", "'list' object is not an iterator")]
        [InlineData("iter(3)", "'int' object is not iterable")]
        [InlineData("hash([1])", "unhashable type: 'list'")]
        public void ObjectErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        [Fact]
        public void UnsupportedBuiltinsRefused()
        {
            Assert.Same(PyExceptionTypes.NameError, Ev.ErrType("x = eval('1')\n"));
        }
    }
}
