using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Modules.Support
{
    // Per-EvalContext compiled-pattern cache. Isolation: a purge is visible only in
    // its own run. Cap 512; full clear on overflow (like CPython _MAXCACHE). Compilation is charged.
    internal sealed class RegexCache
    {
        internal const int MaxEntries = 512;

        private readonly Dictionary<Key, PatternValue> _map = new Dictionary<Key, PatternValue>();

        // A compiled pattern the run holds, as the CLR keeps it (the Regex, its translation, the PatternValue): ~1.6 KB for
        // a short one, which the free 1,024 translated characters left at 40 B charged, so 100,000 kept in a list grew
        // the process 1.6 GB under a 64 MB limit. The patterns alive are charged at their high-water mark: one a loop
        // compiles and drops is given back once the CLR has collected it. Every regex of a pattern is held so, the
        // match/fullmatch twins and the exact translation from when they are built, at what .NET keeps of each
        // (RegexFootprint.Held). Held, not charged per compile: 32 B a character past 1,024 paid by every compile made
        // a loop that builds a 500-word alternation a row fail at row 1,900 of 3,000 that 1.1.0 ran on 115 MB (review 14).
        private readonly List<WeakReference> _live = new List<WeakReference>();
        private long _held, _paid;
        private int _gcSeen = -1;

        public PatternValue GetOrCompile(EvalContext ctx, string pattern, int flags)
        {
            var key = new Key(pattern, flags);
            PatternValue pv;
            if (_map.TryGetValue(key, out pv))
            {
                ctx.Budget.Step(1);
                return pv;
            }

            ctx.Budget.Step(64 + pattern.Length / 8);   // compilation ~ pattern length
            if (pattern.Length <= RegexTranslator.MaxPatternChars)
                RegexValidator.Check(ctx, pattern, flags);
            RegexTranslator.Result tr = RegexTranslator.Translate(ctx, pattern, flags);
            // With a \w or \b, the pattern .NET runs on a subject free of WordRisk characters is the one with .NET's own
            // classes (1.1.0's: compiled 12 times faster than the exact word set); the exact one is compiled when a
            // subject first needs it. One that .NET refuses falls back to the exact one, which reports the error.
            Regex net = null;
            long held = 0;
            if (tr.NativePattern != null)
                net = Compile(ctx, pattern.Length, tr.NativePattern, tr.Options, false, out held);
            string exact = tr.NetPattern;
            if (net == null)
            {
                net = Compile(ctx, pattern.Length, tr.NetPattern, tr.Options, true, out held);
                exact = null;
            }
            tr.Groups.Bind(net);
            pv = new PatternValue(this, net, held, exact, tr.CaseRisk, tr.Groups, tr.EffectiveFlags, pattern);
            Hold(ctx, pv);
            if (_map.Count >= MaxEntries)
                _map.Clear();
            _map[key] = pv;
            return pv;
        }

        // What .NET gets can be far longer than the source (a Unicode \b is three copies of the word class, an
        // IGNORECASE class takes sre's extra letters): charged in steps at the source's rate, or 2,000 \b compiled
        // for 0.2 s on 4,000 steps. What it keeps is held once it is built, and one that cannot be held is refused
        // before .NET builds it; what building takes and the CLR collects after is not paid (1.1.0 compiled 2,000
        // Unicode \b under 40 MB). A pattern .NET refuses is re.error, or null when the caller has another to fall
        // back on. held is what the caller holds it at, read off the pattern once (each read is a pass over it). A
        // leading c{m} pays for the Boyer-Moore table .NET builds over its m c's in steps too (RegexFootprint).
        internal Regex Compile(EvalContext ctx, int sourceChars, string netPattern, RegexOptions options, bool raise, out long held)
        {
            long longer = (long)netPattern.Length - sourceChars;
            if (longer > 0)
                ctx.Budget.Step((int)Math.Min(int.MaxValue, longer / 8));
            int repeat = RegexFootprint.LeadingRepeat(netPattern, (options & RegexOptions.IgnorePatternWhitespace) != 0);
            if (repeat > 0)
                ctx.Budget.Step(RegexFootprint.RepeatSteps(repeat));
            held = RegexFootprint.Held(netPattern, repeat, (options & RegexOptions.IgnoreCase) != 0,
                (options & RegexOptions.IgnorePatternWhitespace) != 0);
            Reserve(ctx, held);
            try
            {
                return new Regex(netPattern, options, TimeSpan.FromMilliseconds(ctx.Limits.RegexPerOpTimeoutMs));
            }
            catch (ArgumentException ex)
            {
                if (!raise)
                    return null;
                throw Raise.Make(ctx, PyExceptionTypes.ReError, MapNetError(ex));
            }
            catch (OutOfMemoryException)
            {
                // A pathological pattern can exceed the engine's max array size during compilation; treat it
                // as a memory-budget abort (uncatchable), consistent with the match-time guard in RunMatch.
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "RegexEngine",
                    ctx.Limits.MaxAllocBytes, ctx.Limits.MaxAllocBytes);
            }
        }

        private void Hold(EvalContext ctx, PatternValue pv)
        {
            if (_live.Count >= 64 && GC.CollectionCount(0) != _gcSeen)   // only a collection can have freed one
            {
                _gcSeen = GC.CollectionCount(0);
                Sweep();
            }
            _live.Add(new WeakReference(pv));
            HoldMore(ctx, pv.HeldBytes);
        }

        // The patterns the CLR has collected leave the list, and what they held leaves the total.
        private void Sweep()
        {
            int kept = 0;
            long held = 0;
            for (int i = 0; i < _live.Count; i++)
            {
                var pv = (PatternValue)_live[i].Target;
                if (pv != null)
                {
                    held += pv.HeldBytes;
                    _live[kept++] = _live[i];
                }
            }
            _live.RemoveRange(kept, _live.Count - kept);
            _held = held;
        }

        // What holding bytes more would charge must fit the limit, or MaxAllocBytes is raised now. In a finally the
        // reserve takes it, and what was charged here is paid for the hold that follows, not charged twice.
        private void Reserve(EvalContext ctx, long bytes)
        {
            long over = _held + bytes - _paid;
            if (over > ctx.Budget.AllocRemaining)
            {
                ctx.Budget.ChargeAllocation(over);
                _paid += over;
            }
        }

        internal void HoldMore(EvalContext ctx, long bytes)
        {
            _held += bytes;
            if (_held > _paid)
            {
                ctx.Budget.ChargeAllocation(_held - _paid);
                _paid = _held;
            }
        }

        public void Purge()
        {
            _map.Clear();
        }

        private static string MapNetError(ArgumentException ex)
        {
            string m = ex.Message ?? "";
            if (m.IndexOf("reverse", StringComparison.Ordinal) >= 0)
                return "bad character range";
            return "invalid regular expression";
        }

        private struct Key : IEquatable<Key>
        {
            private readonly string _pattern;
            private readonly int _flags;

            public Key(string pattern, int flags)
            {
                _pattern = pattern;
                _flags = flags;
            }

            public bool Equals(Key other)
            {
                return _flags == other._flags && string.Equals(_pattern, other._pattern, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is Key && Equals((Key)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (StringComparer.Ordinal.GetHashCode(_pattern) * 397) ^ _flags;
                }
            }
        }
    }
}
