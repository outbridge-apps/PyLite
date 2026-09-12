namespace Outbridge.PyLite.Hosting
{
    // explicit RAM-return knobs, defaults off. The AOS process is shared, so an
    // unconditional GC.Collect per run would trade RAM for pauses in other workloads; the hint runs on the
    // dying runner thread, is threshold-gated, and uses GCCollectionMode.Optimized (the CLR may decline).
    public sealed class MemoryHygieneOptions
    {
        // 0 (default) = never. If a completed run's Stats.AllocatedBytes exceeded this threshold, the
        // runner thread (NOT the caller) executes GC.Collect(2, Optimized) right before exiting.
        public long PostRunGcHintThresholdBytes { get; set; }

        // 0 (default) = never. Every N-th GC hint additionally sets
        // GCSettings.LargeObjectHeapCompactionMode = CompactOnce before collecting — defragments the LOH
        // after big-string runs (A8-#22).
        public int LohCompactionEveryNHints { get; set; }
    }
}
