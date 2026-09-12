using System;
using System.Collections.Generic;

namespace Outbridge.PyLite.Runtime
{
    // Per-RUN csv state: the dialects a script registered and its field size limit. Neither can live in
    // the module dictionary, which is frozen at engine build and shared by every run — a script that
    // registered a dialect or raised the limit would otherwise be changing the next run's csv. Held here,
    // both start fresh with each run and are collected with it.
    //
    // The registry values are the csv module's own dialect records; this layer only stores them, so the
    // type stays object rather than dragging a module type into the runtime.
    public sealed class CsvState
    {
        public const int DefaultFieldLimit = 131072;   // CPython's _csv default

        public int FieldLimit = DefaultFieldLimit;

        private Dictionary<string, object> _dialects;

        public bool TryGetDialect(string name, out object dialect)
        {
            dialect = null;
            return _dialects != null && _dialects.TryGetValue(name, out dialect);
        }

        public void Register(string name, object dialect)
        {
            if (_dialects == null)
                _dialects = new Dictionary<string, object>(StringComparer.Ordinal);
            _dialects[name] = dialect;
        }

        public bool Unregister(string name)
        {
            return _dialects != null && _dialects.Remove(name);
        }

        public IEnumerable<string> Names
        {
            get { return _dialects == null ? (IEnumerable<string>)Array.Empty<string>() : _dialects.Keys; }
        }
    }
}
