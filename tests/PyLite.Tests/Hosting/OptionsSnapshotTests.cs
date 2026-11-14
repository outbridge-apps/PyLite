using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // The second sandbox review (C5/C6): EngineOptions and RunRequest are documented as snapshots, but the
    // prelude list, the override tables and the request's lists were the host's own objects, read live: a host
    // changing them was seen inside later runs (1,104 of 1,200 runs faulted with overrides changed meanwhile).
    [Trait("Category", "Sandbox")]
    public class OptionsSnapshotTests
    {
        [Fact]
        public void ThePreludeListIsCopiedAtBuild()
        {
            var prelude = new List<string> { "lib" };
            var e = new ScriptEngine(new EngineOptions
            {
                PythonModules = new Dictionary<string, string> { ["lib"] = "ANSWER = 42\n" },
                PreludeModules = prelude,
            });
            prelude.Clear();
            prelude.Add("missing");
            RunResult r = e.Run("a = ANSWER\n", new RunRequest { ScalarOutputNames = new[] { "a" } });
            Assert.True(r.Succeeded, r.Error == null ? null : r.Error.Message);
            Assert.Equal("42", r.ScalarsOut["a"]);
        }

        [Fact]
        public void AnOverrideTableIsCopiedAtBuild()
        {
            var members = new Dictionary<string, BuiltinDelegate>
            {
                ["answer"] = (self, args, kw, c) => c.Values.Int(42),
            };
            var e = new ScriptEngine(new EngineOptions
            {
                ModuleOverrides = new Dictionary<string, IReadOnlyDictionary<string, BuiltinDelegate>> { ["math"] = members },
            });
            members["answer"] = (self, args, kw, c) => c.Values.Int(43);
            members["extra"] = (self, args, kw, c) => c.Values.Int(0);
            RunResult r = e.Run("import math\na = math.answer()\nb = hasattr(math, 'extra')\n",
                new RunRequest { ScalarOutputNames = new[] { "a", "b" } });
            Assert.True(r.Succeeded, r.Error == null ? null : r.Error.Message);
            Assert.Equal("42", r.ScalarsOut["a"]);
            Assert.Equal("False", r.ScalarsOut["b"]);
        }

        // The output names are read when the run ends: a name the host added meanwhile is not asked for.
        [Fact]
        public void TheRequestIsCopiedWhenTheRunStarts()
        {
            var outputs = new List<string> { "x" };
            var inputs = new Dictionary<string, string> { ["a"] = "1" };
            var request = new RunRequest
            {
                ScalarsIn = inputs,
                ScalarOutputNames = outputs,
                PrintSink = line =>
                {
                    outputs.Add("missing");
                    inputs["a"] = "2";
                },
            };
            RunResult r = new ScriptEngine(new EngineOptions()).Run("print('go')\nx = a\n", request);
            Assert.True(r.Succeeded, r.Error == null ? null : r.Error.Message);
            Assert.Equal("1", r.ScalarsOut["x"]);
            Assert.False(r.ScalarsOut.ContainsKey("missing"));
        }
    }
}
