using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Outbridge.PyLite.Syntax
{
    // Identity comparer for per-node resolver dictionaries: two structurally equal nodes
    // (e.g. two NameNode "x") must remain distinct keys.
    public sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        private ReferenceEqualityComparer() { }
        bool IEqualityComparer<object>.Equals(object a, object b) { return ReferenceEquals(a, b); }
        int IEqualityComparer<object>.GetHashCode(object o) { return RuntimeHelpers.GetHashCode(o); }
    }
}
