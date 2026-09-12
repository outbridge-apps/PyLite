using System;

namespace Outbridge.PyLite.Syntax
{
    // The only error channel of the Syntax layer (lexer + parser + resolver).
    internal sealed class PySyntaxErrorException : Exception
    {
        public readonly string PythonType; // "SyntaxError" | "IndentationError" | "TabError"
        public readonly int Line;          // 1-based
        public readonly int Col;           // 1-based

        public PySyntaxErrorException(string pythonType, string message, int line, int col)
            : base(message)
        {
            PythonType = pythonType;
            Line = line;
            Col = col;
        }

        public static PySyntaxErrorException Syntax(string message, int line, int col)
        {
            return new PySyntaxErrorException("SyntaxError", message, line, col);
        }

        public static PySyntaxErrorException Indentation(string message, int line, int col)
        {
            return new PySyntaxErrorException("IndentationError", message, line, col);
        }

        public static PySyntaxErrorException Tab(int line, int col)
        {
            return new PySyntaxErrorException("TabError", "inconsistent use of tabs and spaces in indentation", line, col);
        }
    }
}
