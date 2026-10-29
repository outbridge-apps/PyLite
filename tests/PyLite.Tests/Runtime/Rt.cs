using System;
using System.Diagnostics;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;

namespace Outbridge.PyLite.Tests
{
    // Shared helper: a fresh EvalContext with a running clock, a fixed hash seed, an empty frozen
    // module registry and builtins table, a UTC SystemClock and the default sleep policy.
    internal static class Rt
    {
        public static EvalContext Ctx(ResourceLimits limits = null)
        {
            var lim = limits ?? ResourceLimits.Default;
            var budget = new Budget(lim, Stopwatch.StartNew(), CancellationToken.None);
            var modules = new ModuleRegistry();
            modules.Freeze();
            return new EvalContext(budget, lim, 0x12345678u, modules, HostFunctionTable.Empty,
                new SystemClock(TimeSpan.Zero, "UTC"), new DefaultSleepPolicy(), new RunStatistics());
        }
    }
}
