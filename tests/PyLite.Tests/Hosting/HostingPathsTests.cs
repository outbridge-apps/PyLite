using System;
using System.Diagnostics;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // Review 6, T-3: hosting paths no test ran. Alone in their pass: the wait's window grows with the run threads of the
    // process, and the start fault reaches every run while it is set.
    [Collection("HeapSensitive")]
    [Trait("Category", "Sandbox")]
    public class HostingPathsTests
    {
        private static readonly ResourceLimits Short = ResourceLimits.Default.With(deadlineMs: 50);

        // A run in the engine's own code with a clock past deadline + grace and no progress is not hung: it is waited
        // for until the give-up, not leaked after one window.
        [Fact]
        public void EngineCodeWithoutProgressIsWaitedForUntilTheGiveUp()
        {
            var thread = new RunnerThread(() => { Thread.Sleep(4000); return null; }, () => null);
            var holder = new[] { Rt.Ctx() };
            thread.Start();
            Thread.Sleep(400);   // the context's own clock passes deadline + grace
            var watch = Stopwatch.StartNew();
            Assert.False(ScriptEngine.WaitForRun(thread, holder, Short, 1500, 200));
            Assert.True(watch.ElapsedMilliseconds >= 1400, watch.ElapsedMilliseconds.ToString());
        }

        // Round 4 review: a window longer than the time left before the give-up is cut to it (a window can be 2 minutes).
        [Fact]
        public void TheGiveUpCapsALongWindow()
        {
            var thread = new RunnerThread(() => { Thread.Sleep(8000); return null; }, () => null);
            var holder = new[] { Rt.Ctx() };
            thread.Start();
            Thread.Sleep(400);
            var watch = Stopwatch.StartNew();
            Assert.False(ScriptEngine.WaitForRun(thread, holder, Short, 1500, 6000));
            Assert.InRange(watch.ElapsedMilliseconds, 1400, 4000);
        }

        [Fact]
        public void HostCodeWithoutProgressIsHungAfterOneWindow()
        {
            var thread = new RunnerThread(() =>
            {
                int m = RunnerThread.EnterHost(null);
                Thread.Sleep(4000);
                RunnerThread.LeaveHost(m);
                return null;
            }, () => null);
            var holder = new[] { Rt.Ctx() };
            thread.Start();
            Thread.Sleep(400);
            var watch = Stopwatch.StartNew();
            Assert.False(ScriptEngine.WaitForRun(thread, holder, Short, 10000, 200));
            Assert.True(watch.ElapsedMilliseconds < 2500, watch.ElapsedMilliseconds.ToString());
        }

        // Steps that still advance inside host code (a host function re-entering the engine) are progress.
        [Fact]
        public void HostCodeThatStillStepsIsNotHung()
        {
            EvalContext ctx = Rt.Ctx();
            var thread = new RunnerThread(() =>
            {
                int m = RunnerThread.EnterHost(ctx);
                var w = Stopwatch.StartNew();
                while (w.ElapsedMilliseconds < 1500)
                {
                    ctx.Budget.Step();
                    Thread.Sleep(10);
                }
                RunnerThread.LeaveHost(m);
                return null;
            }, () => null);
            thread.Start();
            Thread.Sleep(400);
            Assert.True(ScriptEngine.WaitForRun(thread, new[] { ctx }, Short, 10000, 200));
        }

        // Script code a host function calls back (a __repr__ through ScriptValue.Repr) is the engine's code: stalled
        // without progress it is waited for until the give-up, not leaked after one window.
        [Fact]
        public void ScriptCodeTheHostCallsBackIsNotHostCode()
        {
            EvalContext ctx = Rt.Ctx();
            var thread = new RunnerThread(() =>
            {
                int m = RunnerThread.EnterHost(ctx);
                ctx.CallDepth++;
                Thread.Sleep(4000);
                ctx.CallDepth--;
                RunnerThread.LeaveHost(m);
                return null;
            }, () => null);
            thread.Start();
            Thread.Sleep(400);
            var watch = Stopwatch.StartNew();
            Assert.False(ScriptEngine.WaitForRun(thread, new[] { ctx }, Short, 1500, 200));
            Assert.True(watch.ElapsedMilliseconds >= 1400, watch.ElapsedMilliseconds.ToString());
        }

        // Round 4 review: an abort thrown inside a call-back's EnterCall left CallDepth one high; a host that swallowed it
        // and then hangs is still leaked on time (it waited for the 45 s sleep).
        [Fact]
        public void AHostThatSwallowsACallBackAbortAndHangsIsLeakedOnTime()
        {
            var release = new ManualResetEventSlim(false);
            var table = HostFunctionTable.CreateDefault();
            table.Set("probe", (s, a, kw, c) =>
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        a[0].Repr(c);
                    }
                    catch (Exception)
                    {
                    }
                }
                release.Wait(45000);
                return c.Values.None;
            });
            var started = Stopwatch.StartNew();
            RunResult r = new ScriptEngine(new EngineOptions()).Run(
                "class C:\n    def __repr__(self):\n        while True:\n            pass\nprobe(C())\n",
                new RunRequest { Builtins = table, Limits = ResourceLimits.Default.With(deadlineMs: 1000) });
            long ms = started.ElapsedMilliseconds;
            release.Set();
            Assert.Contains("leaked", r.Error.Message);
            Assert.InRange(ms, 1000, 25000);
        }

        // Host calls nest through call-backs: each level is host code again, and leaving one restores the outer state.
        [Fact]
        public void HostCallsNestThroughCallBacks()
        {
            EvalContext ctx = Rt.Ctx();
            var seen = new System.Collections.Generic.List<bool>();
            RunnerThread thread = null;
            thread = new RunnerThread(() =>
            {
                seen.Add(thread.InHostCode);
                int outer = RunnerThread.EnterHost(ctx);
                seen.Add(thread.InHostCode);
                ctx.CallDepth++;
                seen.Add(thread.InHostCode);
                int inner = RunnerThread.EnterHost(ctx);
                int clock = RunnerThread.EnterHost(null);
                seen.Add(thread.InHostCode);
                RunnerThread.LeaveHost(clock);
                seen.Add(thread.InHostCode);
                RunnerThread.LeaveHost(inner);
                seen.Add(thread.InHostCode);
                ctx.CallDepth--;
                seen.Add(thread.InHostCode);
                RunnerThread.LeaveHost(outer);
                seen.Add(thread.InHostCode);
                return null;
            }, () => null);
            thread.Start();
            Assert.True(thread.WaitCompleted(10000));
            Assert.Equal(new[] { false, true, false, true, true, false, true, false }, seen.ToArray());
        }

        // A method a host builds with SlotDescriptor is host code: a hang in it is a leak on time (here, not among the
        // parallel tests: the window grows with the run threads of the process).
        [Fact]
        public void AHungHostBuiltMethodIsLeakedOnTime()
        {
            var release = new ManualResetEventSlim(false);
            var table = HostFunctionTable.CreateDefault();
            table.Set("make", (s, a, kw, c) => c.Values.BoundMethod(c.Values.Int(1),
                Outbridge.PyLite.Runtime.Values.SlotDescriptor.MakeMethod("block", (self, args, k, cx) => { release.Wait(); return cx.Values.None; })));
            var started = Stopwatch.StartNew();
            RunResult r = new ScriptEngine(new EngineOptions()).Run("make()()\n",
                new RunRequest { Builtins = table, Limits = ResourceLimits.Default.With(deadlineMs: 100) });
            long ms = started.ElapsedMilliseconds;
            release.Set();
            Assert.Contains("leaked", r.Error.Message);
            Assert.InRange(ms, 4000, 20000);
        }

        // The thread that finished as the wait gave up is not counted as a leak: its own epilogue got there first.
        [Fact]
        public void AThreadThatFinishedIsNotRegisteredAsALeak()
        {
            var breaker = new LeakBreaker(4);
            var thread = new RunnerThread(() => null, () => null, breaker);
            thread.Start();
            Assert.True(thread.WaitCompleted(10000));
            Assert.False(breaker.RegisterLeak(thread));
            Assert.Equal(0, breaker.LeakedThreads);
        }

        // A run whose thread cannot start is a HostError naming the refusal; nothing of it ran, and the count of
        // admitted stacks is back to zero.
        [Fact]
        public void ARunWhoseThreadCannotStartIsARefusal()
        {
            var engine = new ScriptEngine(new EngineOptions());
            RunnerThread.StartFaultForTests = () => new OutOfMemoryException();
            RunResult r;
            try
            {
                r = engine.Run("print('ran')\n", new RunRequest());
            }
            finally
            {
                RunnerThread.StartFaultForTests = null;
            }
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Equal("ThreadStart", r.Error.LimitName);
            Assert.Equal("", r.PrintOutput);
            Assert.Equal(0, ScriptEngine.UnstartedStackBytes);
            Assert.True(engine.Run("x = 1\n", new RunRequest()).Succeeded);
        }
    }
}
