using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // Review 3, P2: what the host supplies is fenced (clock, sleep policy, texts, collections), the answer does
    // not depend on the host thread's culture or the process, and the API keeps its documented contract.
    [Trait("Category", "Sandbox")]
    public class HostContractTests
    {
        private sealed class Clock : IScriptClock
        {
            public Func<DateTime> Now = () => DateTime.UtcNow;
            public string Name = "UTC";
            public TimeSpan Offset = TimeSpan.Zero;
            public DateTime UtcNow { get { return Now(); } }
            public TimeSpan LocalOffset { get { return Offset; } }
            public string LocalName { get { return Name; } }
        }

        private sealed class Sleep : ISleepPolicy
        {
            public TimeSpan Q = TimeSpan.FromMilliseconds(25);
            public TimeSpan Clamp(TimeSpan requested, TimeSpan sleptSoFar) { return requested; }
            public TimeSpan Quantum { get { return Q; } }
        }

        private static RunResult RunWith(IScriptClock clock, ISleepPolicy sleep, string src)
        {
            return new ScriptEngine(new EngineOptions { Clock = clock, SleepPolicy = sleep }).Run(src, null);
        }

        [Fact]
        public void AThrowingClockIsAHostError()
        {
            var c = new Clock { Now = () => { throw new InvalidOperationException("clock down"); } };
            RunResult r = RunWith(c, null, "import time\ntime.time()\n");
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("clock down", r.Error.Message);
        }

        [Theory]
        [InlineData("name")]
        [InlineData("kind")]
        [InlineData("offset")]
        public void AClockOutsideItsContractIsAHostError(string broken)
        {
            var c = new Clock();
            if (broken == "name")
                c.Name = null;
            else if (broken == "kind")
                c.Now = () => DateTime.Now;
            else
                c.Offset = TimeSpan.FromSeconds(30);
            RunResult r = RunWith(c, null, "import time, datetime\ntime.tzname\ndatetime.datetime.now()\ntime.localtime()\n");
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
        }

        [Fact]
        public void ASleepQuantumThatWaitsForeverIsAHostError()
        {
            RunResult r = RunWith(null, new Sleep { Q = TimeSpan.FromMilliseconds(-1) }, "import time\ntime.sleep(0.05)\n");
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("Quantum", r.Error.Message);
        }

        private sealed class EvilException : Exception
        {
            public override string Message { get { throw new InvalidOperationException("no message"); } }
            public override string ToString() { throw new InvalidOperationException("no text"); }
        }

        [Fact]
        public void AHostExceptionWhoseMessageThrowsIsStillReported()
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            t.Set("f", (s, a, kw, c) => { throw new EvilException(); });
            RunResult r = new ScriptEngine(new EngineOptions()).Run("f()\n", new RunRequest { Builtins = t });
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("EvilException", r.Error.Message);
        }

        private sealed class ThrowingList : IReadOnlyList<string>
        {
            public string this[int index] { get { throw new InvalidOperationException("no item"); } }
            public int Count { get { return 1; } }
            public IEnumerator<string> GetEnumerator() { throw new NotSupportedException("no enumerator"); }
            IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }

        [Fact]
        public void ARequestWhoseCollectionThrowsIsAHostError()
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("x = 1\n", new RunRequest { ScalarOutputNames = new ThrowingList() });
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
        }

        [Fact]
        public void NullBuiltinsAreRefusedWhenSet()
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            Assert.Throws<ArgumentNullException>(() => t.SetValue("x", null));
            Assert.Throws<ArgumentNullException>(() => t.Set("f", null));
        }

        // RFC 1321's test suite, RFC 2104's first HMAC-MD5 vector, and CPython's uuid3 of the DNS namespace.
        [Theory]
        [InlineData("hashlib.md5(b'').hexdigest()", "d41d8cd98f00b204e9800998ecf8427e")]
        [InlineData("hashlib.md5(b'abc').hexdigest()", "900150983cd24fb0d6963f7d28e17f72")]
        [InlineData("hashlib.md5(b'message digest').hexdigest()", "f96b697d7cb7938d525a2f31aaf161d0")]
        [InlineData("hashlib.md5(b'12345678901234567890123456789012345678901234567890123456789012345678901234567890').hexdigest()", "57edf4a22be3c955ac49da2e2107b67a")]
        [InlineData("hashlib.md5(b'a' * 1000000).hexdigest()", "7707d6ae4e027c70eea2a935c2296f21")]
        [InlineData("hmac.new(b'\\x0b' * 16, b'Hi There', 'md5').hexdigest()", "9294727a3638bb1c13f48ef8158bfc9d")]
        [InlineData("str(uuid.uuid3(uuid.NAMESPACE_DNS, 'python.org'))", "6fa459ea-ee8a-3ca4-894e-db77e160355e")]
        public void Md5IsTheManagedOne(string expr, string expected)
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("import hashlib, hmac, uuid\nr = " + expr + "\n",
                new RunRequest { ScalarOutputNames = new[] { "r" } });
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
            Assert.Equal(expected, r.ScalarsOut["r"]);
        }

        // From .NET 4.6 on, setting CurrentCulture on an unstarted Thread object set the CALLER's: every Run
        // switched the host's thread (a web request) to the invariant culture and left an AsyncLocal on its
        // context, where a Thread.Abort fail-fasts the process. The caller's culture is the caller's.
        [Fact]
        public void ARunLeavesTheCallersCultureAlone()
        {
            string culture = null, uiCulture = null;
            Exception failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
                    new ScriptEngine(new EngineOptions()).Run("x = 1\n", new RunRequest());
                    culture = CultureInfo.CurrentCulture.Name;
                    uiCulture = CultureInfo.CurrentUICulture.Name;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            t.Start();
            Assert.True(t.Join(30000));
            Assert.Null(failure);
            Assert.Equal("de-DE", culture);
            Assert.Equal("ru-RU", uiCulture);
        }

        // A culture whose minus sign is U+2212 and whose texts would come from it: a run reads neither.
        [Fact]
        public void TheCallersCultureNeverReachesARun()
        {
            var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            odd.NumberFormat.NegativeSign = "\u2212";
            odd.NumberFormat.NumberDecimalSeparator = ",";
            Thread t = Thread.CurrentThread;
            CultureInfo saved = t.CurrentCulture;
            RunResult r;
            try
            {
                t.CurrentCulture = odd;
                r = new ScriptEngine(new EngineOptions()).Run(
                    "import calendar\ntry:\n    calendar.monthrange(2020, -1)\nexcept Exception as e:\n    r = str(e)\n",
                    new RunRequest { ScalarOutputNames = new[] { "r" } });
            }
            finally
            {
                t.CurrentCulture = saved;
            }
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
            Assert.DoesNotContain("\u2212", r.ScalarsOut["r"]);
            Assert.Contains("-1", r.ScalarsOut["r"]);
        }

        [Fact]
        public void AZoneInfoHashesAsItsKey()
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("import zoneinfo\nr = hash(zoneinfo.ZoneInfo('UTC')) == hash('UTC')\n",
                new RunRequest { ScalarOutputNames = new[] { "r" } });
            Assert.Equal("True", r.ScalarsOut["r"]);
        }

        [Fact]
        public void AFaultTheRunCaughtStillRaisesTheEvent()
        {
            var events = new List<EngineEvent>();
            var e = new ScriptEngine(new EngineOptions { EventSink = ev => { lock (events) events.Add(ev); } });
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            // an engine-side builtin (unfenced) that fails: what an engine defect looks like
            t.SetValue("broken", BuiltinFunctionValue.Make("broken", (s, a, kw, c) => { throw new InvalidOperationException("defect"); }));
            RunResult r = e.Run("broken()\n", new RunRequest { Builtins = t });
            Assert.Equal(ScriptErrorKind.EngineFault, r.Error.Kind);
            lock (events)
                Assert.Contains(events, ev => ev.Kind == EngineEventKind.EngineFault && ev.Message.Contains("defect"));
        }

        [Fact]
        public void NullsNeverThrow()
        {
            var e = new ScriptEngine(new EngineOptions());
            Assert.Equal(ScriptErrorKind.HostError, e.Run((string)null, null).Error.Kind);
            Assert.Equal(ScriptErrorKind.HostError, e.Run((Outbridge.PyLite.Syntax.CompiledScript)null, null).Error.Kind);
            Outbridge.PyLite.Syntax.CompiledScript cs;
            ScriptError err;
            Assert.False(e.TryCompile(null, "x", out cs, out err));
            Assert.Equal(ScriptErrorKind.HostError, err.Kind);
            Assert.Equal(ResourceLimits.Default.MaxSteps, ResourceLimits.Default.ClampTo(null).MaxSteps);

            var runner = new ScriptRunner();
            runner.SetScalar(null, "1");
            runner.SetJson(null, "1");
            Assert.Equal((int)ScriptErrorKind.HostError, runner.Run("x = 1\n"));
            Assert.Null(runner.GetScalar(null));
            Assert.Null(runner.GetJson(null));
        }

        [Fact]
        public void ARequestRefusedBeforeTheRunCompilesNothing()
        {
            var engine = new ScriptEngine(new EngineOptions());
            string source = "x = 1\n" + new string('#', 1000) + "\n";
            RunResult r = engine.Run(source, new RunRequest { ScalarsIn = new Dictionary<string, string> { ["1bad"] = "1" } });
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Equal(TimeSpan.Zero, r.Stats.CompileTime);
            Assert.Equal(0, engine.CachedSourceChars);
        }

        [Fact]
        public void ARunThatCompilesReportsItsCompileTime()
        {
            var engine = new ScriptEngine(new EngineOptions());
            Assert.True(engine.Run("x = 1\n" + new string('#', 1000) + "\n", null).Stats.CompileTime > TimeSpan.Zero);
            RunResult bad = engine.Run("x = (\n" + new string('#', 1000) + "\n", null);
            Assert.Equal(ScriptErrorKind.SyntaxError, bad.Error.Kind);
            Assert.True(bad.Stats.CompileTime > TimeSpan.Zero);
            Assert.Equal("", bad.PrintOutput);
        }

        [Fact]
        public void TheSharedEngineIsConfiguredOnlyBeforeItIsBuilt()
        {
            new ScriptRunner();
            Assert.False(ScriptRunner.ConfigureSharedEngine(new EngineOptions()));
        }

        [Theory]
        [InlineData("1\0", "str")]
        [InlineData("+5", "int")]
        [InlineData("-007", "int")]
        [InlineData("1_000", "str")]
        public void ScalarsInTakeOnlyPlainIntegers(string raw, string type)
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("r = type(v).__name__\n",
                new RunRequest { ScalarsIn = new Dictionary<string, string> { ["v"] = raw }, ScalarOutputNames = new[] { "r" } });
            Assert.Equal(type, r.ScalarsOut["r"]);
        }

        [Fact]
        public void ScalarsInTakeAtMost4300Digits()
        {
            string Type(string raw)
            {
                RunResult r = new ScriptEngine(new EngineOptions()).Run("r = type(v).__name__\n",
                    new RunRequest { ScalarsIn = new Dictionary<string, string> { ["v"] = raw }, ScalarOutputNames = new[] { "r" } });
                return r.ScalarsOut["r"];
            }
            Assert.Equal("int", Type(new string('9', 4300)));
            Assert.Equal("int", Type("-" + new string('9', 4300)));
            Assert.Equal("str", Type(new string('9', 4301)));
        }

        [Theory]
        [InlineData("json.loads('\"\\\\ud800\"')", "'\\ud800'")]
        [InlineData("json.loads('\"a\\\\ud800b\\\\ufffdc\\\\udc00\"')", "'a\\ud800b\\ufffdc\\udc00'")]
        [InlineData("json.loads('\"\\\\ud83d\\\\ude00\"')", "'\\U0001f600'")]
        [InlineData("json.loads('[\"\\\\udc00\", {\"\\\\ud800\": 1}]')", "['\\udc00', {'\\ud800': 1}]")]
        public void JsonKeepsALoneSurrogate(string expr, string expected)
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("import json\nr = ascii(" + expr + ")\n",
                new RunRequest { ScalarOutputNames = new[] { "r" } });
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
            Assert.Equal(expected, r.ScalarsOut["r"]);
        }

        [Fact]
        public void MemoryHygieneIsCopiedAtConstruction()
        {
            var h = new MemoryHygieneOptions { PostRunGcHintThresholdBytes = 1 };
            var e = new ScriptEngine(new EngineOptions { MemoryHygiene = h });
            object held = typeof(ScriptEngine).GetField("_hygiene", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(e);
            Assert.NotSame(h, held);
            Assert.Equal(1, ((MemoryHygieneOptions)held).PostRunGcHintThresholdBytes);
        }

        // Review 4, M9: what the documents promise.
        [Fact]
        public void EveryCallToRunReportsRunCompleted()
        {
            var events = new List<EngineEvent>();
            var e = new ScriptEngine(new EngineOptions { EventSink = events.Add });
            e.Run("def\n", null);
            e.Run((string)null, null);
            e.Run("x = 1\n", new RunRequest { ScalarOutputNames = new[] { "1bad" } });
            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            e.Run("x = 1\n", new RunRequest { CancellationToken = cancelled.Token });
            e.Run("x = 1\n", null);
            List<EngineEvent> completed = events.FindAll(ev => ev.Kind == EngineEventKind.RunCompleted);
            Assert.Equal(5, completed.Count);
            Assert.All(completed, ev => Assert.NotNull(ev.Stats));
            Assert.Equal(5, new HashSet<long>(completed.ConvertAll(ev => ev.RunId)).Count);
        }

        private sealed class CountingProvider : IModuleProvider
        {
            public int Created;
            public string Name { get { return "counted"; } }

            public ModuleValue CreateInstance(Outbridge.PyLite.Runtime.EvalContext ctx)
            {
                Interlocked.Increment(ref Created);
                var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
                m["j"] = ctx.Values.Int(9);
                m["s"] = ctx.Values.Int(9);
                m["p"] = ctx.Values.Int(9);
                return ctx.Values.Module("counted", m);
            }
        }

        [Fact]
        public void InvalidJsonEndsTheRunBeforeAProviderRunsAndInputsBeatThePrelude()
        {
            var provider = new CountingProvider();
            var e = new ScriptEngine(new EngineOptions { ExtraModules = new IModuleProvider[] { provider }, PreludeModules = new[] { "counted" } });
            int before = provider.Created;
            RunResult bad = e.Run("x = 1\n", new RunRequest { JsonIn = new Dictionary<string, string> { ["j"] = "{bad" } });
            Assert.Equal(ScriptErrorKind.HostError, bad.Error.Kind);
            Assert.Contains("'j'", bad.Error.Message);
            Assert.Equal(before, provider.Created);

            RunResult ok = e.Run("r = (j, s, p)\n", new RunRequest
            {
                JsonIn = new Dictionary<string, string> { ["j"] = "1" },
                ScalarsIn = new Dictionary<string, string> { ["s"] = "2" },
                ScalarOutputNames = new[] { "r" },
            });
            Assert.True(ok.Succeeded, ok.Error == null ? "" : ok.Error.Message);
            Assert.Equal("(1, 2, 9)", ok.ScalarsOut["r"]);
        }

        [Theory]
        [InlineData("class O:\n    def __str__(self):\n        raise ValueError('no text')\nr = O()\n", "no text")]
        [InlineData("class M:\n    def __str__(self):\n        raise KeyError('x')\nclass O:\n    def __str__(self):\n        raise ValueError(M())\nr = O()\n", "'r'")]
        public void AScalarOutputWhoseStrFailsIsAHostError(string src, string expected)
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run(src, new RunRequest { ScalarOutputNames = new[] { "r" } });
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("'r'", r.Error.Message);
            Assert.Contains(expected, r.Error.Message);
        }

        [Theory]
        [InlineData(2040)]
        [InlineData(2041)]
        public void ACutMessageKeepsASurrogatePairWhole(int padding)
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("raise ValueError('a' * " + padding + " + '\\U0001F600' * 20)\n", null);
            string m = r.Error.Message;
            Assert.EndsWith("[truncated]", m);
            for (int i = 0; i < m.Length; i++)
            {
                if (char.IsHighSurrogate(m[i]))
                    Assert.True(i + 1 < m.Length && char.IsLowSurrogate(m[i + 1]), "a lone surrogate at " + i);
            }
        }

        [Fact]
        public void PrintOutputIsEmptyWhenNothingRanAndNullWithASink()
        {
            var e = new ScriptEngine(new EngineOptions());
            Assert.Equal("", e.Run("def\n", null).PrintOutput);
            Assert.Equal("", e.Run((string)null, null).PrintOutput);
            Assert.Null(e.Run("def\n", new RunRequest { PrintSink = line => { } }).PrintOutput);
        }
    }
}
