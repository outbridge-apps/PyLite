using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // A call's keyword arguments: parallel arrays, no dictionary — a typical call has
    // few kwargs. Names are Ordinal-unique (the builder dedups keywords against ** merges via a seen-set;
    // explicit-only dups are a parse error). Empty when _names == null.
    public struct KwArgs
    {
        private readonly string[] _names;
        private readonly ScriptValue[] _values;

        public static readonly KwArgs Empty = new KwArgs(null, null);

        private KwArgs(string[] names, ScriptValue[] values)
        {
            _names = names;
            _values = values;
        }

        public int Count { get { return _names == null ? 0 : _names.Length; } }
        public string NameAt(int i) { return _names[i]; }
        public ScriptValue ValueAt(int i) { return _values[i]; }

        public bool TryGet(string name, out ScriptValue value)
        {
            if (_names != null)
            {
                for (int i = 0; i < _names.Length; i++)
                {
                    if (_names[i] == name)
                    {
                        value = _values[i];
                        return true;
                    }
                }
            }
            value = null;
            return false;
        }

        internal static KwArgs FromArrays(string[] names, ScriptValue[] values)
        {
            if (names == null || names.Length == 0)
            {
                return Empty;
            }
            return new KwArgs(names, values);
        }
    }
}
