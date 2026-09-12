using System.Collections.Generic;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        // migration texts. Contract with the lexer: it passes @, ':' and bytes/complex
        // through as tokens so the parser can point at the exact position; it rejects
        // underscore-in-number and \N{} itself.
        private static readonly Dictionary<string, string> ForbiddenTable = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "yield", "generators are not supported in this dialect; use a generator expression" },
            { "with",  "'with' statements are not supported in this dialect; use try/finally" },
            { "async", "async is not supported in this dialect" },
            { "await", "await is not supported in this dialect" },
            { "import-star", "from ... import * is not supported in this dialect; import names explicitly" },
            { "relative", "relative imports are not supported in this dialect" },
        };

        private PySyntaxErrorException Forbidden(string key) { return Error(ForbiddenTable[key]); }
    }
}
