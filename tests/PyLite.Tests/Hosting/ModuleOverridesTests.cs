using System;
using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // T-HST-14 (RFC-003 §5.3): EngineOptions.ModuleOverrides — member-level stdlib patching.
    [Trait("Category", "Sandbox")]
    public class ModuleOverridesTests
    {
        private static EngineOptions WithOverrides(string module, IDictionary<string, BuiltinDelegate> members)
        {
            return new EngineOptions
            {
                ModuleOverrides = new Dictionary<string, IReadOnlyDictionary<string, BuiltinDelegate>>
                {
                    [module] = new Dictionary<string, BuiltinDelegate>(members) as IReadOnlyDictionary<string, BuiltinDelegate>,
                },
            };
        }

        private static string Out(RunResult r, string name)
        {
            Assert.True(r.Succeeded, r.Error == null ? "?" : r.Error.Message);
            return r.ScalarsOut[name];
        }

        [Fact]
        public void ReplaceMember()
        {
            // Replace time.sleep with a counting no-op — the canonical RFC-003 example.
            int calls = 0;
            var e = new ScriptEngine(WithOverrides("time", new Dictionary<string, BuiltinDelegate>
            {
                ["sleep"] = (self, args, kw, c) => { calls++; return c.Values.None; },
            }));
            RunResult r = e.Run("import time\ntime.sleep(999)\nx = 'done'\n",
                new RunRequest { ScalarOutputNames = new[] { "x" } });
            Assert.Equal("done", Out(r, "x"));
            Assert.Equal(1, calls);
        }

        [Fact]
        public void AddMember()
        {
            var e = new ScriptEngine(WithOverrides("math", new Dictionary<string, BuiltinDelegate>
            {
                ["answer"] = (self, args, kw, c) => c.Values.Int(42),
            }));
            RunResult r = e.Run("import math\nx = math.answer()\ny = math.floor(2.5)\n",
                new RunRequest { ScalarOutputNames = new[] { "x", "y" } });
            Assert.Equal("42", Out(r, "x"));
            Assert.Equal("2", r.ScalarsOut["y"]);   // untouched members survive
        }

        [Fact]
        public void RemoveMember()
        {
            var e = new ScriptEngine(WithOverrides("uuid", new Dictionary<string, BuiltinDelegate>
            {
                ["uuid1"] = null,   // null delegate = remove
            }));
            RunResult r = e.Run("import uuid\nx = hasattr = 'no'\ntry:\n    uuid.uuid1\nexcept AttributeError:\n    x = 'gone'\n",
                new RunRequest { ScalarOutputNames = new[] { "x" } });
            Assert.Equal("gone", Out(r, "x"));
        }

        [Fact]
        public void UnknownModuleFailsAtBuild()
        {
            var ex = Assert.Throws<ArgumentException>(() => new ScriptEngine(
                WithOverrides("no_such_module", new Dictionary<string, BuiltinDelegate>
                {
                    ["f"] = (self, args, kw, c) => c.Values.None,
                })));
            Assert.Contains("no_such_module", ex.Message);
        }

        [Fact]
        public void OverrideExceptionIsHostError()
        {
            // The overriding delegate is HostCallGuard-fenced: an arbitrary C# exception -> HostError.
            var e = new ScriptEngine(WithOverrides("math", new Dictionary<string, BuiltinDelegate>
            {
                ["sqrt"] = (self, args, kw, c) => { throw new InvalidOperationException("host bug"); },
            }));
            RunResult r = e.Run("import math\nx = math.sqrt(4)\n", new RunRequest());
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
        }

        [Fact]
        public void WholeModuleReplacementViaPythonModules()
        {
            // A PythonModules entry colliding with a stdlib name REPLACES it (last-wins policy).
            var e = new ScriptEngine(new EngineOptions
            {
                PythonModules = new Dictionary<string, string>
                {
                    ["html"] = "def escape(s):\n    return 'X' * len(s)\n",
                },
            });
            RunResult r = e.Run("import html\nx = html.escape('abc')\n",
                new RunRequest { ScalarOutputNames = new[] { "x" } });
            Assert.Equal("XXX", Out(r, "x"));
        }

        [Fact]
        public void OverrideAppliesToPythonModuleToo()
        {
            // Overrides post-process ANY registered module, including host Python-source ones.
            var e = new ScriptEngine(new EngineOptions
            {
                PythonModules = new Dictionary<string, string> { ["lib"] = "def f():\n    return 1\n" },
                ModuleOverrides = new Dictionary<string, IReadOnlyDictionary<string, BuiltinDelegate>>
                {
                    ["lib"] = new Dictionary<string, BuiltinDelegate>
                    {
                        ["f"] = (self, args, kw, c) => c.Values.Int(2),
                    },
                },
            });
            RunResult r = e.Run("import lib\nx = lib.f()\n",
                new RunRequest { ScalarOutputNames = new[] { "x" } });
            Assert.Equal("2", Out(r, "x"));
        }
    }
}
