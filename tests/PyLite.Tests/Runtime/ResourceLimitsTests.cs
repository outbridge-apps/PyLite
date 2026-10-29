using System;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    [Trait("Category", "ResourceLimits")]
    public class ResourceLimitsTests
    {
        [Fact] public void DefaultMaxSteps() => Assert.Equal(200_000_000, ResourceLimits.Default.MaxSteps);

        [Fact]
        public void WithChangesOneFieldOnly()
        {
            var r = ResourceLimits.Default.With(deadlineMs: 5000);
            Assert.Equal(5000, r.DeadlineMs);
            Assert.Equal(ResourceLimits.Default.MaxSteps, r.MaxSteps);
            Assert.Equal(ResourceLimits.Default.MaxAllocBytes, r.MaxAllocBytes);
        }

        [Fact]
        public void ClampBringsPerRunDownToServer()
            => Assert.Equal(20_000, ResourceLimits.Default.With(deadlineMs: 999_999).ClampTo(ResourceLimits.Default).DeadlineMs);

        [Fact]
        public void BatchClampedToDefault()
        {
            var r = ResourceLimits.Batch.ClampTo(ResourceLimits.Default);
            Assert.Equal(20_000, r.DeadlineMs);
            Assert.Equal(268_435_456, r.MaxAllocBytes);
        }

        [Fact] public void ZeroStepsRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => ResourceLimits.Default.With(maxSteps: 0));
        [Fact] public void NegativeAllocRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => ResourceLimits.Default.With(maxAllocBytes: -1));

        [Fact]
        public void TerminatingGraceTakenFromCeiling()
        {
            var perRun = new ResourceLimits(200_000_000, 20_000, 268_435_456, 200, 128, 16_000_000, 5_000_000,
                1_000_000, 16_777_216, 250, 5_000, terminatingGraceSteps: 50_000, terminatingGraceMs: 100);
            Assert.Equal(100_000, perRun.ClampTo(ResourceLimits.Default).TerminatingGraceSteps);
        }

        [Fact]
        public void ClampDefaultToDefaultIsIdentity()
        {
            var r = ResourceLimits.Default.ClampTo(ResourceLimits.Default);
            Assert.Equal(ResourceLimits.Default.MaxSteps, r.MaxSteps);
            Assert.Equal(ResourceLimits.Default.DeadlineMs, r.DeadlineMs);
            Assert.Equal(ResourceLimits.Default.MaxIntBits, r.MaxIntBits);
        }

        [Fact]
        public void ClampToEngineHardMaximum()
        {
            var x = ResourceLimits.Default.With(deadlineMs: 400_000);
            Assert.Equal(300_000, x.ClampTo(x).DeadlineMs);
        }

        [Fact]
        public void DataDepthClampedToEngineHardMaximum()
        {
            var x = ResourceLimits.Default.With(maxDataDepth: 1_000_000);
            Assert.Equal(10_000, x.ClampTo(x).MaxDataDepth);
        }

        [Fact]
        public void IntBitsClampedToEngineHardMaximum()
        {
            var x = ResourceLimits.Default.With(maxIntBits: int.MaxValue);
            Assert.Equal(4_000_000, x.ClampTo(x).MaxIntBits);
        }

        [Fact]
        public void ProfileDeadlines()
        {
            Assert.Equal(5_000, ResourceLimits.Interactive.DeadlineMs);
            Assert.Equal(60_000, ResourceLimits.Batch.DeadlineMs);
        }
    }
}
