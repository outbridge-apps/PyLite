using System;

namespace Outbridge.PyLite.Modules.Support
{
    // Double- and triple-length arithmetic of CPython's mathmodule.c, for hypot, dist and sumprod.
    internal static class DoubleLength
    {
        private const double Split = 134217729.0;     // 2^27 + 1
        private const double BigSide = 1e290;         // past this the split overflows
        private const double Down = 5.551115123125783e-17, Up = 18014398509481984.0;   // 2^-54, 2^54

        // x*y == hi + lo exactly, unless the product underflows.
        public static void Mul(double x, double y, out double hi, out double lo)
        {
            hi = x * y;
            double scaleBack = 1.0;
            if (Math.Abs(x) > BigSide)
            {
                x *= Down;
                scaleBack = Up;
            }
            if (Math.Abs(y) > BigSide)
            {
                y *= Down;
                scaleBack *= Up;
            }
            double p = scaleBack == 1.0 ? hi : x * y;
            double tx = Split * x, hx = tx - (tx - x), lx = x - hx;
            double ty = Split * y, hy = ty - (ty - y), ly = y - hy;
            lo = (((hx * hy - p) + hx * ly + lx * hy) + lx * ly) * scaleBack;
        }

        private static void Sum(double a, double b, out double s, out double e)
        {
            s = a + b;
            double z = s - a;
            e = (a - (s - z)) + (b - z);
        }

        // Algorithm 5.10 of Ogita, Rump and Oishi with SumKVert for K=3.
        public static void TripleFma(double x, double y, double hi, double lo, double tiny, out double newHi, out double newLo, out double newTiny)
        {
            double prHi, prLo, smLo, r1Hi, r1Lo, r2Lo;
            Mul(x, y, out prHi, out prLo);
            Sum(hi, prHi, out newHi, out smLo);
            Sum(lo, prLo, out r1Hi, out r1Lo);
            Sum(r1Hi, smLo, out newLo, out r2Lo);
            newTiny = tiny + r1Lo + r2Lo;
        }

        public static double TripleToDouble(double hi, double lo, double tiny)
        {
            double s, e;
            Sum(lo, hi, out s, out e);
            return tiny + e + s;
        }
    }
}
