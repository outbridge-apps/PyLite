using System;
using System.Collections.Generic;
using System.Numerics;

namespace Outbridge.PyLite.Runtime.Values
{
    // The long big-int operations, on magnitudes held as little-endian uint limbs, with a deadline check every
    // CheckEvery word products (a few ms): a stopped run stops here too, and no helper thread computes on.
    // BigOps sends here only what is past LongWork; net472's own code keeps the short operations. At these
    // sizes the kernel is also the faster one: Karatsuba against schoolbook multiplication, Knuth's algorithm D
    // against net472's division, Lehmer's gcd, and a divide-and-conquer decimal conversion against repeated
    // division by 10^9.
    internal static class BigKernel
    {
        private const int KaraLimit = 48;         // at or below this many limbs a schoolbook product is faster
        private const int KaraSqLimit = 64;       // the schoolbook square does half the products, so it lasts longer
        private const int LeafLimbs = 48;         // decimal conversion: repeated division by 10^9 below this
        private const long CheckEvery = 1L << 20;
        private const uint Billion = 1000000000;

        // Counts word products and checks the deadline every CheckEvery of them.
        private sealed class Meter
        {
            private readonly EvalContext _ctx;
            private long _work;

            public Meter(EvalContext ctx)
            {
                _ctx = ctx;
            }

            public void Add(long words)
            {
                _work += words;
                if (_work >= CheckEvery)
                {
                    _work = 0;
                    _ctx.Budget.CheckDeadlineNow();
                }
            }
        }

        // ---- entry points (same results as the BigInteger operators) ----

        internal static BigInteger Multiply(EvalContext ctx, BigInteger a, BigInteger b)
        {
            int an, bn;
            uint[] x = Limbs(a, out an), y = Limbs(b, out bn);
            if (an == 0 || bn == 0)
                return BigInteger.Zero;
            var meter = new Meter(ctx);
            uint[] r = an >= bn ? Mul(x, an, y, bn, meter) : Mul(y, bn, x, an, meter);
            return Big(r, an + bn, (a.Sign < 0) != (b.Sign < 0));
        }

        internal static BigInteger Square(EvalContext ctx, BigInteger a)
        {
            int n;
            uint[] x = Limbs(a, out n);
            if (n == 0)
                return BigInteger.Zero;
            var r = new uint[2 * n];
            SquareInto(x, 0, n, r, 0, new Meter(ctx));
            return Big(r, 2 * n, false);
        }

        // Truncated division, as BigInteger.DivRem: the quotient rounds toward zero, the remainder takes a's sign.
        internal static BigInteger DivRem(EvalContext ctx, BigInteger a, BigInteger b, out BigInteger remainder)
        {
            if (b.IsZero)
                throw new DivideByZeroException();
            int un, vn;
            uint[] u = Limbs(a, out un), v = Limbs(b, out vn);
            if (Compare(u, un, v, vn) < 0)
            {
                remainder = a;
                return BigInteger.Zero;
            }
            var q = new uint[un - vn + 1];
            var r = new uint[vn];
            DivRemLimbs(u, un, v, vn, q, r, new Meter(ctx));
            remainder = Big(r, vn, a.Sign < 0);
            return Big(q, q.Length, (a.Sign < 0) != (b.Sign < 0));
        }

