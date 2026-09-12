namespace Outbridge.PyLite.Syntax
{
    // Shared source position carried by every AST node; visible to the runtime for diagnostics.
    public struct SourceInfo
    {
        public readonly int Line;
        public readonly int Col;
        public SourceInfo(int line, int col) { Line = line; Col = col; }
    }
}
