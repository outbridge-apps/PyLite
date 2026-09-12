using System;

namespace Outbridge.PyLite.Syntax
{
    internal sealed class SyntaxLimits
    {
        public static readonly SyntaxLimits Default = new SyntaxLimits();

        public int MaxSourceChars          { get; set; } = 524288; // 512 K UTF-16 units
        public int MaxTokens               { get; set; } = 500000;
        public int MaxNumericLiteralDigits { get; set; } = 4300;   // digits after the base prefix, all bases
        public int MaxStringLiteralChars   { get; set; } = 524288; // cooked length of one literal/chunk
        public int MaxFStringNesting       { get; set; } = 3;      // frames, outermost = 1
        public int MaxIndentLevels         { get; set; } = 100;    // MAXINDENT
        public int MaxExprDepth            { get; set; } = 64;     // recursive-descent nesting (parser C# stack)
        public int MaxStmtDepth            { get; set; } = 64;     // used by the parser
        // Max AST depth of a single expression. Unlike MaxExprDepth (recursive-descent nesting), this bounds
        // the depth of the tree BUILT by the iterative fold/trailer loops (1+1+…, a[0][0]…, a.b.b…), which
        // those loops grow without recursing. Keeps the resolver/const-folder/evaluator tree-walks off a
        // StackOverflow. Higher than MaxExprDepth: a flat chain costs ~1 downstream frame per level, whereas
        // recursive-descent nesting costs ~15 parser frames per level.
        public int MaxAstDepth             { get; set; } = 1000;

        // Host bug, not a script bug — called by ScriptEngine.Compile before the lexer is created.
        public void Validate()
        {
            Check(MaxSourceChars, 1024, nameof(MaxSourceChars));
            Check(MaxTokens, 128, nameof(MaxTokens));
            Check(MaxNumericLiteralDigits, 32, nameof(MaxNumericLiteralDigits));
            Check(MaxStringLiteralChars, 256, nameof(MaxStringLiteralChars));
            Check(MaxFStringNesting, 1, nameof(MaxFStringNesting));
            Check(MaxIndentLevels, 4, nameof(MaxIndentLevels));
            Check(MaxExprDepth, 8, nameof(MaxExprDepth));
            Check(MaxStmtDepth, 8, nameof(MaxStmtDepth));
            Check(MaxAstDepth, 16, nameof(MaxAstDepth));
        }

        private static void Check(int value, int min, string name)
        {
            if (value < min)
                throw new ArgumentOutOfRangeException(name, value, "must be >= " + min);
        }
    }
}
