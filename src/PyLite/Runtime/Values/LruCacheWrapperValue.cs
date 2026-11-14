using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // functools.lru_cache's wrapper: calls Wrapped and keeps up to Maxsize results, least recently used first
    // in one dict's insertion order, with the hit and miss counts cache_info() reports. Maxsize 0 caches
    // nothing (and needs no hashable arguments), as in CPython.
    internal sealed class LruCacheWrapperValue : ScriptValue
    {
        private static readonly ScriptTypeInfo WType = new ScriptTypeInfo("functools._lru_cache_wrapper", BuildSlots());
        private static readonly string[] InfoFields = { "hits", "misses", "maxsize", "currsize" };
        private static readonly NamedTupleInfo CacheInfoType = new NamedTupleInfo("CacheInfo", InfoFields,
            NamedTupleValue.BuildInstanceType("CacheInfo", InfoFields));

        internal readonly ScriptValue Wrapped;
        private readonly int _maxsize;
        private readonly bool _typed;
        private DictValue _cache;   // null when nothing is cached
        private long _hits;
        private long _misses;
        private readonly long _identity;

        internal LruCacheWrapperValue(EvalContext ctx, ScriptValue wrapped, int maxsize, bool typed)
        {
            Wrapped = wrapped;
            _maxsize = maxsize;
            _typed = typed;
            _identity = ctx.Values.NextIdentitySeq();
            if (maxsize > 0)
                _cache = ctx.Values.Dict(8);
        }

        public override ScriptTypeInfo TypeInfo { get { return WType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }
        internal override bool IsCallable { get { return true; } }
        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return _identity; }   // hashable like the function it wraps

        protected internal override ScriptValue CallCore(ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (_cache == null)
            {
                _misses++;
                return ctx.CallHook(Wrapped, args, kw);
            }
            ScriptValue key = MakeKey(ctx, args, kw, _typed);   // hashing an unhashable arg raises here (parity)
            ScriptValue hit;
            if (_cache.TryGet(key, ctx, out hit))
            {
                _hits++;
                _cache.Table.MoveToEnd(key, PyOps.Hash(key, ctx, 0), ctx, 0);   // most recent
                return hit;
            }
            _misses++;
            ScriptValue result = ctx.CallHook(Wrapped, args, kw);
            _cache.SetItem(key, result, ctx);
            if (_cache.Count > _maxsize)
                EvictOldest(_cache, ctx);
            return result;
        }

        private static ScriptValue MakeKey(EvalContext ctx, ScriptValue[] args, KwArgs kw, bool typed)
        {
            if (kw.Count == 0 && !typed)
                return ctx.Values.Tuple((ScriptValue[])args.Clone());
            var parts = new List<ScriptValue>(args.Length + 1 + kw.Count * 2);
            parts.AddRange(args);
            if (kw.Count > 0)
                parts.Add(KwMark.Instance);   // CPython's kwd_mark: a private object no argument can equal
            for (int i = 0; i < kw.Count; i++)
            {
                parts.Add(ctx.Values.Str(kw.NameAt(i)));
                parts.Add(kw.ValueAt(i));
            }
            if (typed)   // CPython's _make_key appends the type of every argument: f(1) and f(1.0) differ
            {
                foreach (ScriptValue v in args)
                    parts.Add(ctx.Values.Str(v.PyTypeName));
                for (int i = 0; i < kw.Count; i++)
                    parts.Add(ctx.Values.Str(kw.ValueAt(i).PyTypeName));
            }
            return ctx.Values.Tuple(parts.ToArray());
        }

        private static void EvictOldest(DictValue cache, EvalContext ctx)
        {
            IScriptIterator it = PyOps.GetIterator(cache, ctx);   // keys, insertion order
            ScriptValue oldest;
            if (it.MoveNext(ctx, out oldest))
                cache.DelItemOrThrow(oldest, ctx);
        }

        // maxsize=None and sizes past the engine cap report the cap they are held to (a documented difference).
        private static ScriptValue CacheInfo(LruCacheWrapperValue w, EvalContext ctx)
        {
            ctx.Values.PreCharge(64);   // a kept CacheInfo is the tuple and its array
            return new NamedTupleValue(CacheInfoType, new ScriptValue[]
            {
                ctx.Values.Int(w._hits), ctx.Values.Int(w._misses), ctx.Values.Int(w._maxsize),
                ctx.Values.Int(w._cache == null ? 0 : w._cache.Count),
            });
        }

        private static ScriptValue CacheParameters(LruCacheWrapperValue w, EvalContext ctx)
        {
            DictValue d = ctx.Values.Dict(2);
            d.SetItem(ctx.Values.Str("maxsize"), ctx.Values.Int(w._maxsize), ctx);
            d.SetItem(ctx.Values.Str("typed"), ctx.Values.Bool(w._typed), ctx);
            return d;
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            s["cache_info"] = SlotDescriptor.MakeMethod("cache_info",
                Args.Positional("cache_info", 0, 0, (self, a, kw, ctx) => CacheInfo((LruCacheWrapperValue)self, ctx)));
            s["cache_clear"] = SlotDescriptor.MakeMethod("cache_clear", Args.Positional("cache_clear", 0, 0, (self, a, kw, ctx) =>
            {
                var w = (LruCacheWrapperValue)self;
                if (w._cache != null)
                    w._cache = ctx.Values.Dict(8);
                w._hits = 0;
                w._misses = 0;
                return ctx.Values.None;
            }));
            s["cache_parameters"] = SlotDescriptor.MakeMethod("cache_parameters",
                Args.Positional("cache_parameters", 0, 0, (self, a, kw, ctx) => CacheParameters((LruCacheWrapperValue)self, ctx)));
            return s;
        }
    }

    // Separates the positional arguments from the keyword pairs in an lru_cache key. A string marker could be
    // passed as an argument (f('<marker>', 'a', 1) and f(a=1) shared a key); this object never reaches a script.
    internal sealed class KwMark : ScriptValue
    {
        public static readonly KwMark Instance = new KwMark();
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("_kwd_mark", null);

        private KwMark() { }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return 0x6B77; }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("<kwd_mark>"); }
    }
}
