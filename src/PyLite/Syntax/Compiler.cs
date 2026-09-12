using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal static class DialectConstants
    {
        // Bumped on any breaking grammar/semantics change so the host SHA-256 cache invalidates.
        public const int Version = 1;
    }

    // The single Lexer -> Parser -> Resolver entry point. Runs on the CALLING thread (ScriptEngine.Compile,
    // cache), so every recursive pass stack-probes and converts a near-overflow into a SyntaxError.
    // SyntaxErrorException propagates upward without rephrasing.
    internal static class Compiler
    {
        public static CompiledScript Compile(string source, string scriptName, SyntaxLimits limits)
        {
            var lexer = new Lexer(source, limits);
            ModuleNode module = Parser.ParseModule(lexer, limits);
            ResolvedProgram resolved = Resolver.Resolve(module, limits);
            return new CompiledScript(resolved, scriptName, DialectConstants.Version);
        }
    }
}
