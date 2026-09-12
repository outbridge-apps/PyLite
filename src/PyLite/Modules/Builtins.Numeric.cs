using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    internal static partial class Builtins
    {
        static partial void RegisterNumeric(Dictionary<string, ScriptValue> d)
        {
            Add(d, "round", (self, args, kw, c) => RoundBuiltin(c, args, kw));
            Add(d, "divmod", (self, args, kw, c) => DivmodBuiltin(c, args));
            Add(d, "pow", (self, args, kw, c) => PowBuiltin(c, args));
            Add(d, "abs", (self, args, kw, c) => AbsBuiltin(c, args));
            Add(d, "hex", (self, args, kw, c) => BaseReprBuiltin(c, args, "0x", 16));
            Add(d, "oct", (self, args, kw, c) => BaseReprBuiltin(c, args, "0o", 8));
            Add(d, "bin", (self, args, kw, c) => BaseReprBuiltin(c, args, "0b", 2));
        }

        // ---- round ----

        private static ScriptValue RoundBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            ScriptValue number = args.Length >= 1 ? args[0] : null;
            if (args.Length > 2)
                throw Raise.TypeError(c, "round expected at most 2 arguments, got " + args.Length);
            ScriptValue ndigits = args.Length >= 2 ? args[1] : null;
            var r = new KwReader(c, kw, "round");
            ndigits = r.Get("ndigits", ndigits);
            number = r.Get("number", number);
            r.RejectInvalid();
            if (number == null)
                throw Raise.TypeError(c, "round() missing required argument: 'number'");

            if (number.Kind == ValueKind.Int || number.Kind == ValueKind.Bool)
            {
                BigInteger x = NumericOps.AsBigInteger(number);
                if (ndigits == null)
                    return number.Kind == ValueKind.Bool ? c.Values.Int(x) : number;
                int n = NDigits(c, ndigits);
                if (n >= 0)
                    return number.Kind == ValueKind.Bool ? c.Values.Int(x) : number;
                // Round to 10^p. p = -n as a long so -int.MinValue cannot overflow; once 10^p exceeds |x|'s
                // magnitude the nearest multiple is 0 (half-even), which also bounds Pow to |x|-sized work.
                long p = -(long)n;
                if (p > (long)NumericOps.BitLength(x) / 3 + 2)
                    return c.Values.Int(BigInteger.Zero);
                return c.Values.Int(RoundIntNeg(x, (int)p));
            }
            if (number.Kind == ValueKind.Float)
            {
                double x = ((FloatValue)number).Value;
                if (ndigits == null)
                    return c.Values.Int(IntFromFloat(c, FloatRepr.RoundToDigits(c, x, 0)));
                int n = NDigits(c, ndigits);
                return c.Values.Float(FloatRepr.RoundToDigits(c, x, n));
            }
            ScriptValue viaHook = number.RoundCore(ndigits, c);
            if (viaHook != null)
                return viaHook;
            throw Raise.TypeError(c, "type " + number.PyTypeName + " doesn't define __round__ method");
        }

        private static int NDigits(EvalContext c, ScriptValue v)
        {
            BigInteger b = Coerce.ToIndex(c, v, "round()");
            if (b > int.MaxValue)
                return int.MaxValue;
            if (b < int.MinValue)
                return int.MinValue;
            return (int)b;
        }

        // round(int, n<0): magnitude/10^p to nearest, half-even, times 10^p, sign restored.
        private static BigInteger RoundIntNeg(BigInteger x, int p)
        {
            BigInteger pow = BigInteger.Pow(10, p);
            BigInteger mag = BigInteger.Abs(x);
            BigInteger r = FloatRepr.DivmodNearHalfEven(mag, pow) * pow;
            return x.Sign < 0 ? -r : r;
        }

        // ---- divmod ----

        private static ScriptValue DivmodBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length != 2)
                throw Raise.TypeError(c, "divmod expected 2 arguments, got " + args.Length);
            ScriptValue a = args[0], b = args[1];
            bool aInt = a.Kind == ValueKind.Int || a.Kind == ValueKind.Bool;
            bool bInt = b.Kind == ValueKind.Int || b.Kind == ValueKind.Bool;
            if (aInt && bInt)
            {
                BigInteger x = NumericOps.AsBigInteger(a), y = NumericOps.AsBigInteger(b);
                if (y.IsZero)
                    throw Raise.ZeroDivision(c, "integer division or modulo by zero");
                BigInteger r;
                BigInteger q = BigInteger.DivRem(x, y, out r);
                if (!r.IsZero && (r.Sign != y.Sign))
                {
                    q -= 1;
                    r += y;
                }
                return c.Values.Tuple(new ScriptValue[] { c.Values.Int(q), c.Values.Int(r) });
            }
            if (NumericOps.IsNumber(a) && NumericOps.IsNumber(b))
            {
                double x = Coerce.ToDouble(c, a, "divmod"), y = Coerce.ToDouble(c, b, "divmod");
                if (y == 0.0)
                    throw Raise.ZeroDivision(c, "float divmod()");
                double mod = x % y;
                double div = (x - mod) / y;
                if (mod != 0.0)
                {
                    if ((y < 0.0) != (mod < 0.0))
                    {
                        mod += y;
                        div -= 1.0;
                    }
                }
                else
                {
                    mod = y < 0.0 ? -0.0 : 0.0;
                }
                double floordiv;
                if (div != 0.0)
                {
                    floordiv = Math.Floor(div);
                    if (div - floordiv > 0.5)
                        floordiv += 1.0;
                }
                else
                {
                    floordiv = BitConverter.DoubleToInt64Bits(x / y) < 0 ? -0.0 : 0.0;
                }
                return c.Values.Tuple(new ScriptValue[] { c.Values.Float(floordiv), c.Values.Float(mod) });
            }
            ScriptValue viaHook = a.DivmodCore(b, false, c);
            if (viaHook == null && !ReferenceEquals(a.TypeInfo, b.TypeInfo))
                viaHook = b.DivmodCore(a, true, c);
            if (viaHook != null)
                return viaHook;
            throw Raise.TypeError(c, "unsupported operand type(s) for divmod(): '" + a.PyTypeName + "' and '" + b.PyTypeName + "'");
        }

        // ---- pow ----

        private static ScriptValue PowBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length == 2)
                return PyOps.BinaryOp(PyBinOp.Pow, args[0], args[1], c);
            if (args.Length != 3)
                throw Raise.TypeError(c, "pow expected 2 or 3 arguments, got " + args.Length);
            ScriptValue xv = args[0], yv = args[1], zv = args[2];
            if (!IsIntLike(xv) || !IsIntLike(yv) || !IsIntLike(zv))
                throw Raise.TypeError(c, "pow() 3rd argument not allowed unless all arguments are integers");
            BigInteger x = NumericOps.AsBigInteger(xv), y = NumericOps.AsBigInteger(yv), z = NumericOps.AsBigInteger(zv);
            if (z.IsZero)
                throw Raise.ValueError(c, "pow() 3rd argument cannot be 0");
            BigInteger m = BigInteger.Abs(z);
            BigInteger baseMod = ((x % m) + m) % m;
            if (y < 0)
            {
                // 3.8+: a negative exponent means the modular inverse of the base.
                BigInteger inv;
                if (!TryModInverse(baseMod, m, out inv))
                    throw Raise.ValueError(c, "base is not invertible for the given modulus");
                baseMod = inv;
                y = -y;
            }
            BigInteger r = BigInteger.ModPow(baseMod, y, m);
            if (z.Sign < 0 && !r.IsZero)
                r -= m;
            return c.Values.Int(r);
        }

        private static bool IsIntLike(ScriptValue v)
        {
            return v.Kind == ValueKind.Int || v.Kind == ValueKind.Bool;
        }

        // Extended Euclid; false when gcd(a, m) != 1. Result is in [0, m).
        private static bool TryModInverse(BigInteger a, BigInteger m, out BigInteger inverse)
        {
            BigInteger r0 = m, r1 = a % m, t0 = BigInteger.Zero, t1 = BigInteger.One;
            while (!r1.IsZero)
            {
                BigInteger q = BigInteger.Divide(r0, r1);
                BigInteger r2 = r0 - q * r1;
                r0 = r1;
                r1 = r2;
                BigInteger t2 = t0 - q * t1;
                t0 = t1;
                t1 = t2;
            }
            if (!r0.IsOne)
            {
                inverse = BigInteger.Zero;
                return false;
            }
            inverse = ((t0 % m) + m) % m;
            return true;
        }

        // ---- abs ----

        internal static ScriptValue AbsBuiltin(EvalContext c, ScriptValue[] args)
        {
            Args.Exactly(c, args, "abs", 1);
            ScriptValue x = args[0];
            if (x.Kind == ValueKind.Int)
                return c.Values.Int(BigInteger.Abs(((IntValue)x).Value));
            if (x.Kind == ValueKind.Bool)
                return c.Values.Int(((BoolValue)x).Value ? 1 : 0);
            if (x.Kind == ValueKind.Float)
                return c.Values.Float(Math.Abs(((FloatValue)x).Value));
            ScriptValue viaHook = x.AbsCore(c);
            if (viaHook != null)
                return viaHook;
            throw Raise.TypeError(c, "bad operand type for abs(): '" + x.PyTypeName + "'");
        }

        // ---- hex / oct / bin ----

        private static ScriptValue BaseReprBuiltin(EvalContext c, ScriptValue[] args, string prefix, int radix)
        {
            if (args.Length != 1)
                throw Raise.TypeError(c, "expected 1 argument, got " + args.Length);
            ScriptValue x = args[0];
            if (!IsIntLike(x))
                throw Raise.TypeError(c, "'" + x.PyTypeName + "' object cannot be interpreted as an integer");
            BigInteger v = NumericOps.AsBigInteger(x);
            c.Values.PreCharge((long)NumericOps.BitLength(v) / 3 + 16);
            string body = MagnitudeInBase(BigInteger.Abs(v), radix);
            return c.Values.Str((v.Sign < 0 ? "-" : "") + prefix + body);
        }

        private static string MagnitudeInBase(BigInteger a, int radix)
        {
            if (a.IsZero)
                return "0";
            string hex = a.ToString("x", CultureInfo.InvariantCulture);
            hex = TrimLeadingZeros(hex);
            if (radix == 16)
                return hex;
            var bits = new StringBuilder(hex.Length * 4);
            for (int i = 0; i < hex.Length; i++)
            {
                int nib = HexDigit(hex[i]);
                for (int b = 3; b >= 0; b--)
                    bits.Append((char)('0' + ((nib >> b) & 1)));
            }
            string bin = TrimLeadingZeros(bits.ToString());
            if (radix == 2)
                return bin;
            int pad = (3 - bin.Length % 3) % 3;
            if (pad > 0)
                bin = new string('0', pad) + bin;
            var oct = new StringBuilder(bin.Length / 3);
            for (int i = 0; i < bin.Length; i += 3)
            {
                int val = (bin[i] - '0') * 4 + (bin[i + 1] - '0') * 2 + (bin[i + 2] - '0');
                oct.Append((char)('0' + val));
            }
            return TrimLeadingZeros(oct.ToString());
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            return c - 'a' + 10;   // BigInteger "x" gives lowercase
        }

        private static string TrimLeadingZeros(string s)
        {
            int i = 0;
            while (i < s.Length - 1 && s[i] == '0')
                i++;
            return s.Substring(i);
        }
    }
}
