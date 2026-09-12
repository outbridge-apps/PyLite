using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // copy: copy.copy (shallow), copy.deepcopy (iterative), copy.Error.
    // A single switch over the concrete value type; immutables return themselves, mutable containers are
    // shallow-cloned into the correct subtype with metadata preserved (default_factory, maxlen, order).
    public static partial class CopyModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["copy"] = BuiltinFunctionValue.Make("copy", CopyFn);
            m["deepcopy"] = BuiltinFunctionValue.Make("deepcopy", DeepCopyFn);
            m["Error"] = ErrorType();
            return ctx.Values.Module("copy", m);
        }

        private static TypeValue ErrorType()
        {
            BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(PyExceptionTypes.CopyError, args);
            return TypeValue.Make("Error", ctor, null, null, PyExceptionTypes.CopyError);
        }

        private static ScriptValue CopyFn(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "copy", 1);
            return ShallowCopy(ctx, args[0]);
        }

        internal static ScriptValue ShallowCopy(EvalContext ctx, ScriptValue x)
        {
            switch (x.Kind)
            {
                case ValueKind.List:
                    return CloneList(ctx, (ListValue)x);
                case ValueKind.Set:
                    return CloneSet(ctx, (SetValue)x);
                case ValueKind.Dict:
                    return CloneDict(ctx, (DictValue)x);
                case ValueKind.Opaque:
                    DequeValue dq = x as DequeValue;
                    return dq != null ? dq.CloneShallow(ctx) : x;   // other opaques (Decimal/Fraction/partial/...) are immutable
                case ValueKind.RecordInstance:
                {
                    // a new instance sharing the field values (CPython's default __copy__ via __reduce_ex__)
                    RecordInstanceValue ri = (RecordInstanceValue)x;
                    ctx.Values.PreCharge(24 + 8L * ri.Slots.Length);
                    return new RecordInstanceValue(ri.Class, (ScriptValue[])ri.Slots.Clone());
                }
                default:
                    return x;   // int/float/bool/str/bytes/None/tuple/namedtuple/frozenset/range/slice/func/type/module
            }
        }

        internal static ListValue CloneList(EvalContext ctx, ListValue src)
        {
            ListValue dst = ctx.Values.List(src.Items.Count);
            for (int i = 0; i < src.Items.Count; i++)
                dst.Add(src.Items[i], ctx);
            return dst;
        }

        internal static SetValue CloneSet(EvalContext ctx, SetValue src)
        {
            SetValue dst = ctx.Values.Set(src.Count);
            IScriptIterator it = PyOps.GetIterator(src, ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                dst.AddItem(v, ctx);
            return dst;
        }

        // Empty clone of the same dict subtype, metadata preserved (factory by reference, order).
        internal static DictValue NewSameDict(EvalContext ctx, DictValue src)
        {
            if (src is CounterValue)
                return ctx.Values.Counter(src.Count);
            if (src is DefaultDictValue)
                return ctx.Values.DefaultDict(((DefaultDictValue)src).DefaultFactory, src.Count);
            if (src is OrderedDictValue)
                return ctx.Values.OrderedDict(src.Count);
            return ctx.Values.Dict(src.Count);
        }

        internal static ScriptValue CloneDict(EvalContext ctx, DictValue src)
        {
            DictValue dst = NewSameDict(ctx, src);
            for (int p = 0; p < src.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (src.Table.TryGetEntryAt(p, out h, out k, out v))
                    dst.SetItem(k, v, ctx);
            }
            return dst;
        }
    }
}
