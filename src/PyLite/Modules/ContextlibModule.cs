using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // contextlib: the managers that need no generator - suppress, nullcontext, closing. A resource limit
    // passes through every `with` without calling the manager, so suppress can never swallow one.
    internal static class ContextlibModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["suppress"] = ctx.Values.Type("contextlib.suppress", (self, a, kw, c) =>
            {
                if (kw.Count > 0)
                    throw KwReader.UnexpectedError(c, "suppress", kw.NameAt(0));
                c.Values.PreCharge(48 + 8L * a.Length);
                return new SuppressValue((ScriptValue[])a.Clone());
            }, SuppressValue.SuppressType, null);
            m["nullcontext"] = ctx.Values.Type("contextlib.nullcontext", (self, a, kw, c) =>
            {
                ScriptValue result = a.Length > 0 ? a[0] : null;
                if (a.Length > 1)
                    throw Raise.TypeError(c, "nullcontext() takes at most 1 argument (" + a.Length + " given)");
                for (int i = 0; i < kw.Count; i++)
                {
                    if (kw.NameAt(i) != "enter_result" || result != null)
                        throw Raise.TypeError(c, "nullcontext() got an unexpected keyword argument '" + kw.NameAt(i) + "'");
                    result = kw.ValueAt(i);
                }
                c.Values.PreCharge(32);
                return new NullContextValue(result ?? c.Values.None);
            }, NullContextValue.NullType, null);
            m["closing"] = ctx.Values.Type("contextlib.closing", (self, a, kw, c) =>
            {
                if (kw.Count > 0)
                    throw KwReader.UnexpectedError(c, "closing", kw.NameAt(0));
                if (a.Length != 1)
                    throw Raise.TypeError(c, "closing() takes 1 argument (" + a.Length + " given)");
                c.Values.PreCharge(32);
                return new ClosingValue(a[0]);
            }, ClosingValue.ClosingType, null);
            return ctx.Values.Module("contextlib", m);
        }

    }

    // suppress(*exceptions): the block's exception is dropped when it is one of them (subclasses too).
    internal sealed class SuppressValue : ScriptValue, IContextManager
    {
        internal static readonly ScriptTypeInfo SuppressType = new ScriptTypeInfo("contextlib.suppress", null);
        private readonly ScriptValue[] _exceptions;

        internal SuppressValue(ScriptValue[] exceptions) { _exceptions = exceptions; }

        public override ScriptTypeInfo TypeInfo { get { return SuppressType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        public ScriptValue Enter(EvalContext ctx) { return ctx.Values.None; }

        // As CPython's issubclass(exctype, self._exceptions): checked only when there is an exception.
        public bool Exit(EvalContext ctx, ExceptionValue pending)
        {
            if (pending == null)
                return false;
            bool hit = false;
            foreach (ScriptValue e in _exceptions)
            {
                TypeValue tv = e as TypeValue;
                if (tv == null || tv.ExcType == null)
                    throw Raise.TypeError(ctx, "issubclass() arg 2 must be a class, a tuple of classes, or a union");
                hit |= pending.ExcType.IsSubtypeOf(tv.ExcType);
            }
            return hit;
        }
    }

    // nullcontext(enter_result=None): does nothing; `as` gets enter_result.
    internal sealed class NullContextValue : ScriptValue, IContextManager
    {
        internal static readonly ScriptTypeInfo NullType = new ScriptTypeInfo("contextlib.nullcontext", null);
        private readonly ScriptValue _result;

        internal NullContextValue(ScriptValue result) { _result = result; }

        public override ScriptTypeInfo TypeInfo { get { return NullType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        public ScriptValue Enter(EvalContext ctx) { return _result; }
        public bool Exit(EvalContext ctx, ExceptionValue pending) { return false; }
    }

    // closing(thing): `as` gets thing; thing.close() runs when the block ends, however it ends.
    internal sealed class ClosingValue : ScriptValue, IContextManager
    {
        internal static readonly ScriptTypeInfo ClosingType = new ScriptTypeInfo("contextlib.closing", null);
        private readonly ScriptValue _thing;

        internal ClosingValue(ScriptValue thing) { _thing = thing; }

        public override ScriptTypeInfo TypeInfo { get { return ClosingType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        public ScriptValue Enter(EvalContext ctx) { return _thing; }

        public bool Exit(EvalContext ctx, ExceptionValue pending)
        {
            ctx.CallHook(PyOps.GetAttr(_thing, "close", ctx), Array.Empty<ScriptValue>(), KwArgs.Empty);
            return false;
        }
    }
}
