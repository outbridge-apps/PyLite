using System;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    internal sealed partial class DecimalValue
    {
        // A computed (coefficient, exponent), rounded to the context precision with the context mode.
        internal static DecimalValue Fix(EvalContext ctx, BigInteger coeff, int exp, bool neg)
        {
            int prec = ctx.DecimalCtx.Prec;
            int digits = coeff.IsZero ? 1 : DecimalBits.SignificantDigits(coeff);
            if (digits > prec)
            {
                int drop = digits - prec;
                BigInteger divisor = BigInteger.Pow(10, drop);
                BigInteger r;
                BigInteger q = BigInteger.DivRem(coeff, divisor, out r);
                q = DecimalRound.Apply(q, r, divisor, neg, ctx.DecimalCtx.Mode);
                if (DecimalBits.SignificantDigits(q) > prec)   // rounding up grew it: 999 -> 1000
                {
                    q /= 10;
                    drop++;
                }
                coeff = q;
                exp += drop;
            }
            return Make(ctx, coeff, exp, neg);
        }
    }

    // sqrt / ln / log10 / exp and the non-integer power, in integer arithmetic at the context precision
    // plus guard digits. sqrt is exact (an integer square root); the other three are series with argument
    // reduction, and a result that lands on a rounding boundary is recomputed with more working digits —
    // the same device CPython uses to get correct rounding out of an approximate core.
    internal static class DecimalMath
    {
        private const int Guard = 12;      // working digits above the context precision
        private const int MaxGuard = 42;   // past this a boundary is taken at face value

        private struct Approx
        {
            public BigInteger Coeff;
            public int Exp;
            public bool Neg;
        }

        // ---- sqrt ----

        // Lib/_pydecimal.py's algorithm: scale the coefficient so its integer square root already carries
        // prec+1 digits, take it by Newton, and let the exact case fall back onto the ideal exponent.
        internal static ScriptValue Sqrt(EvalContext ctx, DecimalValue x)
        {
            BigInteger c;
            int e;
            bool neg;
            x.Coeff(out c, out e, out neg);
            if (c.IsZero)
                return DecimalValue.Make(ctx, BigInteger.Zero, e >> 1, neg);
            if (neg)
                throw InvalidOp(ctx);

            int prec = ctx.DecimalCtx.Prec + 1;
            ctx.Values.PreCharge(16L * prec + 64);
            int digits = DecimalBits.SignificantDigits(c);
            int exp = e >> 1;
            int l;
            if ((e & 1) != 0)
            {
                c *= 10;
                l = (digits >> 1) + 1;
            }
            else
            {
                l = (digits + 1) >> 1;
            }
            int shift = prec - l;
            bool exact;
            if (shift >= 0)
            {
                c *= BigInteger.Pow(100, shift);
                exact = true;
            }
            else
            {
                BigInteger rem;
                c = BigInteger.DivRem(c, BigInteger.Pow(100, -shift), out rem);
                exact = rem.IsZero;
            }
            exp -= shift;

            BigInteger n = BigInteger.Pow(10, prec);
            while (true)
            {
                ctx.Budget.Step();
                BigInteger q = c / n;
                if (n <= q)
                    break;
                n = (n + q) >> 1;
            }
            exact = exact && n * n == c;
            if (exact)
            {
                if (shift >= 0)
                    n /= BigInteger.Pow(10, shift);
                else
                    n *= BigInteger.Pow(10, -shift);
                exp += shift;
            }
            else if ((n % 5).IsZero)
            {
                n += BigInteger.One;   // off the tie, so the rounding below cannot land on it by accident
            }
            return DecimalValue.Fix(ctx, n, exp, false);
        }

        // ---- ln / log10 / exp / power ----

        internal static ScriptValue Ln(EvalContext ctx, DecimalValue x)
        {
            BigInteger c;
            int e;
            bool neg;
            x.Coeff(out c, out e, out neg);
            if (c.IsZero)
                throw NoInfinity(ctx, "ln(0)");
            if (neg)
                throw InvalidOp(ctx);
            if (c.IsOne && e == 0)
                return DecimalValue.Make(ctx, BigInteger.Zero, 0, false);
            return Settle(ctx, w => Fixed(LnFixed(ctx, c, e, w), w));
        }

        internal static ScriptValue Log10(EvalContext ctx, DecimalValue x)
        {
            BigInteger c;
            int e;
            bool neg;
            x.Coeff(out c, out e, out neg);
            if (c.IsZero)
                throw NoInfinity(ctx, "log10(0)");
            if (neg)
                throw InvalidOp(ctx);
            // An exact power of ten answers its exponent exactly, as the spec requires.
            BigInteger t = c;
            int te = e;
            while (t % 10 == 0 && !t.IsZero)
            {
                t /= 10;
                te++;
            }
            if (t.IsOne)
                return DecimalValue.Make(ctx, new BigInteger(Math.Abs((long)te)), 0, te < 0);
            return Settle(ctx, w =>
            {
                BigInteger S = BigInteger.Pow(10, w);
                return Fixed(DivNearest(LnFixed(ctx, c, e, w) * S, Ln10(ctx, w)), w);
            });
        }

        internal static ScriptValue Exp(EvalContext ctx, DecimalValue x)
        {
            BigInteger c;
            int e;
            bool neg;
            x.Coeff(out c, out e, out neg);
            if (c.IsZero)
                return DecimalValue.Make(ctx, BigInteger.One, 0, false);
            return Settle(ctx, w => ExpApprox(ctx, Point(c, e, neg, w), w));
        }

        // x ** y for a y that is not a whole number: exp(y * ln x), with CPython's domain rules.
        internal static ScriptValue Power(EvalContext ctx, DecimalValue x, DecimalValue y)
        {
            BigInteger cx;
            int ex;
            bool negx;
            x.Coeff(out cx, out ex, out negx);
            BigInteger cy;
            int ey;
            bool negy;
            y.Coeff(out cy, out ey, out negy);
            if (cy.IsZero)
                return DecimalValue.Make(ctx, BigInteger.One, 0, false);
            if (cx.IsZero)
            {
                if (negy)
                    throw InvalidOp(ctx);   // 0 ** a negative power has no finite value
                return DecimalValue.Make(ctx, BigInteger.Zero, 0, false);
            }
            if (negx)
                throw InvalidOp(ctx);       // a negative base with a fractional exponent is not real
            if (cx.IsOne && ex == 0)
                return DecimalValue.Make(ctx, BigInteger.One, 0, false);
            return Settle(ctx, w =>
            {
                BigInteger S = BigInteger.Pow(10, w);
                BigInteger lnx = LnFixed(ctx, cx, ex, w);
                BigInteger yf = Point(cy, ey, negy, w);
                return ExpApprox(ctx, DivNearest(yf * lnx, S), w);
            });
        }

        // ---- the fixed-point core: a BigInteger u at w digits stands for u / 10^w ----

        // exp(u/10^w) as a coefficient and an exponent. Splitting the answer as 10^n * 10^g with g in
        // [0, 1) keeps the series argument inside [0, ln 10] whatever the magnitude of the result.
        private static Approx ExpApprox(EvalContext ctx, BigInteger u, int w)
        {
            BigInteger S = BigInteger.Pow(10, w);
            BigInteger ln10 = Ln10(ctx, w);
            BigInteger y = DivNearest(u * S, ln10);
            BigInteger n = FloorDiv(y, S);
            if (n > 200 || n < -200)
                throw n > 0 ? Overflow(ctx) : DecimalBits.Underflow(ctx);
            BigInteger g = y - n * S;
            BigInteger v = ExpSeries(ctx, DivNearest(g * ln10, S), S);
            return new Approx { Coeff = v, Exp = (int)n - w, Neg = false };
        }

        // exp(r/S) for 0 <= r/S <= ln 10, by the Taylor series.
        private static BigInteger ExpSeries(EvalContext ctx, BigInteger r, BigInteger S)
        {
            BigInteger t = S;
            BigInteger acc = S;
            int i = 1;
            while (!t.IsZero)
            {
                ctx.Budget.Step();
                t = DivNearest(t * r, S * i);
                acc += t;
                i++;
            }
            return acc;
        }

        // ln(c * 10^e) at w digits. The value is split as m * 10^k with m near 1, so the series argument
        // (m-1)/(m+1) stays under 0.53 and the k * ln 10 part is exact bookkeeping.
        private static BigInteger LnFixed(EvalContext ctx, BigInteger c, int e, int w)
        {
            ctx.Values.PreCharge(16L * w + 64);
            BigInteger S = BigInteger.Pow(10, w);
            int d = DecimalBits.SignificantDigits(c);
            int adjusted = e + d - 1;
            BigInteger mu = Rescale(c, w - (d - 1));
            if (mu * mu >= 10 * S * S)   // above sqrt(10): shift a power of ten into the exponent
            {
                mu = DivNearest(mu, 10);
                adjusted += 1;
            }
            BigInteger lnm = 2 * AtanhSeries(ctx, DivNearest((mu - S) * S, mu + S), S);
            return adjusted == 0 ? lnm : lnm + adjusted * Ln10(ctx, w);
        }

        // ln 10 = 6 atanh(1/3) + 2 atanh(1/9): both series converge fast and need no stored constant.
        private static BigInteger Ln10(EvalContext ctx, int w)
        {
            return 6 * AtanhInv(ctx, 3, w) + 2 * AtanhInv(ctx, 9, w);
        }

        private static BigInteger AtanhInv(EvalContext ctx, int k, int w)
        {
            BigInteger kk = (BigInteger)k * k;
            BigInteger t = BigInteger.Pow(10, w) / k;
            BigInteger acc = t;
            int i = 1;
            while (!t.IsZero)
            {
                ctx.Budget.Step();
                t /= kk;
                if (t.IsZero)
                    break;
                acc += t / (2 * i + 1);
                i++;
            }
            return acc;
        }

        private static BigInteger AtanhSeries(EvalContext ctx, BigInteger z, BigInteger S)
        {
            BigInteger zz = DivNearest(z * z, S);
            BigInteger t = z;
            BigInteger acc = z;
            int i = 1;
            while (!t.IsZero)
            {
                ctx.Budget.Step();
                t = DivNearest(t * zz, S);
                if (t.IsZero)
                    break;
                acc += t / (2 * i + 1);
                i++;
            }
            return acc;
        }

        // ---- plumbing ----

        // The value at w digits, rounded to the context precision. A prec+1-digit result sitting exactly
        // on a rounding boundary cannot be resolved from an approximation, so it is recomputed wider;
        // one that is merely near the boundary is nudged off it, which cannot change a correct answer.
        private static ScriptValue Settle(EvalContext ctx, Func<int, Approx> compute)
        {
            int prec = ctx.DecimalCtx.Prec;
            for (int extra = Guard; ; extra += 10)
            {
                Approx a = compute(prec + extra);
                if (a.Coeff.IsZero)
                    return DecimalValue.Make(ctx, BigInteger.Zero, 0, false);
                int digits = DecimalBits.SignificantDigits(a.Coeff);
                int keep = prec + 1;
                if (digits > keep)
                {
                    int drop = digits - keep;
                    BigInteger divisor = BigInteger.Pow(10, drop);
                    BigInteger r;
                    BigInteger q = BigInteger.DivRem(a.Coeff, divisor, out r);
                    bool onBoundary = (q % 5).IsZero;
                    if (onBoundary && r.IsZero && extra < MaxGuard)
                        continue;
                    if (onBoundary && !r.IsZero)
                        q += BigInteger.One;
                    a.Coeff = q;
                    a.Exp += drop;
                }
                return DecimalValue.Fix(ctx, a.Coeff, a.Exp, a.Neg);
            }
        }

        private static Approx Fixed(BigInteger u, int w)
        {
            bool neg = u.Sign < 0;
            return new Approx { Coeff = neg ? -u : u, Exp = -w, Neg = neg };
        }

        // The signed value c * 10^e as a fixed-point number at w digits.
        private static BigInteger Point(BigInteger c, int e, bool neg, int w)
        {
            BigInteger u = Rescale(c, e + w);
            return neg ? -u : u;
        }

        private static BigInteger Rescale(BigInteger c, int k)
        {
            if (k >= 0)
                return c * BigInteger.Pow(10, k);
            return DivNearest(c, BigInteger.Pow(10, -k));
        }

        // Round-half-even division by a positive divisor, for a dividend of either sign.
        private static BigInteger DivNearest(BigInteger a, BigInteger b)
        {
            BigInteger r;
            BigInteger q = BigInteger.DivRem(a, b, out r);
            if (r.Sign < 0)
            {
                q -= BigInteger.One;
                r += b;
            }
            BigInteger twice = r * 2;
            if (twice > b || (twice == b && !q.IsEven))
                q += BigInteger.One;
            return q;
        }

        private static BigInteger FloorDiv(BigInteger a, BigInteger b)
        {
            BigInteger r;
            BigInteger q = BigInteger.DivRem(a, b, out r);
            if (r.Sign < 0)
                q -= BigInteger.One;
            return q;
        }

        private static ScriptException InvalidOp(EvalContext ctx)
        {
            return Raise.Make(ctx, PyExceptionTypes.DecimalInvalidOperation, "[<class 'decimal.InvalidOperation'>]");
        }

        private static ScriptException Overflow(EvalContext ctx)
        {
            return Raise.Make(ctx, PyExceptionTypes.DecimalOverflow, "[<class 'decimal.Overflow'>]");
        }

        // CPython answers -Infinity here; this backend has no infinities to answer with.
        private static ScriptException NoInfinity(EvalContext ctx, string what)
        {
            return Raise.Make(ctx, PyExceptionTypes.DecimalInvalidOperation,
                what + " is -Infinity, which this backend cannot represent");
        }
    }
}
