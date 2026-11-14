using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Stress
{
    // Adversarial value-model structures (impl/03 T-VAL-13 acceptance): the iterative hash/equals/repr
    // engines never overflow the C# stack; over-deep data yields a deterministic RuntimeError, oversized
    // scalars a budget abort, and cycles print an ellipsis and terminate.
    [Trait("Category", "ResourceLimits")]
    public class ValueModelStressTests
    {
        private static ScriptValue NestTuple(Outbridge.PyLite.Runtime.EvalContext ctx, int depth)
        {
            ScriptValue t = ctx.Values.Int(0);
            for (int i = 0; i < depth; i++)
            {
                t = ctx.Values.Tuple(new[] { t });
            }
            return t;
        }

        [Fact]
        public void DeepTupleHashIsDeterministicRecursionError()
        {
            var ctx = Rt.Ctx();
            var t = NestTuple(ctx, 200);   // > MaxDataDepth (128)
            var ex = Assert.Throws<ScriptException>(() => PyOps.Hash(t, ctx, 0));
            Assert.Same(PyExceptionTypes.RecursionError, ex.Value.ExcType);   // a RuntimeError subclass, as in CPython
            Assert.Contains("maximum recursion depth exceeded", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void TupleMatryoshka500HashRaises()
        {
            var ctx = Rt.Ctx();
            var t = NestTuple(ctx, 500);
            Assert.Throws<ScriptException>(() => PyOps.Hash(t, ctx, 0));
        }

        [Fact]
        public void HashAndEqualsShareOneDataDepthRule()
        {
            // Reconciles the "depth-128 computed two ways" audit note: hash and equals both enforce
            // MaxDataDepth via `stack.Count + depth >= MaxDataDepth`, so they agree below the cap.
            var ctx = Rt.Ctx();
            var shallow = NestTuple(ctx, 100);   // < 128
            long h = PyOps.Hash(shallow, ctx, 0);          // no throw
            Assert.True(PyOps.Equals(shallow, NestTuple(ctx, 100), ctx, 0));
        }

        [Fact]
        public void DeepEqualsIsDeterministicRecursionError()
        {
            var ctx = Rt.Ctx();
            var a = NestTuple(ctx, 200);
            var b = NestTuple(ctx, 200);
            var ex = Assert.Throws<ScriptException>(() => PyOps.Equals(a, b, ctx, 0));
            Assert.Same(PyExceptionTypes.RecursionError, ex.Value.ExcType);   // a RuntimeError subclass, as in CPython
        }

        [Fact]
        public void DeepListReprNeverOverflows()
        {
            var ctx = Rt.Ctx();
            ScriptValue cur = ctx.Values.Int(0);
            for (int i = 0; i < 5000; i++)
            {
                var l = ctx.Values.List(1);
                l.Add(cur, ctx);
                cur = l;
            }
            // The process surviving to this assert proves the iterative repr did not SOE. A RecursionError is
            // also acceptable; any other exception is a defect, not a pass.
            try
            {
                string r = cur.Repr(ctx);
                Assert.True(r.Length >= 5000);
            }
            catch (ScriptException ex)
            {
                Assert.Equal("RecursionError", ex.Value.ExcType.Name);
            }
        }

        [Fact]
        public void CyclicListReprPrintsEllipsisAndTerminates()
        {
            var ctx = Rt.Ctx();
            var l = ctx.Values.List(1);
            l.Add(l, ctx);   // l references itself
            string r = l.Repr(ctx);
            Assert.Contains("...", r);
        }

        [Fact]
        public void HugeIntShiftAbortsOnIntBits()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<EngineAbort>(() =>
                PyOps.BinaryOp(PyBinOp.LShift, ctx.Values.Int(1), ctx.Values.Int(100_000_000), ctx));
            Assert.Equal(EngineAbortKind.Memory, ex.Kind);
            Assert.Equal("MaxIntBits", ex.LimitName);
        }

        // A host-raised MaxDataDepth lets a leaf re-enter repr (deque, slice, exception args) or asdict past
        // what the C# stack holds: the probe ends the run cleanly instead of a process-killing overflow.
        [Theory]
        [InlineData("from collections import deque\nd = deque()\nfor i in range(200000):\n    d = deque([d])\nx = repr(d)\n")]
        [InlineData("s = slice(1)\nfor i in range(200000):\n    s = slice(s)\nx = str(s)\n")]
        [InlineData("e = ValueError()\nfor i in range(200000):\n    e = ValueError(e)\nx = repr(e)\n")]
        [InlineData("from dataclasses import dataclass, asdict\n@dataclass\nclass N:\n    n: object = None\n"
            + "a = N()\nfor i in range(200000):\n    a = N(a)\nx = asdict(a)\n")]
        [InlineData("e = ValueError()\nfor i in range(200000):\n    e = ValueError(e)\nx = str(e)\n")]
        [InlineData("import itertools\nr = 0\nfor i in range(200000):\n    r = itertools.repeat(r)\nx = repr(r)\n")]
        [InlineData(NestedCounters + "x = a <= b\n")]
        public void DeepLeafReentryAbortsOnStack(string src)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, TestEngines.Unhurried.With(maxDataDepth: 1_000_000)));   // the stack decides, not the clock
            Assert.Equal("StackDepth", ex.LimitName);
        }

        private const string CountersHead = "from collections import Counter\na = Counter()\nb = Counter()\n";
        private const string CountersBody = ":\n    x = Counter()\n    x['k'] = a\n    a = x\n    y = Counter()\n    y['k'] = b\n    b = y\n";
        private const string NestedCounters = CountersHead + "for i in range(200000)" + CountersBody;
        private const string Counters1000 = CountersHead + "for i in range(1000)" + CountersBody;

        // str of an exception wrapping an exception, repr of a repeat of a repeat and the order of a Counter
        // of Counters re-entered their engine at depth 0, so no depth check or stack probe ever fired and the
        // process died at default limits. They now count their depth like every other leaf.
        [Theory]
        [InlineData("e = ValueError(1)\nfor i in range(1000):\n    e = ValueError(e)\ntry:\n    str(e)\nexcept RecursionError:\n    r = 'ok'\n")]
        [InlineData("import itertools\nx = 0\nfor i in range(1000):\n    x = itertools.repeat(x)\ntry:\n    repr(x)\nexcept RecursionError:\n    r = 'ok'\n")]
        [InlineData(Counters1000 + "try:\n    a <= b\nexcept RecursionError:\n    r = 'ok'\n")]
        public void NestedLeafReentryIsARecursionError(string src)
        {
            Assert.Equal("'ok'", Ev.Run(src).Repr("r"));
        }

        // The pow/mul pre-checks refuse only what surely passes MaxIntBits; the borderline is computed.
        [Theory]
        [InlineData("r = (7 ** 3500).bit_length()", "9826")]   // 3 bits per factor of 7 would refuse it
        [InlineData("r = (2 ** 9999).bit_length()", "10000")]
        [InlineData("r = (2 ** 4999 * 2 ** 5000).bit_length()", "10000")]
        public void PowAndMulNearIntBitsAreExact(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src + "\n", ResourceLimits.Default.With(maxIntBits: 10_000)).Repr("r"));
        }

        [Theory]
        [InlineData("r = 2 ** 10000")]
        [InlineData("r = 2 ** 5000 * 2 ** 5000")]
        public void PowAndMulPastIntBitsAbort(string src)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src + "\n", ResourceLimits.Default.With(maxIntBits: 10_000)));
            Assert.Equal("MaxIntBits", ex.LimitName);
        }

        [Fact]
        public void HugeStrRepeatAbortsOnStrChars()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<EngineAbort>(() =>
                PyOps.BinaryOp(PyBinOp.Mul, ctx.Values.Str("x"), ctx.Values.Int(1_000_000_000), ctx));
            Assert.Equal(EngineAbortKind.Memory, ex.Kind);
            Assert.Equal("MaxStrChars", ex.LimitName);
        }
    }
}
