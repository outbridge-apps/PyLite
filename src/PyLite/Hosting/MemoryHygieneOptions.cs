namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// Explicit knobs for giving memory back after big runs, off by default. The host process is shared, so an
    /// unconditional collection per run would trade memory for pauses in other work; the hint runs on the dying
    /// runner thread, only past a threshold, with <c>GCCollectionMode.Optimized</c> (the runtime may decline).
    /// </summary>
    public sealed class MemoryHygieneOptions
    {
        /// <summary>
        /// When a completed run's <see cref="RunStatistics.AllocatedBytes"/> exceeds this, its runner thread (not
        /// the caller) runs a generation-2 optimized collection just before it exits. 0 (the default): never.
        /// </summary>
        public long PostRunGcHintThresholdBytes { get; set; }

        /// <summary>
        /// Every N-th such collection also compacts the large object heap once, which defragments it after runs
        /// that built big strings. 0 (the default): never.
        /// </summary>
        public int LohCompactionEveryNHints { get; set; }
    }
}
