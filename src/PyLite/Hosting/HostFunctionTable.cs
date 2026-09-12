using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Hosting
{
    // Name -> builtin value table. Mutable while the host assembles it, then frozen at
    // run start so a script cannot see the table change under it. Ordinal keys. A CreateDefault() copy is
    // never shared between runs (each Run clones + freezes its own).
    public sealed class HostFunctionTable
    {
        private readonly Dictionary<string, ScriptValue> _entries;
        private bool _frozen;

        internal HostFunctionTable(Dictionary<string, ScriptValue> entries)
        {
            _entries = entries;
        }

        // An always-frozen empty table (used by low-level tests and the null-host path).
        public static readonly HostFunctionTable Empty = MakeEmpty();

        private static HostFunctionTable MakeEmpty()
        {
            var t = new HostFunctionTable(new Dictionary<string, ScriptValue>(StringComparer.Ordinal));
            t._frozen = true;
            return t;
        }

        // A fresh mutable copy of the immutable default template (49 v1 builtins + exception classes).
        public static HostFunctionTable CreateDefault()
        {
            return new HostFunctionTable(BuiltinsTable.CreateCopy());
        }

        public void Set(string name, BuiltinDelegate fn)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("builtin name must be non-empty", nameof(name));
            CheckMutable();
            _entries[name] = BuiltinFunctionValue.MakeHost(name, fn);   // host-provided => fenced by HostCallGuard
        }

        public void SetValue(string name, ScriptValue value)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("builtin name must be non-empty", nameof(name));
            CheckMutable();
            _entries[name] = value;
        }

        public void Remove(string name)
        {
            CheckMutable();
            _entries.Remove(name);
        }

        public bool TryGet(string name, out ScriptValue value)
        {
            return _entries.TryGetValue(name, out value);
        }

        public bool Contains(string name)
        {
            return _entries.ContainsKey(name);
        }

        public int Count { get { return _entries.Count; } }

        public HostFunctionTable Clone()
        {
            return new HostFunctionTable(new Dictionary<string, ScriptValue>(_entries, StringComparer.Ordinal));
        }

        internal void Freeze()
        {
            _frozen = true;
        }

        internal bool IsFrozen { get { return _frozen; } }

        private void CheckMutable()
        {
            if (_frozen)
                throw new InvalidOperationException("HostFunctionTable is frozen (a run is using it)");
        }

        // The Evaluator consults this on a name-resolution miss to give the "not supported" NameError
        // instead of the plain one.
        internal static bool TryGetUnsupportedMessage(string name, out string message)
        {
            return BuiltinsTable.TryGetUnsupportedMessage(name, out message);
        }

        // Attribute-name -> hint tail, appended by the core when raising AttributeError for a known-absent
        // v1 method.
        public static IReadOnlyDictionary<string, string> KnownMissingAttributeHints
        {
            get { return BuiltinsTable.KnownMissingAttributeHints; }
        }
    }

    // print callback: the host receives each printed line WITHOUT the trailing newline.
    public delegate void PrintDelegate(string line);
}