        // gcd(|a|, |b|), as BigInteger.GreatestCommonDivisor.
        internal static BigInteger Gcd(EvalContext ctx, BigInteger a, BigInteger b)
        {
            int xn, yn;
            uint[] x = Limbs(a, out xn), y = Limbs(b, out yn);
            if (Compare(x, xn, y, yn) < 0)
            {
                Swap(ref x, ref y);
                Swap(ref xn, ref yn);
            }
            var meter = new Meter(ctx);
            // HAC 14.57 (Lehmer) on the leading 63 bits (14.58 (ii)): Euclid's steps are run on the leads while
            // they are certain to agree with the full numbers' (Jebelean's condition), then applied to the full
            // numbers at once as a 2x2 cofactor matrix - one linear pass for about 30 bits of reduction.
            while (yn > 2)
            {
                ulong xs, ys;
                Leads(x, xn, y, yn, out xs, out ys);
                uint ca = 1, cb = 0, cc = 0, cd = 1;
                int steps = 0;
                while (ys != 0)
                {
                    ulong q = xs / ys;
                    if (q > uint.MaxValue)
                        break;
                    ulong na = ca + q * cc, nb = cb + q * cd, t = xs - q * ys;
                    if (na > int.MaxValue || nb > int.MaxValue || t < nb || t + na > ys - cc)
                        break;
                    ca = (uint)na;
                    cb = (uint)nb;
                    xs = t;
                    steps++;
                    if (xs == cb)
                        break;
                    q = ys / xs;
                    if (q > uint.MaxValue)
                        break;
                    ulong nd = cd + q * cb, nc = cc + q * ca;
                    t = ys - q * xs;
                    if (nd > int.MaxValue || nc > int.MaxValue || t < nc || t + nd > xs - cb)
                        break;
                    cd = (uint)nd;
                    cc = (uint)nc;
                    ys = t;
                    steps++;
                    if (ys == cc)
                        break;
                }
                if (cb == 0)
                {
                    // the leads decided nothing: one full step, x mod y, and the roles swap
                    xn = RemInPlace(x, xn, y, yn, meter);
                    Swap(ref x, ref y);
                    Swap(ref xn, ref yn);
                }
                else
                {
                    Cofactors(x, xn, y, yn, ca, cb, cc, cd);
                    xn = Trim(x, yn);
                    yn = Trim(y, yn);
                    meter.Add(2L * yn);
                    if ((steps & 1) == 1)
                    {
                        Swap(ref x, ref y);
                        Swap(ref xn, ref yn);
                    }
                }
            }
            if (yn == 0)
                return Big(x, xn, false);
            xn = RemInPlace(x, xn, y, yn, meter);
            ulong g = Word64(y, yn), h = Word64(x, xn);
            while (h != 0)
            {
                ulong t = g % h;
                g = h;
                h = t;
            }
            return new BigInteger(g);
        }

        // Decimal digits: x = q * 10^(9 * 2^k) + r with the power about half x's size, both halves recursively,
        // r zero-padded to its 9 * 2^k digits; below LeafLimbs, repeated division by 10^9.
        internal static string ToDecimalString(EvalContext ctx, BigInteger v)
        {
            if (v.IsZero)
                return "0";
            int n;
            uint[] x = Limbs(v, out n);
            var meter = new Meter(ctx);
            var pows = new List<uint[]>();
            var powLens = new List<int>();
            uint[] p = { Billion };
            int pn = 1;
            while (2 * pn <= n + 1)
            {
                pows.Add(p);
                powLens.Add(pn);
                var sq = new uint[2 * pn];
                SquareInto(p, 0, pn, sq, 0, meter);
                p = sq;
                pn = Trim(sq, 2 * pn);
            }
            // the digit count plus room for the top leaf's 9-digit chunk, whose leading zeros are trimmed after
            var buf = new char[(int)(n * 32 * 0.30102999566398120) + 10];
            int start = Write(x, n, pows, powLens, buf, buf.Length, 0, meter);
            string s = new string(buf, start, buf.Length - start);
            return v.Sign < 0 ? "-" + s : s;
        }

        // ---- limbs ----

        // |x| as limbs; len is the significant length (0 for zero).
        private static uint[] Limbs(BigInteger x, out int len)
        {
            byte[] b = (x.Sign < 0 ? -x : x).ToByteArray();
            var l = new uint[(b.Length + 3) / 4];
            Buffer.BlockCopy(b, 0, l, 0, b.Length);
            len = Trim(l, l.Length);
            return l;
        }

