using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // collections.defaultdict. A DictValue with a default_factory; d[k] on a miss calls
    // the factory with no args, INSERTS the result, then returns it (__missing__). .get/in/.setdefault do NOT.
    internal sealed class DefaultDictValue : DictValue
    {
        private static readonly ScriptTypeInfo DDType = new ScriptTypeInfo("collections.defaultdict", BuildSlots());

        internal ScriptValue DefaultFactory;   // a callable or None; writable via d.default_factory = ...

        internal DefaultDictValue(OrderedTable table, ScriptValue defaultFactory) : base(table)
        {
            DefaultFactory = defaultFactory;
        }

        public override ScriptTypeInfo TypeInfo { get { return DDType; } }

        internal override string ReprPrefix(EvalContext ctx)
        {
            return "defaultdict(" + (DefaultFactory == null ? "None" : DefaultFactory.Repr(ctx)) + ", ";
        }

        internal override string ReprSuffix(EvalContext ctx) { return ")"; }

        public override bool TryGetItem(ScriptValue key, EvalContext ctx, out ScriptValue value)
        {
            if (TryGet(key, ctx, out value))
                return true;
            if (DefaultFactory == null || DefaultFactory.Kind == ValueKind.None)
                return false;
            value = ctx.CallHook(DefaultFactory, Array.Empty<ScriptValue>(), KwArgs.Empty);
            SetItem(key, value, ctx);   // insertion happens BEFORE returning; a factory exception -> no entry
            return true;
        }

        protected internal override bool TrySetAttrCore(string name, ScriptValue value, EvalContext ctx)
        {
            if (name == "default_factory")
            {
                DefaultFactory = value;
                return true;
            }
            return false;
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            IDictionary<string, SlotDescriptor> slots = DictMethods.BuildSlots();
            slots["copy"] = SlotDescriptor.MakeMethod("copy", (self, a, kw, ctx) =>
            {
                DefaultDictValue src = (DefaultDictValue)self;
                DefaultDictValue r = ctx.Values.DefaultDict(src.DefaultFactory, src.Count);
                DictMethods.Merge(ctx, r, src);
                return r;
            });
            slots["default_factory"] = SlotDescriptor.MakeProperty("default_factory",
                (self, ctx) =>
                {
                    ScriptValue f = ((DefaultDictValue)self).DefaultFactory;
                    return f ?? ctx.Values.None;
                });
            return slots;
        }
    }
}
