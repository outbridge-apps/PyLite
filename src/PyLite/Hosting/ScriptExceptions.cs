using System;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// How a host function raises an exception the script can catch. Any other exception a host function throws
    /// ends the run as an uncatchable <see cref="ScriptErrorKind.HostError"/>; one of these is an ordinary script
    /// exception (and, uncaught, a <see cref="ScriptErrorKind.RuntimeError"/>). The host writes
    /// <c>throw ScriptExceptions.ValueError("...")</c>.
    /// </summary>
    public static class ScriptExceptions
    {
        /// <summary>
        /// An exception of a script exception class, to throw. The limits' and the engine's own failures cannot be
        /// raised this way.
        /// </summary>
        /// <param name="pythonType">The class name, as scripts spell it ("ValueError", "LookupError", ...).</param>
        /// <param name="message">Its text; null for none.</param>
        /// <exception cref="ArgumentNullException"><paramref name="pythonType"/> is null.</exception>
        /// <exception cref="PyLiteEngineException">No script exception class has that name.</exception>
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
            return new HostScriptException(new ExceptionValue(type, args));   // copied into each run that raises it
        }

        /// <summary>A <c>ValueError</c> to throw.</summary>
        public static Exception ValueError(string message)
        {
            return PyError("ValueError", message);
        }

        /// <summary>A <c>TypeError</c> to throw.</summary>
        public static Exception TypeError(string message)
        {
            return PyError("TypeError", message);
        }

        /// <summary>A <c>RuntimeError</c> to throw.</summary>
        public static Exception RuntimeError(string message)
        {
            return PyError("RuntimeError", message);
        }

        /// <summary>A <c>KeyError</c> for a missing str key, to throw; its text is the key's repr, as CPython's.</summary>
        public static Exception KeyError(string key)
        {
            return PyError("KeyError", key);
        }
    }
}
