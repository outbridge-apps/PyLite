using System;

namespace Outbridge.PyLite.Hosting
{
    // Thrown only by ScriptEngine.Compile on a syntax error. Run(string,…) never throws this —
    // it returns a RunResult with Kind==SyntaxError instead.
    public sealed class PyLiteSyntaxException : Exception
    {
        public ScriptError Error { get; }

        internal PyLiteSyntaxException(ScriptError error)
            : base(error != null ? error.Message : "syntax error")
        {
            Error = error;
        }
    }

    // Incorrect engine API use by the host (not a script error): e.g. an invalid input/output variable name.
    public sealed class PyLiteEngineException : Exception
    {
        internal PyLiteEngineException(string message)
            : base(message)
        {
        }
    }
}
