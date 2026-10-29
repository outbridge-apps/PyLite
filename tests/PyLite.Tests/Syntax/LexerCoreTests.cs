using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class LexerCoreTests
    {
        [Fact] public void Empty() => Assert.Equal("EndOfFile", L.Kinds(""));

        [Fact]
        public void SimpleAssign_SynthesizedNewline()
        {
            Assert.Equal("Name Assign IntLiteral NewLine EndOfFile", L.Kinds("x=1"));
            var t = L.Go("x=1");
            Assert.Equal(1, t[0].Line); Assert.Equal(1, t[0].Col);
            Assert.Equal(2, t[1].Col);
            Assert.Equal(3, t[2].Col);
            Assert.Equal(4, t[3].Col); // synthesized NewLine at EOF position
        }

        [Fact] public void CommentOnly() => Assert.Equal("EndOfFile", L.Kinds("# comment only\n\n"));
        [Fact] public void TwoLines() => Assert.Equal("Name NewLine Name NewLine EndOfFile", L.Kinds("a\nb\n"));
        [Fact] public void Semicolon() => Assert.Equal("Name Semicolon Name NewLine EndOfFile", L.Kinds("a;b\n"));

        [Fact]
        public void LineContinuation()
        {
            Assert.Equal("Name Assign IntLiteral Plus IntLiteral NewLine EndOfFile", L.Kinds("x = 1 + \\\n2\n"));
            var t = L.Go("x = 1 + \\\n2\n");
            Assert.Equal(2, t[4].Line); Assert.Equal(1, t[4].Col); // Int(2) at 2:1
        }

        [Fact]
        public void BadContinuation()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("x = \\ 1\n"));
            Assert.Equal("unexpected character after line continuation character", e.Message);
            Assert.Equal(1, e.Line); Assert.Equal(6, e.Col);
        }

        [Fact]
        public void ContinuationAtEof()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("x = \\"));
            Assert.Equal("unexpected EOF while parsing", e.Message);
        }

        [Fact]
        public void NewlineInsideBracketsSuppressed()
            => Assert.Equal("LeftParen IntLiteral Comma IntLiteral RightParen NewLine EndOfFile", L.Kinds("(1,\n2)\n"));

        [Fact]
        public void CrLfCrMix()
        {
            var t = L.Go("a\r\nb\rc\n");
            Assert.Equal("Name NewLine Name NewLine Name NewLine EndOfFile", L.Kinds("a\r\nb\rc\n"));
            Assert.Equal(2, t[2].Line); // b at 2:1
            Assert.Equal(3, t[4].Line); // c at 3:1
        }

        [Fact]
        public void BomSkipped()
        {
            var t = L.Go("﻿x=1\n");
            Assert.Equal(TokenKind.Name, t[0].Kind);
            Assert.Equal(1, t[0].Col);
        }

        [Fact]
        public void NullByte()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("x\0"));
            Assert.Equal("source code string cannot contain null bytes", e.Message);
            Assert.Equal(1, e.Line); Assert.Equal(2, e.Col);
        }

        [Fact]
        public void CommentBeforeNewline()
            => Assert.Equal("Name Assign IntLiteral NewLine Name NewLine EndOfFile", L.Kinds("x = 1  # comment\ny\n"));

        [Fact]
        public void SourceTooLarge()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(new string(' ', 524289)));
            Assert.Equal("source too large (limit is 524288 characters)", e.Message);
        }

        [Fact]
        public void ExtraCloseParen_NoNegativeDepth()
            => Assert.Equal("RightParen NewLine EndOfFile", L.Kinds(")"));

        [Fact]
        public void RepeatedNextAfterEof()
        {
            var lex = new Lexer("", SyntaxLimits.Default);
            for (int i = 0; i < 1000; i++)
                Assert.Equal(TokenKind.EndOfFile, lex.Next().Kind);
        }
    }
}
