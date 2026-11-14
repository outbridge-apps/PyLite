using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // functools: reduce, partial, lru_cache/cache, wraps/update_wrapper, cmp_to_key.
    // All reentrant calls into script callables go through ctx.CallHook (the single Evaluator.Call point).
    internal static class FunctoolsModule
    {
        private const int MaxCacheSize = 4096;   // hard cap

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["reduce"] = BuiltinFunctionValue.Make("reduce", Reduce);
            m["partial"] = BuiltinFunctionValue.Make("partial", Partial);
            m["lru_cache"] = BuiltinFunctionValue.Make("lru_cache", Args.KnownKeywords("lru_cache", 2, LruCache, "maxsize", "typed"));
            m["cache"] = BuiltinFunctionValue.Make("cache", Cache);
            m["wraps"] = BuiltinFunctionValue.Make("wraps", Wraps);
            m["update_wrapper"] = BuiltinFunctionValue.Make("update_wrapper", UpdateWrapper);
            m["cmp_to_key"] = BuiltinFunctionValue.Make("cmp_to_key", Args.BoundKeywords("cmp_to_key", 1, CmpToKey, "mycmp"));
            return ctx.Values.Module("functools", m);
        }

        // reduce(function, iterable[, initializer])
        private static ScriptValue Reduce(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ScriptValue initial = args.Length == 3 ? args[2] : null;
            for (int j = 0; j < kw.Count; j++)
            {
                if (kw.NameAt(j) != "initial")
                    throw KwReader.UnexpectedError(ctx, "reduce", kw.NameAt(j));
                if (initial != null)
                    throw Raise.TypeError(ctx, "reduce() takes at most 3 arguments (4 given)");
                initial = kw.ValueAt(j);
            }
            if (args.Length < 2 || args.Length > 3)
                throw Raise.TypeError(ctx, "reduce expected 2 or 3 arguments, got " + args.Length);
            ScriptValue fn = args[0];
            IScriptIterator it = PyOps.GetIterator(args[1], ctx);
            ScriptValue acc;
            if (initial != null)
            {
                acc = initial;
            }
            else if (!it.MoveNext(ctx, out acc))
            {
                throw Raise.TypeError(ctx, "reduce() of empty iterable with no initial value");
            }
            ScriptValue x;
            while (it.MoveNext(ctx, out x))
                acc = ctx.CallHook(fn, new[] { acc, x }, KwArgs.Empty);
            return acc;
        }

        // partial(func, *args, **keywords) -> a PartialValue (flattening nested partials).
        private static ScriptValue Partial(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Unpack(ctx, args, "partial", 1, int.MaxValue);
            ScriptValue func = args[0];
            var bound = new ScriptValue[args.Length - 1];
            Array.Copy(args, 1, bound, 0, bound.Length);
            return PartialValue.Create(ctx, func, bound, kw);
        }

        // lru_cache: used bare ('@lru_cache', arg is the function) or as a factory ('@lru_cache(maxsize=N)').
        private static ScriptValue LruCache(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length == 1 && kw.Count == 0 && args[0].IsCallable)
                return MakeCachedWrapper(ctx, args[0], 128, false);
            int maxsize = ResolveMaxsize(args, kw);
            ScriptValue t;
            if (!kw.TryGet("typed", out t))
                t = args.Length >= 2 ? args[1] : null;
            bool typed = t != null && PyOps.Truth(t, ctx);
            return ctx.Values.Closure("lru_cache", Args.Positional("lru_cache", 1, 1, (s2, a2, k2, c2) =>
            {
                if (a2.Length != 1 || !a2[0].IsCallable)
                    throw Raise.TypeError(c2, "the first argument to lru_cache must be a callable");
                return MakeCachedWrapper(c2, a2[0], maxsize, typed);
            }));
        }

        // cache = lru_cache(maxsize=None) (Python 3.9).
        private static ScriptValue Cache(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            // def cache(user_function, /): CPython's texts for each way to miss it
            ScriptValue uf;
            if (kw.TryGet("user_function", out uf))
                throw Raise.TypeError(ctx, "cache() got some positional-only arguments passed as keyword arguments: 'user_function'");
            if (kw.Count > 0)
                throw KwReader.UnexpectedError(ctx, "cache", kw.NameAt(0));
            if (args.Length == 0)
                throw Raise.TypeError(ctx, "cache() missing 1 required positional argument: 'user_function'");
            if (args.Length > 1)
                throw Raise.TypeError(ctx, "cache() takes 1 positional argument but " + args.Length + " were given");
            if (!args[0].IsCallable)
                throw Raise.TypeError(ctx, "the first argument must be callable");
            return MakeCachedWrapper(ctx, args[0], MaxCacheSize, false);
        }

        // One cache per decorated function (LruCacheWrapperValue): cache_info, cache_clear, cache_parameters
        // and __wrapped__ as in CPython.
        private static ScriptValue MakeCachedWrapper(EvalContext ctx, ScriptValue fn, int maxsize, bool typed)
        {
            ctx.Values.ChargeObject(2);
            return new LruCacheWrapperValue(ctx, fn, maxsize, typed);
        }

        private static int ResolveMaxsize(ScriptValue[] args, KwArgs kw)
        {
            ScriptValue ms;
            if (!kw.TryGet("maxsize", out ms))
                ms = args.Length >= 1 ? args[0] : null;
            if (ms == null)
                return 128;   // lru_cache() and lru_cache(typed=True): CPython's default, not unbounded
            if (ms.Kind == ValueKind.None)
                return MaxCacheSize;
            IntValue iv = ms as IntValue;
            if (iv == null)
                return MaxCacheSize;
            if (iv.Value <= 0)
                return 0;   // CPython: a negative maxsize is 0, and 0 caches nothing
            return iv.Value >= MaxCacheSize ? MaxCacheSize : (int)iv.Value;
        }

        // wraps(wrapped, assigned=..., updated=...) -> a decorator that copies the wrapped callable's __name__
        // onto the wrapper unless `assigned` leaves it out, and always sets __wrapped__ (script functions
        // only; __doc__/__dict__ do not exist in this dialect). `updated` names __dict__ entries, so there is
        // nothing to update.
        private static ScriptValue Wraps(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ScriptValue[] a = Args.Named(ctx, args, kw, "wraps", "wrapped", "assigned", "updated");
            Args.AtLeast(ctx, a, "wraps", 1);
            ScriptValue wrapped = a[0];
            bool copyName = CopiesName(ctx, a.Length > 1 ? a[1] : null);
            return ctx.Values.Closure("wraps", Args.Positional("wraps", 1, 1, (s2, a2, k2, c2) =>
            {
                UpdateFrom(wrapped, a2[0], copyName);
                return a2[0];
            }));
        }

        private static ScriptValue UpdateWrapper(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ScriptValue[] a = Args.Named(ctx, args, kw, "update_wrapper", "wrapper", "wrapped", "assigned", "updated");
            Args.AtLeast(ctx, a, "update_wrapper", 2);
            UpdateFrom(a[1], a[0], CopiesName(ctx, a.Length > 2 ? a[2] : null));
            return a[0];
        }

        // The default WRAPPER_ASSIGNMENTS includes __name__; an explicit `assigned` decides.
        private static bool CopiesName(EvalContext ctx, ScriptValue assigned)
        {
            if (assigned == null || assigned.Kind == ValueKind.None)
                return true;
            IScriptIterator it = PyOps.GetIterator(assigned, ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
            {
                StrValue sv = v as StrValue;
                if (sv != null && (sv.Value == "__name__" || sv.Value == "__qualname__"))
                    return true;
            }
            return false;
        }

        private static void UpdateFrom(ScriptValue wrapped, ScriptValue wrapper, bool copyName)
        {
            FunctionValue w = wrapper as FunctionValue;
            if (w == null)
                return;
            w.Wrapped = wrapped;   // CPython sets it last, whatever `assigned` says
            string name = copyName ? PyOps.CallableName(wrapped) : null;
            if (name != null)
                w.NameOverride = name;
        }

        // cmp_to_key(cmp) -> key(obj) -> a wrapper whose ordering calls cmp(a, b) (see CmpKeyValue).
        private static ScriptValue CmpToKey(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Unpack(ctx, args, "cmp_to_key", 1, 1);
            ScriptValue cmp = args[0];
            return ctx.Values.Closure("cmp_to_key.<locals>.K", (s2, a2, k2, c2) =>
            {
                Args.Exactly(c2, a2, "K", 1);
                c2.Values.ChargeObject(2);
                return new CmpKeyValue(cmp, a2[0]);
            }, 1);
        }
    }

    // The comparable key produced by functools.cmp_to_key. Ordering routes through TryCompareLeafCore
    // (opaque values order via leaf hooks R), which calls the user cmp(a, b) via ctx.CallHook.
    internal sealed class CmpKeyValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("functools.KeyWrapper", null);

        private readonly ScriptValue _cmp;
        private readonly ScriptValue _obj;
        internal CmpKeyValue(ScriptValue cmp, ScriptValue obj) { _cmp = cmp; _obj = obj; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            CmpKeyValue o = other as CmpKeyValue;
            if (o == null)
            {
                cmp = 0;
                return false;
            }
            ScriptValue r = ctx.CallHook(_cmp, new[] { _obj, o._obj }, KwArgs.Empty);
            cmp = SignOf(r, ctx);
            return true;
        }

        // K(a) == K(b) iff cmp(a, b) == 0 (functools.cmp_to_key's __eq__).
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            int cmp;
            return TryCompareLeafCore(other, ctx, depth, out cmp) && cmp == 0;
        }

        private static int SignOf(ScriptValue r, EvalContext ctx)
        {
            IntValue iv = r as IntValue;
            if (iv != null)
                return iv.Value.Sign;
            BoolValue bv = r as BoolValue;
            if (bv != null)
                return bv.Value ? 1 : 0;
            throw Raise.TypeError(ctx, "comparison function must return an int");
        }
    }
}
