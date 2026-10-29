using System;
using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    [Trait("Category", "ResourceLimits")]
    public class EngineAbortTests
    {
        private static EngineAbort Abort() => new EngineAbort(EngineAbortKind.Steps, "MaxSteps", 100, 101);

        private static Exception WrapN(Exception inner, int n)
        {
            Exception cur = inner;
            for (int i = 0; i < n; i++)
                cur = new InvalidOperationException("w", cur);
            return cur;
        }

        [Fact] public void UnwrapDirect() { var a = Abort(); Assert.Same(a, EngineAbort.TryUnwrap(a)); }

        [Fact]
        public void UnwrapSingleWrapper()
        { var a = Abort(); Assert.Same(a, EngineAbort.TryUnwrap(new InvalidOperationException("w", a))); }

        [Fact]
        public void UnwrapFromAggregate()
        { var a = Abort(); Assert.Same(a, EngineAbort.TryUnwrap(new AggregateException(new InvalidOperationException("io"), a))); }

        [Fact] public void UnwrapDepth3() { var a = Abort(); Assert.Same(a, EngineAbort.TryUnwrap(WrapN(a, 3))); }
        [Fact] public void UnwrapDepth9IsNull() { var a = Abort(); Assert.Null(EngineAbort.TryUnwrap(WrapN(a, 9))); }
        [Fact] public void UnwrapNoInnerIsNull() => Assert.Null(EngineAbort.TryUnwrap(new InvalidOperationException("no inner")));

        [Fact]
        public void NotAScriptException()
            => Assert.Equal(typeof(Exception), typeof(EngineAbort).BaseType);

        [Fact]
        public void DeadlineMessage()
            => Assert.Equal("script exceeded time limit (20000 ms)",
                new EngineAbort(EngineAbortKind.Deadline, "DeadlineMs", 20000, 20001).Message);

        [Fact]
        public void RecursionMessage()
            => Assert.Equal("maximum recursion depth exceeded",
                new EngineAbort(EngineAbortKind.Recursion, "MaxCallDepth", 200, 201).Message);

        [Fact]
        public void UnwrapNestedAggregateFlatten()
        {
            var a = Abort();
            var nested = new AggregateException(new AggregateException(a));
            Assert.Same(a, EngineAbort.TryUnwrap(nested));
        }

        [Fact]
        public void MemoryMessageIncludesLimitName()
            => Assert.Equal("script exceeded memory limit (MaxStrChars=16000000)",
                new EngineAbort(EngineAbortKind.Memory, "MaxStrChars", 16000000, 16000001).Message);
    }
}
