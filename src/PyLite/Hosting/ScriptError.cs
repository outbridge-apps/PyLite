using System.Globalization;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>The kind of a run's outcome, as one code. The numeric values are part of the host contract.</summary>
    public enum ScriptErrorKind
    {
        /// <summary>No error.</summary>
        Ok = 0,
        /// <summary>The script did not compile.</summary>
        SyntaxError = 1,
        /// <summary>The script raised an exception it did not handle; <see cref="ScriptError.PythonType"/> names it.</summary>
        RuntimeError = 2,
        /// <summary>A limit ended the run; <see cref="ScriptError.LimitName"/> names it.</summary>
        BudgetExceeded = 3,
        /// <summary>A host function, provider or callback failed, the host misused the API, or the engine refused to start the
        /// run (<see cref="ScriptError.LimitName"/> names the refusal).</summary>
        HostError = 4,
        /// <summary>An internal engine error (a defect).</summary>
        EngineFault = 5,
    }

    /// <summary>
    /// Why a run failed: immutable, built by the engine. The message is capped when it is made.
    /// </summary>
    public sealed class ScriptError
    {
        private const string TruncatedSuffix = " …[truncated]";

        /// <summary>The kind of failure.</summary>
        public ScriptErrorKind Kind { get; }

        /// <summary>
        /// The exception's class name: "ValueError", "KeyError", … for <see cref="ScriptErrorKind.RuntimeError"/>,
        /// "SyntaxError", "IndentationError" or "TabError" for <see cref="ScriptErrorKind.SyntaxError"/>; else null.
        /// </summary>
        public string PythonType { get; }

        /// <summary>The error text: at most 2,048 characters, then a marker when cut; a surrogate pair stays whole.</summary>
        public string Message { get; }

        /// <summary>The script line, or 0 when unknown.</summary>
        public int Line { get; }

        /// <summary>The column, or 0 when unknown.</summary>
        public int Column { get; }

        /// <summary>The limit that ended the run ("MaxSteps", "DeadlineMs", …) for <see cref="ScriptErrorKind.BudgetExceeded"/>, or the
        /// refusal of a run that never started ("FreeMemory", "ThreadStart", "CircuitBreaker") for <see cref="ScriptErrorKind.HostError"/>;
        /// null otherwise.</summary>
        public string LimitName { get; }

        /// <summary>The traceback of the script's own frames in CPython's format, or null when there is none.</summary>
        public string ScriptTraceback { get; }

        internal ScriptError(ScriptErrorKind kind, string pythonType, string message,
            int line, int column, string limitName, string traceback)
        {
            Kind = kind;
            PythonType = pythonType;
            Message = Cap(message);
            Line = line;
            Column = column;
            LimitName = limitName;
            ScriptTraceback = traceback;
        }

        private static string Cap(string message)
        {
            if (message == null)
            {
                return "";
            }
            if (message.Length > EngineHardLimits.MaxErrorMessageChars)
            {
                int cut = EngineHardLimits.MaxErrorMessageChars;
                if (char.IsHighSurrogate(message[cut - 1]))
                    cut--;   // a pair stays whole
                return message.Substring(0, cut) + TruncatedSuffix;
            }
            return message;
        }

        /// <summary>The kind, the place, the exception type and the text, for logs.</summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} at {1}:{2}: [{3}] {4}",
                Kind, Line, Column, PythonType ?? "-", Message);
        }
    }
}
