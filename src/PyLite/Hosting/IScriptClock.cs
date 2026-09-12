using System;

namespace Outbridge.PyLite.Hosting
{
    // Time source for the engine. The engine never calls DateTime.Now — all wall-clock
    // access goes through this. Consumed by the datetime/time modules; carried by EvalContext.
    public interface IScriptClock
    {
        DateTime UtcNow { get; }      // Kind must be Utc (validated at Run start)
        TimeSpan LocalOffset { get; } // fixed offset east of UTC; whole minutes, |off| < 24h
        string LocalName { get; }     // zone name, ordinal, not null
    }

    // Real-time clock with a host-configured fixed local zone.
    public sealed class SystemClock : IScriptClock
    {
        private readonly TimeSpan _offset;
        private readonly string _name;

        public SystemClock(TimeSpan localOffset, string localName)
        {
            _offset = localOffset;
            _name = localName ?? "UTC";
        }

        public DateTime UtcNow { get { return DateTime.UtcNow; } }
        public TimeSpan LocalOffset { get { return _offset; } }
        public string LocalName { get { return _name; } }
    }

    // Deterministic clock for tests: a frozen instant.
    public sealed class FrozenClock : IScriptClock
    {
        private readonly DateTime _utc;
        private readonly TimeSpan _offset;
        private readonly string _name;

        public FrozenClock(DateTime utc, TimeSpan localOffset, string localName)
        {
            _utc = utc;
            _offset = localOffset;
            _name = localName ?? "UTC";
        }

        public DateTime UtcNow { get { return _utc; } }
        public TimeSpan LocalOffset { get { return _offset; } }
        public string LocalName { get { return _name; } }
    }
}
