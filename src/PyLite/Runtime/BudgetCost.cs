namespace Outbridge.PyLite.Runtime
{
    // Central table of budget charge constants. No magic numbers elsewhere.
    internal static class BudgetCost
    {
        public const int BackEdge = 1;
        public const int Call = 2;
        public const int NodeBatch = 1;
        public const int Raise = 4;
        public const int HandlerMatch = 1;
        public const int LinearChunk = 1;
        public const int LinearChunkSize = 4096;
        public const int ImportModule = 16;
        public const int LargeAllocThresholdBytes = 262_144;
        public const int DeadlineCheckInterval = 1024;
    }
}
