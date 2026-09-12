using System;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Hosting
{
    // The sanctioned way for a host function (a HostFunctionTable builtin) to raise an error the SCRIPT can
    // catch with try/except. A host delegate that throws an arbitrary C# exception becomes
    // an uncatchable HostError; throwing one of these instead produces an ordinary script exception that the
    // evaluator's except dispatcher handles (and, if uncaught, surfaces as RuntimeError — not HostError).
    public static class ScriptExceptions
    {
        // Build (do NOT throw — the host code writes `throw ScriptExceptions.PyError(...)`). pythonType must be
        // a concrete script exception name; the budget/engine channels cannot be forged.
        public static Exception PyError(string pythonType, string message)
        {
            if (pythonType == null)
            {
                throw new ArgumentNullException(nameof(pythonType));
            }
            PyExceptionType type = PyExceptionTypes.GetByName(pythonType);
            if (type == null)
            {
                throw new PyLiteEngineException("unknown script exception type '" + pythonType + "'");
            }
            ScriptValue[] args = message == null
                ? Array.Empty<ScriptValue>()
                : new ScriptValue[] { new StrValue(message) };
            return new ScriptException(new ExceptionValue(type, args));
        }

        public static Exception ValueError(string message)
        {
            return PyError("ValueError", message);
        }

        public static Exception TypeError(string message)
        {
            return PyError("TypeError", message);
        }

        public static Exception RuntimeError(string message)
        {
            return PyError("RuntimeError", message);
        }

        // The message text becomes repr(key) (== 'key' for a string), matching CPython's KeyError.
        public static Exception KeyError(string key)
        {
            return PyError("KeyError", key);
        }
    }
}
