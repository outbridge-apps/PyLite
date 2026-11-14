using System;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// The limits a run is held to, immutable. A run that reaches one ends with
    /// <see cref="ScriptErrorKind.BudgetExceeded"/>, which the script cannot catch; only
    /// <see cref="MaxDataDepth"/> raises a script exception instead.
    /// </summary>
    public sealed class ResourceLimits
    {
        /// <summary>The steps a run may take: each statement, call and unit of native work is charged.</summary>
        public long MaxSteps { get; }

        /// <summary>The run's wall-clock time, in milliseconds.</summary>
        public int DeadlineMs { get; }

        /// <summary>The bytes a run may allocate, charged as values and buffers are made.</summary>
        public long MaxAllocBytes { get; }

        /// <summary>The deepest call nesting.</summary>
        public int MaxCallDepth { get; }

        /// <summary>
        /// The deepest data nesting a comparison, hash or repr descends; past it the script gets a catchable
        /// <c>RecursionError</c>.
        /// </summary>
        public int MaxDataDepth { get; }

        /// <summary>The longest string.</summary>
        public int MaxStrChars { get; }

        /// <summary>The most items in one collection.</summary>
        public int MaxCollectionItems { get; }

        /// <summary>The widest int, in bits.</summary>
        public int MaxIntBits { get; }

        /// <summary>The bytes a run may print.</summary>
        public int MaxOutputBytes { get; }

        /// <summary>The time one regular expression operation may take, in milliseconds.</summary>
        public int RegexPerOpTimeoutMs { get; }

        /// <summary>The total <c>time.sleep</c> a run may wait, in milliseconds.</summary>
        public int SleepTotalMs { get; }

        /// <summary>
        /// The steps a run may still take after a limit is reached, while it unwinds; the engine's, not per run.
        /// </summary>
        public int TerminatingGraceSteps { get; }

        /// <summary>The time a run may still take after a limit is reached; the engine's, not per run.</summary>
        public int TerminatingGraceMs { get; }

        /// <summary>Limits with every value given; each must be positive.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A value is zero or negative.</exception>
        public ResourceLimits(long maxSteps, int deadlineMs, long maxAllocBytes, int maxCallDepth,
            int maxDataDepth, int maxStrChars, int maxCollectionItems, int maxIntBits, int maxOutputBytes,
            int regexPerOpTimeoutMs, int sleepTotalMs, int terminatingGraceSteps, int terminatingGraceMs)
        {
            RequirePositive(maxSteps, nameof(maxSteps));
            RequirePositive(deadlineMs, nameof(deadlineMs));
            RequirePositive(maxAllocBytes, nameof(maxAllocBytes));
            RequirePositive(maxCallDepth, nameof(maxCallDepth));
            RequirePositive(maxDataDepth, nameof(maxDataDepth));
            RequirePositive(maxStrChars, nameof(maxStrChars));
            RequirePositive(maxCollectionItems, nameof(maxCollectionItems));
            RequirePositive(maxIntBits, nameof(maxIntBits));
            RequirePositive(maxOutputBytes, nameof(maxOutputBytes));
            RequirePositive(regexPerOpTimeoutMs, nameof(regexPerOpTimeoutMs));
            RequirePositive(sleepTotalMs, nameof(sleepTotalMs));
            RequirePositive(terminatingGraceSteps, nameof(terminatingGraceSteps));
            RequirePositive(terminatingGraceMs, nameof(terminatingGraceMs));

            MaxSteps = maxSteps;
            DeadlineMs = deadlineMs;
            MaxAllocBytes = maxAllocBytes;
            MaxCallDepth = maxCallDepth;
            MaxDataDepth = maxDataDepth;
            MaxStrChars = maxStrChars;
            MaxCollectionItems = maxCollectionItems;
            MaxIntBits = maxIntBits;
            MaxOutputBytes = maxOutputBytes;
            RegexPerOpTimeoutMs = regexPerOpTimeoutMs;
            SleepTotalMs = sleepTotalMs;
            TerminatingGraceSteps = terminatingGraceSteps;
            TerminatingGraceMs = terminatingGraceMs;
        }

        private static void RequirePositive(long value, string field)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(field, "must be positive");
        }

        /// <summary>
        /// The defaults: 200M steps, 20 s, 256 MB, call depth 200, data depth 128, 16M-char strings, 5M items,
        /// 1M-bit ints, 16 MB of output, 250 ms per regex operation, 5 s of sleep.
        /// </summary>
        public static ResourceLimits Default { get; } = new ResourceLimits(
            maxSteps: 200_000_000, deadlineMs: 20_000, maxAllocBytes: 268_435_456, maxCallDepth: 200,
            maxDataDepth: 128, maxStrChars: 16_000_000, maxCollectionItems: 5_000_000, maxIntBits: 1_000_000,
            maxOutputBytes: 16_777_216, regexPerOpTimeoutMs: 250, sleepTotalMs: 5_000,
            terminatingGraceSteps: 100_000, terminatingGraceMs: 250);

        /// <summary>The defaults with 5 s and 64 MB: for runs a user waits on.</summary>
        public static ResourceLimits Interactive { get; } =
            Default.With(deadlineMs: 5_000, maxAllocBytes: 67_108_864);

        /// <summary>The defaults with 60 s and 512 MB: for background work.</summary>
        public static ResourceLimits Batch { get; } =
            Default.With(deadlineMs: 60_000, maxAllocBytes: 536_870_912);

        /// <summary>A copy with the given limits changed and the rest kept; the grace values stay the engine's.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A given value is zero or negative.</exception>
        public ResourceLimits With(long? maxSteps = null, int? deadlineMs = null, long? maxAllocBytes = null,
            int? maxCallDepth = null, int? maxDataDepth = null, int? maxStrChars = null,
            int? maxCollectionItems = null, int? maxIntBits = null, int? maxOutputBytes = null,
            int? regexPerOpTimeoutMs = null, int? sleepTotalMs = null)
        {
            return new ResourceLimits(
                maxSteps ?? MaxSteps, deadlineMs ?? DeadlineMs, maxAllocBytes ?? MaxAllocBytes,
                maxCallDepth ?? MaxCallDepth, maxDataDepth ?? MaxDataDepth, maxStrChars ?? MaxStrChars,
                maxCollectionItems ?? MaxCollectionItems, maxIntBits ?? MaxIntBits, maxOutputBytes ?? MaxOutputBytes,
                regexPerOpTimeoutMs ?? RegexPerOpTimeoutMs, sleepTotalMs ?? SleepTotalMs,
                TerminatingGraceSteps, TerminatingGraceMs);
        }

        /// <summary>
        /// These limits lowered to <paramref name="serverCeiling"/> where they are higher, and to the engine's hard
        /// maximums: a run asking for more than its engine allows gets what the engine allows. This is what the
        /// engine does with <see cref="RunRequest.Limits"/>.
        /// </summary>
        /// <param name="serverCeiling">The ceiling, usually <see cref="EngineOptions.DefaultLimits"/>; null: the hard maximums alone.</param>
        public ResourceLimits ClampTo(ResourceLimits serverCeiling)
        {
            if (serverCeiling == null)
                serverCeiling = this;   // no ceiling: the engine hard caps alone
            return new ResourceLimits(
                Math.Min(Math.Min(MaxSteps, serverCeiling.MaxSteps), EngineHardLimits.MaxSteps),
                (int)Math.Min(Math.Min((long)DeadlineMs, serverCeiling.DeadlineMs), EngineHardLimits.MaxDeadlineMs),
                Math.Min(Math.Min(MaxAllocBytes, serverCeiling.MaxAllocBytes), EngineHardLimits.MaxAllocBytes),
                (int)Math.Min(Math.Min((long)MaxCallDepth, serverCeiling.MaxCallDepth), EngineHardLimits.MaxCallDepth),
                Math.Min(Math.Min(MaxDataDepth, serverCeiling.MaxDataDepth), EngineHardLimits.MaxDataDepth),
                Math.Min(MaxStrChars, serverCeiling.MaxStrChars),
                Math.Min(MaxCollectionItems, serverCeiling.MaxCollectionItems),
                Math.Min(Math.Min(MaxIntBits, serverCeiling.MaxIntBits), EngineHardLimits.MaxIntBits),
                Math.Min(MaxOutputBytes, serverCeiling.MaxOutputBytes),
                Math.Min(RegexPerOpTimeoutMs, serverCeiling.RegexPerOpTimeoutMs),
                Math.Min(SleepTotalMs, serverCeiling.SleepTotalMs),
                serverCeiling.TerminatingGraceSteps, serverCeiling.TerminatingGraceMs);
        }
    }

    // Absolute engine maximums; no per-run configuration can exceed these.
    internal static class EngineHardLimits
    {
        public const int MaxDeadlineMs = 300_000;
        public const long MaxSteps = 2_000_000_000;
        public const long MaxAllocBytes = 1_073_741_824;
        public const int MaxCallDepth = 512;
        public const int MaxDataDepth = 10_000;   // leaf re-entry is a C# frame per level: far inside the 32 MB stack
        // Long big-int operations stop at the deadline inside BigKernel; the cap bounds one value's memory.
        public const int MaxIntBits = 4_000_000;
        [Obsolete("Ignored: the engine does not limit how many runs execute at once.")]
        public const int MaxConcurrentRuns = 32;
        public const long TerminatingMemoryReserve = 1_048_576;
        public const int MaxErrorMessageChars = 2_048;
        public const int MaxTracebackFrames = 32;
        public const int MaxExceptionChainDepth = 16;
        public const int MaxTracebackChars = 262_144;   // ScriptError.ScriptTraceback, marked when cut
    }
}