        private static BigInteger Big(uint[] x, int len, bool negative)
        {
            len = Trim(x, len);
            if (len == 0)
                return BigInteger.Zero;
            var b = new byte[4 * len + 1];   // the extra zero byte keeps it positive
            Buffer.BlockCopy(x, 0, b, 0, 4 * len);
            var r = new BigInteger(b);
            return negative ? -r : r;
        }

        private static int Trim(uint[] x, int len)
        {
            while (len > 0 && x[len - 1] == 0)
                len--;
            return len;
        }

        private static int Compare(uint[] x, int xn, uint[] y, int yn)
        {
            if (xn != yn)
                return xn < yn ? -1 : 1;
            for (int i = xn - 1; i >= 0; i--)
            {
                if (x[i] != y[i])
                    return x[i] < y[i] ? -1 : 1;
            }
            return 0;
        }

        private static void Swap<T>(ref T a, ref T b)
        {
            T t = a;
            a = b;
            b = t;
        }

        private static ulong Word64(uint[] x, int n)
        {
            return n == 0 ? 0 : n == 1 ? x[0] : ((ulong)x[1] << 32) | x[0];
        }

        // ---- multiplication ----

        // a[0, an) * b[0, bn) as a new an + bn limb array; an >= bn > 0.
        private static uint[] Mul(uint[] a, int an, uint[] b, int bn, Meter meter)
        {
            var r = new uint[an + bn];
            MulInto(a, 0, an, b, 0, bn, r, 0, meter);
            return r;
        }

        // r[ro, ro + an + bn) = a * b, an >= bn > 0.
        private static void MulInto(uint[] a, int ao, int an, uint[] b, int bo, int bn, uint[] r, int ro, Meter meter)
        {
            if (bn <= KaraLimit)
            {
                School(a, ao, an, b, bo, bn, r, ro, meter);
                return;
            }
            if (an == bn)
            {
                Kara(a, ao, b, bo, an, r, ro, new uint[KaraScratch(an)], 0, meter);
                return;
            }
            // the longer operand in bn-limb slices, each a balanced product
            Array.Clear(r, ro, an + bn);
            var t = new uint[2 * bn];
            var s = new uint[KaraScratch(bn)];
            int off = 0;
            for (; off + bn <= an; off += bn)
            {
                Kara(a, ao + off, b, bo, bn, t, 0, s, 0, meter);
                AddIn(r, ro + off, an + bn - off, t, 0, 2 * bn);
            }
            int rest = an - off;
            if (rest > 0)
            {
                var u = new uint[rest + bn];
                MulInto(b, bo, bn, a, ao + off, rest, u, 0, meter);
                AddIn(r, ro + off, an + bn - off, u, 0, rest + bn);
            }
        }

        // r[ro, ro + an + bn) = a * b, row by row.
        private static void School(uint[] a, int ao, int an, uint[] b, int bo, int bn, uint[] r, int ro, Meter meter)
        {
            Array.Clear(r, ro, an + bn);
            for (int i = 0; i < an; i++)
            {
                ulong ai = a[ao + i];
                if (ai != 0)
                {
                    ulong c = 0;
                    int k = ro + i;
                    for (int j = 0; j < bn; j++, k++)
                    {
                        ulong t = ai * b[bo + j] + r[k] + c;
                        r[k] = (uint)t;
                        c = t >> 32;
                    }
                    r[k] = (uint)c;
                }
                meter.Add(bn);
            }
        }

        // r[ro, ro + 2n) = a * b for two n-limb operands. z0 and z2 go straight into r; the middle term and
        // the half sums live in the scratch s[so..), which the recursion shares.
        private static void Kara(uint[] a, int ao, uint[] b, int bo, int n, uint[] r, int ro, uint[] s, int so, Meter meter)
        {
            if (n <= KaraLimit)
            {
                School(a, ao, n, b, bo, n, r, ro, meter);
                return;
            }
            int m = n >> 1, h = n - m;
            Kara(a, ao, b, bo, m, r, ro, s, so, meter);
            Kara(a, ao + m, b, bo + m, h, r, ro + 2 * m, s, so, meter);
            int sa = so, sb = so + h + 1, z = so + 2 * (h + 1);
            AddHalves(a, ao, m, h, s, sa);
            AddHalves(b, bo, m, h, s, sb);
            Kara(s, sa, s, sb, h + 1, s, z, s, z + 2 * (h + 1), meter);
            SubIn(s, z, 2 * (h + 1), r, ro, 2 * m);
            SubIn(s, z, 2 * (h + 1), r, ro + 2 * m, 2 * h);
            AddIn(r, ro + m, 2 * n - m, s, z, 2 * (h + 1));
        }

