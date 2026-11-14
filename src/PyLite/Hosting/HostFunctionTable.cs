using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// The builtins a run sees, name to value (ordinal names). Mutable while the host assembles it; each run takes a
    /// frozen copy, so a script never sees it change.
    /// </summary>
    public sealed class HostFunctionTable
    {
        private readonly Dictionary<string, ScriptValue> _entries;
        private bool _frozen;

        internal HostFunctionTable(Dictionary<string, ScriptValue> entries)
        {
            _entries = entries;
        }

        /// <summary>An empty table that cannot be changed.</summary>
        public static readonly HostFunctionTable Empty = MakeEmpty();

        private static HostFunctionTable MakeEmpty()
        {
            var t = new HostFunctionTable(new Dictionary<string, ScriptValue>(StringComparer.Ordinal));
            t._frozen = true;
            return t;
        }

        /// <summary>A new, changeable copy of the engine's default builtins and exception classes.</summary>
        public static HostFunctionTable CreateDefault()
        {
            return new HostFunctionTable(BuiltinsTable.CreateCopy());
        }

        /// <summary>Adds or replaces a function the host implements; its calls are fenced like every host call.</summary>
        /// <exception cref="ArgumentException">The name is null or empty.</exception>
        /// <exception cref="InvalidOperationException">The table is frozen.</exception>
        public void Set(string name, BuiltinDelegate fn)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("builtin name must be non-empty", nameof(name));
            if (fn == null)
                throw new ArgumentNullException(nameof(fn));
            CheckMutable();
            _entries[name] = BuiltinFunctionValue.MakeHost(name, fn);   // host-provided => fenced by HostCallGuard
        }

        /// <summary>Adds or replaces a value under a builtin name.</summary>
        /// <exception cref="ArgumentException">The name is null or empty.</exception>
        /// <exception cref="InvalidOperationException">The table is frozen.</exception>
        public void SetValue(string name, ScriptValue value)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("builtin name must be non-empty", nameof(name));
            if (value == null)   // a null builtin was an engine fault at its first use; Remove takes a name out
                throw new ArgumentNullException(nameof(value));
            CheckMutable();
            _entries[name] = value;
        }

        /// <summary>Takes a name out, so scripts get a NameError for it.</summary>
        /// <exception cref="InvalidOperationException">The table is frozen.</exception>
        public void Remove(string name)
        {
            CheckMutable();
            _entries.Remove(name);
        }

        /// <summary>The value under a name, if there is one.</summary>
        public bool TryGet(string name, out ScriptValue value)
        {
            return _entries.TryGetValue(name, out value);
        }

        /// <summary>Whether a name is in the table.</summary>
        public bool Contains(string name)
        {
            return _entries.ContainsKey(name);
        }

        /// <summary>The number of names.</summary>
        public int Count { get { return _entries.Count; } }

        /// <summary>A changeable copy.</summary>
        public HostFunctionTable Clone()
        {
            return new HostFunctionTable(new Dictionary<string, ScriptValue>(_entries, StringComparer.Ordinal));
        }

        internal void Freeze()
        {
            _frozen = true;
        }

        private void CheckMutable()
        {
            if (_frozen)
                throw new InvalidOperationException("HostFunctionTable is frozen");
        }

        // The Evaluator consults this on a name-resolution miss to give the "not supported" NameError
        // instead of the plain one.
        internal static bool TryGetUnsupportedMessage(string name, out string message)
        {
            return BuiltinsTable.TryGetUnsupportedMessage(name, out message);
        }

        /// <summary>
        /// Method name to the hint the engine appends to the AttributeError of a method the dialect leaves out.
        /// </summary>
        public static IReadOnlyDictionary<string, string> KnownMissingAttributeHints
        {
            get { return BuiltinsTable.KnownMissingAttributeHints; }
        }
    }

    /// <summary>Receives one printed line, without its trailing newline.</summary>
    public delegate void PrintDelegate(string line);
}
