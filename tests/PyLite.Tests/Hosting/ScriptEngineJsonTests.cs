using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // PKG-07 JSON in/out marshaling (unblocked by impl/08 PyJson): RunRequest.JsonIn / JsonOutputNames.
    [Trait("Category", "Sandbox")]
    public class ScriptEngineJsonTests
    {
        private static ScriptEngine NewEngine()
        {
            return new ScriptEngine(new EngineOptions());
        }

        [Fact]
        public void JsonInputIsParsedAndBound()
        {
            RunResult r = NewEngine().Run("total = sum(data)\n", new RunRequest
            {
                JsonIn = new Dictionary<string, string> { ["data"] = "[1, 2, 3, 4]" },
                ScalarOutputNames = new[] { "total" },
            });
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.ToString());
            Assert.Equal("10", r.ScalarsOut["total"]);
        }

        [Fact]
        public void JsonInputObjectBecomesDict()
        {
            RunResult r = NewEngine().Run("name = cfg['name']\n", new RunRequest
            {
                JsonIn = new Dictionary<string, string> { ["cfg"] = "{\"name\": \"alice\", \"age\": 30}" },
                ScalarOutputNames = new[] { "name" },
            });
            Assert.Equal("alice", Get(r, "name"));
        }

        [Fact]
        public void JsonOutputIsSerialized()
        {
            RunResult r = NewEngine().Run("d = {'k': [1, 2], 'ok': True}\n", new RunRequest
            {
                JsonOutputNames = new[] { "d" },
            });
            Assert.True(r.Succeeded);
            Assert.Equal("{\"k\":[1,2],\"ok\":true}", r.JsonOut["d"]);   // compact, MarshalDefault
        }

        [Fact]
        public void JsonRoundTripThroughHost()
        {
            RunResult r = NewEngine().Run("out = data\n", new RunRequest
            {
                JsonIn = new Dictionary<string, string> { ["data"] = "{\"a\":[1,2,3],\"b\":null}" },
                JsonOutputNames = new[] { "out" },
            });
            Assert.Equal("{\"a\":[1,2,3],\"b\":null}", r.JsonOut["out"]);
        }

        [Fact]
        public void BrokenJsonInputIsHostError()
        {
            RunResult r = NewEngine().Run("x = 1\n", new RunRequest
            {
                JsonIn = new Dictionary<string, string> { ["bad"] = "{not json}" },
            });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("invalid JSON in input variable 'bad'", r.Error.Message);
        }

        [Fact]
        public void NonSerializableJsonOutputIsHostError()
        {
            RunResult r = NewEngine().Run("f = lambda: 1\n", new RunRequest
            {
                JsonOutputNames = new[] { "f" },
            });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("not serializable", r.Error.Message);
        }

        [Fact]
        public void NanJsonOutputIsHostError()
        {
            // MarshalDefault has allow_nan=false -> a NaN in the output is a HostError, not invalid JSON.
            RunResult r = NewEngine().Run("v = float('nan')\n", new RunRequest
            {
                JsonOutputNames = new[] { "v" },
            });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
        }

        // ScriptRunner facade — T-HST-09 test 6.
        [Fact]
        public void ScriptRunnerJsonRoundTrip()
        {
            var runner = new ScriptRunner(NewEngine());
            runner.SetJson("d", "{\"k\": 1}");
            runner.DeclareJsonOutput("d");
            Assert.Equal(0, runner.Run("d['k'] += 1\n"));
            Assert.Equal("{\"k\":2}", runner.GetJson("d"));
        }

        private static string Get(RunResult r, string name)
        {
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.ToString());
            return r.ScalarsOut[name];
        }
    }
}