        private static int KaraScratch(int n)
        {
            if (n <= KaraLimit)
                return 0;
            int h = n - (n >> 1);
            return 4 * (h + 1) + KaraScratch(h + 1);
        }

        private static void SquareInto(uint[] a, int ao, int n, uint[] r, int ro, Meter meter)
        {
            if (n <= KaraSqLimit)
                SchoolSquare(a, ao, n, r, ro, meter);
            else
                KaraSquare(a, ao, n, r, ro, new uint[KaraSqScratch(n)], 0, meter);
        }

        // r[ro, ro + 2n) = a^2: each cross product once, doubled, then the squares of the limbs.
        private static void SchoolSquare(uint[] a, int ao, int n, uint[] r, int ro, Meter meter)
        {
            Array.Clear(r, ro, 2 * n);
            for (int i = 0; i < n - 1; i++)
            {
                ulong ai = a[ao + i];
                if (ai != 0)
                {
                    ulong c = 0;
                    int k = ro + 2 * i + 1;
                    for (int j = i + 1; j < n; j++, k++)
                    {
                        ulong t = ai * a[ao + j] + r[k] + c;
                        r[k] = (uint)t;
                        c = t >> 32;
                    }
                    r[k] = (uint)c;
                }
                meter.Add(n - i);
            }
            uint top = 0;
            for (int k = ro; k < ro + 2 * n; k++)
            {
                uint w = r[k];
                r[k] = (w << 1) | top;
                top = w >> 31;
            }
            ulong carry = 0;
            for (int i = 0; i < n; i++)
            {
                ulong sq = (ulong)a[ao + i] * a[ao + i];
                int k = ro + 2 * i;
                ulong t = (ulong)r[k] + (uint)sq + carry;
                r[k] = (uint)t;
                t = (ulong)r[k + 1] + (sq >> 32) + (t >> 32);
                r[k + 1] = (uint)t;
                carry = t >> 32;
            }
        }

        private static void KaraSquare(uint[] a, int ao, int n, uint[] r, int ro, uint[] s, int so, Meter meter)
        {
            if (n <= KaraSqLimit)
            {
                SchoolSquare(a, ao, n, r, ro, meter);
                return;
            }
            int m = n >> 1, h = n - m;
            KaraSquare(a, ao, m, r, ro, s, so, meter);
            KaraSquare(a, ao + m, h, r, ro + 2 * m, s, so, meter);
            int z = so + h + 1;
            AddHalves(a, ao, m, h, s, so);
            KaraSquare(s, so, h + 1, s, z, s, z + 2 * (h + 1), meter);
            SubIn(s, z, 2 * (h + 1), r, ro, 2 * m);
            SubIn(s, z, 2 * (h + 1), r, ro + 2 * m, 2 * h);
            AddIn(r, ro + m, 2 * n - m, s, z, 2 * (h + 1));
        }

        private static int KaraSqScratch(int n)
        {
            if (n <= KaraSqLimit)
                return 0;
            int h = n - (n >> 1);
            return 3 * (h + 1) + KaraSqScratch(h + 1);
        }

        // d[dofs, dofs + h] = x[xo + m, xo + m + h) + x[xo, xo + m), m <= h.
        private static void AddHalves(uint[] x, int xo, int m, int h, uint[] d, int dofs)
        {
            ulong c = 0;
            for (int i = 0; i < h; i++)
            {
                ulong t = (ulong)x[xo + m + i] + (i < m ? x[xo + i] : 0u) + c;
                d[dofs + i] = (uint)t;
                c = t >> 32;
            }
            d[dofs + h] = (uint)c;
        }

