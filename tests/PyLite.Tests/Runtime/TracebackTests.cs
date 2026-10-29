using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    [Trait("Category", "Compatibility")]
    public class TracebackTests
    {
        private static ExceptionValue Exc(EvalContext ctx, string message)
        {
            return Raise.ValueError(ctx, message).Value;
        }

        [Fact]
        public void SingleFrameText()
        {
            var ctx = Rt.Ctx();
            var e = Exc(ctx, "bad value");
            TracebackBuilder.AppendFrame(e, "<module>", 5);
            string text = TracebackBuilder.BuildText(e, e.GetMessageText(ctx));
            Assert.Equal(
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 5, in <module>\n" +
                "ValueError: bad value", text);
        }

        [Fact]
        public void TwoFramesRenderOutermostFirst()
        {
            var ctx = Rt.Ctx();
            var e = Exc(ctx, "boom");
            TracebackBuilder.AppendFrame(e, "f", 7);          // innermost accumulated first
            TracebackBuilder.AppendFrame(e, "<module>", 2);   // call-boundary frame appended after
            string text = TracebackBuilder.BuildText(e, e.GetMessageText(ctx));
            Assert.Equal(
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 2, in <module>\n" +
                "  File \"<script>\", line 7, in f\n" +
                "ValueError: boom", text);
        }

        [Fact]
        public void TruncatesAt32FramesWithOmittedLine()
        {
            var ctx = Rt.Ctx();
            var e = Exc(ctx, "x");
            for (int i = 0; i < 41; i++)
            {
                TracebackBuilder.AppendFrame(e, "fn", i);
            }
            Assert.True(e.TracebackTruncated);
            Assert.Equal(9, e.TracebackDropped);
            string text = TracebackBuilder.BuildText(e, "x");
            Assert.Contains("[... 9 more frames omitted ...]", text);
            int fileLines = text.Split('\n').Length;
            // header + omitted-line + 32 frames + final line = 35 lines
            Assert.Equal(35, fileLines);
        }

        [Fact]
        public void BuildScriptError_BudgetAbort()
        {
            var ctx = Rt.Ctx();
            ctx.CurrentLine = 12;
            var abort = new EngineAbort(EngineAbortKind.Steps, "MaxSteps", 100, 101);
            ScriptError err = ScriptErrorBuilder.Build(abort, ctx);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, err.Kind);
            Assert.Null(err.PythonType);
            Assert.Equal("MaxSteps", err.LimitName);
            Assert.Equal("", err.ScriptTraceback);
            Assert.Equal(12, err.Line);
            Assert.Equal("script exceeded step limit (100)", err.Message);
        }

        [Fact]
        public void BuildScriptError_ScriptException()
        {
            var ctx = Rt.Ctx();
            var se = Raise.ValueError(ctx, "x");
            ScriptError err = ScriptErrorBuilder.Build(se, ctx);
            Assert.Equal(ScriptErrorKind.RuntimeError, err.Kind);
            Assert.Equal("ValueError", err.PythonType);
            Assert.Equal("x", err.Message);
        }

        [Fact]
        public void BuildScriptError_WrappedAbortUnwrapped()
        {
            var ctx = Rt.Ctx();
            var abort = new EngineAbort(EngineAbortKind.Memory, "MaxAllocBytes", 1000, 1100);
            var wrapped = new InvalidOperationException("w", abort);
            ScriptError err = ScriptErrorBuilder.Build(wrapped, ctx);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, err.Kind);
            Assert.Equal("MaxAllocBytes", err.LimitName);
        }

        [Fact]
        public void BuildScriptError_EngineFaultHidesData()
        {
            var ctx = Rt.Ctx();
            ScriptError err = ScriptErrorBuilder.Build(new NullReferenceException("secret"), ctx);
            Assert.Equal(ScriptErrorKind.EngineFault, err.Kind);
            Assert.Equal("internal engine error", err.Message);
            Assert.Null(err.PythonType);
        }

        [Fact]
        public void MessageTruncatedToCap()
        {
            var ctx = Rt.Ctx();
            var se = Raise.Make(ctx, PyExceptionTypes.ValueError, new string('a', 100_000));
            ScriptError err = ScriptErrorBuilder.Build(se, ctx);
            Assert.Equal(EngineHardLimits.MaxErrorMessageChars, err.Message.Length);
        }

        // ---------- end-to-end: frames collected by RunBody / RunModule during unwind ----------

        private static ScriptError RunErr(string src)
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run(src, new RunRequest());
            Assert.False(r.Succeeded);
            return r.Error;
        }

        [Fact]
        public void UncaughtDeepErrorBuildsFrameChain()
        {
            ScriptError err = RunErr(
                "def inner(x):\n    return 10 / x\ndef outer():\n    return inner(0)\n\nr = outer()\n");
            Assert.Equal(
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 6, in <module>\n" +
                "  File \"<script>\", line 4, in outer\n" +
                "  File \"<script>\", line 2, in inner\n" +
                "ZeroDivisionError: division by zero", err.ScriptTraceback);
            Assert.Equal(2, err.Line);   // innermost frame = the raise site
        }

        // The callee's last line must not leak into an error raised later in the same caller statement.
        [Fact]
        public void LineRestoredAfterCallReturns()
        {
            ScriptError err = RunErr("def f():\n    return 1\n\nr = f() + undefined_name\n");
            Assert.Equal(4, err.Line);
            Assert.Contains("line 4, in <module>", err.ScriptTraceback);
        }

        // C functions get no frame of their own (CPython): only the caller's frame appears.
        [Fact]
        public void BuiltinRaiseHasCallerFrameOnly()
        {
            ScriptError err = RunErr("x = 1\nv = int('abc')\n");
            Assert.Equal(2, err.Line);
            Assert.Equal(
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 2, in <module>\n" +
                "ValueError: invalid literal for int() with base 10: 'abc'", err.ScriptTraceback);
        }

        [Fact]
        public void LambdaFrameUsesBodyLine()
        {
            ScriptError err = RunErr("g = lambda: 1 // 0\nr = g()\n");
            Assert.Equal(1, err.Line);
            Assert.Contains("line 1, in <lambda>", err.ScriptTraceback);
            Assert.Contains("line 2, in <module>", err.ScriptTraceback);
        }

        // A bare `raise` re-raises with the ORIGINAL raise line, not the raise statement's line.
        [Fact]
        public void BareReRaiseKeepsOriginalRaiseLine()
        {
            ScriptError err = RunErr(
                "def f():\n    try:\n        1 // 0\n    except ZeroDivisionError:\n        raise\nr = f()\n");
            Assert.Equal(3, err.Line);
            Assert.Contains("line 3, in f", err.ScriptTraceback);
            Assert.Contains("line 6, in <module>", err.ScriptTraceback);
        }

        [Fact]
        public void DeepRecursionCapsFramesEndToEnd()
        {
            // 41 script frames + <module> = 42; innermost 32 kept, 10 dropped.
            ScriptError err = RunErr(
                "def r(n):\n    return 10 // 0 if n == 0 else r(n - 1)\nq = r(40)\n");
            Assert.Contains("[... 10 more frames omitted ...]", err.ScriptTraceback);
            Assert.Equal(2, err.Line);
        }
        [Fact]
        public void CauseChainRendersInnerFirst()
        {
            var ctx = Rt.Ctx();
            var inner = Exc(ctx, "inner");
            TracebackBuilder.AppendFrame(inner, "f", 2);
            var outer = Raise.TypeError(ctx, "outer").Value;
            outer.Cause = inner;
            outer.SuppressContext = true;
            TracebackBuilder.AppendFrame(outer, "<module>", 6);
            string text = TracebackBuilder.BuildText(outer, "outer", e => e.GetMessageText(ctx));
            Assert.Equal(
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 2, in f\n" +
                "ValueError: inner\n" +
                "\nThe above exception was the direct cause of the following exception:\n\n" +
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 6, in <module>\n" +
                "TypeError: outer", text);
        }

        [Fact]
        public void ContextChainIsPrintedUnlessSuppressed()
        {
            var ctx = Rt.Ctx();
            var first = Exc(ctx, "first");   // never propagated: no frames, so no header
            var second = Raise.RuntimeError(ctx, "second").Value;
            second.Context = first;
            TracebackBuilder.AppendFrame(second, "<module>", 4);
            string text = TracebackBuilder.BuildText(second, "second", e => e.GetMessageText(ctx));
            Assert.Equal(
                "ValueError: first\n" +
                "\nDuring handling of the above exception, another exception occurred:\n\n" +
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 4, in <module>\n" +
                "RuntimeError: second", text);
            second.SuppressContext = true;
            Assert.StartsWith("Traceback", TracebackBuilder.BuildText(second, "second", e => e.GetMessageText(ctx)));
        }

        // An engine-raised exception caught in the frame that raised it never reaches a frame exit, so
        // the except handler stamps its raise site; otherwise it would print as a bare "KeyError: 'k'"
        // with no Traceback header once it became a __context__.
        [Fact]
        public void EngineErrorCaughtInSameFrameKeepsItsFrame()
        {
            ScriptError err = RunErr("x = 1\ntry:\n    {}['k']\nexcept KeyError:\n    raise RuntimeError('x')\n");
            Assert.Equal(
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 3, in <module>\n" +
                "KeyError: 'k'\n" +
                "\nDuring handling of the above exception, another exception occurred:\n\n" +
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 5, in <module>\n" +
                "RuntimeError: x", err.ScriptTraceback);
        }

        [Fact]
        public void EngineErrorCaughtInsideFunctionKeepsThatFunctionsFrame()
        {
            ScriptError err = RunErr(
                "def f(d):\n    try:\n        return d['k']\n    except KeyError:\n        raise ValueError('no k')\n"
                + "def g():\n    return f({})\ng()\n");
            Assert.Equal(
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 3, in f\n" +
                "KeyError: 'k'\n" +
                "\nDuring handling of the above exception, another exception occurred:\n\n" +
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 8, in <module>\n" +
                "  File \"<script>\", line 7, in g\n" +
                "  File \"<script>\", line 5, in f\n" +
                "ValueError: no k", err.ScriptTraceback);
        }

        // The stamp must not double-count: a bare re-raise out of the handler still reports f once.
        [Fact]
        public void StampedFrameIsNotAppendedTwice()
        {
            ScriptError err = RunErr("def f():\n    try:\n        1 // 0\n    except ZeroDivisionError:\n        raise\nf()\n");
            Assert.Equal(
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 6, in <module>\n" +
                "  File \"<script>\", line 3, in f\n" +
                "ZeroDivisionError: integer division or modulo by zero", err.ScriptTraceback);
        }

        // `raise e` re-arms the frame, so the re-raise site and the original raise site both show.
        [Fact]
        public void ExplicitReRaiseShowsBothSites()
        {
            ScriptError err = RunErr("def f():\n    try:\n        1 // 0\n    except ZeroDivisionError as e:\n        raise e\nf()\n");
            Assert.Equal(
                "Traceback (most recent call last):\n" +
                "  File \"<script>\", line 6, in <module>\n" +
                "  File \"<script>\", line 5, in f\n" +
                "  File \"<script>\", line 3, in f\n" +
                "ZeroDivisionError: integer division or modulo by zero", err.ScriptTraceback);
        }
    }
}
