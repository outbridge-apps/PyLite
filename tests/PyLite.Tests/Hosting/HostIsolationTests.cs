using System;
using System.Collections.Generic;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // Review 3, P0: what one run leaves behind must not reach another (a host-built exception, the compile
    // cache), and a host failure ends the run whatever the script's finally does.
    [Trait("Category", "Sandbox")]
    public class HostIsolationTests
    {
        private static readonly Exception SharedKeyError = ScriptExceptions.KeyError("acct-42");

        private static RunRequest WithLookup(PrintDelegate sink = null)
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            t.Set("lookup", (self, a, kw, ctx) => { throw SharedKeyError; });
            return new RunRequest { Builtins = t, PrintSink = sink, ScalarOutputNames = new[] { "r" } };
        }

        private static string Out(RunResult r)
        {
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Kind + " " + r.Error.Message);
            return r.ScalarsOut["r"];
        }

        [Fact]
        public void RunBSeesNoNoteOrCauseRunALeftOnTheHostException()
        {
            var e = new ScriptEngine(new EngineOptions());
            RunResult a = e.Run("try:\n    try:\n        lookup()\n    except KeyError as ex:\n        ex.add_note('tenant A secret')\n"
                + "        raise ex from ValueError('A')\nexcept KeyError:\n    pass\nr = 1\n", WithLookup());
            Assert.Equal("1", Out(a));
            RunResult b = e.Run("try:\n    lookup()\nexcept KeyError as ex:\n    r = (getattr(ex, '__notes__', None), ex.__cause__)\n", WithLookup());
            Assert.Equal("(None, None)", Out(b));
        }

        [Fact]
        public void TheHostExceptionsMessageHashesWithEachEnginesSeed()
        {
            var e1 = new ScriptEngine(new EngineOptions { StrHashSeed = 1 });
            var e2 = new ScriptEngine(new EngineOptions { StrHashSeed = 2 });
            const string src = "try:\n    lookup()\nexcept KeyError as ex:\n    k = ex.args[0]\n    r = (k in {'acct-42': 1}, k in {'acct-42'})\n";
            Assert.Equal("(True, True)", Out(e1.Run(src, WithLookup())));
            Assert.Equal("(True, True)", Out(e2.Run(src, WithLookup())));
        }

        [Fact]
        public void SourcesThatDifferOnlyInSurrogatesAreCachedApart()
        {
            var e = new ScriptEngine(new EngineOptions());
            var req = new RunRequest { ScalarOutputNames = new[] { "r" } };
            Assert.Equal("'x\\ud800y'", Out(e.Run("r = ascii('x\ud800y')\n", req)));
            Assert.Equal("'x\\ufffdy'", Out(e.Run("r = ascii('x\ufffdy')\n", req)));
            Assert.Equal("'x\\udbffy'", Out(e.Run("r = ascii('x\udbffy')\n", req)));
        }

        [Fact]
        public void TheCacheKeepsTheRequestedScriptName()
        {
            var e = new ScriptEngine(new EngineOptions());
            Assert.Equal("tenantA/a.py", e.Compile("x = 1\n", "tenantA/a.py").ScriptName);
            Assert.Equal("tenantB/b.py", e.Compile("x = 1\n", "tenantB/b.py").ScriptName);
            Assert.Same(e.Compile("x = 2\n", "n"), e.Compile("x = 2\n", "n"));
        }

        [Fact]
        public void ALoneSurrogateRaisesWhereCPythonEncodesStrictly()
        {
            var e = new ScriptEngine(new EngineOptions());
            RunResult r = e.Run("import random\nrandom.seed('a\ud800')\n", null);
            Assert.Equal("UnicodeEncodeError", r.Error.PythonType);
        }

        [Theory]
        [InlineData("def f():\n    try:\n        boom()\n    finally:\n        return 1\nf()\n")]
        [InlineData("for i in range(3):\n    try:\n        boom()\n    finally:\n        continue\n")]
        [InlineData("for i in range(3):\n    try:\n        boom()\n    finally:\n        break\n")]
        [InlineData("try:\n    boom()\nfinally:\n    raise ValueError('replaced')\n")]
        [InlineData("class C:\n    def __enter__(self):\n        return self\n    def __exit__(self, *a):\n        return True\nwith C():\n    boom()\n")]
        [InlineData("import contextlib\nwith contextlib.suppress(Exception):\n    boom()\n")]
        public void AHostFailureEndsTheRunWhateverTheFinallyDoes(string src)
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            t.Set("boom", (self, a, kw, ctx) => { throw new InvalidOperationException("host failure"); });
            RunResult r = new ScriptEngine(new EngineOptions()).Run(src, new RunRequest { Builtins = t });
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("host failure", r.Error.Message);
        }

        [Fact]
        public void ACatchableHostErrorIsStillTheScriptsToSwallow()
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            t.Set("boom", (self, a, kw, ctx) => { throw ScriptExceptions.ValueError("catchable"); });
            RunResult r = new ScriptEngine(new EngineOptions()).Run(
                "def f():\n    try:\n        boom()\n    finally:\n        return 1\nr = f()\n",
                new RunRequest { Builtins = t, ScalarOutputNames = new[] { "r" } });
            Assert.Equal("1", Out(r));
        }

        private sealed class ThrowingProvider : IModuleProvider
        {
            public string Name { get { return "svc"; } }

            public ModuleValue CreateInstance(EvalContext ctx)
            {
                var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
                m["fail"] = ctx.Values.BuiltinFunction("fail", (self, a, kw, c) => { throw new InvalidOperationException("svc down"); });
                m["lookup"] = ctx.Values.BuiltinFunction("lookup", (self, a, kw, c) => { throw SharedKeyError; });
                return ctx.Values.Module("svc", m);
            }
        }

        private sealed class ThrowingIterator : IScriptIterator
        {
            public bool MoveNext(EvalContext ctx, out ScriptValue value) { throw new InvalidOperationException("feed down"); }
        }

        // What host code makes for a script with ctx.Values (a function, a type, an iterator) is fenced wherever the
        // script gets it from, not only as a module's top-level member (review 4, C3).
        [Fact]
        public void WhatTheHostMakesWithValuesIsFencedWhereverItIsHandedOver()
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            t.Set("mkfn", (self, a, kw, ctx) => ctx.Values.BuiltinFunction("made", (s, a2, k2, c) => { throw new InvalidOperationException("fn down"); }));
            t.Set("mkshared", (self, a, kw, ctx) => ctx.Values.BuiltinFunction("lookup2", (s, a2, k2, c) => { throw SharedKeyError; }));
            t.Set("mktype", (self, a, kw, ctx) => ctx.Values.Type("T", (s, a2, k2, c) => { throw new InvalidOperationException("ctor down"); }, null, null));
            t.Set("mkiter", (self, a, kw, ctx) => ctx.Values.Iterator(new ThrowingIterator(), "feed"));
            var e = new ScriptEngine(new EngineOptions());
            foreach (string src in new[] { "f = mkfn()\nf()\n", "T = mktype()\nT()\n", "for x in mkiter():\n    pass\n",
                "f = mkfn()\ntry:\n    f()\nexcept Exception:\n    pass\n" })
            {
                RunResult r = e.Run(src, new RunRequest { Builtins = t });
                Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            }
            RunResult ra = e.Run("f = mkshared()\ntry:\n    f()\nexcept KeyError as ex:\n    ex.add_note('tenant A')\nr = 1\n",
                new RunRequest { Builtins = t, ScalarOutputNames = new[] { "r" } });
            Assert.Equal("1", Out(ra));
            RunResult rb = e.Run("f = mkshared()\ntry:\n    f()\nexcept KeyError as ex:\n    r = getattr(ex, '__notes__', None)\n",
                new RunRequest { Builtins = t, ScalarOutputNames = new[] { "r" } });
            Assert.Equal("None", Out(rb));
        }

        private sealed class NullProvider : IModuleProvider
        {
            public string Name { get { return "nothing"; } }
            public ModuleValue CreateInstance(EvalContext ctx) { return null; }
        }

        [Fact]
        public void AHostModulesFunctionsAreFenced()
        {
            var e = new ScriptEngine(new EngineOptions { ExtraModules = new IModuleProvider[] { new ThrowingProvider(), new NullProvider() } });
            RunResult r = e.Run("import svc\nsvc.fail()\n", null);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("svc down", r.Error.Message);
            RunResult n = e.Run("import nothing\n", null);
            Assert.Equal(ScriptErrorKind.HostError, n.Error.Kind);
            RunResult k = e.Run("import svc\ntry:\n    svc.lookup()\nexcept KeyError as ex:\n    ex.add_note('A')\n    r = 1\n",
                new RunRequest { ScalarOutputNames = new[] { "r" } });
            Assert.Equal("1", Out(k));
            RunResult k2 = e.Run("import svc\ntry:\n    svc.lookup()\nexcept KeyError as ex:\n    r = getattr(ex, '__notes__', None)\n",
                new RunRequest { ScalarOutputNames = new[] { "r" } });
            Assert.Equal("None", Out(k2));
        }

        // The request's table is copied on the caller's thread: a host changing it while runs start makes those
        // runs host errors, never engine faults.
        [Fact]
        public void ATableChangedWhileRunsStartIsAHostErrorNotAFault()
        {
            var e = new ScriptEngine(new EngineOptions());
            HostFunctionTable shared = HostFunctionTable.CreateDefault();
            int stop = 0, faults = 0;
            var writer = new Thread(() =>
            {
                int i = 0;
                while (Volatile.Read(ref stop) == 0)
                    shared.Set("f" + (i++ % 64), (s, a, kw, c) => c.Values.None);
            });
            writer.Start();
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    RunResult r = e.Run("x = len([1, 2])\n", new RunRequest { Builtins = shared });
                    if (!r.Succeeded && r.Error.Kind != ScriptErrorKind.HostError)
                        faults++;
                }
            }
            finally
            {
                Volatile.Write(ref stop, 1);
                writer.Join();
            }
            Assert.Equal(0, faults);
        }

        [Fact]
        public void TheRequestsTableIsFrozenOnlyInTheCopy()
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            new ScriptEngine(new EngineOptions()).Run("x = 1\n", new RunRequest { Builtins = t });
            t.Set("later", (s, a, kw, c) => c.Values.None);   // the host's own table stays mutable
            Assert.True(t.Contains("later"));
        }

        [Fact]
        public void AHostFailureInsideTheErrorMessageIsStillTheHosts()
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            t.Set("boom", (s, a, kw, c) => { throw new InvalidOperationException("svc down"); });
            RunResult r = new ScriptEngine(new EngineOptions()).Run(
                "class O:\n    def __str__(self):\n        return boom()\nraise ValueError(O())\n", new RunRequest { Builtins = t });
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("svc down", r.Error.Message);
        }

        // Response.End does this: the run ends, the runner survives, and the script's handlers do not see it.
        [Fact]
        public void AHostCallAbortingItsThreadEndsTheRunNotTheThread()
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            t.Set("end", (s, a, kw, c) =>
            {
                Thread.CurrentThread.Abort();
                return c.Values.None;
            });
            var printed = new List<string>();
            var e = new ScriptEngine(new EngineOptions());
            RunResult r = e.Run("try:\n    end()\nexcept BaseException:\n    print('caught')\n",
                new RunRequest { Builtins = t, PrintSink = text => printed.Add(text) });
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("'end'", r.Error.Message);
            Assert.Empty(printed);
            Assert.Equal("1", Out(e.Run("r = 1\n", new RunRequest { ScalarOutputNames = new[] { "r" } })));
        }

        [Fact]
        public void AScriptExceptionWithNoValueIsTheHostsFailure()
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            t.Set("bad", (s, a, kw, c) => { throw new Outbridge.PyLite.Runtime.Errors.ScriptException(null); });
            RunResult r = new ScriptEngine(new EngineOptions()).Run("bad()\n", new RunRequest { Builtins = t });
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
        }

        private sealed class NullMemberProvider : IModuleProvider
        {
            public string Name { get { return "holes"; } }

            public ModuleValue CreateInstance(EvalContext ctx)
            {
                var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
                m["missing"] = null;
                return ctx.Values.Module("holes", m);
            }
        }

        [Fact]
        public void AModuleWithANullMemberIsTheProvidersFailure()
        {
            var e = new ScriptEngine(new EngineOptions { ExtraModules = new IModuleProvider[] { new NullMemberProvider() } });
            RunResult r = e.Run("import holes\nholes.missing\n", null);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("missing", r.Error.Message);
        }
    }
}
