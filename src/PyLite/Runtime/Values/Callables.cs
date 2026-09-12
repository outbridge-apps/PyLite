using System.Collections.Generic;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Values
{
    public sealed class Cell
    {
        public ScriptValue Value;   // null => the free variable is unbound
    }

    public sealed class FunctionValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("function", null);

        public readonly string Name;                                     // "f" | "<lambda>"
        public readonly FunctionInfo Info;
        public readonly ScriptValue[] Defaults;
        public readonly KeyValuePair<string, ScriptValue>[] KwDefaults;
        public readonly Cell[] Closure;
        public readonly object GlobalsToken;
        public readonly ResolvedProgram Program;        // the compiled unit that owns Body's nodes; the
                                                        // Evaluator resolves this function's name bindings
                                                        // against it, so a function called from another module
                                                        // (prelude) still resolves correctly.
        public readonly IReadOnlyList<StmtNode> Body;   // def body; null for a lambda
        public readonly ExprNode LambdaBody;            // lambda expression; null for a def
        public readonly KeyValuePair<string, string>[] Annotations;   // (param, name) pairs, then ("return", name); the plain-name ones only

        private static readonly KeyValuePair<string, string>[] NoAnnotations = new KeyValuePair<string, string>[0];
        private readonly long _identity;

        // Frame pool (perf): a bounded LIFO freelist of Environments for a cell-free function
        // (locals cleared on release, so unbound-local semantics hold). A single slot would be
        // defeated by recursion — every nested activation would allocate; the freelist hands each
        // depth its own frame and sibling subtrees reuse unwound ones. Typed object[] to keep the
        // Values layer free of the Environment type; runs are single-threaded.
        internal object[] PooledEnvs;   // created lazily on first release; length = the pool cap
        internal int PooledEnvCount;

        // functools.wraps/update_wrapper set this; __name__ and repr read it (null => the def name).
        internal string NameOverride;
        internal string EffectiveName { get { return NameOverride ?? Name; } }

        internal FunctionValue(string name, FunctionInfo info, ScriptValue[] defaults,
            KeyValuePair<string, ScriptValue>[] kwDefaults, Cell[] closure, object globalsToken,
            ResolvedProgram program, IReadOnlyList<StmtNode> body, ExprNode lambdaBody, long identity,
            KeyValuePair<string, string>[] annotations = null)
        {
            Name = name;
            Info = info;
            Defaults = defaults;
            KwDefaults = kwDefaults;
            Closure = closure;
            GlobalsToken = globalsToken;
            Program = program;
            Body = body;
            LambdaBody = lambdaBody;
            _identity = identity;
            Annotations = annotations ?? NoAnnotations;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Function; } }
        internal override bool IsCallable { get { return true; } }

        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return _identity; }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("<function " + EffectiveName + ">"); }
    }

    public sealed class BuiltinFunctionValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("builtin_function_or_method", null);

        public readonly string Name;
        public readonly BuiltinDelegate Fn;
        public readonly bool IsHostProvided;   // set via HostFunctionTable.Set: the call is wrapped by
                                               // HostCallGuard so an arbitrary exception -> HostError
        private readonly long _identity;

        internal BuiltinFunctionValue(string name, BuiltinDelegate fn, long identity, bool isHostProvided = false)
        {
            Name = name;
            Fn = fn;
            IsHostProvided = isHostProvided;
            _identity = identity;
        }

        // Shared-identity instance for the immutable builtins template.
        internal static BuiltinFunctionValue Make(string name, BuiltinDelegate fn)
        {
            return new BuiltinFunctionValue(name, fn, SharedIdentitySource.Next());
        }

        // A host-provided override (HostFunctionTable.Set) — its call is fenced by HostCallGuard.
        internal static BuiltinFunctionValue MakeHost(string name, BuiltinDelegate fn)
        {
            return new BuiltinFunctionValue(name, fn, SharedIdentitySource.Next(), true);
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.BuiltinFunction; } }
        internal override bool IsCallable { get { return true; } }

        protected internal override long HashLeafCore(EvalContext ctx, int depth) { return _identity; }
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth) { return ReferenceEquals(this, other); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("<built-in function " + Name + ">"); }
    }

    public sealed class BoundMethodValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("builtin_function_or_method", null);

        public readonly ScriptValue Self;
        public readonly SlotDescriptor Descriptor;   // Kind must be Method

        internal BoundMethodValue(ScriptValue self, SlotDescriptor descriptor) { Self = self; Descriptor = descriptor; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.BoundMethod; } }
        internal override bool IsCallable { get { return true; } }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            return unchecked(PyOps.Hash(Self, ctx, depth + 1) * 1000003L ^ Descriptor.Name.GetHashCode());
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            BoundMethodValue o = other as BoundMethodValue;
            return o != null && ReferenceEquals(Descriptor, o.Descriptor) && ReferenceEquals(Self, o.Self);
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("<built-in method " + Descriptor.Name + " of " + Self.PyTypeName + " object>");
        }
    }
}
