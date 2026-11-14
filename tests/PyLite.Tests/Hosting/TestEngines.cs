using Outbridge.PyLite.Hosting;

namespace Outbridge.PyLite.Tests
{
    // A request's limits are only ever lowered to the engine's DefaultLimits (the host's ceiling), so a test that
    // asks for more than the defaults on new EngineOptions() runs with 20 s and 200M steps all the same, and a
    // loaded machine ends it on the deadline. These give the ceiling the test means.
    internal static class TestEngines
    {
        // For a test whose subject is steps, memory or a limit other than the deadline: the deadline is only a
        // guard against a hang, six times the default, so a busy machine cannot decide the outcome; the same for a regex
        // operation.
        public static readonly ResourceLimits Unhurried = ResourceLimits.Default.With(deadlineMs: 120_000, regexPerOpTimeoutMs: 60_000);

        public static ScriptEngine WithCeiling(ResourceLimits ceiling)
        {
            return new ScriptEngine(new EngineOptions { DefaultLimits = ceiling });
        }
    }
}
