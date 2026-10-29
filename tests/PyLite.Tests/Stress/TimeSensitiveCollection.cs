using Xunit;

namespace Outbridge.PyLite.Tests.Stress
{
    // Tests that assert WALL-CLOCK bounds (a bomb stops near its deadline, the engine did not hang)
    // cannot run in parallel with the rest of the suite: 4000 sibling tests saturating the box push a
    // healthy stop past the ceiling and the cap fires on host load rather than on engine behaviour.
    // xUnit runs DisableParallelization collections in their own sequential pass, so the window is
    // quiet — the same fix HeapSensitive already uses for absolute-heap deltas.
    [CollectionDefinition("TimeSensitive", DisableParallelization = true)]
    public sealed class TimeSensitiveCollection { }
}
