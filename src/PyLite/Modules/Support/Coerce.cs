using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // Typed argument coercions with unified TypeError texts. Every throwing path takes
    // ctx (Raise needs the budget); the impl doc omits it but the errors are script exceptions.
    internal static class Coerce
    {
        // int|bool -> BigInteger; otherwise "'{t}' object cannot be interpreted as an integer".
        internal static BigInteger ToIndex(EvalContext ctx, ScriptValue v, string context)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
                return iv.Value;
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? BigInteger.One : BigInteger.Zero;
            throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
        }

        // int|bool -> int, magnitude clamped to the int range. For optional size/offset/count args a
        // non-int must be a script TypeError (not an unchecked (IntValue)v cast) AND a huge int must
        // not overflow the (int) cast — both would otherwise leak a C# exception as an engine fault.
        internal static int ToInt32(EvalContext ctx, ScriptValue v, string context)
        {
            BigInteger b = ToIndex(ctx, v, context);
            if (b > int.MaxValue)
                return int.MaxValue;
            if (b < int.MinValue)
                return int.MinValue;
            return (int)b;
        }

        // int|bool -> int as CPython's C-level converters answer: a value past the range is an OverflowError
        // with PyLong_AsSsize_t's text ("C ssize_t") or PyLong_AsInt's ("C int"), never an unchecked cast.
        // ToInt32 above is the other policy, for a parameter that pure Python would take as a number.
        internal static int ToSsize(EvalContext ctx, ScriptValue v, string context)
        {
            return ToCRange(ctx, ToIndex(ctx, v, context), "C ssize_t");
        }

        internal static int ToSsize(EvalContext ctx, BigInteger n)
        {
            return ToCRange(ctx, n, "C ssize_t");
        }

        internal static int ToCInt(EvalContext ctx, ScriptValue v, string context)
        {
            return ToCRange(ctx, ToIndex(ctx, v, context), "C int");
        }

        private static int ToCRange(EvalContext ctx, BigInteger n, string cType)
        {
            if (n > int.MaxValue || n < int.MinValue)
                throw Raise.Overflow(ctx, "Python int too large to convert to " + cType);
            return (int)n;
        }

        internal static bool TryToBigIndex(ScriptValue v, out BigInteger value)
        {
            IntValue iv = v as IntValue;
            if (iv != null) { value = iv.Value; return true; }
            BoolValue bv = v as BoolValue;
            if (bv != null) { value = bv.Value ? BigInteger.One : BigInteger.Zero; return true; }
            value = BigInteger.Zero;
            return false;
        }

        // Python negative-index normalization; clamps into [0, len] (used by insert and slice-like paths).
        internal static int ToClampedIndex(BigInteger i, int len)
        {
            if (i < 0)
            {
                i += len;
                if (i < 0)
                    return 0;
            }
            else if (i > len)
            {
                return len;
            }
            return (int)i;
        }

        // int|float|bool -> double; int overflow past double range is an OverflowError (CPython text).
        internal static double ToDouble(EvalContext ctx, ScriptValue v, string context)
        {
            FloatValue fv = v as FloatValue;
            if (fv != null)
                return fv.Value;
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                double d = (double)iv.Value;
                if (double.IsInfinity(d))
                    throw Raise.Overflow(ctx, "long int too large to convert to float");
                return d;
            }
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? 1.0 : 0.0;
            throw Raise.TypeError(ctx, context);
        }

        // ADJUST_INDICES (Objects/stringlib/find.h). None -> 0/len; end clamped above by len, start is
        // NOT clamped above (an out-of-range start yields an empty slice via start > end). Non-int (except
        // None/bool) -> TypeError.
        internal static void AdjustIndices(EvalContext ctx, ScriptValue start, ScriptValue end, int len,
            out int startOut, out int endOut)
        {
            long e = ToSliceLong(ctx, end, len);   // None -> len
            if (e > len) e = len;
            else if (e < 0) { e += len; if (e < 0) e = 0; }

            long s;
            if (start == null || start.Kind == ValueKind.None)
            {
                s = 0;
            }
            else
            {
                s = ToSliceLong(ctx, start, len);   // ToSliceLong caps a >len start at len+1 (no int overflow)
                if (s < 0) { s += len; if (s < 0) s = 0; }
            }
            startOut = (int)s;
            endOut = (int)e;
        }

        // A slice bound: None -> len sentinel handled by the caller; int/bool -> clamped long; else TypeError.
        private static long ToSliceLong(EvalContext ctx, ScriptValue v, int len)
        {
            if (v == null || v.Kind == ValueKind.None)
                return len;
            BigInteger b;
            if (!TryToBigIndex(v, out b))
                throw Raise.TypeError(ctx, "slice indices must be integers or None or have an __index__ method");
            if (b > len) return len + 1L;      // any value > len collapses to "past end"
            if (b < long.MinValue / 2) return long.MinValue / 2;
            return (long)b;
        }
    }
}
