using System.Globalization;

namespace Outbridge.PyLite.Hosting
{
    // The flat error code for a host that maps results to numbers. Numeric values are the host contract.
    public enum ScriptErrorKind
    {
        Ok = 0,
        SyntaxError = 1,
        RuntimeError = 2,
        BudgetExceeded = 3,
        HostError = 4,
        EngineFault = 5,
    }

    // Immutable host-facing error description. Built only by the
    // package-04 ScriptErrorBuilder and by hosting code. Message is capped at construction.
    public sealed class ScriptError
    {
        private const string TruncatedSuffix = " …[truncated]";

        public ScriptErrorKind Kind { get; }
        public string PythonType { get; }      // non-null only for RuntimeError ("ValueError", "KeyError", …)
        public string Message { get; }         // <= MaxErrorMessageChars (+ suffix), Ordinal
        public int Line { get; }               // 0 if unknown
        public int Column { get; }             // 0 if unknown
        public string LimitName { get; }       // BudgetExceeded only ("MaxSteps", "DeadlineMs", …)
        public string ScriptTraceback { get; } // script frames only, CPython format; null if none

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
                return message.Substring(0, EngineHardLimits.MaxErrorMessageChars) + TruncatedSuffix;
            }
            return message;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} at {1}:{2}: [{3}] {4}",
                Kind, Line, Column, PythonType ?? "-", Message);
        }
    }
}