        // z[zo, zo + zn) -= y[yo, yo + yn); the result is known not to go below zero.
        private static void SubIn(uint[] z, int zo, int zn, uint[] y, int yo, int yn)
        {
            long borrow = 0;
            int i = 0;
            for (; i < yn; i++)
            {
                long t = (long)z[zo + i] - y[yo + i] - borrow;
                z[zo + i] = (uint)t;
                borrow = (t >> 63) & 1;
            }
            for (; borrow != 0 && i < zn; i++)
            {
                uint w = z[zo + i];
                z[zo + i] = w - 1;
                borrow = w == 0 ? 1 : 0;
            }
        }

        // r[ro, ro + rn) += y[yo, yo + yn); the sum is known to fit.
        private static void AddIn(uint[] r, int ro, int rn, uint[] y, int yo, int yn)
        {
            while (yn > 0 && y[yo + yn - 1] == 0)
                yn--;
            ulong c = 0;
            int i = 0;
            for (; i < yn; i++)
            {
                ulong t = (ulong)r[ro + i] + y[yo + i] + c;
                r[ro + i] = (uint)t;
                c = t >> 32;
            }
            for (; c != 0 && i < rn; i++)
            {
                ulong t = (ulong)r[ro + i] + c;
                r[ro + i] = (uint)t;
                c = t >> 32;
            }
        }

        // ---- division ----

        // u / v and u % v for un >= vn > 0 (both trimmed): q gets un - vn + 1 limbs, r gets vn; either may be
        // null when not wanted. Knuth's algorithm D (TAOCP 4.3.1, as in Hacker's Delight divmnu).
        private static void DivRemLimbs(uint[] u, int un, uint[] v, int vn, uint[] q, uint[] r, Meter meter)
        {
            if (vn == 1)
            {
                ulong d = v[0], rem = 0;
                for (int i = un - 1; i >= 0; i--)
                {
                    ulong cur = (rem << 32) | u[i];
                    ulong qd = cur / d;
                    if (q != null)
                        q[i] = (uint)qd;
                    rem = cur - qd * d;
                }
                if (r != null)
                    r[0] = (uint)rem;
                meter.Add(un);
                return;
            }
            int s = Nlz(v[vn - 1]);
            var vs = new uint[vn];
            ShiftUp(v, vn, s, vs);
            var us = new uint[un + 1];
            ShiftUp(u, un, s, us);
            Knuth(us, un, vs, vn, q, meter);
            if (r != null)
                ShiftDown(us, vn, s, r);
        }

        // d[0, n] = x[0, n) << s (0 <= s < 32); d[n] takes the bits shifted out of the top.
        private static void ShiftUp(uint[] x, int n, int s, uint[] d)
        {
            if (d.Length > n)
                d[n] = s == 0 ? 0 : x[n - 1] >> (32 - s);
            for (int i = n - 1; i > 0; i--)
                d[i] = (x[i] << s) | (s == 0 ? 0 : x[i - 1] >> (32 - s));
            d[0] = x[0] << s;
        }

        // d[0, n) = x[0, n] >> s.
        private static void ShiftDown(uint[] x, int n, int s, uint[] d)
        {
            for (int i = 0; i < n; i++)
                d[i] = (x[i] >> s) | (s == 0 ? 0 : x[i + 1] << (32 - s));
        }

