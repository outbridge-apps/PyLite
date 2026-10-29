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
            Add(d, "divmod", 2, 2, (self, args, kw, c) => DivmodBuiltin(c, args));
            Add(d, "pow", (self, args, kw, c) => PowBuiltin(c, StrMethods.WithKw(c, args, kw, "pow", "base", "exp", "mod")));
            Add(d, "abs", 1, 1, (self, args, kw, c) => AbsBuiltin(c, args));
            Add(d, "hex", 1, 1, (self, args, kw, c) => BaseReprBuiltin(c, args, "0x", 16));
            Add(d, "oct", 1, 1, (self, args, kw, c) => BaseReprBuiltin(c, args, "0o", 8));
            Add(d, "bin", 1, 1, (self, args, kw, c) => BaseReprBuiltin(c, args, "0b", 2));
        }

        // ---- round ----

        private static ScriptValue RoundBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            ScriptValue number = args.Length >= 1 ? args[0] : null;
            Args.Unpack(c, args, "round", 0, 2);
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
                return c.Values.Int(RoundIntNeg(c, x, (int)p));
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

        private static int NDigits(EvalContext c, ScriptValue v) { return Coerce.ToInt32(c, v, "round()"); }

        // round(int, n<0): magnitude/10^p to nearest, half-even, times 10^p, sign restored. The power, the
        // quotient and the product are |x|-sized, so each runs under BigOps.
        private static BigInteger RoundIntNeg(EvalContext c, BigInteger x, int p)
        {
            BigInteger pow = BigOps.Pow(c, 10, p, p * 3.3219280948873622);
            BigInteger rem;
            BigInteger q = BigOps.DivRem(c, BigInteger.Abs(x), pow, out rem);
            BigInteger twice = rem * 2;
            if (twice > pow || (twice == pow && !q.IsEven))
                q += 1;
            BigInteger r = BigOps.Multiply(c, q, pow);
            return x.Sign < 0 ? -r : r;
        }

        // ---- divmod ----

        private static ScriptValue DivmodBuiltin(EvalContext c, ScriptValue[] args)
        {
            ScriptValue a = args[0], b = args[1];
            bool aInt = a.Kind == ValueKind.Int || a.Kind == ValueKind.Bool;
            bool bInt = b.Kind == ValueKind.Int || b.Kind == ValueKind.Bool;
            if (aInt && bInt)
            {
                BigInteger x = NumericOps.AsBigInteger(a), y = NumericOps.AsBigInteger(b);
                if (y.IsZero)
                    throw Raise.ZeroDivision(c, "integer division or modulo by zero");
                BigInteger r;
                BigInteger q = NumericOps.FloorDiv(x, y, out r, c);
                return c.Values.Tuple(new ScriptValue[] { c.Values.Int(q), c.Values.Int(r) });
            }
            if (NumericOps.IsNumber(a) && NumericOps.IsNumber(b))
            {
                double x = Coerce.ToDouble(c, a, "divmod"), y = Coerce.ToDouble(c, b, "divmod");
                if (y == 0.0)
                    throw Raise.ZeroDivision(c, "float divmod()");
                double mod;
                double floordiv = NumericOps.FloatDivMod(x, y, out mod);
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
            if (args.Length == 2 || (args.Length == 3 && args[2].Kind == ValueKind.None))   // pow(x, y, None) == x ** y
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
            BigInteger baseMod;
            NumericOps.FloorDiv(x, m, out baseMod, c);   // in [0, m)
            if (y < 0)
            {
                // 3.8+: a negative exponent means the modular inverse of the base.
                BigInteger inv;
                if (!TryModInverse(c, baseMod, m, out inv))
                    throw Raise.ValueError(c, "base is not invertible for the given modulus");
                baseMod = inv;
                y = -y;
            }
            BigInteger r = ModPowBounded(c, baseMod, y, m);
            if (z.Sign < 0 && !r.IsZero)
                r -= m;
            return c.Values.Int(r);
        }

        private static bool IsIntLike(ScriptValue v)
        {
            return v.Kind == ValueKind.Int || v.Kind == ValueKind.Bool;
        }

        // ModPow is one native call of about bits(y) * words(m)^2 word products. Past ModPowChunkWork the
        // exponent is taken from the top in chunks that cost about that much each (r = r^(2^len) * b^chunk),
        // each charged and followed by a deadline check, so a huge exponent or modulus ends on the budget
        // instead of running past the deadline. y >= 0, 0 <= b < m.
        private const long ModPowChunkWork = 1L << 24;

        private static BigInteger ModPowBounded(EvalContext c, BigInteger b, BigInteger y, BigInteger m)
        {
            long words = 1 + NumericOps.BitLength(m) / 64;
            long perBit = words * words;
            long ybits = NumericOps.BitLength(y);
            if (ybits * perBit <= ModPowChunkWork)
                return BigOps.ModPow(c, b, y, m, ybits * perBit);
            int k = (int)Math.Max(1, Math.Min(4096, ModPowChunkWork / perBit));
            BigInteger r = BigInteger.One;
            for (long top = ybits; top > 0; top -= k)
            {
                int len = (int)Math.Min(k, top);
                c.Budget.Step((int)Math.Min(int.MaxValue, 1 + 2 * len * perBit / 64));
                c.Budget.CheckDeadlineNow();
                BigInteger chunk = (y >> (int)(top - len)) & ((BigInteger.One << len) - 1);
                r = BigOps.ModPow(c, r, BigInteger.One << len, m, len * perBit);   // past LongWork each call runs under BigOps
                if (!chunk.IsZero)
                    r = Mod(c, BigOps.Multiply(c, r, BigOps.ModPow(c, b, chunk, m, len * perBit)), m);
            }
            return r;
        }

        // x mod m for x >= 0, m > 0, deadline-bounded.
        private static BigInteger Mod(EvalContext c, BigInteger x, BigInteger m)
        {
            BigInteger r;
            BigOps.DivRem(c, x, m, out r);
            return r;
        }

        // Extended Euclid; false when gcd(a, m) != 1. Result is in [0, m). A step and a deadline check per
        // quotient (a million-bit modulus takes about a million of them); a long quotient or product runs
        // under BigOps.
        private static bool TryModInverse(EvalContext c, BigInteger a, BigInteger m, out BigInteger inverse)
        {
            BigInteger r0 = m, r1 = a % m, t0 = BigInteger.Zero, t1 = BigInteger.One;
            while (!r1.IsZero)
            {
                c.Budget.Step();
                c.Budget.CheckDeadlineNow();
                BigInteger q = BigOps.Divide(c, r0, r1);
                BigInteger r2 = r0 - BigOps.Multiply(c, q, r1);
                r0 = r1;
                r1 = r2;
                BigInteger t2 = t0 - BigOps.Multiply(c, q, t1);
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
            ScriptValue x = args[0];
            if (!IsIntLike(x))
                throw Raise.TypeError(c, "'" + x.PyTypeName + "' object cannot be interpreted as an integer");
            BigInteger v = NumericOps.AsBigInteger(x);
            c.Values.EnsureStrLen(NumericOps.BitLength(v) + 1);   // InBase works through a binary string that long
            string body = Support.PyFormat.InBase(BigInteger.Abs(v), radix);
            return c.Values.Str((v.Sign < 0 ? "-" : "") + prefix + body);
        }
    }
}
