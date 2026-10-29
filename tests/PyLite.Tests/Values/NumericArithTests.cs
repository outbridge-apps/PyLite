using System;
using System.Numerics;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class NumericArithTests
    {
        private static ScriptValue Bin(EvalContext ctx, PyBinOp op, ScriptValue a, ScriptValue b) => PyOps.BinaryOp(op, a, b, ctx);
        private static BigInteger I(ScriptValue v) => ((IntValue)v).Value;
        private static double D(ScriptValue v) => ((FloatValue)v).Value;
        private static bool Bl(ScriptValue v) => ((BoolValue)v).Value;

        [Theory]
        [InlineData(7, 2, 3, 1)]
        [InlineData(-7, 2, -4, 1)]
        [InlineData(7, -2, -4, -1)]
        [InlineData(-7, -2, 3, -1)]
        [InlineData(-7, 3, -3, 2)]
        [InlineData(7, -3, -3, -2)]
        public void IntFloorDivMod(int a, int b, int q, int r)
        {
            var ctx = Rt.Ctx();
            Assert.Equal((BigInteger)q, I(Bin(ctx, PyBinOp.FloorDiv, ctx.Values.Int(a), ctx.Values.Int(b))));
            Assert.Equal((BigInteger)r, I(Bin(ctx, PyBinOp.Mod, ctx.Values.Int(a), ctx.Values.Int(b))));
        }

        [Fact]
        public void FloatFloorDivModSigns()
        {
            var ctx = Rt.Ctx();
            Assert.Equal(-4.0, D(Bin(ctx, PyBinOp.FloorDiv, ctx.Values.Float(-7.0), ctx.Values.Float(2.0))));
            Assert.Equal(2.0, D(Bin(ctx, PyBinOp.Mod, ctx.Values.Float(-7.0), ctx.Values.Float(3.0))));
            Assert.Equal(-2.0, D(Bin(ctx, PyBinOp.Mod, ctx.Values.Float(7.0), ctx.Values.Float(-3.0))));
        }

        // //, % and divmod share CPython's _float_div_mod, so they agree with each other and with CPython:
        // floor(1 / 0.1) is 10.0 but 1 // 0.1 is 9.0; a zero remainder takes the divisor's sign (B13).
        [Theory]
        [InlineData(1.0, 0.1, 9.0, 0.09999999999999995)]
        [InlineData(-6.0, 3.0, -2.0, 0.0)]
        [InlineData(6.0, -3.0, -2.0, -0.0)]
        [InlineData(0.0, -3.0, -0.0, -0.0)]
        [InlineData(-1.0, double.PositiveInfinity, -1.0, double.PositiveInfinity)]
        public void FloatFloorDivModMatchCPython(double x, double y, double q, double r)
        {
            var ctx = Rt.Ctx();
            double gotQ = D(Bin(ctx, PyBinOp.FloorDiv, ctx.Values.Float(x), ctx.Values.Float(y)));
            double gotR = D(Bin(ctx, PyBinOp.Mod, ctx.Values.Float(x), ctx.Values.Float(y)));
            Assert.Equal(BitConverter.DoubleToInt64Bits(q), BitConverter.DoubleToInt64Bits(gotQ));
            Assert.Equal(BitConverter.DoubleToInt64Bits(r), BitConverter.DoubleToInt64Bits(gotR));
        }

        [Fact]
        public void DivModInvariant()
        {
            var ctx = Rt.Ctx();
            var rnd = new Random(20260707);
            for (int i = 0; i < 1000; i++)
            {
                BigInteger a = rnd.Next(-100000, 100000);
                BigInteger b = rnd.Next(-500, 500);
                if (b.IsZero)
                    b = 7;
                var q = I(Bin(ctx, PyBinOp.FloorDiv, ctx.Values.Int(a), ctx.Values.Int(b)));
                var r = I(Bin(ctx, PyBinOp.Mod, ctx.Values.Int(a), ctx.Values.Int(b)));
                Assert.Equal(a, q * b + r);
            }
        }

        [Fact] public void TrueDivMakesFloat() { var ctx = Rt.Ctx(); Assert.Equal(3.5, D(Bin(ctx, PyBinOp.TrueDiv, ctx.Values.Int(7), ctx.Values.Int(2)))); }

        [Fact]
        public void ZeroDivisionTexts()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("division by zero", ZdeText(ctx, PyBinOp.TrueDiv, ctx.Values.Int(1), ctx.Values.Int(0)));
            Assert.Equal("integer division or modulo by zero", ZdeText(ctx, PyBinOp.FloorDiv, ctx.Values.Int(1), ctx.Values.Int(0)));
            Assert.Equal("integer division or modulo by zero", ZdeText(ctx, PyBinOp.Mod, ctx.Values.Int(1), ctx.Values.Int(0)));
            Assert.Equal("float division by zero", ZdeText(ctx, PyBinOp.TrueDiv, ctx.Values.Float(1.0), ctx.Values.Float(0.0)));
            Assert.Equal("float modulo", ZdeText(ctx, PyBinOp.Mod, ctx.Values.Float(1.0), ctx.Values.Float(0.0)));
            Assert.Equal("float divmod()", ZdeText(ctx, PyBinOp.FloorDiv, ctx.Values.Float(1.0), ctx.Values.Float(0.0)));
        }

        private static string ZdeText(EvalContext ctx, PyBinOp op, ScriptValue a, ScriptValue b)
        {
            var ex = Assert.Throws<ScriptException>(() => Bin(ctx, op, a, b));
            Assert.Equal("ZeroDivisionError", ex.Value.ExcType.Name);
            return ex.Value.GetMessageText(ctx);
        }

        [Fact]
        public void IntTrueDivOverflow()
        {
            var ctx = Rt.Ctx();
            var big = ctx.Values.Int(BigInteger.Pow(10, 400));
            var ex = Assert.Throws<ScriptException>(() => Bin(ctx, PyBinOp.TrueDiv, big, ctx.Values.Int(1)));
            Assert.Equal("integer division result too large for a float", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void IntToFloatOverflow()
        {
            var ctx = Rt.Ctx();
            var big = ctx.Values.Int(BigInteger.Pow(10, 400));
            var ex = Assert.Throws<ScriptException>(() => Bin(ctx, PyBinOp.Mul, big, ctx.Values.Float(1.0)));
            Assert.Equal("int too large to convert to float", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void LargeExactTrueDiv()
        {
            var ctx = Rt.Ctx();
            var r = Bin(ctx, PyBinOp.TrueDiv, ctx.Values.Int(BigInteger.Pow(10, 25)), ctx.Values.Int(BigInteger.Pow(10, 3)));
            Assert.Equal(1e22, D(r));
        }

        [Fact] public void NegExponentGoesFloat() { var ctx = Rt.Ctx(); Assert.Equal(0.5, D(Bin(ctx, PyBinOp.Pow, ctx.Values.Int(2), ctx.Values.Int(-1)))); }

        [Fact]
        public void ZeroToNegativePower()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => Bin(ctx, PyBinOp.Pow, ctx.Values.Float(0.0), ctx.Values.Int(-2)));
            Assert.Equal("0.0 cannot be raised to a negative power", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void NegFractionalPower()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => Bin(ctx, PyBinOp.Pow, ctx.Values.Float(-2.0), ctx.Values.Float(0.5)));
            Assert.Equal("negative number cannot be raised to a fractional power", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void BoolTower()
        {
            var ctx = Rt.Ctx();
            Assert.Equal((BigInteger)2, I(Bin(ctx, PyBinOp.Add, ctx.Values.Bool(true), ctx.Values.Bool(true))));   // Int 2
            Assert.True(Bl(Bin(ctx, PyBinOp.BitAnd, ctx.Values.Bool(true), ctx.Values.Bool(true))));               // Bool True
            Assert.Equal((BigInteger)1, I(Bin(ctx, PyBinOp.BitAnd, ctx.Values.Bool(true), ctx.Values.Int(1))));    // 1 & 1 -> Int 1
            Assert.Equal((BigInteger)0, I(Bin(ctx, PyBinOp.BitAnd, ctx.Values.Bool(true), ctx.Values.Int(2))));    // 1 & 2 -> Int 0
            Assert.Equal((BigInteger)(-1), I(PyOps.UnaryOp(PyUnaryOp.Invert, ctx.Values.Bool(false), ctx)));       // ~False -> Int -1
        }

        [Fact]
        public void NegativeShiftCount()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => Bin(ctx, PyBinOp.LShift, ctx.Values.Int(1), ctx.Values.Int(-1)));
            Assert.Equal("negative shift count", ex.Value.GetMessageText(ctx));
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void HugeShiftAborts()
        {
            var ctx = Rt.Ctx();
            Assert.Throws<EngineAbort>(() => Bin(ctx, PyBinOp.LShift, ctx.Values.Int(1), ctx.Values.Int(BigInteger.Pow(10, 8))));
        }

        [Fact]
        public void HugePowerAborts()
        {
            var ctx = Rt.Ctx();
            Assert.Throws<EngineAbort>(() => Bin(ctx, PyBinOp.Pow, ctx.Values.Int(10), ctx.Values.Int(BigInteger.Pow(10, 6))));
        }

        [Fact] public void ArithmeticRightShift() { var ctx = Rt.Ctx(); Assert.Equal((BigInteger)(-4), I(Bin(ctx, PyBinOp.RShift, ctx.Values.Int(-7), ctx.Values.Int(1)))); }

        [Fact]
        public void FloatOverflowSilentVsRaised()
        {
            var ctx = Rt.Ctx();
            Assert.True(double.IsInfinity(D(Bin(ctx, PyBinOp.Mul, ctx.Values.Float(1e308), ctx.Values.Float(10.0)))));
            var ex = Assert.Throws<ScriptException>(() => Bin(ctx, PyBinOp.Pow, ctx.Values.Float(1e308), ctx.Values.Float(2.0)));
            Assert.Equal("(34, 'Numerical result out of range')", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void TrueDivExactWithinDoubleRange()
        {
            var ctx = Rt.Ctx();
            var rnd = new Random(99);
            for (int i = 0; i < 200; i++)
            {
                long a = rnd.Next(1, 1 << 30);
                long b = rnd.Next(1, 1 << 30);
                double expected = (double)a / (double)b;
                double got = D(Bin(ctx, PyBinOp.TrueDiv, ctx.Values.Int(a), ctx.Values.Int(b)));
                Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(got));
            }
        }
    }
}
