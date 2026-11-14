using System;

namespace Outbridge.PyLite.Hosting
{
    /// <summary>
    /// The engine's only source of wall-clock time (it never reads <c>DateTime.Now</c>): <c>datetime</c>,
    /// <c>time</c> and <c>uuid1</c> read it.
    /// </summary>
    public interface IScriptClock
    {
        /// <summary>The current instant; its <c>Kind</c> must be <c>Utc</c> (checked at each read: any other ends the run as a host error).</summary>
        DateTime UtcNow { get; }

        /// <summary>The local zone's fixed offset east of UTC: whole minutes, less than 24 hours either way.</summary>
        TimeSpan LocalOffset { get; }

        /// <summary>The local zone's name (<c>time.tzname</c>, <c>%Z</c>); not null.</summary>
        string LocalName { get; }
    }

    /// <summary>The real clock, with a local zone the host fixes.</summary>
    public sealed class SystemClock : IScriptClock
    {
        private readonly TimeSpan _offset;
        private readonly string _name;

        /// <summary>A real clock whose local zone is <paramref name="localOffset"/> east of UTC.</summary>
        /// <param name="localOffset">Whole minutes, less than 24 hours either way.</param>
        /// <param name="localName">The zone's name; null reads as "UTC".</param>
        public SystemClock(TimeSpan localOffset, string localName)
        {
            _offset = localOffset;
            _name = localName ?? "UTC";
        }

        /// <inheritdoc/>
        public DateTime UtcNow { get { return DateTime.UtcNow; } }

        /// <inheritdoc/>
        public TimeSpan LocalOffset { get { return _offset; } }

        /// <inheritdoc/>
        public string LocalName { get { return _name; } }
    }

    /// <summary>A clock stopped at one instant, for tests and reproducible runs.</summary>
    public sealed class FrozenClock : IScriptClock
    {
        private readonly DateTime _utc;
        private readonly TimeSpan _offset;
        private readonly string _name;

        /// <summary>A clock that always answers <paramref name="utc"/>.</summary>
        /// <param name="utc">The instant, with <c>Kind</c> <c>Utc</c>.</param>
        /// <param name="localOffset">Whole minutes, less than 24 hours either way.</param>
        /// <param name="localName">The zone's name; null reads as "UTC".</param>
        public FrozenClock(DateTime utc, TimeSpan localOffset, string localName)
        {
            if (utc.Kind != DateTimeKind.Utc)   // refused here, not as a HostError in the middle of some run
                throw new ArgumentException("the instant must have DateTimeKind.Utc, not " + utc.Kind, nameof(utc));
            _utc = utc;
            _offset = localOffset;
            _name = localName ?? "UTC";
        }

        /// <inheritdoc/>
        public DateTime UtcNow { get { return _utc; } }

        /// <inheritdoc/>
        public TimeSpan LocalOffset { get { return _offset; } }

        /// <inheritdoc/>
        public string LocalName { get { return _name; } }
    }
}
