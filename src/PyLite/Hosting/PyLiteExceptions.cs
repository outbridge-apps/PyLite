using System;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// Thrown only by <see cref="ScriptEngine.Compile(string, string)"/> on a syntax error. <c>Run(string, …)</c> never throws it:
    /// it returns a <see cref="RunResult"/> whose error kind is <see cref="ScriptErrorKind.SyntaxError"/>.
    /// </summary>
    public sealed class PyLiteSyntaxException : Exception
    {
        /// <summary>The syntax error: its text, line and column.</summary>
        public ScriptError Error { get; }

        internal PyLiteSyntaxException(ScriptError error)
            : base(error != null ? error.Message : "syntax error")
        {
            Error = error;
        }
    }

    /// <summary>
    /// The host used the engine API wrongly (not a script error), e.g. an invalid input or output variable name.
    /// </summary>
    public sealed class PyLiteEngineException : Exception
    {
        internal PyLiteEngineException(string message)
            : base(message)
        {
        }
    }
}
