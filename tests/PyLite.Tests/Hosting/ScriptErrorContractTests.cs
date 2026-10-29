using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    [Trait("Category", "Sandbox")]
    public class ScriptErrorContractTests
    {
        [Fact]
        public void LongMessageTruncatedWithSuffix()
        {
            var e = new ScriptError(ScriptErrorKind.RuntimeError, "X", new string('a', 5000), 0, 0, null, null);
            Assert.True(e.Message.Length <= EngineHardLimits.MaxErrorMessageChars + 16);
            Assert.EndsWith("[truncated]", e.Message);
        }

        [Fact]
        public void KindNumericValuesAreTheXppContract()
        {
            Assert.Equal(0, (int)ScriptErrorKind.Ok);
            Assert.Equal(1, (int)ScriptErrorKind.SyntaxError);
            Assert.Equal(2, (int)ScriptErrorKind.RuntimeError);
            Assert.Equal(3, (int)ScriptErrorKind.BudgetExceeded);
            Assert.Equal(4, (int)ScriptErrorKind.HostError);
            Assert.Equal(5, (int)ScriptErrorKind.EngineFault);
        }

        [Fact]
        public void RuntimeTypePreserved()
        {
            var e = new ScriptError(ScriptErrorKind.RuntimeError, "KeyError", "k", 3, 0, null, null);
            Assert.Equal("KeyError", e.PythonType);
        }

        [Fact]
        public void BudgetErrorCarriesLimitNameNotPythonType()
        {
            var e = new ScriptError(ScriptErrorKind.BudgetExceeded, null, "over", 0, 0, "MaxSteps", "");
            Assert.Equal("MaxSteps", e.LimitName);
            Assert.Null(e.PythonType);
        }

        [Fact]
        public void ShortMessageUnchanged()
        {
            var e = new ScriptError(ScriptErrorKind.RuntimeError, "ValueError", "small", 1, 2, null, null);
            Assert.Equal("small", e.Message);
        }
    }

    [Trait("Category", "Sandbox")]
    public class RunStatisticsTests
    {
        [Fact]
        public void DefaultsAreZeroAndNotNull()
        {
            var s = new RunStatistics();
            Assert.Equal(0L, s.Steps);
            Assert.Equal(0, s.PeakCallDepth);
            Assert.False(s.TerminatingEntered);
        }

        [Fact]
        public void FieldsRoundTrip()
        {
            var s = new RunStatistics { Steps = 42, PeakCallDepth = 3, ModulesImported = 2, TerminatingEntered = true };
            Assert.Equal(42L, s.Steps);
            Assert.Equal(3, s.PeakCallDepth);
            Assert.Equal(2, s.ModulesImported);
            Assert.True(s.TerminatingEntered);
        }
    }
}
