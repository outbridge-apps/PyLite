using System;
using System.Runtime.CompilerServices;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // Public protocol facades — the Evaluator's only entry points. Container traversal
    // (Hash tuple engine, Equals/RichCompare over containers) is added by later T-VAL tasks.
    public static partial class PyOps
    {
        // Layer-skipping shortcut for the dominant small-int case: the evaluator's binop/augassign
        // sites call this BEFORE the full protocol. IntOpSmall is the same function the full path
        // lands in (IntValue is sealed, so Int x Int dispatch is deterministic) — this just skips
        // BinaryOp -> BinaryOpCore -> IsNumber -> BinaryNumeric, which cost more than the op itself.
        // Null => not two machine ints, or the op defers (overflow/edge) — run the full protocol.
        internal static ScriptValue TrySmallIntBinOp(PyBinOp op, ScriptValue a, ScriptValue b, EvalContext ctx)
        {
            if (a.Kind != ValueKind.Int || b.Kind != ValueKind.Int)
                return null;
            IntValue ia = (IntValue)a, ib = (IntValue)b;
            if (!ia.IsSmall || !ib.IsSmall)
                return null;
            return NumericOps.IntOpSmall(op, ia.Small, ib.Small, ctx);
        }

        public static ScriptValue BinaryOp(PyBinOp op, ScriptValue a, ScriptValue b, EvalContext ctx)
        {
            ScriptValue r = a.BinaryOpCore(op, b, false, ctx);
            if (r != null)
                return r;
            if (!ReferenceEquals(a.TypeInfo, b.TypeInfo))
            {
                r = b.BinaryOpCore(op, a, true, ctx);
                if (r != null)
                    return r;
            }
            throw Raise.TypeError(ctx, "unsupported operand type(s) for " + Sym(op)
                + ": '" + a.PyTypeName + "' and '" + b.PyTypeName + "'");
        }

        public static ScriptValue UnaryOp(PyUnaryOp op, ScriptValue a, EvalContext ctx)
        {
            ScriptValue r = a.UnaryOpCore(op, ctx);
            if (r != null)
                return r;
            throw Raise.TypeError(ctx, "bad operand type for unary " + Sym(op) + ": '" + a.PyTypeName + "'");
        }

        public static bool Truth(ScriptValue v, EvalContext ctx) { return v.IsTruthyCore(ctx); }

        // Needle classification for the sequence membership/count loops: a machine-word int (or bool,
        // which compares as 0/1). Classified ONCE outside the loop; per element SmallIntEq then skips
        // the full Equals dispatch for the overwhelmingly common int-vs-int case.
        internal static bool TryGetSmallInt(ScriptValue v, out long val)
        {
            if (v.Kind == ValueKind.Int)
            {
                IntValue iv = (IntValue)v;
                if (iv.IsSmall)
                {
                    val = iv.Small;
                    return true;
                }
            }
            else if (v.Kind == ValueKind.Bool)
            {
                val = ((BoolValue)v).Value ? 1L : 0L;
                return true;
            }
            val = 0;
            return false;
        }

        // 1 = equal, 0 = not equal, -1 = undecidable fast (element is not a machine int — e.g. a float,
        // where 1 == 1.0, or a user type with __eq__ — the caller falls back to the full Equals). A big
        // IntValue can never equal a small needle: long-fitting values are stored small (canonical).
        internal static int SmallIntEq(long needle, ScriptValue element)
        {
            if (element.Kind == ValueKind.Int)
            {
                IntValue iv = (IntValue)element;
                return iv.IsSmall ? (iv.Small == needle ? 1 : 0) : 0;
            }
            if (element.Kind == ValueKind.Bool)
                return (((BoolValue)element).Value ? 1L : 0L) == needle ? 1 : 0;
            return -1;
        }

        public static long Length(ScriptValue v, EvalContext ctx)
        {
            long ctxLen;
            if (v.TryLengthWithContextCore(ctx, out ctxLen))
                return ctxLen;
            System.Numerics.BigInteger big;
            if (v.TryLengthBig(out big))
            {
                if (big > long.MaxValue)
                    throw Raise.Overflow(ctx, "Python int too large to convert to C ssize_t");
                return (long)big;
            }
            throw Raise.TypeError(ctx, "object of type '" + v.PyTypeName + "' has no len()");
        }

        public static IScriptIterator GetIterator(ScriptValue v, EvalContext ctx)
        {
            IScriptIterator it = TryGetIterator(v, ctx);
            if (it == null)
                throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object is not iterable");
            return it;
        }

        // The asking half: null when the value has no iterator, so a caller with its own TypeError text
        // tests instead of catching. What a hook raises on its own (a generator body) still propagates.
        public static IScriptIterator TryGetIterator(ScriptValue v, EvalContext ctx)
        {
            return v.GetIteratorCore(ctx);
        }

        public static bool Contains(ScriptValue item, ScriptValue container, EvalContext ctx)
        {
            bool found;
            if (container.TryContainsCore(item, ctx, 0, out found))
                return found;
            IScriptIterator it = TryGetIterator(container, ctx);
            if (it == null)
                throw Raise.TypeError(ctx, "argument of type '" + container.PyTypeName + "' is not iterable");
            ScriptValue cur;
            while (it.MoveNext(ctx, out cur))
                if (Equals(item, cur, ctx, 0))
                    return true;
            return false;
        }

        // Subscript obj[idx] (a contract for the Evaluator and for str.format field access). The builtin
        // containers are dispatched by kind; everything else answers through GetItemCore.
        // obj[idx] = value and del obj[idx]: the same contract as GetItem, for the Evaluator and operator.
        public static void SetItem(ScriptValue obj, ScriptValue idx, ScriptValue value, EvalContext ctx)
        {
            switch (obj.Kind)
            {
                case ValueKind.List: ((ListValue)obj).SetItem(idx, value, ctx); return;
                case ValueKind.Dict: ((DictValue)obj).SetItem(idx, value, ctx); return;
                default:
                    if (obj.TrySetItemCore(idx, value, ctx))
                        return;
                    throw Raise.TypeError(ctx, "'" + obj.PyTypeName + "' object does not support item assignment");
            }
        }

        public static void DelItem(ScriptValue obj, ScriptValue idx, EvalContext ctx)
        {
            switch (obj.Kind)
            {
                case ValueKind.List: ((ListValue)obj).DelItem(idx, ctx); return;
                case ValueKind.Dict: ((DictValue)obj).DelItemOrThrow(idx, ctx); return;
                default:
                    if (obj.TryDelItemCore(idx, ctx))
                        return;
                    throw Raise.TypeError(ctx, "'" + obj.PyTypeName + "' object doesn't support item deletion");
            }
        }

        public static ScriptValue GetItem(ScriptValue obj, ScriptValue idx, EvalContext ctx)
        {
            switch (obj.Kind)
            {
                case ValueKind.List: return ((ListValue)obj).GetItem(idx, ctx);
                case ValueKind.Tuple: return ((TupleValue)obj).GetItem(idx, ctx);
                case ValueKind.Str: return ((StrValue)obj).GetItem(idx, ctx);
                case ValueKind.Bytes: return ((BytesValue)obj).GetItem(idx, ctx);
                case ValueKind.Dict: return ((DictValue)obj).GetItemOrThrow(idx, ctx);
                case ValueKind.Range: return ((RangeValue)obj).GetItem(idx, ctx);
                default:
                    ScriptValue viaHook = obj.GetItemCore(idx, ctx);
                    if (viaHook != null)
                        return viaHook;
                    throw Raise.TypeError(ctx, "'" + obj.PyTypeName + "' object is not subscriptable");
            }
        }

        // Attribute access obj.attr (a contract for the Evaluator). Method slot -> a fresh BoundMethod
        // per access; Property slot -> the getter; TypeValue -> its static slots; else AttributeError.
        // Dunders are banned from slot tables, so the exception/callable data attributes
        // (args, __cause__, __name__, ...) are dispatched here by kind.
        public static ScriptValue GetAttr(ScriptValue obj, string name, EvalContext ctx)
        {
            ModuleValue mod = obj as ModuleValue;
            if (mod != null)
            {
                ScriptValue mv;
                if (mod.Members.TryGetValue(name, out mv))
                    return mv;
                if (name == "__name__")
                    return ctx.Values.Str(mod.Name);
                throw Raise.Make(ctx, PyExceptionTypes.AttributeError,
                    "module '" + mod.Name + "' has no attribute '" + name + "'");
            }

            SlotDescriptor d;
            if (obj.TypeInfo.Slots.TryGetValue(name, out d))
                return d.Kind == SlotKind.Method ? ctx.Values.BoundMethod(obj, d) : d.Getter(obj, ctx);

            if (obj.Kind == ValueKind.Exception)
            {
                ExceptionValue ev = (ExceptionValue)obj;
                if (name == "args")
                    return ctx.Values.Tuple(ev.Args);   // Args is never mutated; adopting it is safe
                if (name == "__cause__")
                    return ev.Cause != null ? (ScriptValue)ev.Cause : ctx.Values.None;
                if (name == "__context__")
                    return ev.Context != null ? (ScriptValue)ev.Context : ctx.Values.None;
                if (name == "__notes__" && ev.Notes != null)
                {
                    ListValue notes = ctx.Values.List(ev.Notes.Count);
                    foreach (string n in ev.Notes)
                        notes.Items.Add(ctx.Values.Str(n));
                    return notes;
                }
                ScriptValue data;
                if (ev.Data != null && ev.Data.TryGetValue(name, out data))
                    return data;
                if (ev.Args.Length == 5 && ev.ExcType.IsUnicodeCodecError)
                {
                    switch (name)
                    {
                        case "encoding": return ev.Args[0];
                        case "object": return ev.Args[1];
                        case "start": return ev.Args[2];
                        case "end": return ev.Args[3];
                        case "reason": return ev.Args[4];
                    }
                }
            }
            else if (name == "__name__")
            {
                string nm = CallableName(obj);
                if (nm != null)
                    return ctx.Values.Str(nm);
            }

            if (obj.Kind == ValueKind.Type)
            {
                TypeValue tv = (TypeValue)obj;
                ScriptValue sv;
                if (tv.StaticSlots != null && tv.StaticSlots.TryGetValue(name, out sv))
                    return sv;
                sv = tv.TryGetUnboundSlot(name);
                if (sv != null)
                    return sv;
                throw Raise.Make(ctx, PyExceptionTypes.AttributeError, "type object '" + tv.Name + "' has no attribute '" + name + "'");
            }
            if (obj.Kind == ValueKind.RecordInstance)
                return ((RecordInstanceValue)obj).GetAttribute(name, ctx);
            if (obj.Kind == ValueKind.RecordClass)
                return ((RecordClassValue)obj).GetStaticAttr(name, ctx);
            throw Raise.Attribute(ctx, obj.PyTypeName, name);
        }

        // Attribute assignment obj.attr = value; shared by the evaluator's store paths and setattr()
        // so both refuse the same things. Record instances have settable slots; every other
        // kind is attribute-immutable unless it opts in through TrySetAttrCore.
        public static void SetAttr(ScriptValue obj, string name, ScriptValue value, EvalContext ctx)
        {
            if (obj.Kind == ValueKind.RecordInstance)
            {
                ((RecordInstanceValue)obj).SetAttribute(name, value, ctx);
                return;
            }
            if (obj.TrySetAttrCore(name, value, ctx))
                return;
            ModuleValue smod = obj as ModuleValue;
            if (smod != null)
                throw Raise.Make(ctx, PyExceptionTypes.AttributeError,
                    "module '" + smod.Name + "' is read-only in this environment");
            // A name the type does not carry at all is "no attribute" (as in CPython); one it exposes
            // read-only keeps the not-writable wording.
            if (obj.TypeInfo.Slots.ContainsKey(name))
                throw Raise.Make(ctx, PyExceptionTypes.AttributeError,
                    "attribute '" + name + "' of '" + obj.PyTypeName + "' objects is not writable");
            throw Raise.Attribute(ctx, obj.PyTypeName, name);
        }

        // The __name__ of a callable-ish object; null when the kind has none.
        internal static string CallableName(ScriptValue obj)
        {
            switch (obj.Kind)
            {
                case ValueKind.Function: return ((FunctionValue)obj).EffectiveName;
                case ValueKind.BuiltinFunction: return ((BuiltinFunctionValue)obj).Name;
                case ValueKind.Type: return ((TypeValue)obj).Name;
                case ValueKind.RecordClass: return ((RecordClassValue)obj).Name;
                case ValueKind.BoundMethod:
                {
                    BoundUserMethodValue um = obj as BoundUserMethodValue;
                    if (um != null)
                        return um.Func.EffectiveName;
                    BoundMethodValue bm = obj as BoundMethodValue;
                    return bm != null ? bm.Descriptor.Name : null;
                }
                default: return null;
            }
        }

        internal static string Sym(PyBinOp op)
        {
            switch (op)
            {
                case PyBinOp.Add: return "+";
                case PyBinOp.Sub: return "-";
                case PyBinOp.Mul: return "*";
                case PyBinOp.TrueDiv: return "/";
                case PyBinOp.FloorDiv: return "//";
                case PyBinOp.Mod: return "%";
                case PyBinOp.Pow: return "** or pow()";
                case PyBinOp.LShift: return "<<";
                case PyBinOp.RShift: return ">>";
                case PyBinOp.BitAnd: return "&";
                case PyBinOp.BitOr: return "|";
                default: return "^";
            }
        }

        internal static string Sym(PyUnaryOp op)
        {
            switch (op)
            {
                case PyUnaryOp.Neg: return "-";
                case PyUnaryOp.Pos: return "+";
                default: return "~";
            }
        }

        internal static string Sym(PyCmpOp op)
        {
            switch (op)
            {
                case PyCmpOp.Eq: return "==";
                case PyCmpOp.Ne: return "!=";
                case PyCmpOp.Lt: return "<";
                case PyCmpOp.Le: return "<=";
                case PyCmpOp.Gt: return ">";
                default: return ">=";
            }
        }

        private static void StackProbe(EvalContext ctx)
        {
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                throw ctx.Budget.CreateAbort(EngineAbortKind.Recursion, "StackDepth", 0, 0);
            }
        }
    }
}
