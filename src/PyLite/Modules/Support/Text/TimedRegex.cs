using System;
using System.Text.RegularExpressions;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Modules.Support
{
    // An engine-side pattern (csv, textwrap) under the same per-op MatchTimeout as a script's own. Regex
    // fixes its timeout at construction, so one instance is kept per distinct RegexPerOpTimeoutMs (a host
    // uses a handful); past MaxVariants a call builds its own.
    internal sealed class TimedRegex
    {
        private const int MaxVariants = 8;

        private sealed class Variant
        {
            public int Ms;
            public Regex Re;
        }

        private readonly string _pattern;
        private readonly RegexOptions _options;
        private readonly object _lock = new object();
        private volatile Variant[] _variants = new Variant[0];

        public TimedRegex(string pattern, RegexOptions options)
        {
            _pattern = pattern;
            _options = options;
        }

        public Regex For(EvalContext ctx)
        {
            int ms = ctx.Limits.RegexPerOpTimeoutMs;
            Variant[] v = _variants;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i].Ms == ms)
                    return v[i].Re;
            }
            return Add(ms);
        }

        private Regex Add(int ms)
        {
            lock (_lock)
            {
                Variant[] v = _variants;
                for (int i = 0; i < v.Length; i++)
                {
                    if (v[i].Ms == ms)
                        return v[i].Re;
                }
                var re = new Regex(_pattern, _options, TimeSpan.FromMilliseconds(ms));
                if (v.Length < MaxVariants)
                {
                    var grown = new Variant[v.Length + 1];
                    Array.Copy(v, grown, v.Length);
                    grown[v.Length] = new Variant { Ms = ms, Re = re };
                    _variants = grown;
                }
                return re;
            }
        }

        public static TimeSpan Timeout(EvalContext ctx)
        {
            return TimeSpan.FromMilliseconds(ctx.Limits.RegexPerOpTimeoutMs);
        }

        // What a timed-out engine-side match ends the run with: the abort a script pattern gets.
        public static EngineAbort Abort(EvalContext ctx)
        {
            return ctx.Budget.CreateAbort(EngineAbortKind.RegexTimeout, "RegexPerOpTimeout",
                ctx.Limits.RegexPerOpTimeoutMs, ctx.Limits.RegexPerOpTimeoutMs);
        }
    }
}
