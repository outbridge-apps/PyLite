using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // super(Class, obj): the class attributes of obj looked up from the class above Class in obj's chain,
    // bound to obj. Instance fields are not visible through it, as in CPython.
    public sealed class SuperValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("super", null);
        internal readonly RecordClassValue ThisClass;   // the class of obj's chain whose method asked
        internal readonly RecordInstanceValue Self;

        internal SuperValue(RecordClassValue thisClass, RecordInstanceValue self) { ThisClass = thisClass; Self = self; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        internal ScriptValue GetAttribute(string name, EvalContext ctx)
        {
            RecordClassValue start = ThisClass.Base;
            if (name == "__init__")
            {
                FunctionValue written = null;
                RecordClassValue generated = null;
                if (start != null)
                    start.ResolveInit(out written, out generated);
                return written != null ? (ScriptValue)new BoundUserMethodValue(Self, written) : new SuperInitValue(generated, Self);
            }
            ScriptValue k;
            FunctionValue m;
            if (start != null && start.TryFindClassAttr(name, out k, out m))
            {
                FunctionValue kf = k as FunctionValue;
                return m != null ? new BoundUserMethodValue(Self, m) : kf != null ? new BoundUserMethodValue(Self, kf) : k;
            }
            throw Raise.Make(ctx, PyExceptionTypes.AttributeError, "'super' object has no attribute '" + name + "'");
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("<super: <class '" + ThisClass.Name + "'>, <" + Self.Class.Name + " object>>");
        }
    }

    // super().__init__ when no written __init__ is above: the one @dataclass generated, or object.__init__.
    internal sealed class SuperInitValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("method", null);
        private readonly RecordClassValue _generated;   // null => object.__init__
        private readonly RecordInstanceValue _self;

        internal SuperInitValue(RecordClassValue generated, RecordInstanceValue self) { _generated = generated; _self = self; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }
        internal override bool IsCallable { get { return true; } }

        protected internal override ScriptValue CallCore(ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (_generated != null)
                return ctx.DataclassInitHook(_generated, _self, args, kw);
            if (args.Length > 0 || kw.Count > 0)
                throw Raise.TypeError(ctx, "object.__init__() takes exactly one argument (the instance to initialize)");
            return ctx.Values.None;
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("<bound method __init__ of " + _self.Class.Name + " object>");
        }
    }
}