        // The quotient loop of algorithm D on normalized operands: us[0, un] (one limb above the dividend) is
        // left holding the normalized remainder in its low vn limbs; q gets un - vn + 1 limbs unless null.
        private static void Knuth(uint[] us, int un, uint[] vs, int vn, uint[] q, Meter meter)
        {
            ulong vTop = vs[vn - 1], vNext = vs[vn - 2];
            for (int j = un - vn; j >= 0; j--)
            {
                ulong num = ((ulong)us[j + vn] << 32) | us[j + vn - 1];
                ulong qhat = num / vTop;
                ulong rhat = num - qhat * vTop;
                while (qhat > uint.MaxValue || qhat * vNext > ((rhat << 32) | us[j + vn - 2]))
                {
                    qhat--;
                    rhat += vTop;
                    if (rhat > uint.MaxValue)
                        break;
                }
                long k = 0, t;
                for (int i = 0; i < vn; i++)
                {
                    ulong p = qhat * vs[i];
                    t = (long)us[i + j] - k - (long)(p & 0xFFFFFFFFUL);
                    us[i + j] = (uint)t;
                    k = (long)(p >> 32) - (t >> 32);
                }
                t = (long)us[j + vn] - k;
                us[j + vn] = (uint)t;
                if (t < 0)
                {
                    // qhat was one too large (rare): add v back
                    qhat--;
                    ulong c = 0;
                    for (int i = 0; i < vn; i++)
                    {
                        ulong a = (ulong)us[i + j] + vs[i] + c;
                        us[i + j] = (uint)a;
                        c = a >> 32;
                    }
                    us[j + vn] = (uint)(us[j + vn] + c);
                }
                if (q != null)
                    q[j] = (uint)qhat;
                meter.Add(vn);
            }
        }

        // ---- modular power ----

        // b^e mod m for m > 0, b >= 0, e >= 0. Left-to-right sliding window over e's bits; every square or
        // product goes into one 2n-limb buffer and is reduced by algorithm D against the modulus normalized
        // once, so a step allocates nothing (net472's ModPow is right-to-left binary with full-size products).
        internal static BigInteger ModPow(EvalContext ctx, BigInteger b, BigInteger e, BigInteger m)
        {
            int n;
            uint[] mod = Limbs(m, out n);
            var meter = new Meter(ctx);
            byte[] eb = e.ToByteArray();
            int ebits = eb.Length * 8;
            while (ebits > 0 && ((eb[(ebits - 1) >> 3] >> ((ebits - 1) & 7)) & 1) == 0)
                ebits--;
            if (n == 1)
                return ModPowWord(b, eb, ebits, mod[0], meter);
            var red = new Reducer(mod, n, meter);
            int xn;
            uint[] xl = Limbs(b, out xn);
            if (xn > 2 * n)
            {
                var rem = new uint[n];
                DivRemLimbs(xl, xn, mod, n, null, rem, meter);
                xl = rem;
                xn = n;
            }
            var x = new uint[n];
            red.Reduce(xl, xn, x);
            var prod = new uint[2 * n];
            uint[] mulScratch = n > KaraLimit ? new uint[KaraScratch(n)] : null;
            uint[] sqScratch = n > KaraSqLimit ? new uint[KaraSqScratch(n)] : null;

            int k = ebits > 512 ? 5 : ebits > 128 ? 4 : ebits > 24 ? 3 : 1;
            var odd = new uint[1 << (k - 1)][];   // x^1, x^3, x^5, ...
            odd[0] = x;
            if (k > 1)
            {
                var x2 = new uint[n];
                SquareMod(x, x2, prod, sqScratch, red, n, meter);
                for (int i = 1; i < odd.Length; i++)
                {
                    odd[i] = new uint[n];
                    MulMod(odd[i - 1], x2, odd[i], prod, mulScratch, red, n, meter);
                }
            }
            var r = new uint[n];
            bool one = true;   // r stands for 1 until the first window
            int bit = ebits - 1;
            while (bit >= 0)
            {
                if (((eb[bit >> 3] >> (bit & 7)) & 1) == 0)
                {
                    if (!one)
                        SquareMod(r, r, prod, sqScratch, red, n, meter);
                    bit--;
                    continue;
                }
                int low = Math.Max(bit - k + 1, 0);
                while (((eb[low >> 3] >> (low & 7)) & 1) == 0)
                    low++;
                int value = 0;
                for (int i = bit; i >= low; i--)
                    value = (value << 1) | ((eb[i >> 3] >> (i & 7)) & 1);
                if (one)
                {
                    Array.Copy(odd[value >> 1], r, n);
                    one = false;
                }
                else
                {
                    for (int i = bit; i >= low; i--)
                        SquareMod(r, r, prod, sqScratch, red, n, meter);
                    MulMod(r, odd[value >> 1], r, prod, mulScratch, red, n, meter);
                }
                bit = low - 1;
            }
            if (one)
                return BigInteger.One;   // e == 0 and m > 1
            return Big(r, n, false);
        }

