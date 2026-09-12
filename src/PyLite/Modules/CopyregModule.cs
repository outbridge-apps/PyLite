using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // copyreg: a stub so `import copyreg` and `from copyreg import pickle, constructor`
    // succeed. pickle validates and writes into a per-run dispatch_table that is never read (copy does not
    // consult it). `import pickle` still fails with ImportError — the real boundary.
    public static class CopyregModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            DictValue dispatch = ctx.Values.Dict(4);   // per-run (the module is cached per Import)
            DictValue extRegistry = ctx.Values.Dict(4);   // (module, name) -> code
            DictValue invRegistry = ctx.Values.Dict(4);   // code -> (module, name)
            DictValue extCache = ctx.Values.Dict(4);
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["dispatch_table"] = dispatch;
            m["_extension_registry"] = extRegistry;
            m["_inverted_registry"] = invRegistry;
            m["_extension_cache"] = extCache;
            m["pickle"] = BuiltinFunctionValue.Make("pickle", (s, a, k, c) => Pickle(c, a, dispatch));
            m["constructor"] = BuiltinFunctionValue.Make("constructor", Constructor);
            m["add_extension"] = BuiltinFunctionValue.Make("add_extension", (s, a, k, c) => AddExtension(c, a, extRegistry, invRegistry));
            m["remove_extension"] = BuiltinFunctionValue.Make("remove_extension", (s, a, k, c) => RemoveExtension(c, a, extRegistry, invRegistry, extCache));
            m["clear_extension_cache"] = BuiltinFunctionValue.Make("clear_extension_cache", (s, a, k, c) =>
            {
                extCache.Table.Clear(c);
                return c.Values.None;
            });
            return ctx.Values.Module("copyreg", m);
        }

        // --- extension registry (port of Lib/copyreg.py) ----

        private static ScriptValue AddExtension(EvalContext ctx, ScriptValue[] a, DictValue reg, DictValue inv)
        {
            TupleValue key;
            IntValue code;
            ReadExtensionArgs(ctx, a, out key, out code);
            ScriptValue curCode, curKey;
            bool haveKey = reg.TryGet(key, ctx, out curCode);
            bool haveCode = inv.TryGet(code, ctx, out curKey);
            if (haveKey && haveCode
                && PyOps.Equals(curCode, code, ctx, 0) && PyOps.Equals(curKey, key, ctx, 0))
                return ctx.Values.None;   // idempotent re-registration
            if (haveKey)
                throw Raise.ValueError(ctx, "key " + key.Repr(ctx) + " is already registered with code " + curCode.Repr(ctx));
            if (haveCode)
                throw Raise.ValueError(ctx, "code " + code.Repr(ctx) + " is already in use for key " + curKey.Repr(ctx));
            reg.SetItem(key, code, ctx);
            inv.SetItem(code, key, ctx);
            return ctx.Values.None;
        }

        private static ScriptValue RemoveExtension(EvalContext ctx, ScriptValue[] a, DictValue reg, DictValue inv, DictValue cache)
        {
            TupleValue key;
            IntValue code;
            ReadExtensionArgs(ctx, a, out key, out code);
            ScriptValue curCode, curKey;
            if (!reg.TryGet(key, ctx, out curCode) || !PyOps.Equals(curCode, code, ctx, 0)
                || !inv.TryGet(code, ctx, out curKey) || !PyOps.Equals(curKey, key, ctx, 0))
                throw Raise.ValueError(ctx, "key " + key.Repr(ctx) + " is not registered with code " + code.Repr(ctx));
            reg.DelItemOrThrow(key, ctx);
            inv.DelItemOrThrow(code, ctx);
            ScriptValue ignored;
            if (cache.TryGet(code, ctx, out ignored))
                cache.DelItemOrThrow(code, ctx);
            return ctx.Values.None;
        }

        private static void ReadExtensionArgs(EvalContext ctx, ScriptValue[] a, out TupleValue key, out IntValue code)
        {
            if (a.Length != 3)
                throw Raise.TypeError(ctx, "expected 3 arguments, got " + a.Length);
            IntValue c = a[2] as IntValue;
            if (c == null)
                throw Raise.TypeError(ctx, "code must be an int");
            if (c.Value < 1 || c.Value > 0x7fffffff)
                throw Raise.ValueError(ctx, "code out of range");
            key = (TupleValue)ctx.Values.Tuple(new[] { a[0], a[1] });
            code = c;
        }

        private static ScriptValue Pickle(EvalContext ctx, ScriptValue[] args, DictValue dispatch)
        {
            Args.Between(ctx, args, "pickle", 2, 3);
            ScriptValue obType = args[0];
            ScriptValue pickleFn = args[1];
            if (!pickleFn.IsCallable)
                throw Raise.TypeError(ctx, "reduction functions must be callable.");
            if (args.Length == 3 && args[2].Kind != ValueKind.None && !args[2].IsCallable)
                throw Raise.TypeError(ctx, "constructors must be callable.");
            dispatch.SetItem(obType, pickleFn, ctx);
            return ctx.Values.None;
        }

        private static ScriptValue Constructor(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "constructor", 1);
            if (!args[0].IsCallable)
                throw Raise.TypeError(ctx, "constructors must be callable.");
            return ctx.Values.None;
        }
    }
}
