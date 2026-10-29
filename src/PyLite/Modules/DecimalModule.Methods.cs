using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    internal static partial class DecimalModule
    {
        // getcontext() returns the active Context; setcontext(c) makes c active; localcontext() makes a copy
        // active for a `with` block and restores the previous one after it, also on an exception.
        internal static void AddGetContext(EvalContext ctx, Dictionary<string, ScriptValue> m)
        {
            m["getcontext"] = BuiltinFunctionValue.Make("getcontext", Args.Positional("getcontext", 0, 0, (self, args, kw, c) => ContextValue.Active(c)));
            m["setcontext"] = BuiltinFunctionValue.Make("setcontext", (self, args, kw, c) =>
            {
                ContextValue cv = args.Length == 1 && kw.Count == 0 ? args[0] as ContextValue : null;
                if (cv == null)
                    throw Raise.TypeError(c, "argument must be a context");
                cv.Activate(c);
                return c.Values.None;
            });
            m["localcontext"] = BuiltinFunctionValue.Make("localcontext", LocalContext);
            m["Context"] = ctx.Values.Type("decimal.Context", (self, args, kw, c) =>
            {
                if (args.Length > 2)
                    throw Raise.TypeError(c, "Context() takes prec and rounding in this dialect");
                var target = new DecimalContext();
                ContextValue.Apply(c, target, args.Length > 0 ? args[0] : null, args.Length > 1 ? args[1] : null, kw, "Context");
                c.Values.PreCharge(64);
                return new ContextValue(target);
            }, ContextValue.ContextType, null);
        }

        // localcontext(ctx=None, *, prec=None, rounding=None): the arguments are checked here, as CPython does.
        private static ScriptValue LocalContext(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext c)
        {
            if (args.Length > 1)
                throw Raise.TypeError(c, "localcontext() takes at most 1 positional argument (" + args.Length + " given)");
            ScriptValue source = args.Length == 1 ? args[0] : null;
            if (source != null && source.Kind != ValueKind.None && !(source is ContextValue))
                throw Raise.TypeError(c, "optional argument must be a context");
            var probe = new DecimalContext();
            ContextValue.Apply(c, probe, null, null, kw, "localcontext");
            c.Values.PreCharge(64);
            return new LocalContextManager(source as ContextValue, kw);
        }
    }

    // A decimal Context: its own prec and rounding. The active one is ctx.DecimalCtx, which every
    // arithmetic operation reads; prec above 28 is refused because the backend cannot hold it.
    internal sealed class ContextValue : ScriptValue
    {
        internal static readonly ScriptTypeInfo ContextType = BuildType();
        internal readonly DecimalContext Target;

        internal ContextValue(DecimalContext target) { Target = target; }

        public override ScriptTypeInfo TypeInfo { get { return ContextType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        // The object for the run's active context, made on first use so `getcontext() is getcontext()`.
        internal static ContextValue Active(EvalContext ctx)
        {
            ContextValue cv = ctx.DecimalView as ContextValue;
            if (cv == null || !ReferenceEquals(cv.Target, ctx.DecimalCtx))
            {
                cv = new ContextValue(ctx.DecimalCtx);
                ctx.DecimalView = cv;
            }
            return cv;
        }

        internal void Activate(EvalContext ctx)
        {
            ctx.DecimalCtx = Target;
            ctx.DecimalView = this;
        }

        internal ContextValue Copy(EvalContext ctx)
        {
            ctx.Values.PreCharge(64);
            return new ContextValue(new DecimalContext { Prec = Target.Prec, Rounding = Target.Rounding });
        }

        // prec/rounding from positional values or keywords; anything else is refused by name.
        internal static void Apply(EvalContext ctx, DecimalContext target, ScriptValue prec, ScriptValue rounding, KwArgs kw, string who)
        {
            for (int i = 0; i < kw.Count; i++)
            {
                string k = kw.NameAt(i);
                if (k == "prec" && prec == null)
                    prec = kw.ValueAt(i);
                else if (k == "rounding" && rounding == null)
                    rounding = kw.ValueAt(i);
                else if (k == "Emin" || k == "Emax" || k == "capitals" || k == "clamp" || k == "flags" || k == "traps")
                    throw Raise.TypeError(ctx, who + "(): '" + k + "' is not supported on the System.Decimal backend; only prec and rounding");
                else
                    throw Raise.TypeError(ctx, "'" + k + "' is an invalid keyword argument for " + who + "()");
            }
            if (prec != null && prec.Kind != ValueKind.None)
                target.Prec = CheckPrec(ctx, prec);
            if (rounding != null && rounding.Kind != ValueKind.None)
                target.Rounding = CheckRounding(ctx, rounding);
        }

        private static int CheckPrec(EvalContext ctx, ScriptValue value)
        {
            if (value.Kind != ValueKind.Int && value.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "prec must be an integer");
            System.Numerics.BigInteger n = NumericOps.AsBigInteger(value);
            if (n < 1 || n > DecimalContext.MaxPrec)
                throw Raise.ValueError(ctx, "valid range for prec is [1, " + DecimalContext.MaxPrec
                    + "] on the System.Decimal backend");
            return (int)n;
        }

        private static string CheckRounding(EvalContext ctx, ScriptValue value)
        {
            StrValue s = value as StrValue;
            if (s == null || Array.IndexOf(RoundNames, s.Value) < 0)
                throw Raise.ValueError(ctx, "rounding must be one of the ROUND_* constants");
            return s.Value;
        }

        protected internal override bool TrySetAttrCore(string name, ScriptValue value, EvalContext ctx)
        {
            if (name == "prec")
            {
                Target.Prec = CheckPrec(ctx, value);
                return true;
            }
            if (name == "rounding")
            {
                Target.Rounding = CheckRounding(ctx, value);
                return true;
            }
            return false;
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("Context(prec=" + Target.Prec + ", rounding=" + Target.Rounding + ")");
        }

        private static readonly string[] RoundNames =
        {
            "ROUND_CEILING", "ROUND_FLOOR", "ROUND_UP", "ROUND_DOWN",
            "ROUND_HALF_UP", "ROUND_HALF_DOWN", "ROUND_HALF_EVEN", "ROUND_05UP",
        };

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["prec"] = SlotDescriptor.MakeProperty("prec", (self, c) => c.Values.Int(((ContextValue)self).Target.Prec)),
                ["rounding"] = SlotDescriptor.MakeProperty("rounding", (self, c) => c.Values.Str(((ContextValue)self).Target.Rounding)),
                ["copy"] = SlotDescriptor.MakeMethod("copy", Args.Positional("copy", 0, 0, (self, a, kw, c) => ((ContextValue)self).Copy(c))),
            };
            return new ScriptTypeInfo("decimal.Context", d);
        }
    }

    // `with localcontext(...) as ctx:` - a copy of the given (or the active) context, with the keyword
    // settings applied, is active inside the block; the previous one is restored on the way out.
    internal sealed class LocalContextManager : ScriptValue, IContextManager
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("decimal.ContextManager", null);
        private readonly ContextValue _source;
        private readonly KwArgs _kw;
        private readonly Stack<KeyValuePair<DecimalContext, ScriptValue>> _saved = new Stack<KeyValuePair<DecimalContext, ScriptValue>>();

        internal LocalContextManager(ContextValue source, KwArgs kw) { _source = source; _kw = kw; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        public ScriptValue Enter(EvalContext ctx)
        {
            ContextValue local = (_source ?? ContextValue.Active(ctx)).Copy(ctx);
            ContextValue.Apply(ctx, local.Target, null, null, _kw, "localcontext");
            _saved.Push(new KeyValuePair<DecimalContext, ScriptValue>(ctx.DecimalCtx, ctx.DecimalView));
            local.Activate(ctx);
            return local;
        }

        public bool Exit(EvalContext ctx, ExceptionValue pending)
        {
            if (_saved.Count > 0)
            {
                KeyValuePair<DecimalContext, ScriptValue> prev = _saved.Pop();
                ctx.DecimalCtx = prev.Key;
                ctx.DecimalView = prev.Value;
            }
            return false;
        }
    }
}