        private static void SquareMod(uint[] a, uint[] dst, uint[] prod, uint[] scratch, Reducer red, int n, Meter meter)
        {
            if (scratch == null)
                SchoolSquare(a, 0, n, prod, 0, meter);
            else
                KaraSquare(a, 0, n, prod, 0, scratch, 0, meter);
            red.Reduce(prod, 2 * n, dst);
        }

        private static void MulMod(uint[] a, uint[] b, uint[] dst, uint[] prod, uint[] scratch, Reducer red, int n, Meter meter)
        {
            if (scratch == null)
                School(a, 0, n, b, 0, n, prod, 0, meter);
            else
                Kara(a, 0, b, 0, n, prod, 0, scratch, 0, meter);
            red.Reduce(prod, 2 * n, dst);
        }

        // A one-limb modulus: the products fit in a ulong.
        private static BigInteger ModPowWord(BigInteger b, byte[] eb, int ebits, uint m, Meter meter)
        {
            ulong x = (ulong)(b % m), r = 1 % (ulong)m;
            for (int bit = ebits - 1; bit >= 0; bit--)
            {
                r = r * r % m;
                if (((eb[bit >> 3] >> (bit & 7)) & 1) != 0)
                    r = r * x % m;
                meter.Add(2);
            }
            return new BigInteger(r);
        }

        // Remainders by one modulus of n >= 2 limbs: the modulus is normalized once and the dividend shifted into
        // a buffer kept for the next call.
        private sealed class Reducer
        {
            private readonly uint[] _v;
            private readonly uint[] _u;
            private readonly int _n, _s;
            private readonly Meter _meter;

            public Reducer(uint[] m, int n, Meter meter)
            {
                _n = n;
                _s = Nlz(m[n - 1]);
                _v = new uint[n];
                ShiftUp(m, n, _s, _v);
                _u = new uint[2 * n + 1];
                _meter = meter;
            }

            // dst[0, n) = x[0, xn) mod m, xn <= 2n; dst may be the array x's value came from.
            public void Reduce(uint[] x, int xn, uint[] dst)
            {
                xn = Trim(x, xn);
                if (xn < _n)
                {
                    // below m already (m's top limb is non-zero)
                    for (int i = 0; i < _n; i++)
                        dst[i] = i < xn ? x[i] : 0u;
                    return;
                }
                Array.Clear(_u, 0, _u.Length);
                ShiftUp(x, xn, _s, _u);
                Knuth(_u, xn, _v, _n, null, _meter);
                ShiftDown(_u, _n, _s, dst);
            }
        }

        // x = x mod y in place (xn >= yn > 0); returns x's new length.
        private static int RemInPlace(uint[] x, int xn, uint[] y, int yn, Meter meter)
        {
            if (Compare(x, xn, y, yn) < 0)
                return xn;
            var r = new uint[yn];
            DivRemLimbs(x, xn, y, yn, null, r, meter);
            Array.Clear(x, 0, xn);
            Array.Copy(r, x, yn);
            return Trim(x, yn);
        }

        private static int Nlz(uint x)
        {
            if (x == 0)
                return 32;
            int n = 0;
            if ((x & 0xFFFF0000u) == 0) { n += 16; x <<= 16; }
            if ((x & 0xFF000000u) == 0) { n += 8; x <<= 8; }
            if ((x & 0xF0000000u) == 0) { n += 4; x <<= 4; }
            if ((x & 0xC0000000u) == 0) { n += 2; x <<= 2; }
            if ((x & 0x80000000u) == 0) { n += 1; }
            return n;
        }

