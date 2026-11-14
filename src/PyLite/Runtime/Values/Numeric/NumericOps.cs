using System;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // Numeric-tower helpers (arithmetic is in NumericOps.Arith); this file holds the shared bit-length and
    // int->str charging primitives.
    internal static partial class NumericOps
    {
        internal static bool IsNumber(ScriptValue v)
        {
            ValueKind k = v.Kind;
            return k == ValueKind.Bool || k == ValueKind.Int || k == ValueKind.Float;
        }

        internal static BigInteger AsBigInteger(ScriptValue v)
        {
            BoolValue b = v as BoolValue;
            if (b != null)
                return b.Value ? BigInteger.One : BigInteger.Zero;
            return ((IntValue)v).Value;
        }

        private static readonly BigInteger LongMinBig = long.MinValue;
        private static readonly BigInteger LongMaxBig = long.MaxValue;

        // net472 has no GetBitLength, and ToByteArray copied the whole number for every big result (12.5 us
        // per add on 34K bits): the magnitude words are read in place, ToByteArray stays the fallback.
        private static readonly Func<BigInteger, uint[]> MagnitudeOf = MakeMagnitudeReader();

        // BigInteger keeps |v| outside the int range in a private uint[] _bits (little-endian, top word
        // non-zero). Taken only when that holds on this runtime: a probe value is read back before it is used.
        private static Func<BigInteger, uint[]> MakeMagnitudeReader()
        {
            try
            {
                System.Reflection.FieldInfo f = typeof(BigInteger).GetField("_bits",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (f == null || f.FieldType != typeof(uint[]))
                    return null;
                var dm = new System.Reflection.Emit.DynamicMethod("MagnitudeOf", typeof(uint[]), new[] { typeof(BigInteger) },
                    typeof(NumericOps).Module, true);
                System.Reflection.Emit.ILGenerator il = dm.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarga_S, (byte)0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, f);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                var read = (Func<BigInteger, uint[]>)dm.CreateDelegate(typeof(Func<BigInteger, uint[]>));
                uint[] probe = read(-(BigInteger.One << 100) - 5);   // |x| = 2**100 + 5: words 5, 0, 0, 16
                if (probe == null || probe.Length != 4 || probe[0] != 5 || probe[1] != 0 || probe[2] != 0 || probe[3] != 16)
                    return null;
                return read;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Exact bit length of |v|; BitLength(0) == 0.
        internal static long BitLength(BigInteger v)
        {
            if (v >= LongMinBig && v <= LongMaxBig)
                return BitLengthLong((long)v);   // a machine-size int needs no ToByteArray copy
            uint[] mag = MagnitudeOf != null ? MagnitudeOf(v) : null;   // |v| past the int range, top word non-zero
            if (mag != null && mag.Length > 0 && mag[mag.Length - 1] != 0)
                return 32L * (mag.Length - 1) + BitLengthLong(mag[mag.Length - 1]);
            byte[] bytes = BigInteger.Abs(v).ToByteArray();   // little-endian magnitude, top byte may be a 0 sign byte
            int top = bytes.Length - 1;
            if (bytes[top] == 0)
                top--;
            return 8L * top + BitLengthLong(bytes[top]);
        }

        // Exact bit length of |v| without BigInteger; BitLengthLong(0) == 0, long.MinValue -> 64.
        internal static int BitLengthLong(long v)
        {
            ulong u = v >= 0 ? (ulong)v : unchecked((ulong)(-v));   // MinValue wraps to 2^63, its own |v|
            int bits = 0;
            if ((u >> 32) != 0) { bits += 32; u >>= 32; }
            if ((u >> 16) != 0) { bits += 16; u >>= 16; }
            if ((u >> 8) != 0) { bits += 8; u >>= 8; }
            if ((u >> 4) != 0) { bits += 4; u >>= 4; }
            if ((u >> 2) != 0) { bits += 2; u >>= 2; }
            if ((u >> 1) != 0) { bits += 1; u >>= 1; }
            return bits + (int)u;
        }

        // CPython's Py_NAN, the NaN of float('nan'), math.nan and json: positive. .NET's double.NaN has the
        // sign bit set, which copysign, struct.pack and -x showed. NaNs from arithmetic stay the hardware's.
        internal static readonly double PyNaN = BitConverter.Int64BitsToDouble(0x7FF8000000000000L);

        // int -> float as CPython's PyLong_AsDouble: correctly rounded, OverflowError when it rounds past the
        // largest double. Every int-to-float conversion comes here; a bit-length test alone let 2**1024 - 1
        // (1024 bits, rounds to inf) through, so (2**1024 - 1) + 0.5 was inf.
        internal static double ToDoubleChecked(BigInteger v, EvalContext ctx)
        {
            double d = Modules.Support.NearestDouble.FromBigInteger(v);   // inf past the largest double, any size
            if (double.IsInfinity(d))
                throw Raise.Overflow(ctx, "int too large to convert to float");
            return d;
        }

        // pre-charge for the quadratic cost of BigInteger.ToString before printing an int.
        internal static void ChargeIntToStr(BigInteger v, EvalContext ctx)
        {
            BigOps.CheckDigitCap(ctx, v);   // the catchable ValueError before a charge that alone can pass MaxSteps (format, %d)
            ChargeIntToStrBits(BitLength(v), ctx);
        }

        // A BigInteger's decimal digits charged and deadline-bounded as str(int) is: every int-to-text path
        // other than str/repr/format (Fraction, range, json, messages) goes through here.
        internal static string ToDecimalString(BigInteger v, EvalContext ctx)
        {
            if (v >= long.MinValue && v <= long.MaxValue)
            {
                long l = (long)v;
                ChargeIntToStr(l, ctx);
                return l.ToString(CultureInfo.InvariantCulture);
            }
            ChargeIntToStr(v, ctx);
            return BigOps.ToDecimalString(ctx, v);
        }

        // Long twin with identical budget accounting.
        internal static void ChargeIntToStr(long v, EvalContext ctx)
        {
            ChargeIntToStrBits(BitLengthLong(v), ctx);
        }

        private static void ChargeIntToStrBits(long bits, EvalContext ctx)
        {
            long digits = (long)(bits / 3.321928094887362) + 1;
            long cost = digits + digits * digits / 256;
            ctx.Budget.Step((int)Math.Min(int.MaxValue, cost));
            ctx.Budget.PreCharge(2 * digits);   // a large one's Step crosses the deadline-check interval
        }
    }
}
