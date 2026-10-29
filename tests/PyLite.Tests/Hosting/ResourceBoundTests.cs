using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // THE embedding guarantee for a shared host process: no script bomb can (a) run past the wall-clock
    // deadline / hang the caller / leak its runner thread, or (b) allocate past the memory budget or
    // leave garbage rooted after it aborts. Every bomb resolves to BudgetExceeded near its deadline;
    // the aborted run's graph is collectable. Limits are small so the battery is fast.
    [Collection("TimeSensitive")]   // asserts wall-clock bounds; must not race the rest of the suite
    [Trait("Category", "ResourceLimits")]
    [Trait("Category", "MachineSensitive")]
    public class ResourceBoundTests
    {
        private const int DeadlineMs = 300;
        private const long MemBytes = 4L * 1024 * 1024;   // 4 MB logical budget

        // The leak path returns only after deadline + grace(250) + slack(5000) ≈ 5.5 s, so a healthy
        // stop well under that proves the thread was NOT leaked. This ceiling CANNOT be raised past
        // ~5.5 s without the assert losing its meaning — headroom for a busy host comes from the
        // TimeSensitive collection (no sibling tests in the window), not from a bigger number.
        private const int HealthyStopCeilingMs = 4500;

        private static ResourceLimits Limits()
        {
            return ResourceLimits.Default.With(
                deadlineMs: DeadlineMs, maxAllocBytes: MemBytes,
                maxStrChars: 2_000_000, maxCollectionItems: 500_000, maxIntBits: 800_000,
                sleepTotalMs: 500, maxSteps: 2_000_000_000L);
        }

        public static IEnumerable<object[]> Bombs()
        {
            // pure CPU / control-flow (step + deadline cadence must catch these)
            yield return new object[] { "while-true", "while True:\n    pass\n" };
            yield return new object[] { "busy-arith", "x = 0\nwhile True:\n    x = (x * 7 + 1) % 999983\n" };
            yield return new object[] { "nested-for", "s = 0\nfor i in range(10**9):\n    for j in range(10**9):\n        s += 1\n" };
            // range beyond long range: the ExecForRange long-fast guard falls through to the general
            // iterator path (a BigInteger range counter) — must still hit the budget every iteration
            yield return new object[] { "range-bigint", "s = 0\nfor i in range(10**30):\n    s = s + 1\n" };
            yield return new object[] { "recursion", "def f(n):\n    return f(n + 1)\nf(0)\n" };
            yield return new object[] { "call-spin", "def f():\n    return 1\nwhile True:\n    f()\n" };
            // the post-optimization fast paths: membership fast loop and the global-cache slow-path
            // storm (del/re-insert every iteration) must still hit the step/deadline cadence
            yield return new object[] { "contains-spin", "L = list(range(100))\nwhile True:\n    b = 50 in L\n" };
            yield return new object[] { "global-churn", "g = 0\nwhile True:\n    g = g + 1\n    del g\n    g = 0\n" };
            // Tier 2 fast paths: the compiled comprehension body and the CallHook1 filter/map loop
            // skip EvalExpr's per-node sub-charges, so the deadline must still fire via the drain.
            // Float shapes keep this on the COMPILED (non-fused) path.
            yield return new object[] { "compiled-comp", "while True:\n    r = [x * 2.5 + 1 for x in range(200000) if x / 2 > 1]\n" };
            yield return new object[] { "filter-hook", "def k(x):\n    return x % 2 == 0\nwhile True:\n    r = list(filter(k, range(200000)))\n" };
            // Tier 2.3 superinstruction fusion: pure-long bodies skip intermediate charges entirely —
            // the drain's per-element Step must still bound them; the overflow storm forces the
            // per-element big-int fallback (with its charges) every iteration.
            yield return new object[] { "fused-comp", "while True:\n    r = [x * x + 1 for x in range(200000) if x % 3 != 0]\n" };
            yield return new object[] { "fused-overflow-storm", "while True:\n    r = [(x + 10 ** 18) * 16 for x in range(50000)]\n" };
            yield return new object[] { "fused-list-comp", "L = list(range(30000))\nwhile True:\n    r = [x * x for x in L if x % 3 != 0]\n" };
            // T2.3c: the odometer leaf (genexp / multi-clause) — per-MoveNext Step must still bound it.
            yield return new object[] { "fused-genexp", "while True:\n    s = sum(x * x + 1 for x in range(200000))\n" };
            yield return new object[] { "fused-multiclause", "while True:\n    r = [y * 2 for x in range(600) if x % 2 == 0 for y in range(x)]\n" };
            // user __eq__ dispatch: every == is a full script call — the call Step + deadline bound it
            yield return new object[] { "user-eq-spin", "class M:\n    def __init__(self, v):\n        self.v = v\n    def __eq__(self, other):\n        return self.v == other\nm = M(1)\nwhile True:\n    b = m == 1\n" };
            yield return new object[] { "user-eq-membership", "class M:\n    def __init__(self, v):\n        self.v = v\n    def __eq__(self, other):\n        return self.v == other\nL = [M(i) for i in range(200)]\nwhile True:\n    b = 999 in L\n" };
            // single big native ops (must not run for seconds without a budget check)
            yield return new object[] { "int-pow", "x = 10 ** 5000000\n" };
            yield return new object[] { "pow-chain", "x = 2\nwhile True:\n    x = x * x\n" };
            yield return new object[] { "str-of-bigint", "x = 10 ** 700000\ns = str(x)\n" };
            yield return new object[] { "int-of-str", "s = '9' * 2000000\nx = int(s)\n" };
            yield return new object[] { "factorial", "import math\nx = math.factorial(500000)\n" };
            yield return new object[] { "str-repeat-upper", "s = 'abc' * 1000000\nt = s.upper()\n" };
            yield return new object[] { "str-split", "s = 'a,' * 2000000\nt = s.split(',')\n" };
            yield return new object[] { "join-huge", "L = ['x'] * 2000000\ns = ','.join(L)\n" };
            yield return new object[] { "sort-key", "L = list(range(800000))\nL.sort(key=lambda x: -x)\n" };
            // deque.count walks every element; its per-element Step must bound a repeated count of a
            // large deque against a miss (needle absent -> full scan each pass).
            yield return new object[] { "deque-count-spin", "import collections\nd = collections.deque(range(100000))\nwhile True:\n    n = d.count(-1)\n" };
            yield return new object[] { "sum-range", "s = sum(range(2000000000))\n" };
            yield return new object[] { "regex-catastrophic", "import re\nre.match('(a+)+$', 'a' * 60 + 'b')\n" };
            yield return new object[] { "regex-findall", "import re\nre.findall('a', 'a' * 4000000)\n" };
            // memory bombs (MaxAllocBytes must stop them; no OOM)
            yield return new object[] { "list-append", "L = []\nwhile True:\n    L.append(0)\n" };
            yield return new object[] { "list-double", "L = [0]\nwhile True:\n    L = L * 2\n" };
            yield return new object[] { "str-double", "s = 'a'\nwhile True:\n    s = s + s\n" };
            yield return new object[] { "dict-grow", "d = {}\ni = 0\nwhile True:\n    d[i] = i\n    i += 1\n" };
            yield return new object[] { "set-grow", "s = set()\ni = 0\nwhile True:\n    s.add(i)\n    i += 1\n" };
            yield return new object[] { "nested-list", "L = []\nwhile True:\n    L = [L, L]\n" };
            yield return new object[] { "bytes-double", "b = b'x'\nwhile True:\n    b = b + b\n" };
            yield return new object[] { "sleep", "import time\ntime.sleep(60)\n" };
            // compression loops: decompress accumulates the alloc charge (chunked PreCharge), compress
            // and the checksum loops must hit the deadline via the per-chunk / per-1KB poll.
            yield return new object[] { "zlib-decompress-loop", "import zlib\nblob = zlib.compress(b'x' * 500000)\nwhile True:\n    zlib.decompress(blob)\n" };
            yield return new object[] { "zlib-compress-loop", "import zlib\ndata = b'abc' * 300000\nwhile True:\n    zlib.compress(data)\n" };
            yield return new object[] { "gzip-decompress-loop", "import gzip\nblob = gzip.compress(b'y' * 500000)\nwhile True:\n    gzip.decompress(blob)\n" };
            yield return new object[] { "gzip-compress-loop", "import gzip\ndata = b'abc' * 300000\nwhile True:\n    gzip.compress(data)\n" };
            yield return new object[] { "crc32-loop", "import zlib\ndata = b'x' * 1000000\nn = 0\nwhile True:\n    n = zlib.crc32(data)\n" };
            yield return new object[] { "adler32-loop", "import zlib\ndata = b'x' * 1000000\nn = 0\nwhile True:\n    n = zlib.adler32(data)\n" };
        }

        [Theory]
        [MemberData(nameof(Bombs))]
        public void BombIsBoundedInTimeAndBudgetWithoutLeak(string label, string src)
        {
            int leaks = 0;
            var engine = new ScriptEngine(new EngineOptions
            {
                EventSink = ev => { if (ev.Kind == EngineEventKind.ThreadLeaked) Interlocked.Increment(ref leaks); },
            });

            var sw = Stopwatch.StartNew();
            RunResult r = engine.Run(src, new RunRequest { Limits = Limits() });   // never throws
            sw.Stop();

            Assert.False(r.Succeeded, label + " unexpectedly SUCCEEDED — a bomb bypassed the limits");
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
            Assert.True(sw.ElapsedMilliseconds < HealthyStopCeilingMs,
                label + " took " + sw.ElapsedMilliseconds + " ms (>= " + HealthyStopCeilingMs
                + " ms => it hung or took the leaked-thread path)");
            Assert.Equal(0, Volatile.Read(ref leaks));
        }

        // A host builtin that keeps only a WeakReference to what the script hands it — proving nothing it
        // sees is rooted by the engine.
        private sealed class Stasher
        {
            public WeakReference Last;
            public Outbridge.PyLite.Runtime.Values.ScriptValue Stash(
                Outbridge.PyLite.Runtime.Values.ScriptValue self,
                Outbridge.PyLite.Runtime.Values.ScriptValue[] args,
                Outbridge.PyLite.Runtime.Evaluator.KwArgs kw,
                Outbridge.PyLite.Runtime.EvalContext ctx)
            {
                if (args.Length > 0)
                    Last = new WeakReference(args[0]);
                return args.Length > 0 ? args[0] : self;
            }
        }

        // The partial graph an aborted memory bomb built (a large growing list) is unrooted once Run
        // returns — a later GC collects it. Deterministic (WeakReference), unlike process-memory sampling.
        [Fact]
        public void AbortedMemoryBombGraphIsCollected()
        {
            var engine = new ScriptEngine(new EngineOptions());
            WeakReference w = RunBombAndCaptureWeak(engine);

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            Assert.False(w.IsAlive, "the aborted bomb's list graph is still rooted after the run");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RunBombAndCaptureWeak(ScriptEngine engine)
        {
            var stasher = new Stasher();
            HostFunctionTable builtins = HostFunctionTable.CreateDefault();
            builtins.Set("stash", stasher.Stash);
            // Build and hand over a growing list until the memory budget aborts the run.
            RunResult r = engine.Run(
                "L = []\nwhile True:\n    L.append('x' * 100)\n    stash(L)\n",
                new RunRequest { Limits = ResourceLimits.Default.With(maxAllocBytes: MemBytes), Builtins = builtins });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
            Assert.NotNull(stasher.Last);   // the bomb did hand us its list at least once
            return stasher.Last;
        }
    }
}
