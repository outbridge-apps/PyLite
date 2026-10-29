using System;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // Numeric-tower arithmetic. bool ⊂ int ⊂ float; the only exception is that
    // &|^ over two bools yields bool. Int path is BigInteger-exact; float path is IEEE with silent
    // overflow to ±inf.
    internal static partial class NumericOps
    {
        internal static ScriptValue BinaryNumeric(PyBinOp op, ScriptValue a, ScriptValue b, EvalContext ctx)
        {
            // P1 fast path: both operands are long-backed ints (BoolValue never is) — stay in machine words.
            IntValue ia = a as IntValue, ib = b as IntValue;
            if (ia != null && ib != null && ia.IsSmall && ib.IsSmall)
            {
                ScriptValue fast = IntOpSmall(op, ia.Small, ib.Small, ctx);
                if (fast != null)
                    return fast;   // null => overflow/edge; fall through to the exact BigInteger path
            }
            if (op == PyBinOp.Mul && ia != null && ib != null)
            {
                // int*int with a big operand (or small overflow): the MaxIntBits estimate uses the
                // per-value cached bit lengths instead of recomputing BitLength on every multiply.
                return MulIntCached(ia, ib, ctx);
            }
            bool anyFloat = a.Kind == ValueKind.Float || b.Kind == ValueKind.Float;
            if (anyFloat)
            {
                if (IsBitwise(op))
                    return null; // float has no bitwise/shift -> TypeError via dispatch
                return FloatOp(op, ToDouble(a, ctx), ToDouble(b, ctx), ctx);
            }
            bool bothBool = a.Kind == ValueKind.Bool && b.Kind == ValueKind.Bool;
            return IntOp(op, AsBigInteger(a), AsBigInteger(b), bothBool, ctx);
        }

        // Long twin of IntOp. Returns null when the result may not fit (or an exotic MaxIntBits config
        // needs the estimate-based abort of the big path) — the caller then redoes the op exactly.
        // Error messages and budget charges are identical to the BigInteger path. Internal: the
        // evaluator's small-int shortcut (PyOps.TrySmallIntBinOp) enters here directly. Raise sites
        // and the MaxIntBits gates live HERE; the pure arithmetic is TryLongBinOp (shared with the
        // fused comprehension delegates).
        internal static ScriptValue IntOpSmall(PyBinOp op, long a, long b, EvalContext ctx)
        {
            switch (op)
            {
                case PyBinOp.TrueDiv:
                    if (b == 0)
                        throw Raise.ZeroDivision(ctx, "division by zero");
                    // IEEE division of exactly-representable operands is correctly rounded == long_true_divide.
                    const long Exact = 1L << 53;
                    if (a > -Exact && a < Exact && b > -Exact && b < Exact)
                        return ctx.Values.Float((double)a / b);
                    return null;
                case PyBinOp.FloorDiv:
                case PyBinOp.Mod:
                    if (b == 0)
                        throw Raise.ZeroDivision(ctx, "integer division or modulo by zero");
                    break;
                case PyBinOp.LShift:
                    if (b < 0)
                        throw Raise.ValueError(ctx, "negative shift count");
                    if (a == 0)
                        return ctx.Values.Int(0);
                    if (ctx.Limits.MaxIntBits < 128)
                        return null;   // preserve the estimate-based abort of the big path
                    break;
                case PyBinOp.RShift:
                    if (b < 0)
                        throw Raise.ValueError(ctx, "negative shift count");
                    break;
                case PyBinOp.Mul:
                case PyBinOp.Pow:
                    if (ctx.Limits.MaxIntBits < 128)
                        return null;   // big path aborts on the bit-length estimate; keep that behavior
                    break;
            }
            long r;
            return TryLongBinOp(op, a, b, out r) ? ctx.Values.Int(r) : null;
        }

        // The pure-arithmetic core of IntOpSmall: Python int semantics on machine longs, no boxing,
        // no ctx. False = "not decidable in long" (overflow, zero divisor, negative shift, TrueDiv)
        // — the caller redoes the op on the exact path, which raises/charges canonically. Callers
        // that bypass IntOpSmall (the fused comprehension delegates) must gate on MaxIntBits >= 128
        // themselves so the estimate-based aborts of the big path stay reachable.
        // a * b in a long, false on overflow without the cost of a thrown OverflowException (P1): operands
        // both within ±2^31 cannot overflow; otherwise the product is checked by dividing back.
        internal static bool TryMulLong(long a, long b, out long r)
        {
            r = unchecked(a * b);
            if ((ulong)(a + 0x80000000L) <= 0xFFFFFFFFUL && (ulong)(b + 0x80000000L) <= 0xFFFFFFFFUL)
                return true;
            if (a == 0)
                return true;
            if ((a == -1 && b == long.MinValue) || (b == -1 && a == long.MinValue) || r / a != b)
            {
                r = 0;
                return false;
            }
            return true;
        }

        internal static bool TryLongBinOp(PyBinOp op, long a, long b, out long r)
        {
            switch (op)
            {
                case PyBinOp.Add:
                    r = unchecked(a + b);
                    return ((a ^ r) & (b ^ r)) >= 0;
                case PyBinOp.Sub:
                    r = unchecked(a - b);
                    return ((a ^ b) & (a ^ r)) >= 0;
                case PyBinOp.Mul:
                    return TryMulLong(a, b, out r);
                case PyBinOp.FloorDiv:
                case PyBinOp.Mod:
                {
                    r = 0;
                    if (b == 0 || (a == long.MinValue && b == -1))
                        return false;   // zero divisor raises upstream; MinValue/-1 is the only overflowing division
                    long q = a / b, m = a % b;
                    if (m != 0 && ((m < 0) != (b < 0)))
                    {
                        q--;
                        m += b;
                    }
                    r = op == PyBinOp.FloorDiv ? q : m;
                    return true;
                }
                case PyBinOp.LShift:
                    r = 0;
                    if (b < 0)
                        return false;   // negative count raises upstream
                    if (a == 0)
                        return true;
                    if (a == long.MinValue || b > 62 || BitLengthLong(a) + b > 63)
                        return false;
                    r = a << (int)b;
                    return true;
                case PyBinOp.RShift:
                    if (b < 0)
                    {
                        r = 0;
                        return false;   // negative count raises upstream
                    }
                    // fully shifted out => sign; arithmetic shift == floor division by 2^n
                    r = b >= 64 ? (a < 0 ? -1L : 0L) : a >> (int)b;
                    return true;
                case PyBinOp.BitAnd:
                    r = a & b;
                    return true;
                case PyBinOp.BitOr:
                    r = a | b;
                    return true;
                case PyBinOp.BitXor:
                    r = a ^ b;
                    return true;
                case PyBinOp.Pow:
                    // Small positive exponent whose result provably fits: |a|^n <= 2^(bitlen(a)*n) <= 2^62.
                    // 0/±1 bases and everything else keep the exact path (its special cases are cheap).
                    r = 0;
                    if (b < 1 || b > 62 || a == 0 || a == 1 || a == -1 || BitLengthLong(a) * b > 62)
                        return false;
                    {
                        long acc = 1;
                        for (long k = 0; k < b; k++)
                            acc *= a;
                        r = acc;
                        return true;
                    }
                default:
                    r = 0;
                    return false;   // TrueDiv (float result) / anything exotic
            }
        }

        internal static ScriptValue UnaryNumeric(PyUnaryOp op, ScriptValue a, EvalContext ctx)
        {
            // P1 fast path for long-backed ints.
            IntValue iv = a as IntValue;
            if (iv != null && iv.IsSmall)
            {
                switch (op)
                {
                    case PyUnaryOp.Neg:
                        if (iv.Small != long.MinValue)
                            return ctx.Values.Int(-iv.Small);
                        break;   // -MinValue overflows long; exact path below
                    case PyUnaryOp.Pos:
                        return a;
                    case PyUnaryOp.Invert:
                        return ctx.Values.Int(~iv.Small);   // two's complement ~a == -a-1, never overflows
                }
            }
            bool isFloat = a.Kind == ValueKind.Float;
            switch (op)
            {
                case PyUnaryOp.Neg:
                    return isFloat ? (ScriptValue)ctx.Values.Float(-((FloatValue)a).Value) : ctx.Values.Int(-AsBigInteger(a));
                case PyUnaryOp.Pos:
                    if (isFloat)
                        return a;
                    return a.Kind == ValueKind.Bool ? ctx.Values.Int(AsBigInteger(a)) : a;
                case PyUnaryOp.Invert:
                    return isFloat ? null : ctx.Values.Int(-AsBigInteger(a) - 1);
                default:
                    return null;
            }
        }

        private static bool IsBitwise(PyBinOp op)
        {
            return op == PyBinOp.LShift || op == PyBinOp.RShift
                || op == PyBinOp.BitAnd || op == PyBinOp.BitOr || op == PyBinOp.BitXor;
        }

        private static double ToDouble(ScriptValue v, EvalContext ctx)
        {
            FloatValue f = v as FloatValue;
            if (f != null)
                return f.Value;
            // Fast path: a long-backed int converts to double directly — a long always fits (2^63 <<
            // the 2^1024 overflow bound), skipping AsBigInteger + BitLength's BigInteger.Log.
            IntValue iv = v as IntValue;
            if (iv != null && iv.IsSmall)
                return iv.Small;
            return ToDoubleChecked(AsBigInteger(v), ctx);
        }

        // ---------------- int path ----------------

        private static ScriptValue IntOp(PyBinOp op, BigInteger a, BigInteger b, bool bothBool, EvalContext ctx)
        {
            switch (op)
            {
                case PyBinOp.Add: return ctx.Values.Int(a + b);
                case PyBinOp.Sub: return ctx.Values.Int(a - b);
                case PyBinOp.Mul: return MulInt(a, b, ctx);
                case PyBinOp.TrueDiv:
                    if (b.IsZero)
                        throw Raise.ZeroDivision(ctx, "division by zero");
                    return TrueDivide(a, b, ctx);
                case PyBinOp.FloorDiv:
                    if (b.IsZero)
                        throw Raise.ZeroDivision(ctx, "integer division or modulo by zero");
                    { BigInteger r; return ctx.Values.Int(FloorDiv(a, b, out r, ctx)); }
                case PyBinOp.Mod:
                    if (b.IsZero)
                        throw Raise.ZeroDivision(ctx, "integer division or modulo by zero");
                    { BigInteger r; FloorDiv(a, b, out r, ctx); return ctx.Values.Int(r); }
                case PyBinOp.Pow: return PowIntOrFloat(a, b, ctx);
                case PyBinOp.LShift: return LShiftInt(a, b, ctx);
                case PyBinOp.RShift: return RShiftInt(a, b, ctx);
                case PyBinOp.BitAnd: return BitResult(a & b, bothBool, ctx);
                case PyBinOp.BitOr: return BitResult(a | b, bothBool, ctx);
                default: return BitResult(a ^ b, bothBool, ctx);   // BitXor
            }
        }

        private static ScriptValue BitResult(BigInteger r, bool bothBool, EvalContext ctx)
            => bothBool ? (ScriptValue)ctx.Values.Bool(!r.IsZero) : ctx.Values.Int(r);

        // MulInt twin over IntValues — identical gates/charges, cached bit lengths.
        private static ScriptValue MulIntCached(IntValue a, IntValue b, EvalContext ctx)
        {
            long bitsA = a.BitLength(), bitsB = b.BitLength();
            if (bitsA + bitsB - 1 > ctx.Limits.MaxIntBits)   // a product has bitsA+bitsB-1 or bitsA+bitsB bits
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxIntBits", ctx.Limits.MaxIntBits, bitsA + bitsB);
            if ((bitsA / 64 + 1) * (bitsB / 64 + 1) > BigOps.LongWork)
                return ctx.Values.Int(BigOps.MultiplyLong(ctx, a.Value, b.Value));
            bool big = bitsA > 4096 || bitsB > 4096;
            if (big)
                ctx.Budget.CheckDeadlineNow();
            BigInteger r = a.Value * b.Value;
            if (big)
                ctx.Budget.CheckDeadlineNow();
            return ctx.Values.Int(r);
        }

        private static ScriptValue MulInt(BigInteger a, BigInteger b, EvalContext ctx)
        {
            return ctx.Values.Int(Multiply(a, b, ctx));
        }

        // a * b gated on MaxIntBits before the product is built, with deadline checks around a big one (a long
        // one runs under BigOps): the int product, and every product an engine type builds from ints (Fraction).
        internal static BigInteger Multiply(BigInteger a, BigInteger b, EvalContext ctx)
        {
            long bitsA = BitLength(a), bitsB = BitLength(b);
            if (bitsA + bitsB - 1 > ctx.Limits.MaxIntBits)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxIntBits", ctx.Limits.MaxIntBits, bitsA + bitsB);
            if ((bitsA / 64 + 1) * (bitsB / 64 + 1) > BigOps.LongWork)
                return BigOps.MultiplyLong(ctx, a, b);
            bool big = bitsA > 4096 || bitsB > 4096;
            if (big)
                ctx.Budget.CheckDeadlineNow();
            BigInteger r = a * b;
            if (big)
                ctx.Budget.CheckDeadlineNow();
            return r;
        }

        // Floor division with remainder (single point for //, %, divmod).
        internal static BigInteger FloorDiv(BigInteger a, BigInteger b, out BigInteger r)
        {
            return FloorDiv(a, b, out r, null);
        }

        // ctx set: a long division runs under BigOps (deadline-bounded).
        internal static BigInteger FloorDiv(BigInteger a, BigInteger b, out BigInteger r, EvalContext ctx)
        {
            BigInteger q = ctx == null ? BigInteger.DivRem(a, b, out r) : BigOps.DivRem(ctx, a, b, out r);
            if (!r.IsZero && r.Sign != b.Sign)
            {
                q -= 1;
                r += b;
            }
            return q;
        }

        private static ScriptValue PowIntOrFloat(BigInteger a, BigInteger n, EvalContext ctx)
        {
            if (n.Sign < 0)
                return ctx.Values.Float(PowFloat(ToDoubleChecked(a, ctx), ToDoubleChecked(n, ctx), ctx));
            return PowInt(a, n, ctx);
        }

        private static ScriptValue PowInt(BigInteger a, BigInteger n, EvalContext ctx)
        {
            BigInteger abs = BigInteger.Abs(a);
            if (abs <= BigInteger.One)
            {
                if (a.IsZero)
                    return ctx.Values.Int(n.IsZero ? 1 : 0);
                if (a == BigInteger.One)
                    return ctx.Values.Int(1);
                return ctx.Values.Int(n.IsEven ? 1 : -1); // (-1)^n
            }
            // The result has floor(n * log2|a|) + 1 bits: refuse what surely passes MaxIntBits; the
            // borderline is computed and checked exactly by Values.Int.
            double estBits = (double)n * BigInteger.Log(abs, 2);
            if (estBits > ctx.Limits.MaxIntBits + 1.0)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxIntBits", ctx.Limits.MaxIntBits,
                    estBits >= long.MaxValue ? long.MaxValue : (long)estBits);
            ctx.Budget.CheckDeadlineNow();
            BigInteger r = BigOps.Pow(ctx, a, (int)n, estBits);
            ctx.Budget.CheckDeadlineNow();
            return ctx.Values.Int(r);
        }

        private static ScriptValue LShiftInt(BigInteger a, BigInteger n, EvalContext ctx)
        {
            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "negative shift count");
            if (a.IsZero)
                return ctx.Values.Int(0);
            BigInteger total = BitLength(a) + n;
            if (total > ctx.Limits.MaxIntBits)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxIntBits", ctx.Limits.MaxIntBits,
                    total > long.MaxValue ? long.MaxValue : (long)total);
            return ctx.Values.Int(a << (int)n);
        }

        private static ScriptValue RShiftInt(BigInteger a, BigInteger n, EvalContext ctx)
        {
            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "negative shift count");
            if (n > BitLength(a))
                return ctx.Values.Int(a.Sign < 0 ? -1 : 0);   // fully shifted out
            return ctx.Values.Int(a >> (int)n);
        }

        // Port of Objects/longobject.c::long_true_divide.
        private static ScriptValue TrueDivide(BigInteger a, BigInteger b, EvalContext ctx)
        {
            if (a.IsZero)
                return ctx.Values.Float(b.Sign < 0 ? -0.0 : 0.0);
            bool negate = (a.Sign < 0) ^ (b.Sign < 0);
            BigInteger A = BigInteger.Abs(a), B = BigInteger.Abs(b);
            long diff = BitLength(A) - BitLength(B);
            if (diff > 1075)
                throw Raise.Overflow(ctx, "integer division result too large for a float");
            if (diff < -1075)
                return ctx.Values.Float(negate ? -0.0 : 0.0);

            int shift = 55 - (int)diff;
            BigInteger q, r;
            if (shift >= 0)
                q = BigInteger.DivRem(A << shift, B, out r);
            else
                q = BigInteger.DivRem(A, B << (-shift), out r);
            if (!r.IsZero)
                q |= BigInteger.One;   // sticky bit

            long qbits = BitLength(q);
            int extra = (int)(qbits - 53);
            long e = -shift;
            ulong m;
            if (extra > 0)
            {
                BigInteger lowMask = (BigInteger.One << extra) - 1;
                ulong low = (ulong)(q & lowMask);
                m = (ulong)(q >> extra);
                e += extra;
                ulong half = 1UL << (extra - 1);
                if (low > half || (low == half && (m & 1) == 1))
                {
                    m++;
                    if (m == (1UL << 53))
                    {
                        m >>= 1;
                        e++;
                    }
                }
            }
            else
                m = (ulong)q;

            long expField = e + 52 + 1023;
            if (expField >= 2047)
                throw Raise.Overflow(ctx, "integer division result too large for a float");
            if (expField <= 0)
            {
                int rs = 1 - (int)expField;
                if (rs >= 64)
                    m = 0;
                else
                {
                    ulong lost = m & ((1UL << rs) - 1);
                    ulong shifted = m >> rs;
                    ulong halfBit = 1UL << (rs - 1);
                    if (lost > halfBit || (lost == halfBit && (shifted & 1) == 1))
                        shifted++;
                    m = shifted;
                }

                expField = 0;
            }

            long bits = ((long)expField << 52) | (long)(m & 0xFFFFFFFFFFFFFUL);
            double val = BitConverter.Int64BitsToDouble(bits);
            return ctx.Values.Float(negate ? -val : val);
        }

        // ---------------- float path ----------------

        private static ScriptValue FloatOp(PyBinOp op, double x, double y, EvalContext ctx)
        {
            switch (op)
            {
                case PyBinOp.Add: return ctx.Values.Float(x + y);
                case PyBinOp.Sub: return ctx.Values.Float(x - y);
                case PyBinOp.Mul: return ctx.Values.Float(x * y);
                case PyBinOp.TrueDiv:
                    if (y == 0.0)
                        throw Raise.ZeroDivision(ctx, "float division by zero");
                    return ctx.Values.Float(x / y);
                case PyBinOp.FloorDiv:
                    if (y == 0.0)
                        throw Raise.ZeroDivision(ctx, "float divmod()");
                    double ignored;
                    return ctx.Values.Float(FloatDivMod(x, y, out ignored));
                case PyBinOp.Mod:
                    if (y == 0.0)
                        throw Raise.ZeroDivision(ctx, "float modulo");
                    double mod;
                    FloatDivMod(x, y, out mod);
                    return ctx.Values.Float(mod);
                case PyBinOp.Pow:
                    return ctx.Values.Float(PowFloat(x, y, ctx));
                default:
                    return null;   // bitwise/shift not valid for float
            }
        }

        // Port of floatobject.c::_float_div_mod, shared by //, % and divmod so the three agree with each other
        // and with CPython (1 // 0.1 == 9.0, not floor(1 / 0.1) == 10.0; -6.0 % 3 == 0.0, not -0.0). y != 0.
        internal static double FloatDivMod(double x, double y, out double mod)
        {
            mod = x % y;   // C# % on doubles is C's fmod
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
            if (div == 0.0)
                return BitConverter.DoubleToInt64Bits(x / y) < 0 ? -0.0 : 0.0;
            double floordiv = Math.Floor(div);
            if (div - floordiv > 0.5)
                floordiv += 1.0;
            return floordiv;
        }

        // Port of Objects/floatobject.c::float_pow, with the complex result replaced by ValueError.
        private static double PowFloat(double x, double y, EvalContext ctx)
        {
            if (y == 0.0)
                return 1.0;                 // even for x == nan
            if (x == 1.0)
                return 1.0;                 // even for y == nan
            if (double.IsNaN(x) || double.IsNaN(y))
                return double.NaN;
            if (x == 0.0 && y < 0)
                throw Raise.ZeroDivision(ctx, "0.0 cannot be raised to a negative power");
            if (x < 0 && !IsIntegral(y))
                throw Raise.ValueError(ctx, "negative number cannot be raised to a fractional power");
            double r = Math.Pow(x, y);
            if (double.IsInfinity(r) && !double.IsInfinity(x) && !double.IsInfinity(y))
                throw Raise.Overflow(ctx, "(34, 'Numerical result out of range')");
            return r;
        }

        private static bool IsIntegral(double y) { return !double.IsInfinity(y) && y == Math.Floor(y); }
    }
}
