using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // A mutable opaque value copy.copy and copy.deepcopy make a new one of. Deep: an empty clone goes into the memo
    // first, then the values in DeepChildren are copied, and FillClone gets their copies; so cycles converge.
    internal interface IScriptCopyable
    {
        ScriptValue CopyShallow(EvalContext ctx);
        ScriptValue EmptyClone(EvalContext ctx);
        IReadOnlyList<ScriptValue> DeepChildren();
        void FillClone(EvalContext ctx, ScriptValue clone, Func<ScriptValue, ScriptValue> copyOf);
    }

    // A value CPython cannot pickle: copy.copy and copy.deepcopy raise TypeError naming it. A null name copies
    // to the value itself (a CPython copy that shares all of its state).
    internal interface IScriptUncopyable
    {
        string PickleName { get; }
    }

    // copy: copy.copy (shallow), copy.deepcopy (iterative), copy.Error.
    // A single switch over the concrete value type; immutables return themselves, mutable containers are
    // shallow-cloned into the correct subtype with metadata preserved (default_factory, maxlen, order).
    internal static partial class CopyModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["copy"] = BuiltinFunctionValue.Make("copy", Args.BoundKeywords("copy", 1, CopyFn, "x"));
            m["deepcopy"] = BuiltinFunctionValue.Make("deepcopy", DeepCopyFn);
            m["Error"] = ModuleKit.ExceptionClass(PyExceptionTypes.CopyError);
            return ctx.Values.Module("copy", m);
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
                    if (dq != null)
                        return dq.CloneShallow(ctx);
                    IScriptCopyable copyable = x as IScriptCopyable;
                    return copyable != null ? copyable.CopyShallow(ctx) : LeafCopy(ctx, x);
                case ValueKind.Iterator:
                    return LeafCopy(ctx, x);
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

        // A value with no children to copy, for copy and deepcopy alike: a sequence iterator gets a new one at the same
        // position (over the same sequence, as CPython's reduce does), a decimal context an independent copy; what
        // CPython 3.14 cannot pickle raises its TypeError; anything else is immutable or shares all its state.
        internal static ScriptValue LeafCopy(EvalContext ctx, ScriptValue x, bool deep = false)
        {
            IteratorValue it = x as IteratorValue;
            if (it != null)
            {
                ICopyableIterator pos = it.Iterator as ICopyableIterator;
                if (pos != null)
                    return ctx.Values.EngineIterator(pos.CopyPosition(), it.IterTypeName);
                string refused = deep && it.IterTypeName == "itertools._tee" ? "itertools._tee"   // tee has __copy__ only
                    : UnpicklableIteratorName(it.IterTypeName);
                if (refused != null)
                    throw CannotPickle(ctx, refused);
                return x;
            }
            ContextValue dc = x as ContextValue;
            if (dc != null)
                return dc.Copy(ctx);
            IScriptUncopyable u = x as IScriptUncopyable;
            if (u != null && u.PickleName != null)
                throw CannotPickle(ctx, u.PickleName);
            return x;
        }

        private static string UnpicklableIteratorName(string name)
        {
            if (name == null)
                return null;   // a host iterator (ValueFactory.Iterator) may have no name
            switch (name)
            {
                case "generator":
                case "heapq.merge":
                    return "generator";
                case "csv.reader":
                    return "_csv.reader";
                case "itertools._tee":
                    return null;
                default:
                    return name.StartsWith("itertools.", StringComparison.Ordinal) ? name : null;
            }
        }

        private static ScriptException CannotPickle(EvalContext ctx, string name)
        {
            return Raise.TypeError(ctx, "cannot pickle '" + name + "' object");
        }

        internal static ListValue CloneList(EvalContext ctx, ListValue src)
        {
            return ListMethods.CopyList(ctx, src);
        }

        // A set's table is cloned structurally, as set.copy does (no rehash).
        internal static SetValue CloneSet(EvalContext ctx, SetValue src)
        {
            return ctx.Values.Set(src.Table.CloneShallow(ctx));
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
            return ctx.Values.CopyDict(src);
        }
    }
}
