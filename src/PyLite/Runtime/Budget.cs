using System;
using System.Diagnostics;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime
{
    internal enum BudgetState { Normal, Terminating }

    // The single grow-only counter of steps/deadline/memory. Not thread-safe: one RunnerThread only.
    public sealed class Budget
    {
        private readonly ResourceLimits _limits;
        private readonly Stopwatch _clock;
        private readonly long _deadlineTicks;
        private readonly CancellationToken _cancel;

        private long _stepsUsed;
        private long _allocBytesUsed;
        private long _regexTicksUsed;
        private long _outputBytesUsed;
        private long _sleepMsUsed;
        private int _stepsSinceDeadlineCheck;

        private BudgetState _state;
        private EngineAbort _pendingAbort;
        private long _graceStepsRemaining;
        private long _graceDeadlineTicks;
        private bool _hardStop;

        public Budget(ResourceLimits limits, Stopwatch clock, CancellationToken cancel)
        {
            _limits = limits;
            _clock = clock;
            _cancel = cancel;
            _deadlineTicks = _clock.ElapsedTicks + MsToTicks(limits.DeadlineMs);
            _state = BudgetState.Normal;
        }

        private static long MsToTicks(int ms)
        {
            return (long)(Stopwatch.Frequency * (double)ms / 1000.0);
        }

        public void Step(int cost = 1)
        {
            if (_hardStop)
                throw _pendingAbort;
            if (_state == BudgetState.Normal)
            {
                checked
                {
                    _stepsUsed += cost;
                }

                if (_stepsUsed > _limits.MaxSteps)
                    throw CreateAbort(EngineAbortKind.Steps, "MaxSteps", _limits.MaxSteps, _stepsUsed);
            }
            else
            {
                _graceStepsRemaining -= cost;
                if (_graceStepsRemaining < 0)
                {
                    _hardStop = true;
                    throw _pendingAbort;
                }
            }
            _stepsSinceDeadlineCheck += cost;
            if (_stepsSinceDeadlineCheck >= BudgetCost.DeadlineCheckInterval)
            {
                _stepsSinceDeadlineCheck = 0;
                CheckDeadlineNow();
            }
        }

        // One linear pass over `units` items (chars, bytes, elements): LinearChunk steps per LinearChunkSize
        // of them, charged BEFORE the pass so the deadline is seen first. The pass itself is bounded by
        // MaxStrChars / MaxCollectionItems, which is what keeps a single call's overrun finite.
        public void ChargeLinear(long units)
        {
            long cost = BudgetCost.LinearChunk * (1 + units / BudgetCost.LinearChunkSize);
            Step(cost > int.MaxValue ? int.MaxValue : (int)cost);
        }

        public void CheckDeadlineNow()
        {
            if (_hardStop)
                throw _pendingAbort;
            if (_cancel.IsCancellationRequested)
            {
                if (_state == BudgetState.Normal)
                    throw CreateAbort(EngineAbortKind.Cancelled, "CancellationToken", 0, 0);
                _hardStop = true;
                throw _pendingAbort;
            }
            long now = _clock.ElapsedTicks;
            if (_state == BudgetState.Normal)
            {
                if (now > _deadlineTicks)
                    throw CreateAbort(EngineAbortKind.Deadline, "DeadlineMs", _limits.DeadlineMs, _clock.ElapsedMilliseconds);
            }
            else if (now > _graceDeadlineTicks)
            {
                _hardStop = true;
                throw _pendingAbort;
            }
        }

        public void ChargeAllocation(long bytes)
        {
            if (bytes < 0)
                throw new InvalidOperationException("negative allocation charge");
            if (_hardStop)
                throw _pendingAbort;
            long effectiveLimit = _limits.MaxAllocBytes
                + (_state == BudgetState.Terminating ? EngineHardLimits.TerminatingMemoryReserve : 0);
            checked { _allocBytesUsed += bytes; }
            if (_allocBytesUsed > effectiveLimit)
            {
                if (_state == BudgetState.Normal)
                    throw CreateAbort(EngineAbortKind.Memory, "MaxAllocBytes", _limits.MaxAllocBytes, _allocBytesUsed);
                _hardStop = true;
                throw _pendingAbort;
            }
            if (bytes > BudgetCost.LargeAllocThresholdBytes)
                CheckDeadlineNow();
        }

        public void PreCharge(long estimatedBytes) { ChargeAllocation(estimatedBytes); }

        public void ChargeRegexTime(TimeSpan consumed)
        {
            checked { _regexTicksUsed += consumed.Ticks; }
            if (TimeSpan.FromTicks(_regexTicksUsed).TotalMilliseconds > _limits.DeadlineMs)
                throw CreateAbort(EngineAbortKind.RegexTimeout, "RegexTotalTime", _limits.DeadlineMs,
                    (long)TimeSpan.FromTicks(_regexTicksUsed).TotalMilliseconds);
        }

        public void ChargeOutput(long bytes)
        {
            if (bytes < 0)
                throw new InvalidOperationException("negative output charge");
            checked { _outputBytesUsed += bytes; }
            if (_outputBytesUsed > _limits.MaxOutputBytes)
                throw CreateAbort(EngineAbortKind.OutputCap, "MaxOutputBytes", _limits.MaxOutputBytes, _outputBytesUsed);
        }

        public void ChargeSleep(int ms)
        {
            checked { _sleepMsUsed += ms; }
            if (_sleepMsUsed > _limits.SleepTotalMs)
                throw CreateAbort(EngineAbortKind.Deadline, "SleepTotalMs", _limits.SleepTotalMs, _sleepMsUsed);
        }

        public long StepsRemaining
        {
            get { return _state == BudgetState.Normal ? Math.Max(0, _limits.MaxSteps - _stepsUsed) : Math.Max(0, _graceStepsRemaining); }
        }

        public TimeSpan TimeRemaining
        {
            get
            {
                long target = _state == BudgetState.Normal ? _deadlineTicks : _graceDeadlineTicks;
                long remaining = target - _clock.ElapsedTicks;
                if (remaining < 0)
                    remaining = 0;
                return TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency);
            }
        }

        public long AllocRemaining { get { return Math.Max(0, _limits.MaxAllocBytes - _allocBytesUsed); } }
        public bool IsTerminating { get { return _state == BudgetState.Terminating; } }
        public bool HardStopRequested { get { return _hardStop; } }
        internal EngineAbort PendingAbort { get { return _pendingAbort; } }

        // Final-state snapshots for RunStatistics.
        internal long StepsUsed { get { return _stepsUsed; } }
        internal long AllocBytesUsed { get { return _allocBytesUsed; } }
        internal long OutputBytesUsed { get { return _outputBytesUsed; } }
        internal long RegexTicksUsed { get { return _regexTicksUsed; } }
        internal long SleepMsUsed { get { return _sleepMsUsed; } }

        internal void EnterTerminating(EngineAbort first)
        {
            _state = BudgetState.Terminating;
            _pendingAbort = first;
            _graceStepsRemaining = _limits.TerminatingGraceSteps;
            _graceDeadlineTicks = _clock.ElapsedTicks + MsToTicks(_limits.TerminatingGraceMs);
        }

        // Any thrown EngineAbort has already switched the budget into Terminating (invariant).
        internal EngineAbort CreateAbort(EngineAbortKind kind, string limitName, long limitValue, long actual)
        {
            EngineAbort abort = new EngineAbort(kind, limitName, limitValue, actual);
            if (_state == BudgetState.Normal)
                EnterTerminating(abort);
            return abort;
        }
    }
}
