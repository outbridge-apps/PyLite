using System;

namespace Outbridge.PyLite.Runtime.Errors
{
    // The budget/resource error channel. Deliberately NOT a ScriptException: the script's except
    // dispatcher must never catch it.
    internal enum EngineAbortKind
    {
        Deadline,       // DeadlineMs or SleepTotalMs expired
        Steps,          // MaxSteps exhausted
        Memory,         // MaxAllocBytes exhausted OR an object cap (str/collection/int-bits)
        Recursion,      // CallDepth OR InsufficientExecutionStackException OR data-depth > 128
        RegexTimeout,   // per-op or total regex budget
        OutputCap,      // MaxOutputBytes
        Cancelled       // external CancellationToken
    }

    // Wrapper-unwrap application points (a contract for other packages):
    //  1. the evaluator's except dispatcher (before matching clauses);
    //  2. the host-call boundary in Evaluator.Call;
    //  3. our merge sort when BCL wraps a callback;
    //  4. RunnerThread.ThreadMain top-level fault barrier;
    //  5. iterator/genexp finalization during unwinding.
    internal sealed class EngineAbort : Exception
    {
        public EngineAbortKind Kind { get; }
        public string LimitName { get; }
        public long LimitValue { get; }
        public long ActualValue { get; }

        public EngineAbort(EngineAbortKind kind, string limitName, long limitValue, long actualValue)
            : base(FormatMessage(kind, limitName, limitValue))
        {
            Kind = kind;
            LimitName = limitName;
            LimitValue = limitValue;
            ActualValue = actualValue;
        }

        private static string FormatMessage(EngineAbortKind kind, string limitName, long limitValue)
        {
            switch (kind)
            {
                case EngineAbortKind.Deadline:
                    return "script exceeded time limit (" + limitValue + " ms)";
                case EngineAbortKind.Steps:
                    return "script exceeded step limit (" + limitValue + ")";
                case EngineAbortKind.Memory:
                    return "script exceeded memory limit (" + limitName + "=" + limitValue + ")";
                case EngineAbortKind.Recursion:
                    return "maximum recursion depth exceeded";
                case EngineAbortKind.RegexTimeout:
                    return "regular expression exceeded time limit";
                case EngineAbortKind.OutputCap:
                    return "script output exceeded limit (" + limitValue + " bytes)";
                case EngineAbortKind.Cancelled:
                    return "script execution was cancelled by host";
                default:
                    return "engine abort";
            }
        }

        // Unwrap through InnerException (cap 8) + AggregateException.Flatten.
        public static EngineAbort TryUnwrap(Exception ex)
        {
            if (ex == null)
                return null;

            EngineAbort direct = ex as EngineAbort;
            if (direct != null)
                return direct;

            EngineAbort inAgg = ScanAggregate(ex as AggregateException);
            if (inAgg != null)
                return inAgg;

            Exception cur = ex.InnerException;
            int depth = 0;
            while (cur != null && depth < 8)
            {
                EngineAbort ea = cur as EngineAbort;
                if (ea != null)
                    return ea;
                EngineAbort aggHit = ScanAggregate(cur as AggregateException);
                if (aggHit != null)
                    return aggHit;
                cur = cur.InnerException;
                depth++;
            }
            return null;
        }

        private static EngineAbort ScanAggregate(AggregateException agg)
        {
            if (agg == null)
                return null;
            foreach (Exception inner in agg.Flatten().InnerExceptions)
            {
                EngineAbort ea = inner as EngineAbort;
                if (ea != null)
                    return ea;
            }
            return null;
        }

        public static bool IsOrWraps(Exception ex)
        {
            return TryUnwrap(ex) != null;
        }
    }
}
