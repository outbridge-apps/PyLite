namespace Outbridge.PyLite.Runtime
{
    // Central table of budget charge constants. No magic numbers elsewhere.
    internal static class BudgetCost
    {
        public const int BackEdge = 1;
        public const int Call = 2;
        public const int NodeBatch = 1;
        public const int NodeBatchSize = 64;
        public const int MoveNext = 1;
        public const int Raise = 4;
        public const int HandlerMatch = 1;
        public const int WalkNode = 1;
        public const int SortCompare = 1;
        public const int ProbeBatch = 1;
        public const int ProbeBatchSize = 8;
        public const int LinearChunk = 1;
        public const int LinearChunkSize = 4096;
        public const int RegexOp = 8;
        public const int ImportModule = 16;
        public const int SleepQuantum = 1;
        public const int BigOpThresholdBits = 4096;
        public const int LargeAllocThresholdBytes = 262_144;
        public const int DeadlineCheckInterval = 1024;
    }
}
