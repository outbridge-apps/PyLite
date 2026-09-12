using System;

namespace Outbridge.PyLite.Hosting
{
    // Immutable per-run resource limits.
    public sealed class ResourceLimits
    {
        public long MaxSteps { get; }
        public int DeadlineMs { get; }
        public long MaxAllocBytes { get; }
        public int MaxCallDepth { get; }
        public int MaxDataDepth { get; }
        public int MaxStrChars { get; }
        public int MaxCollectionItems { get; }
        public int MaxIntBits { get; }
        public int MaxOutputBytes { get; }
        public int RegexPerOpTimeoutMs { get; }
        public int SleepTotalMs { get; }
        public int TerminatingGraceSteps { get; }   // not configurable per-run
        public int TerminatingGraceMs { get; }      // not configurable per-run

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

        public static ResourceLimits Default { get; } = new ResourceLimits(
            maxSteps: 200_000_000, deadlineMs: 20_000, maxAllocBytes: 268_435_456, maxCallDepth: 200,
            maxDataDepth: 128, maxStrChars: 16_000_000, maxCollectionItems: 5_000_000, maxIntBits: 1_000_000,
            maxOutputBytes: 16_777_216, regexPerOpTimeoutMs: 250, sleepTotalMs: 5_000,
            terminatingGraceSteps: 100_000, terminatingGraceMs: 250);

        public static ResourceLimits Interactive { get; } =
            Default.With(deadlineMs: 5_000, maxAllocBytes: 67_108_864);

        public static ResourceLimits Batch { get; } =
            Default.With(deadlineMs: 60_000, maxAllocBytes: 536_870_912);

        // Builder replacement for init-only (C# 7.3). TerminatingGrace* stay fixed (not per-run configurable).
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

        // Downward-only merge of per-run limits onto a server ceiling; then clamp to engine hard maximums.
        public ResourceLimits ClampTo(ResourceLimits serverCeiling)
        {
            return new ResourceLimits(
                Math.Min(Math.Min(MaxSteps, serverCeiling.MaxSteps), EngineHardLimits.MaxSteps),
                (int)Math.Min(Math.Min((long)DeadlineMs, serverCeiling.DeadlineMs), EngineHardLimits.MaxDeadlineMs),
                Math.Min(Math.Min(MaxAllocBytes, serverCeiling.MaxAllocBytes), EngineHardLimits.MaxAllocBytes),
                (int)Math.Min(Math.Min((long)MaxCallDepth, serverCeiling.MaxCallDepth), EngineHardLimits.MaxCallDepth),
                Math.Min(MaxDataDepth, serverCeiling.MaxDataDepth),
                Math.Min(MaxStrChars, serverCeiling.MaxStrChars),
                Math.Min(MaxCollectionItems, serverCeiling.MaxCollectionItems),
                Math.Min(MaxIntBits, serverCeiling.MaxIntBits),
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
        public const int MaxConcurrentRuns = 32;
        public const long TerminatingMemoryReserve = 1_048_576;
        public const int MaxErrorMessageChars = 2_048;
        public const int MaxTracebackFrames = 32;
        public const int MaxExceptionChainDepth = 16;
    }
}
