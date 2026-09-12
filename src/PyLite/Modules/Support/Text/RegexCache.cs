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
        internal const int PerOpTimeoutMs = 250;

        private readonly Dictionary<Key, PatternValue> _map = new Dictionary<Key, PatternValue>();

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
            RegexTranslator.Result tr = RegexTranslator.Translate(ctx, pattern, flags);
            Regex net;
            try
            {
                net = new Regex(tr.NetPattern, tr.Options, TimeSpan.FromMilliseconds(PerOpTimeoutMs));
            }
            catch (ArgumentException ex)
            {
                throw Raise.Make(ctx, PyExceptionTypes.ReError, MapNetError(ex));
            }
            catch (OutOfMemoryException)
            {
                // A pathological pattern can exceed the engine's max array size during compilation; treat it
                // as a memory-budget abort (uncatchable), consistent with the match-time guard in RunMatch.
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "RegexEngine",
                    ctx.Limits.MaxAllocBytes, ctx.Limits.MaxAllocBytes);
            }

            pv = new PatternValue(net, tr.Groups, tr.EffectiveFlags, pattern);
            if (_map.Count >= MaxEntries)
                _map.Clear();
            _map[key] = pv;
            return pv;
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
