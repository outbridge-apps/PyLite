namespace Outbridge.PyLite.Runtime
{
    // Central table of budget charge constants (byte sizes charged in place are still spelled at their sites).
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
        public const int TreeNodeStride = 8;   // tree-walk nodes per step
        public const int MatchStride = 8;      // regex matches per step
    }
}
