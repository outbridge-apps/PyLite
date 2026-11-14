namespace Outbridge.PyLite.Syntax
{
    // Immutable, thread-safe compile result. A host cache keys it by the source string itself: a hash of its UTF-8
    // bytes merges sources that differ only in lone surrogates.
    public sealed class CompiledScript
    {
        public ResolvedProgram Program { get; }
        public string ScriptName { get; }
        public int DialectVersion { get; }
        internal long TokenCount { get; }   // what the engine's cache weighs the program by

        internal CompiledScript(ResolvedProgram program, string scriptName, int dialectVersion, long tokenCount)
        {
            TokenCount = tokenCount;
            Program = program;
            ScriptName = scriptName;
            DialectVersion = dialectVersion;
        }
    }
}
