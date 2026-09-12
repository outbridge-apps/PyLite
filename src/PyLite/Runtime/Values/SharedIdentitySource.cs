using System.Threading;

namespace Outbridge.PyLite.Runtime.Values
{
    // Identity source for the process-wide immutable builtins template. Per-run values
    // get positive ids from ValueFactory (++_identitySeq); shared template values get negative ids so the
    // two ranges never collide. Identity only feeds hashing; Equals stays ReferenceEquals, so a hash clash
    // between a shared builtin and a shared type is harmless.
    internal static class SharedIdentitySource
    {
        private static long _n;
        internal static long Next() { return Interlocked.Decrement(ref _n); }
    }
}
