using Xunit;

namespace Outbridge.PyLite.Tests.Stress
{
    // Tests that measure absolute process-heap deltas (GC.GetTotalMemory before/after) cannot run in
    // parallel with other tests — concurrent allocations from sibling collections land between the two
    // samples and dwarf the per-run signal. Marking them one non-parallel collection makes the delta
    // clean again (as it already is in isolation). xUnit 2.6+ honours DisableParallelization here.
    [CollectionDefinition("HeapSensitive", DisableParallelization = true)]
    public sealed class HeapSensitiveCollection { }
}
