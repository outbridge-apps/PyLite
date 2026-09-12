using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // The immutable default builtins template. Built once, lazily; HostFunctionTable
    // .CreateDefault() hands out mutable copies. Only the exception classes are wired here in this package
    // phase; free-function builtins and type constructors are appended by later T-BLT tasks through the
    // Builtins partial class (Register* below).
    internal static class BuiltinsTable
    {
        private static readonly Lazy<Dictionary<string, ScriptValue>> _template =
            new Lazy<Dictionary<string, ScriptValue>>(BuildDefaultTemplate);

        // A fresh mutable copy of the immutable template (the source of HostFunctionTable.CreateDefault).
        internal static Dictionary<string, ScriptValue> CreateCopy()
        {
            return new Dictionary<string, ScriptValue>(_template.Value, StringComparer.Ordinal);
        }

        private static Dictionary<string, ScriptValue> BuildDefaultTemplate()
        {
            var d = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);

            // Exception classes: calling one constructs an ExceptionValue (kwargs ignored — exceptions take
            // positional args only in v1). The TypeValue carries the PyExceptionType for except/isinstance.
            foreach (PyExceptionType pt in PyExceptionTypes.All)
            {
                PyExceptionType captured = pt;
                BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(captured, args);
                d[pt.Name] = TypeValue.Make(pt.Name, ctor, null, null, captured);
            }

            Builtins.RegisterAll(d);
            return d;
        }

        // --- "no" builtins: present syntactically, refused with a specific NameError ----

        private static readonly HashSet<string> Unsupported = new HashSet<string>(StringComparer.Ordinal)
        {
            "eval", "exec", "compile", "open", "input", "globals", "locals", "vars", "delattr",
            "bytearray", "memoryview", "complex", "super", "staticmethod", "classmethod", "property",
            "__import__", "help", "id", "object"
            // NOTE: "ascii" was here until the P2 gap-closure made it a real builtin (Builtins.Object).
            // NOTE: "getattr"/"hasattr"/"setattr" were here until the parity pass registered them
            // (Builtins.Object); they reach only the slot-table whitelist, so the sandbox is unchanged.
            // NOTE: "bytes" was here until bytes became a real type (registered by Builtins.RegisterConvert)
            // Environment.GetGlobal now resolves it before this list, so it must NOT be listed as unsupported.
        };

        internal static bool TryGetUnsupportedMessage(string name, out string message)
        {
            if (Unsupported.Contains(name))
            {
                message = "name '" + name + "' is not defined: this builtin is not supported in this environment";
                return true;
            }
            message = null;
            return false;
        }

        // --- hint tails for known-absent v1 attributes ----

        private static readonly Dictionary<string, string> _hints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // NOTE: "encode"/"decode" hints removed once str.encode / bytes.decode became real.
            // format_map/translate/maketrans/isidentifier/isprintable/fromkeys removed — real since the
            // P2 gap-closure (StrMethods / dict statics).
        };

        internal static IReadOnlyDictionary<string, string> KnownMissingAttributeHints { get { return _hints; } }
    }
}
