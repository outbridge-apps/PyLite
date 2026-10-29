using System;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    [Trait("Category", "Sandbox")]
    public class RunnerThreadTests
    {
        private static RunResult Run(Func<RunResult> work, Func<Outbridge.PyLite.Runtime.EvalContext> ctx = null)
        {
            var rt = new RunnerThread(work, ctx ?? (() => null));
            rt.Start();
            Assert.True(rt.WaitCompleted(5000));
            return rt.TakeResult();
        }

        [Fact]
        public void NormalResultHandedBack()
        {
            var ok = RunResult.Success(null, null, "out", new RunStatistics());
            var rt = new RunnerThread(() => ok, () => null);
            rt.Start();
            Assert.True(rt.WaitCompleted(5000));
            Assert.Same(ok, rt.TakeResult());
        }

        [Fact]
        public void EngineAbortBecomesBudgetExceeded()
        {
            var ctx = Rt.Ctx();
            var r = Run(() => { throw new EngineAbort(EngineAbortKind.Steps, "MaxSteps", 1, 2); }, () => ctx);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
            Assert.Equal("MaxSteps", r.Error.LimitName);
        }

        [Fact]
        public void ScriptExceptionBecomesRuntimeError()
        {
            var ctx = Rt.Ctx();
            var r = Run(() => { throw Raise.ValueError(ctx, "bad"); }, () => ctx);
            Assert.Equal(ScriptErrorKind.RuntimeError, r.Error.Kind);
            Assert.Equal("ValueError", r.Error.PythonType);
            Assert.Equal("bad", r.Error.Message);
        }

        [Fact]
        public void UnexpectedExceptionBecomesEngineFault()
        {
            var ctx = Rt.Ctx();
            var rt = new RunnerThread(() => { throw new NullReferenceException("secret"); }, () => ctx);
            rt.Start();
            Assert.True(rt.WaitCompleted(5000));
            Assert.Equal(ScriptErrorKind.EngineFault, rt.TakeResult().Error.Kind);
            Assert.Equal("internal engine error", rt.TakeResult().Error.Message);
            Assert.NotNull(rt.EngineFault);
        }

        [Fact]
        public void WrappedAbortUnwrappedAtBarrier()
        {
            var ctx = Rt.Ctx();
            var abort = new EngineAbort(EngineAbortKind.Memory, "MaxAllocBytes", 1, 2);
            var r = Run(() => { throw new InvalidOperationException("w", abort); }, () => ctx);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
        }

        // The last line of defense: failure CONSTRUCTION itself throws (here: the ctx provider dies,
        // standing in for an OOM while building the pretty error). The barrier must hand back the
        // pre-built fault result — never let anything escape the thread (process kill on net472).
        [Fact]
        public void FailureConstructionThrowingStillYieldsFaultResult()
        {
            var rt = new RunnerThread(
                () => { throw new InvalidOperationException("primary"); },
                () => { throw new OutOfMemoryException("while building the failure"); });
            rt.Start();
            Assert.True(rt.WaitCompleted(5000));
            RunResult r = rt.TakeResult();
            Assert.Equal(ScriptErrorKind.EngineFault, r.Error.Kind);
            Assert.Equal("internal engine error", r.Error.Message);
        }

        // A hostile exception whose members throw when read must not breach the barrier either.
        private sealed class EvilException : Exception
        {
            public override string Message { get { throw new InvalidOperationException("evil message"); } }
            public override string StackTrace { get { throw new InvalidOperationException("evil stack"); } }
        }

        [Fact]
        public void HostileExceptionMembersCannotBreachTheBarrier()
        {
            var ctx = Rt.Ctx();
            var rt = new RunnerThread(() => { throw new EvilException(); }, () => ctx);
            rt.Start();
            Assert.True(rt.WaitCompleted(5000));
            Assert.Equal(ScriptErrorKind.EngineFault, rt.TakeResult().Error.Kind);
        }

        [Fact]
        public void SlowWorkTimesOutThenCompletes()
        {
            var gate = new ManualResetEventSlim(false);
            var rt = new RunnerThread(() =>
            {
                gate.Wait();
                return RunResult.Success(null, null, null, new RunStatistics());
            }, () => null);
            rt.Start();
            Assert.False(rt.WaitCompleted(150));   // still running
            gate.Set();
            Assert.True(rt.WaitCompleted(5000));    // finishes on its own
        }
    }
}
