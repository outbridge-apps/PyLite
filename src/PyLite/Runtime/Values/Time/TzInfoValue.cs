using System.Numerics;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // The tzinfo contract the datetime types consume. A fixed-offset timezone answers the same offset for
    // every instant; a ZoneInfo answers per instant (wall time on the way out, UTC instant on the way in).
    // A time object asks with dt == null and a zone answers null there, exactly as CPython's ZoneInfo does.
    internal abstract class TzInfoValue : ScriptValue
    {
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        // utcoffset(dt) in microseconds, for the wall-clock fields of dt (null => utcoffset(None)).
        internal abstract long? OffsetUsAt(DateTimeValue dt);

        // The offset in force at a UTC instant (naive microseconds since 0001-01-01): fromutc's question.
        internal abstract long OffsetUsForUtc(long utcNaiveUs);

        internal abstract string TzNameAt(DateTimeValue dt);

        // dst(dt) in microseconds; null => dst() answers None (a fixed-offset timezone).
        internal abstract long? DstUsAt(DateTimeValue dt);

        // The fold of the wall time a UTC instant lands on: 1 when that wall time is the second of two
        // occurrences (the ambiguous hour after a fall-back), else 0. Fixed offsets never fold.
        internal virtual int FoldForUtc(long utcNaiveUs) { return 0; }

        internal TimeDeltaValue OffsetTd(EvalContext ctx, long us)
        {
            return TimeDeltaValue.FromUs(ctx, (BigInteger)us);
        }
    }
}
