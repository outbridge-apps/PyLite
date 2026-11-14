using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Modules;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // What one run leaves to the process and to later runs (the 2026-10 sandbox review): a module compiled
    // at a script's stack depth, the compiled-script cache, the host traceback.
    [Trait("Category", "Sandbox")]
    public class CrossRunStateTests
    {
        private const string TemplateResource = "Outbridge.PyLite.string_template.py";

        // A run that parks its thread just above the stack margin and then imports string made the parser's
        // probe fail on our own module source; the process-wide Lazy cached that, and every later import
        // string in every engine was an engine fault until the process restarted.
        [Fact]
        public void AnEmbeddedModuleCompilesFromAnExhaustedStack()
        {
            Exception direct = null, fresh = null;
            CompiledScript compiled = null;
            var t = new Thread(() =>
            {
                // JIT everything first: the JIT runs on this thread and needs stack the edge does not have.
                PythonSourceModules.CompileResource(TemplateResource, "string");
                PythonSourceModules.CompileOnFreshStack(TemplateResource, "string");
                Catch(() => { });
                direct = AtStackEdge(() => Catch(() => PythonSourceModules.CompileResource(TemplateResource, "string")));
                fresh = AtStackEdge(() => Catch(() => compiled = PythonSourceModules.CompileOnFreshStack(TemplateResource, "string")));
            }, 1024 * 1024);
            t.Start();
            t.Join();
            Assert.NotNull(direct);   // the shape that broke: compiling on the exhausted stack itself
            Assert.Null(fresh);
            Assert.NotNull(compiled);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Exception AtStackEdge(Func<Exception> atEdge)
        {
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                return atEdge();
            }
            Exception r = AtStackEdge(atEdge);
            GC.KeepAlive(atEdge);   // not a tail call: every level keeps its frame
            return r;
        }

        private static Exception Catch(Action a)
        {
            try
            {
                a();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        // The cache was bounded by entries only, and an AST takes up to ~80 bytes per source char: 256 large
        // scripts pinned gigabytes. It now holds at most 2M chars of source (or 8/3 of the tokens).
        [Fact]
        public void TheCompiledScriptCacheIsBoundedBySourceSize()
        {
            var engine = new ScriptEngine(new EngineOptions());
            string filler = new string('a', 450000);   // under the 512K source limit
            for (int i = 0; i < 8; i++)
            {
                engine.Compile("x = " + i + "\n# " + filler + "\n");
                Assert.True(engine.CachedSourceChars <= 2000000, "cached " + engine.CachedSourceChars + " chars");
            }
        }

        // The host's ScriptTraceback had no cap: long def names through deep frames and a long chain made a
        // traceback of 136M chars per run. It stops at MaxTracebackChars, marked.
        [Fact]
        public void TheHostTracebackIsCapped()
        {
            string name = "F" + new string('a', 100000);
            string src = "def " + name + "(n):\n    if n == 0:\n        raise ValueError('v')\n    " + name + "(n - 1)\n"
                + name + "(30)\n";
            RunResult r = new ScriptEngine(new EngineOptions()).Run(src, new RunRequest());
            Assert.Equal(ScriptErrorKind.RuntimeError, r.Error.Kind);
            Assert.True(r.Error.ScriptTraceback.Length <= EngineHardLimits.MaxTracebackChars + 64,
                "traceback of " + r.Error.ScriptTraceback.Length + " chars");
            Assert.EndsWith("[... traceback truncated ...]", r.Error.ScriptTraceback);
        }

        // raise e from e rendered the same exception 17 times; CPython stops at an exception it has shown.
        [Fact]
        public void ACauseCycleIsRenderedOnce()
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("e = ValueError('x')\nraise e from e\n", new RunRequest());
            string tb = r.Error.ScriptTraceback;
            Assert.Equal(1, Count(tb, "ValueError: x"));
        }

        // type() and iterator types were cached by name for the whole process, 1,024 names at most; a run that
        // named 1,100 classes filled both, and from then on type(a) is type(b) was False for two re.Match
        // objects in every engine until the process restarted. A type object now lives with its type.
        [Fact]
        public void ARunNamingManyClassesLeavesTypeIdentityAlone()
        {
            var polluter = new StringBuilder("from dataclasses import make_dataclass\n"
                + "for i in range(1100):\n    type(make_dataclass('Z' + str(i), ['a']))\n");
            for (int i = 0; i < 1100; i++)
            {
                polluter.Append("class C").Append(i).Append(":\n    def __init__(self):\n        self.a = [1]\n")
                    .Append("    def __reversed__(self):\n        return iter(self.a)\n")
                    .Append("reversed(C").Append(i).Append("())\n");
            }
            RunResult p = new ScriptEngine(new EngineOptions()).Run(polluter.ToString(), new RunRequest());
            Assert.True(p.Succeeded, p.Succeeded ? null : p.Error.Message);
            const string victim = "import re, itertools\n"
                + "m1 = re.match('a', 'a')\nm2 = re.match('a', 'a')\n"
                + "p1 = itertools.pairwise([1, 2])\np2 = itertools.pairwise([1, 2])\n"
                + "result = [type(m1) is type(m2), type(p1) is type(p2), {type(m1): 1}.get(type(m2))]\n";
            RunResult v = new ScriptEngine(new EngineOptions()).Run(victim, new RunRequest { ScalarOutputNames = new[] { "result" } });
            Assert.True(v.Succeeded, v.Succeeded ? null : v.Error.Message);
            Assert.Equal("[True, True, 1]", v.ScalarsOut["result"]);
        }

        private static int Count(string s, string part)
        {
            int n = 0;
            for (int i = s.IndexOf(part, StringComparison.Ordinal); i >= 0; i = s.IndexOf(part, i + 1, StringComparison.Ordinal))
                n++;
            return n;
        }
    }

    // The inline call cache on a cached script's AST kept the TypeInfo of a namedtuple the run created,
    // slot table and all: +205 MB per run of a 1M-field namedtuple, never released.
    [Collection("HeapSensitive")]
    [Trait("Category", "ResourceLimits")]
    [Trait("Category", "MachineSensitive")]
    public class CrossRunMemoryTests
    {
        [Fact]
        public void ACachedScriptKeepsNoTypeItsRunCreated()
        {
            var engine = TestEngines.WithCeiling(TestEngines.Unhurried);
            const string src = "from collections import namedtuple\n"
                + "P = namedtuple('P', ['f%d' % i for i in range(200000)])\np = P._make(range(200000))\np.count(-1)\n";
            var req = new RunRequest { Limits = TestEngines.Unhurried };
            engine.Run("x = 1\n", req);   // the engine's own lazy state is not what is measured
            long before = GC.GetTotalMemory(true);
            RunResult r = engine.Run(src, req);
            long after = GC.GetTotalMemory(true);
            Assert.True(r.Succeeded, r.Succeeded ? null : r.Error.Message);
            Assert.True(after - before < 8L << 20, "kept " + ((after - before) >> 20) + " MB after the run");
            GC.KeepAlive(engine);
        }

        // In parallel the same was a race: a run that found another run's namedtuple in the call-site cache
        // did not register its own, and the other run's end cleared the site just before it wrote: 1 storm in
        // 8 kept a 100,000-field type for the life of the cached script. Each run now clears its own entry.
        [Fact]
        public void ParallelRunsOfACachedScriptLeaveNoTypeInItsCallSites()
        {
            const string src = "import collections\n"
                + "P = collections.namedtuple('P', ['f%d' % i for i in range(50)])\np = P._make(range(50))\n"
                + "for i in range(3000):\n    p.index(0)\n";
            for (int storm = 0; storm < 6; storm++)
            {
                var engine = new ScriptEngine(new EngineOptions());
                CompiledScript compiled = engine.Compile(src);
                int left = 48, fails = 0;
                var threads = new Thread[8];
                for (int t = 0; t < threads.Length; t++)
                {
                    threads[t] = new Thread(() =>
                    {
                        while (Interlocked.Decrement(ref left) >= 0)
                        {
                            if (!engine.Run(compiled, new RunRequest()).Succeeded)
                                Interlocked.Increment(ref fails);
                        }
                    }) { IsBackground = true };
                    threads[t].Start();
                }
                foreach (Thread t in threads)
                    Assert.True(t.Join(120000));
                Assert.Equal(0, fails);
                Assert.Equal(0, CallSitesHoldingAScriptType(compiled));
            }
        }

        // Every CallNode reachable from the compiled script whose inline cache holds a script-defined type.
        private static int CallSitesHoldingAScriptType(object root)
        {
            var seen = new System.Collections.Generic.HashSet<object>(new RefEq());
            var stack = new System.Collections.Generic.Stack<object>();
            stack.Push(root);
            int held = 0;
            while (stack.Count > 0)
            {
                object o = stack.Pop();
                if (o == null || o is string || o.GetType().IsPrimitive || !seen.Add(o))
                    continue;
                var call = o as Outbridge.PyLite.Syntax.Ast.CallNode;
                var slot = call == null ? null : call.CachedCallSlot as Outbridge.PyLite.Runtime.Evaluator.SlotCache;
                if (slot != null && slot.Type.IsScriptDefined)
                    held++;
                var many = o as System.Collections.IEnumerable;
                if (many != null)
                {
                    foreach (object x in many)
                        stack.Push(x);
                    continue;
                }
                if (o.GetType().Namespace == null || !o.GetType().Namespace.StartsWith("Outbridge.PyLite.Syntax", StringComparison.Ordinal))
                    continue;
                foreach (System.Reflection.FieldInfo f in o.GetType().GetFields(System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    if (!f.FieldType.IsValueType && f.Name != "CachedCallSlot")
                        stack.Push(f.GetValue(o));
                }
            }
            return held;
        }

        private sealed class RefEq : System.Collections.Generic.IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return RuntimeHelpers.GetHashCode(o); }
        }
    }
}
