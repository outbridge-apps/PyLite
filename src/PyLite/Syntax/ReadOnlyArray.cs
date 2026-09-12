using System.Collections;
using System.Collections.Generic;

namespace Outbridge.PyLite.Syntax
{
    // A thin frozen wrapper over T[] implementing IReadOnlyList<T>.
    internal sealed class ReadOnlyArray<T> : IReadOnlyList<T>
    {
        private readonly T[] _items;
        public ReadOnlyArray(T[] items) { _items = items; }
        public T this[int i] { get { return _items[i]; } }
        public int Count { get { return _items.Length; } }
        public IEnumerator<T> GetEnumerator() { return ((IEnumerable<T>)_items).GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return _items.GetEnumerator(); }
    }

    internal static class AstList
    {
        public static IReadOnlyList<T> Freeze<T>(List<T> src) { return new ReadOnlyArray<T>(src.ToArray()); }
        public static IReadOnlyList<T> FreezeArray<T>(T[] src) { return new ReadOnlyArray<T>(src); }
        public static IReadOnlyList<T> Empty<T>() { return EmptyHolder<T>.Value; }
        private static class EmptyHolder<T> { public static readonly IReadOnlyList<T> Value = new ReadOnlyArray<T>(new T[0]); }
    }
}
