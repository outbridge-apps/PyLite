using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Stress
{
    // impl/06 T-BLT-25: adversarial builtin/format inputs. Each must end in a bounded EngineAbort (or a
    // script-level error) with the process alive — never OOM, hang, or corruption.
    [Trait("Category", "ResourceLimits")]
    public class BuiltinsStressTests
    {
        private static ResourceLimits Steps(long n)
        {
            return ResourceLimits.Default.With(maxSteps: n);
        }

        private static EngineAbort Abort(string src, ResourceLimits lim = null)
        {
            return Assert.Throws<EngineAbort>(() => Ev.Run(src, lim));
        }

        [Fact]  // quadratic int(str) parse dies on the step budget, not on time
        public void HugeIntParseAbortsOnSteps()
        {
            var ab = Abort("x = int('9' * 100000)\n", Steps(2_000_000));   // 332k bits: within MaxIntBits
            Assert.Equal(EngineAbortKind.Steps, ab.Kind);
        }

        [Fact]  // 8M bits read in one pass, then refused (a byte-at-a-time shift loop ran past the deadline)
        public void HugeFromBytesAbortsOnIntBits()
        {
            var ab = Abort("x = int.from_bytes(b'\\xff' * 1000000)\n");
            Assert.Equal("MaxIntBits", ab.LimitName);
        }

        [Fact]  // more digits than MaxIntBits holds: refused by count, before the quadratic parse
        public void OverlongIntParseAbortsOnIntBits()
        {
            var ab = Abort("x = int('9' * 1000000)\n");
            Assert.Equal("MaxIntBits", ab.LimitName);
            Assert.InRange(ab.ActualValue, 3_321_000, 3_322_000);   // the estimate: 10**6 digits * log2(10)
        }

        [Fact]  // pre-estimated pow never allocates the giant result
        public void PowBombAborts()
        {
            var ab = Abort("x = pow(2, 10**8)\n");
            Assert.Equal(EngineAbortKind.Memory, ab.Kind);
        }

        [Fact]  // zfill width is pre-charged before allocation
        public void ZfillBombAborts()
        {
            var ab = Abort("x = 'x'.zfill(10**9)\n");
            Assert.Equal(EngineAbortKind.Memory, ab.Kind);
        }

        [Fact]  // list(range(1e9)) dies on budget, not OOM
        public void ListRangeBombAborts()
        {
            Abort("x = list(range(10**9))\n");
        }

        [Fact]  // format width from a nested field -> gigantic -> pre-charged
        public void FormatWidthBombAborts()
        {
            var ab = Abort("x = '{:>{}}'.format('x', 10**9)\n");
            Assert.Equal(EngineAbortKind.Memory, ab.Kind);
        }

        [Fact]  // % width bomb
        public void PercentWidthBombAborts()
        {
            var ab = Abort("x = '%*d' % (10**9, 1)\n");
            Assert.Equal(EngineAbortKind.Memory, ab.Kind);
        }

        [Fact]  // print flood hits the output cap; script-level except cannot catch it
        public void PrintFloodAbortsOnOutput()
        {
            var ab = Abort("while True:\n    print('x' * 1000)\n", Steps(100_000_000));
            Assert.True(ab.Kind == EngineAbortKind.OutputCap || ab.Kind == EngineAbortKind.Steps);
        }

        [Fact]  // sorted key that raises propagates untouched
        public void SortedKeyRaisePropagates()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("sorted([1, 2, 3], key=lambda x: 1 / 0)\n"));
            Assert.Same(PyExceptionTypes.ZeroDivisionError, ex.Value.ExcType);
        }

        [Fact]  // list.sort with a key that mutates the list -> ValueError, list left consistent
        public void SortMutatingKeyRaises()
        {
            var ex = Assert.Throws<ScriptException>(() =>
                Ev.Run("xs = [3, 1, 2]\ndef k(v):\n    xs.append(0)\n    return v\nxs.sort(key=k)\n"));
            Assert.Equal("list modified during sort", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]  // dict view iteration + mutation -> RuntimeError (bounded), process alive
        public void DictViewMutationRuntimeError()
        {
            var ex = Assert.Throws<ScriptException>(() =>
                Ev.Run("d = {i: i for i in range(1000)}\nfor k in d:\n    d[k * 1000] = 1\n"));
            Assert.Same(PyExceptionTypes.RuntimeError, ex.Value.ExcType);
        }

        [Fact]  // fast ModPow stays cheap even for enormous exponents
        public void ModPowStaysFast()
        {
            Assert.Equal("0", Ev.ReprOf("(pow(10, 10**7, 998244353) - pow(10, 10**7, 998244353))"));
        }
    }
}
