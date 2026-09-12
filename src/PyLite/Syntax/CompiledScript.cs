namespace Outbridge.PyLite.Syntax
{
    // Immutable, thread-safe compile result; cacheable by the host keyed by SHA-256(source) + dialect
    // version + limits hash.
    public sealed class CompiledScript
    {
        public ResolvedProgram Program { get; }
        public string ScriptName { get; }
        public int DialectVersion { get; }

        internal CompiledScript(ResolvedProgram program, string scriptName, int dialectVersion)
        {
            Program = program;
            ScriptName = scriptName;
            DialectVersion = dialectVersion;
        }
    }
}
