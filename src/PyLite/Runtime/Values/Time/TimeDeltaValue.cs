using System;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // datetime.timedelta. Immutable normalized CPython triple. All arithmetic runs
    // through BigInteger microseconds (timedelta.max.ToUs() ~ 8.64e19 does NOT fit in long). Floored divmod
    // and half-even DivideAndRound are literal ports of Lib/datetime.py.
    internal sealed class TimeDeltaValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        // INVARIANT: -999999999 <= Days <= 999999999; 0 <= Seconds <= 86399; 0 <= Microseconds <= 999999.
        public readonly int Days;
        public readonly int Seconds;
        public readonly int Microseconds;

        private TimeDeltaValue(int days, int seconds, int us)
        {
            Days = days;
            Seconds = seconds;
            Microseconds = us;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        // ---- factories ----

        // From an already-normalized triple (internal, for arithmetic). Asserts the invariant in DEBUG.
        internal static TimeDeltaValue FromNormalized(EvalContext ctx, int days, int seconds, int us)
        {
            System.Diagnostics.Debug.Assert(seconds >= 0 && seconds <= 86399, "timedelta seconds out of range");
            System.Diagnostics.Debug.Assert(us >= 0 && us <= 999999, "timedelta microseconds out of range");
            System.Diagnostics.Debug.Assert(days >= -999999999 && days <= 999999999, "timedelta days out of range");
            ctx.Values.PreCharge(48);
            return new TimeDeltaValue(days, seconds, us);
        }

        internal BigInteger ToUs()
        {
            return ((BigInteger)Days * CalendarMath.SecondsPerDay + Seconds) * CalendarMath.UsPerSecond + Microseconds;
        }

        // Long twin of ToUs — valid ONLY when |Days| <= ~1e8 (callers guard; timedelta.max would overflow).
        internal long ToUsLong()
        {
            return ((long)Days * CalendarMath.SecondsPerDay + Seconds) * CalendarMath.UsPerSecond + Microseconds;
        }

        internal static TimeDeltaValue FromUs(EvalContext ctx, BigInteger us)
        {
            BigInteger days = FloorDivBig(us, CalendarMath.UsPerDay);
            BigInteger rem = us - days * CalendarMath.UsPerDay;   // 0 <= rem < UsPerDay
            if (days > 999999999 || days < -999999999)
                throw Raise.Overflow(ctx, "days=" + days.ToString(CultureInfo.InvariantCulture) + "; must have magnitude <= 999999999");
            int seconds = (int)(rem / CalendarMath.UsPerSecond);
            int micro = (int)(rem % CalendarMath.UsPerSecond);
            return FromNormalized(ctx, (int)days, seconds, micro);
        }

        // ---- script constructor: timedelta(days, seconds, microseconds, milliseconds, minutes, hours, weeks) ----

        private static readonly string[] CompNames =
            { "days", "seconds", "microseconds", "milliseconds", "minutes", "hours", "weeks" };

        private static readonly ArgSpec Spec = ArgSpec.Create("timedelta")
            .Opt("days", ArgSpec.MISSING).Opt("seconds", ArgSpec.MISSING).Opt("microseconds", ArgSpec.MISSING)
            .Opt("milliseconds", ArgSpec.MISSING).Opt("minutes", ArgSpec.MISSING).Opt("hours", ArgSpec.MISSING)
            .Opt("weeks", ArgSpec.MISSING).Seal();

        internal static TimeDeltaValue Construct(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            ctx.Budget.Step();
            var slots = new ScriptValue[Spec.SlotCount];
            ScriptValue[] star;
            Spec.Bind(ctx, args, kw, slots, out star);

            // Fast path: every component is a small int (|v| <= 1e6, the everyday case) — fold in long.
            // Worst total: (1e6 + 7e6)*86400s + ~3.7e9s + ~1e9us  =>  ~7e17 us, safely inside long.
            long dL, sL, uL, msL, mnL, hL, wL;
            if (SmallComp(slots[0], out dL) && SmallComp(slots[1], out sL) && SmallComp(slots[2], out uL)
                && SmallComp(slots[3], out msL) && SmallComp(slots[4], out mnL) && SmallComp(slots[5], out hL)
                && SmallComp(slots[6], out wL))
            {
                long totalUs = ((dL + wL * 7) * CalendarMath.SecondsPerDay + sL + mnL * 60 + hL * 3600)
                    * CalendarMath.UsPerSecond + uL + msL * 1000;
                long dayQ = totalUs / CalendarMath.UsPerDay;
                long rem = totalUs - dayQ * CalendarMath.UsPerDay;
                if (rem < 0)
                {
                    dayQ--;
                    rem += CalendarMath.UsPerDay;
                }
                return FromNormalized(ctx, (int)dayQ, (int)(rem / CalendarMath.UsPerSecond), (int)(rem % CalendarMath.UsPerSecond));
            }

            Num daysN = Comp(ctx, slots[0], 0), secN = Comp(ctx, slots[1], 1), usN = Comp(ctx, slots[2], 2);
            Num msN = Comp(ctx, slots[3], 3), minN = Comp(ctx, slots[4], 4), hrN = Comp(ctx, slots[5], 5), wkN = Comp(ctx, slots[6], 6);

            // Reject NaN/Inf up front (Python surfaces these at the first int()/round() conversion).
            RejectNonFinite(ctx, daysN); RejectNonFinite(ctx, secN); RejectNonFinite(ctx, usN);
            RejectNonFinite(ctx, msN); RejectNonFinite(ctx, minN); RejectNonFinite(ctx, hrN); RejectNonFinite(ctx, wkN);

            // Unit folding (float promotes, exactly like Python).
            Num days = Add(daysN, Scale(wkN, 7));
            Num seconds = Add(Add(secN, Scale(minN, 60)), Scale(hrN, 3600));
            Num micros = Add(usN, Scale(msN, 1000));

            BigInteger d;
            BigInteger s;
            double daysecondsfrac;
            if (days.IsFloat)
            {
                double dayWhole = Math.Truncate(days.F);
                double dayfrac = days.F - dayWhole;
                double dsWhole = Math.Truncate(dayfrac * 86400.0);
                daysecondsfrac = dayfrac * 86400.0 - dsWhole;
                s = (long)dsWhole;
                d = new BigInteger(dayWhole);
            }
            else
            {
                daysecondsfrac = 0.0;
                d = days.I;
                s = 0;
            }

            double secondsfrac;
            BigInteger secInt;
            if (seconds.IsFloat)
            {
                double secWhole = Math.Truncate(seconds.F);
                secondsfrac = (seconds.F - secWhole) + daysecondsfrac;
                secInt = new BigInteger(secWhole);
            }
            else
            {
                secondsfrac = daysecondsfrac;
                secInt = seconds.I;
            }

            BigInteger qd, rs;
            DivModFloor(secInt, 86400, out qd, out rs);
            d += qd;
            s += rs;

            double usdouble = secondsfrac * 1e6;

            BigInteger micro;
            if (micros.IsFloat)
            {
                BigInteger microInt = RoundToBig(micros.F + usdouble);
                BigInteger sec2, micro2;
                DivModFloor(microInt, 1000000, out sec2, out micro2);
                BigInteger days2, sec2b;
                DivModFloor(sec2, 86400, out days2, out sec2b);
                d += days2;
                s += sec2b;
                micro = micro2;
            }
            else
            {
                BigInteger sec2, micro2;
                DivModFloor(micros.I, 1000000, out sec2, out micro2);
                BigInteger days2, sec2b;
                DivModFloor(sec2, 86400, out days2, out sec2b);
                d += days2;
                s += sec2b;
                micro = RoundToBig((double)micro2 + usdouble);
            }

            // Final carrying of micro into seconds, then seconds into days.
            BigInteger sec3, usFinal;
            DivModFloor(micro, 1000000, out sec3, out usFinal);
            s += sec3;
            BigInteger days3, sFinal;
            DivModFloor(s, 86400, out days3, out sFinal);
            d += days3;

            if (d > 999999999 || d < -999999999)
                throw Raise.Overflow(ctx, "days=" + d.ToString(CultureInfo.InvariantCulture) + "; must have magnitude <= 999999999");

            return FromNormalized(ctx, (int)d, (int)sFinal, (int)usFinal);
        }

        // ---- component parsing (int/bool -> exact, float -> promoted) ----

        private struct Num
        {
            public bool IsFloat;
            public BigInteger I;
            public double F;
        }

        // Small-int component for the fast constructor path: MISSING/bool/long-backed int with |v| <= 1e6.
        private static bool SmallComp(ScriptValue v, out long value)
        {
            if (ReferenceEquals(v, ArgSpec.MISSING))
            {
                value = 0;
                return true;
            }
            IntValue iv = v as IntValue;
            if (iv != null && iv.IsSmall && iv.Small >= -1000000 && iv.Small <= 1000000)
            {
                value = iv.Small;
                return true;
            }
            BoolValue bv = v as BoolValue;
            if (bv != null)
            {
                value = bv.Value ? 1 : 0;
                return true;
            }
            value = 0;
            return false;
        }

        private static Num Comp(EvalContext ctx, ScriptValue v, int idx)
        {
            if (ReferenceEquals(v, ArgSpec.MISSING))
                return new Num { IsFloat = false, I = BigInteger.Zero };
            IntValue iv = v as IntValue;
            if (iv != null)
                return new Num { IsFloat = false, I = iv.Value };
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return new Num { IsFloat = false, I = bv.Value ? BigInteger.One : BigInteger.Zero };
            FloatValue fv = v as FloatValue;
            if (fv != null)
                return new Num { IsFloat = true, F = fv.Value };
            throw Raise.TypeError(ctx, "unsupported type for timedelta " + CompNames[idx] + " component: " + v.PyTypeName);
        }

        private static void RejectNonFinite(EvalContext ctx, Num n)
        {
            if (!n.IsFloat)
                return;
            if (double.IsNaN(n.F))
                throw Raise.ValueError(ctx, "cannot convert float NaN to integer");
            if (double.IsInfinity(n.F))
                throw Raise.Overflow(ctx, "cannot convert float infinity to integer");
        }

        private static Num Scale(Num n, int factor)
        {
            if (n.IsFloat)
                return new Num { IsFloat = true, F = n.F * factor };
            return new Num { IsFloat = false, I = n.I * factor };
        }

        private static Num Add(Num a, Num b)
        {
            if (a.IsFloat || b.IsFloat)
                return new Num { IsFloat = true, F = AsDouble(a) + AsDouble(b) };
            return new Num { IsFloat = false, I = a.I + b.I };
        }

        private static double AsDouble(Num n) { return n.IsFloat ? n.F : (double)n.I; }

        // ---- arithmetic ----

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            TimeDeltaValue td = other as TimeDeltaValue;
            switch (op)
            {
                case PyBinOp.Add:
                    return td != null ? FromUs(ctx, ToUs() + td.ToUs()) : null;
                case PyBinOp.Sub:
                    if (td == null)
                        return null;
                    return reflected ? FromUs(ctx, td.ToUs() - ToUs()) : FromUs(ctx, ToUs() - td.ToUs());
                case PyBinOp.Mul:
                    return MulByNumber(ctx, other);
                case PyBinOp.TrueDiv:
                    return reflected ? null : TrueDiv(ctx, td, other);
                case PyBinOp.FloorDiv:
                    return reflected ? null : FloorDiv(ctx, td, other);
                case PyBinOp.Mod:
                    if (reflected || td == null)
                        return null;
                    if (td.ToUs().IsZero)
                        throw Raise.ZeroDivision(ctx, "integer division or modulo by zero");
                    return FromUs(ctx, FloorModBig(ToUs(), td.ToUs()));
                default:
                    return null;
            }
        }

        private ScriptValue MulByNumber(EvalContext ctx, ScriptValue other)
        {
            BigInteger n;
            if (TryAsInt(other, out n))
                return FromUs(ctx, ToUs() * n);
            FloatValue f = other as FloatValue;
            if (f != null)
            {
                BigInteger num, den;
                AsIntegerRatio(ctx, f.Value, out num, out den);
                return FromUs(ctx, DivideAndRound(ToUs() * num, den));
            }
            return null;
        }

        private ScriptValue TrueDiv(EvalContext ctx, TimeDeltaValue td, ScriptValue other)
        {
            if (td != null)
            {
                if (td.ToUs().IsZero)
                    throw Raise.ZeroDivision(ctx, "division by zero");
                return PyOps.BinaryOp(PyBinOp.TrueDiv, ctx.Values.Int(ToUs()), ctx.Values.Int(td.ToUs()), ctx);
            }
            BigInteger n;
            if (TryAsInt(other, out n))
            {
                if (n.IsZero)
                    throw Raise.ZeroDivision(ctx, "division by zero");
                return FromUs(ctx, DivideAndRound(ToUs(), n));
            }
            FloatValue f = other as FloatValue;
            if (f != null)
            {
                if (f.Value == 0.0)
                    throw Raise.ZeroDivision(ctx, "division by zero");
                BigInteger num, den;
                AsIntegerRatio(ctx, f.Value, out num, out den);
                return FromUs(ctx, DivideAndRound(den * ToUs(), num));
            }
            return null;
        }

        private ScriptValue FloorDiv(EvalContext ctx, TimeDeltaValue td, ScriptValue other)
        {
            if (td != null)
            {
                if (td.ToUs().IsZero)
                    throw Raise.ZeroDivision(ctx, "integer division or modulo by zero");
                return ctx.Values.Int(FloorDivBig(ToUs(), td.ToUs()));
            }
            BigInteger n;
            if (TryAsInt(other, out n))
            {
                if (n.IsZero)
                    throw Raise.ZeroDivision(ctx, "integer division or modulo by zero");
                return FromUs(ctx, FloorDivBig(ToUs(), n));
            }
            return null;
        }

        protected internal override ScriptValue UnaryOpCore(PyUnaryOp op, EvalContext ctx)
        {
            switch (op)
            {
                case PyUnaryOp.Neg:
                    return FromUs(ctx, -ToUs());
                case PyUnaryOp.Pos:
                    return this;
                default:
                    return null;
            }
        }

        protected internal override ScriptValue AbsCore(EvalContext ctx)
        {
            return Days < 0 ? FromUs(ctx, -ToUs()) : this;
        }

        protected internal override ScriptValue DivmodCore(ScriptValue other, bool reflected, EvalContext ctx)
        {
            TimeDeltaValue td = other as TimeDeltaValue;
            if (reflected || td == null)
                return null;
            BigInteger bUs = td.ToUs();
            if (bUs.IsZero)
                throw Raise.ZeroDivision(ctx, "integer division or modulo by zero");
            BigInteger aUs = ToUs();
            ScriptValue q = ctx.Values.Int(FloorDivBig(aUs, bUs));
            ScriptValue r = FromUs(ctx, FloorModBig(aUs, bUs));
            return ctx.Values.Tuple(new[] { q, r });
        }

        // ---- comparisons / hash / truthiness ----

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            TimeDeltaValue o = other as TimeDeltaValue;
            return o != null && Days == o.Days && Seconds == o.Seconds && Microseconds == o.Microseconds;
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            TimeDeltaValue o = other as TimeDeltaValue;
            if (o == null)
            {
                cmp = 0;
                return false;
            }
            cmp = ToUs().CompareTo(o.ToUs());
            return true;
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            var t = ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(Days), ctx.Values.Int(Seconds), ctx.Values.Int(Microseconds) });
            return PyOps.Hash(t, ctx, 0);
        }

        protected internal override bool IsTruthyCore(EvalContext ctx)
        {
            return !(Days == 0 && Seconds == 0 && Microseconds == 0);
        }

        // ---- str / repr ----

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            int ss = Seconds % 60;
            int mmTotal = Seconds / 60;
            int mm = mmTotal % 60;
            int hh = mmTotal / 60;
            string core = Fmt(hh) + ":" + Pad2(mm) + ":" + Pad2(ss);
            if (Days != 0)
            {
                string plural = Math.Abs(Days) != 1 ? "s" : "";
                core = Fmt(Days) + " day" + plural + ", " + core;
            }
            if (Microseconds != 0)
                core += "." + Microseconds.ToString("000000", CultureInfo.InvariantCulture);
            sb.Append(core);
        }

        // 3.7+ keyword form: only the non-zero fields, `timedelta(0)` when all are zero.
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            var parts = new System.Collections.Generic.List<string>(3);
            if (Days != 0)
                parts.Add("days=" + Fmt(Days));
            if (Seconds != 0)
                parts.Add("seconds=" + Fmt(Seconds));
            if (Microseconds != 0)
                parts.Add("microseconds=" + Fmt(Microseconds));
            sb.Append("datetime.timedelta(" + (parts.Count == 0 ? "0" : string.Join(", ", parts)) + ")");
        }

        private static string Fmt(int n) { return n.ToString(CultureInfo.InvariantCulture); }
        private static string Pad2(int n) { return n.ToString("00", CultureInfo.InvariantCulture); }

        // ---- slots ----

        private static ScriptTypeInfo BuildType()
        {
            var d = new System.Collections.Generic.Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["days"] = SlotDescriptor.MakeProperty("days", (self, c) => c.Values.Int(((TimeDeltaValue)self).Days)),
                ["seconds"] = SlotDescriptor.MakeProperty("seconds", (self, c) => c.Values.Int(((TimeDeltaValue)self).Seconds)),
                ["microseconds"] = SlotDescriptor.MakeProperty("microseconds", (self, c) => c.Values.Int(((TimeDeltaValue)self).Microseconds)),
                ["total_seconds"] = SlotDescriptor.MakeMethod("total_seconds", (self, a, kw, c) => ((TimeDeltaValue)self).TotalSeconds(c)),
            };
            return new ScriptTypeInfo("datetime.timedelta", d);
        }

        private ScriptValue TotalSeconds(EvalContext ctx)
        {
            return PyOps.BinaryOp(PyBinOp.TrueDiv, ctx.Values.Int(ToUs()), ctx.Values.Int(CalendarMath.UsPerSecond), ctx);
        }

        // ---- numeric helpers ----

        private static bool TryAsInt(ScriptValue v, out BigInteger n)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                n = iv.Value;
                return true;
            }
            BoolValue bv = v as BoolValue;
            if (bv != null)
            {
                n = bv.Value ? BigInteger.One : BigInteger.Zero;
                return true;
            }
            n = BigInteger.Zero;
            return false;
        }

        internal static BigInteger FloorDivBig(BigInteger a, BigInteger b)
        {
            BigInteger q, r;
            DivModFloor(a, b, out q, out r);
            return q;
        }

        internal static BigInteger FloorModBig(BigInteger a, BigInteger b)
        {
            BigInteger q, r;
            DivModFloor(a, b, out q, out r);
            return r;
        }

        private static void DivModFloor(BigInteger a, BigInteger b, out BigInteger q, out BigInteger r)
        {
            q = BigInteger.DivRem(a, b, out r);
            if (!r.IsZero && (r.Sign != b.Sign))
            {
                q -= 1;
                r += b;
            }
        }

        // Half-even division of two BigIntegers (port of Lib/datetime.py::_divide_and_round).
        internal static BigInteger DivideAndRound(BigInteger a, BigInteger b)
        {
            BigInteger q, r;
            DivModFloor(a, b, out q, out r);
            BigInteger r2 = r * 2;
            bool greater = b > 0 ? r2 > b : r2 < b;
            if (greater || (r2 == b && !q.IsEven))
                q += 1;
            return q;
        }

        private static BigInteger RoundToBig(double x)
        {
            return new BigInteger(Math.Round(x, MidpointRounding.ToEven));
        }

        // Exact num/den for a finite double (value == num/den). Not reduced (irrelevant for DivideAndRound).
        private static void AsIntegerRatio(EvalContext ctx, double d, out BigInteger num, out BigInteger den)
        {
            if (double.IsNaN(d))
                throw Raise.ValueError(ctx, "cannot convert float NaN to integer");
            if (double.IsInfinity(d))
                throw Raise.Overflow(ctx, "cannot convert float infinity to integer");
            if (d == 0.0)
            {
                num = BigInteger.Zero;
                den = BigInteger.One;
                return;
            }
            long bits = BitConverter.DoubleToInt64Bits(d);
            bool neg = bits < 0;
            int exponent = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (exponent == 0)
                exponent++;
            else
                mantissa |= 0x10000000000000L;
            exponent -= 1075;
            num = mantissa;
            if (neg)
                num = -num;
            den = BigInteger.One;
            if (exponent >= 0)
                num <<= exponent;
            else
                den <<= -exponent;
        }
    }
}