        // ---- gcd helpers ----

        // The top 63 bits of x and the bits of y at the same positions (xn >= 3, xn >= yn), so the quotients of
        // the leads are those the full numbers start with.
        private static void Leads(uint[] x, int xn, uint[] y, int yn, out ulong xs, out ulong ys)
        {
            ulong xh = x[xn - 1], xm = x[xn - 2], xl = x[xn - 3];
            ulong yh = 0, ym = 0, yl = 0;
            switch (xn - yn)
            {
                case 0:
                    yh = y[yn - 1];
                    ym = y[yn - 2];
                    yl = y[yn - 3];
                    break;
                case 1:
                    ym = y[yn - 1];
                    yl = y[yn - 2];
                    break;
                case 2:
                    yl = y[yn - 1];
                    break;
            }
            int z = Nlz((uint)xh);
            xs = ((xh << (32 + z)) | (xm << z) | (xl >> (32 - z))) >> 1;
            ys = ((yh << (32 + z)) | (ym << z) | (yl >> (32 - z))) >> 1;
        }

        // x' = a*x - b*y, y' = d*y - c*x over y's yn limbs: both results are known to fit in them, so the limbs
        // past yn (x may be longer) are only cleared.
        private static void Cofactors(uint[] x, int xn, uint[] y, int yn, uint a, uint b, uint c, uint d)
        {
            long xc = 0, yc = 0;
            for (int i = 0; i < yn; i++)
            {
                long xd = (long)a * x[i] - (long)b * y[i] + xc;
                long yd = (long)d * y[i] - (long)c * x[i] + yc;
                xc = xd >> 32;
                yc = yd >> 32;
                x[i] = (uint)xd;
                y[i] = (uint)yd;
            }
            if (xn > yn)
                Array.Clear(x, yn, xn - yn);
        }

        // ---- decimal ----

        // x right-aligned to end; width > 0 pads to exactly width digits (x < 10^width, width a multiple of
        // 9), width 0 writes the digits alone. Returns the first index written.
        private static int Write(uint[] x, int n, List<uint[]> pows, List<int> powLens, char[] buf, int end, int width, Meter meter)
        {
            if (n <= LeafLimbs)
                return WriteLeaf(x, n, buf, end, width, meter);
            int k = powLens.Count - 1;
            while (k > 0 && 2 * powLens[k] > n + 1)
                k--;
            int pn = powLens[k];
            var q = new uint[n - pn + 1];
            var r = new uint[pn];
            DivRemLimbs(x, n, pows[k], pn, q, r, meter);
            int digits = 9 << k;
            Write(r, Trim(r, pn), pows, powLens, buf, end, digits, meter);
            return Write(q, Trim(q, q.Length), pows, powLens, buf, end - digits, width > 0 ? width - digits : 0, meter);
        }

        private static int WriteLeaf(uint[] x, int n, char[] buf, int end, int width, Meter meter)
        {
            var t = new uint[n];
            Array.Copy(x, t, n);
            int pos = end;
            while (n > 0)
            {
                ulong rem = 0;
                for (int i = n - 1; i >= 0; i--)
                {
                    ulong cur = (rem << 32) | t[i];
                    ulong qd = cur / Billion;
                    t[i] = (uint)qd;
                    rem = cur - qd * Billion;
                }
                n = Trim(t, n);
                meter.Add(n + 1);
                uint chunk = (uint)rem;
                for (int d = 0; d < 9; d++)
                {
                    buf[--pos] = (char)('0' + chunk % 10);
                    chunk /= 10;
                }
            }
            if (width > 0)
            {
                while (pos > end - width)
                    buf[--pos] = '0';
                return pos;
            }
            while (pos < end - 1 && buf[pos] == '0')
                pos++;
            return pos;
        }
    }
}
