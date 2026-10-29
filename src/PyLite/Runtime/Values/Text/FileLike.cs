using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // An in-memory file (io.StringIO, io.BytesIO) is its own iterator, as in CPython: iter(f) is f
    // and next(f) reads one line, so `next(f)` can skip a header line before a loop.
    internal interface ILineFile
    {
        bool NextLine(EvalContext ctx, out ScriptValue line);
    }

    internal static class FileArgs
    {
        // An optional size argument: None reads as -1; a non-integer is a TypeError in CPython's text.
        internal static long OptionalSize(EvalContext ctx, ScriptValue[] a, int index)
        {
            if (a.Length <= index || a[index].Kind == ValueKind.None)
                return -1;
            BigInteger n;
            if (!Coerce.TryToBigIndex(a[index], out n))
                throw Raise.TypeError(ctx, "argument should be integer or None, not '" + a[index].PyTypeName + "'");
            return Ssize(ctx, n);
        }

        internal static long Ssize(EvalContext ctx, BigInteger n)
        {
            if (n > long.MaxValue || n < long.MinValue)
                throw Raise.Overflow(ctx, "Python int too large to convert to C ssize_t");
            return (long)n;
        }
    }
}
