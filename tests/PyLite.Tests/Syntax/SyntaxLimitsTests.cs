using System;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "ResourceLimits")]
    public class SyntaxLimitsTests
    {
        [Fact]
        public void Defaults_MatchSpec()
        {
            var d = SyntaxLimits.Default;
            Assert.Equal(524288, d.MaxSourceChars);
            Assert.Equal(500000, d.MaxTokens);
            Assert.Equal(4300, d.MaxNumericLiteralDigits);
            Assert.Equal(524288, d.MaxStringLiteralChars);
            Assert.Equal(3, d.MaxFStringNesting);
            Assert.Equal(100, d.MaxIndentLevels);
            Assert.Equal(64, d.MaxExprDepth);
            Assert.Equal(64, d.MaxStmtDepth);
            Assert.Equal(1000, d.MaxAstDepth);
        }

        [Fact]
        public void Default_Validates()
        {
            SyntaxLimits.Default.Validate();
        }

        [Fact]
        public void MinimumSource_Validates()
        {
            new SyntaxLimits { MaxSourceChars = 1024 }.Validate();
        }

        [Theory]
        [InlineData("MaxTokens")]
        [InlineData("MaxFStringNesting")]
        public void BelowMinimum_Throws(string field)
        {
            var lim = new SyntaxLimits();
            if (field == "MaxTokens")
                lim.MaxTokens = 0;
            else
                lim.MaxFStringNesting = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => lim.Validate());
        }
    }
}
