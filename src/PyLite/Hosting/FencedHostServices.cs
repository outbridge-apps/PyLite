using System;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Hosting
{
    // The host's clock as a run reads it: each read fenced like a host function, and what the contract asks
    // checked (a UTC instant, whole minutes under a day, a zone name). A clock that threw, or a null name,
    // was an engine fault; now the run ends as HostError naming the clock.
    internal sealed class FencedClock : IScriptClock
    {
        private readonly IScriptClock _inner;

        internal FencedClock(IScriptClock inner)
        {
            _inner = inner;
        }

        public DateTime UtcNow
        {
            get
            {
                DateTime t;
                int hostMark = RunnerThread.EnterHost(null);
                try
                {
                    t = _inner.UtcNow;
                }
                catch (Exception ex) when (!(ex is EngineAbort))
                {
                    throw HostCallGuard.Failure("clock", ex);
                }
                finally
                {
                    RunnerThread.LeaveHost(hostMark);
                }
                if (t.Kind != DateTimeKind.Utc)
                    throw Invalid("UtcNow is of DateTimeKind." + t.Kind + ", not Utc");
                return t;
            }
        }

        public TimeSpan LocalOffset
        {
            get
            {
                TimeSpan off;
                int hostMark = RunnerThread.EnterHost(null);
                try
                {
                    off = _inner.LocalOffset;
                }
                catch (Exception ex) when (!(ex is EngineAbort))
                {
                    throw HostCallGuard.Failure("clock", ex);
                }
                finally
                {
                    RunnerThread.LeaveHost(hostMark);
                }
                if (off.Ticks % TimeSpan.TicksPerMinute != 0 || off.Duration() >= TimeSpan.FromDays(1))
                    throw Invalid("LocalOffset must be whole minutes under 24 hours");
                return off;
            }
        }

        public string LocalName
        {
            get
            {
                string name;
                int hostMark = RunnerThread.EnterHost(null);
                try
                {
                    name = _inner.LocalName;
                }
                catch (Exception ex) when (!(ex is EngineAbort))
                {
                    throw HostCallGuard.Failure("clock", ex);
                }
                finally
                {
                    RunnerThread.LeaveHost(hostMark);
                }
                if (name == null)
                    throw Invalid("LocalName is null");
                return name;
            }
        }

        private static HostCallFailure Invalid(string what)
        {
            return new HostCallFailure("clock", new InvalidOperationException(what));
        }
    }

    // The host's sleep policy, fenced the same way. A quantum of -1 ms waited forever: every time.sleep leaked
    // its runner, and two such runs opened the circuit breaker for good.
    internal sealed class FencedSleepPolicy : ISleepPolicy
    {
        private static readonly TimeSpan MinQuantum = TimeSpan.FromMilliseconds(1);
        private static readonly TimeSpan MaxQuantum = TimeSpan.FromSeconds(1);
        private readonly ISleepPolicy _inner;

        internal FencedSleepPolicy(ISleepPolicy inner)
        {
            _inner = inner;
        }

        public TimeSpan Clamp(TimeSpan requested, TimeSpan sleptSoFar)
        {
            TimeSpan eff;
            int hostMark = RunnerThread.EnterHost(null);
            try
            {
                eff = _inner.Clamp(requested, sleptSoFar);
            }
            catch (Exception ex) when (!(ex is EngineAbort))
            {
                throw HostCallGuard.Failure("sleep policy", ex);
            }
            finally
            {
                RunnerThread.LeaveHost(hostMark);
            }
            if (eff < TimeSpan.Zero)
                throw Invalid("Clamp returned a negative wait");
            return eff;
        }

        public TimeSpan Quantum
        {
            get
            {
                TimeSpan q;
                int hostMark = RunnerThread.EnterHost(null);
                try
                {
                    q = _inner.Quantum;
                }
                catch (Exception ex) when (!(ex is EngineAbort))
                {
                    throw HostCallGuard.Failure("sleep policy", ex);
                }
                finally
                {
                    RunnerThread.LeaveHost(hostMark);
                }
                if (q < MinQuantum || q > MaxQuantum)
                    throw Invalid("Quantum must be 1 ms to 1 s, was " + q.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms");
                return q;
            }
        }

        private static HostCallFailure Invalid(string what)
        {
            return new HostCallFailure("sleep policy", new InvalidOperationException(what));
        }
    }
}

namespace Outbridge.PyLite.Hosting
{
    // An exception's text as the engine reports it: a host exception whose Message or ToString throws made
    // Run (and ScriptRunner.Run, which never throws) throw.
    internal static class HostText
    {
        internal static string Of(Exception ex)
        {
            if (ex == null)
                return "unknown error";
            string type = ex.GetType().Name;
            try
            {
                return type + ": " + ex.Message;
            }
            catch (Exception)
            {
                return type;
            }
        }

        internal static string Full(Exception ex)
        {
            try
            {
                return ex.ToString();
            }
            catch (Exception)
            {
                return Of(ex);
            }
        }
    }
}
