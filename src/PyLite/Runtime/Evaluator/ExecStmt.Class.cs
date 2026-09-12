using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // record classes. The class statement builds a RecordClassValue: methods become FunctionValues
    // (built like a def, closing over the enclosing frame), slots are fixed here (base slots, then class-body
    // annotation fields, then top-level self.x = ... in __init__). Construction runs __init__ with self bound.
    internal sealed partial class Evaluator
    {
        private ExecSignal ExecClassDef(ClassDefNode n, Environment env, EvalContext ctx)
        {
            RecordClassValue baseCls = null;
            if (n.Base != null)
            {
                ScriptValue b = EvalExpr(n.Base, env, ctx);
                TypeValue excBase = b as TypeValue;
                if (excBase != null && excBase.ExcType != null)
                    return ExecExceptionClassDef(n, excBase, env, ctx);
                baseCls = b as RecordClassValue;
                if (baseCls == null)
                    throw Raise.TypeError(ctx, "base of class '" + n.Name + "' must be a class defined in this dialect");
                int chain = 0;
                for (RecordClassValue c = baseCls; c != null; c = c.Base)
                    chain++;
                if (chain >= 16)
                    throw Raise.TypeError(ctx, "class inheritance chain too deep (limit 16)");
            }

            var methods = new Dictionary<string, FunctionValue>(StringComparer.Ordinal);
            FuncDefNode initNode = null;
            for (int i = 0; i < n.Methods.Count; i++)
            {
                FuncDefNode meth = n.Methods[i];
                FunctionInfo info = _program.GetFunctionInfo(meth);
                methods[meth.Name] = BuildFunction(meth.Name, info, meth.Params, meth.Body, null, env, ctx, meth.ReturnAnnotationName);
                if (meth.Name == "__init__")
                    initNode = meth;
            }

            List<string> allSlots;
            Dictionary<string, int> slotIndex;
            ComputeSlots(n, baseCls, initNode, out allSlots, out slotIndex);
            IReadOnlyDictionary<string, FieldDefault> annDefaults = BuildAnnotationDefaults(n, baseCls, env, ctx);

            var cls = new RecordClassValue(n.Name, baseCls, allSlots, slotIndex, methods, false, null,
                annDefaults, null, SharedIdentitySource.Next());
            env.BindScopeName(n.Name, ApplyClassDecorators(n, cls, env, ctx));
            return ExecSignal.Normal;
        }

        // A class with a builtin-exception base becomes a NEW node of the exception name tree plus a
        // constructor TypeValue — instances are plain ExceptionValues, so raise / except / isinstance /
        // e.args all work through the existing machinery. The body must be empty (pass/docstring):
        // methods and fields stay record-class-only.
        private ExecSignal ExecExceptionClassDef(ClassDefNode n, TypeValue baseTv, Environment env, EvalContext ctx)
        {
            if (n.Methods.Count > 0 || n.Fields.Count > 0)
                throw Raise.TypeError(ctx, "exception class '" + n.Name + "' may not define methods or fields in this dialect");
            int chain = 0;
            for (PyExceptionType p = baseTv.ExcType; p != null; p = p.Parent)
                chain++;
            if (chain >= 16)
                throw Raise.TypeError(ctx, "class inheritance chain too deep (limit 16)");
            ctx.Values.PreCharge(64);
            var excType = new PyExceptionType(n.Name, baseTv.ExcType);
            BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(excType, args);
            ScriptValue cls = TypeValue.Make(n.Name, ctor, null, null, excType);
            env.BindScopeName(n.Name, ApplyClassDecorators(n, cls, env, ctx));
            return ExecSignal.Normal;
        }

        // Evaluate each class-body field default at class-creation time (merged with base annotation
        // defaults). A dataclasses.field(...) marker contributes its default / default_factory.
        private IReadOnlyDictionary<string, FieldDefault> BuildAnnotationDefaults(ClassDefNode n, RecordClassValue baseCls, Environment env, EvalContext ctx)
        {
            var map = new Dictionary<string, FieldDefault>(StringComparer.Ordinal);
            if (baseCls != null && baseCls.AnnotationDefaults != null)
                foreach (var kv in baseCls.AnnotationDefaults)
                    map[kv.Key] = kv.Value;
            foreach (var f in n.Fields)
            {
                FieldKind kind = f.AnnotationHead == "ClassVar" ? FieldKind.ClassVar
                    : f.AnnotationHead == "InitVar" ? FieldKind.InitVar : FieldKind.Field;
                if (f.Default == null)
                {
                    map[f.Name] = new FieldDefault(false, null, null, f.AnnotationName).WithKind(kind);
                    continue;
                }
                ScriptValue v = EvalExpr(f.Default, env, ctx);
                FieldSpecValue spec = v as FieldSpecValue;
                map[f.Name] = (spec != null
                    ? new FieldDefault(spec.HasDefault || spec.DefaultFactory != null, spec.Default, spec.DefaultFactory,
                        f.AnnotationName, spec.Init, spec.InRepr, spec.Compare, spec.KwOnly, spec.Hash, spec.Metadata)
                    : new FieldDefault(true, v, null, f.AnnotationName)).WithKind(kind);
            }
            return map;
        }

        private ScriptValue ApplyClassDecorators(ClassDefNode n, ScriptValue cls, Environment env, EvalContext ctx)
        {
            if (n.Decorators.Count == 0)
                return cls;
            var decs = new ScriptValue[n.Decorators.Count];
            for (int i = 0; i < decs.Length; i++)
                decs[i] = EvalExpr(n.Decorators[i], env, ctx);
            ScriptValue val = cls;
            for (int i = decs.Length - 1; i >= 0; i--)   // PEP 614: bottom-up
                val = Call(decs[i], new[] { val }, KwArgs.Empty, ctx);
            return val;
        }

        private ScriptValue ConstructRecord(RecordClassValue cls, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ctx.Values.PreCharge(24 + 8L * cls.AllSlots.Count);
            var inst = new RecordInstanceValue(cls, new ScriptValue[cls.AllSlots.Count]);
            FunctionValue init = cls.FindMethod("__init__");
            if (init != null)
            {
                var full = new ScriptValue[args.Length + 1];
                full[0] = inst;
                Array.Copy(args, 0, full, 1, args.Length);
                OwnerOf(init.Program).CallFunction(init, full, kw, ctx);
            }
            else if (cls.Dataclass != null && cls.Dataclass.Init)
            {
                ScriptValue[] initVars = RunGeneratedInit(cls, inst, args, kw, ctx);
                FunctionValue post = cls.FindMethod("__post_init__");   // called by the generated __init__ only
                if (post != null)
                {
                    var postArgs = new ScriptValue[1 + initVars.Length];   // self, then the InitVars in declaration order
                    postArgs[0] = inst;
                    Array.Copy(initVars, 0, postArgs, 1, initVars.Length);
                    OwnerOf(post.Program).CallFunction(post, postArgs, KwArgs.Empty, ctx);
                }
            }
            else if (args.Length > 0 || kw.Count > 0)
            {
                throw Raise.TypeError(ctx, cls.Name + "() takes no arguments");
            }
            FillClassVars(cls, inst);
            return inst;
        }

        // x: ClassVar[int] = 5 reads through the instance as it does in CPython (there, through the class).
        private static void FillClassVars(RecordClassValue cls, RecordInstanceValue inst)
        {
            if (cls.AnnotationDefaults == null)
                return;
            foreach (var kv in cls.AnnotationDefaults)
            {
                int idx;
                if (kv.Value.Kind == FieldKind.ClassVar && kv.Value.HasDefault
                    && cls.SlotIndex.TryGetValue(kv.Key, out idx) && inst.Slots[idx] == null)
                    inst.InitSlot(idx, kv.Value.Default);
            }
        }

        // the __init__ @dataclass generates — bind fields positionally/by keyword, then apply
        // defaults / default_factory. Slots are set through InitSlot so frozen dataclasses can be built.
        // Returns the InitVar values in declaration order: they are parameters of the generated __init__
        // that are handed to __post_init__ and never stored.
        private ScriptValue[] RunGeneratedInit(RecordClassValue cls, RecordInstanceValue inst, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            DataclassSpec ds = cls.Dataclass;
            int n = ds.FieldNames.Length;
            var side = new ScriptValue[n];
            void Store(int i, ScriptValue v)
            {
                if (ds.Fields[i].Kind == FieldKind.InitVar)
                    side[i] = v;
                else
                    inst.InitSlot(cls.SlotIndex[ds.FieldNames[i]], v);
            }
            // Positional parameters are the init fields that are not keyword-only, in declaration order.
            var positional = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                if (ds.Fields[i].Init && !ds.Fields[i].KwOnly)
                    positional.Add(i);
            }
            if (args.Length > positional.Count)
                throw Raise.TypeError(ctx, cls.Name + ".__init__() takes " + positional.Count
                    + " positional arguments but " + args.Length + " were given");
            var filled = new bool[n];
            for (int i = 0; i < args.Length; i++)
            {
                Store(positional[i], args[i]);
                filled[positional[i]] = true;
            }
            for (int j = 0; j < kw.Count; j++)
            {
                int idx = Array.IndexOf(ds.FieldNames, kw.NameAt(j));
                if (idx < 0 || !ds.Fields[idx].Init)   // field(init=False) is not a parameter at all
                    throw Modules.Support.KwReader.UnexpectedError(ctx, cls.Name + ".__init__", kw.NameAt(j));
                if (filled[idx])
                    throw Raise.TypeError(ctx, cls.Name + ".__init__() got multiple values for argument '" + ds.FieldNames[idx] + "'");
                Store(idx, kw.ValueAt(j));
                filled[idx] = true;
            }
            List<string> missing = null;
            List<string> missingKwOnly = null;
            for (int i = 0; i < n; i++)
            {
                if (filled[i])
                    continue;
                FieldDefault fd = ds.Fields[i];
                if (fd.DefaultFactory != null)
                    Store(i, Call(fd.DefaultFactory, System.Array.Empty<ScriptValue>(), KwArgs.Empty, ctx));
                else if (fd.HasDefault)
                    Store(i, fd.Default);
                else if (fd.Init)
                {
                    if (fd.KwOnly)
                        (missingKwOnly ?? (missingKwOnly = new List<string>())).Add(ds.FieldNames[i]);
                    else
                        (missing ?? (missing = new List<string>())).Add(ds.FieldNames[i]);
                }
                // an init=False field with no default stays unset, as in CPython
            }
            if (missingKwOnly != null)
                throw Raise.TypeError(ctx, cls.Name + ".__init__() missing " + missingKwOnly.Count
                    + " required keyword-only argument" + (missingKwOnly.Count == 1 ? "" : "s")
                    + ": " + string.Join(", ", missingKwOnly));
            if (missing != null)
                throw Raise.TypeError(ctx, cls.Name + ".__init__() missing " + missing.Count + " required positional argument"
                    + (missing.Count == 1 ? "" : "s") + ": " + string.Join(", ", missing));

            var initVars = new List<ScriptValue>();
            for (int i = 0; i < n; i++)
                if (ds.Fields[i].Kind == FieldKind.InitVar)
                    initVars.Add(side[i]);
            return initVars.ToArray();
        }

        // Slots: base slots, then class-body annotation fields (dataclass source), then every top-level
        // self.<name> = ... assignment in __init__, all first-appearance order.
        private void ComputeSlots(ClassDefNode n, RecordClassValue baseCls, FuncDefNode initNode,
            out List<string> allSlots, out Dictionary<string, int> slotIndex)
        {
            var order = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (baseCls != null)
                foreach (string s in baseCls.AllSlots)
                    if (seen.Add(s))
                        order.Add(s);
            foreach (var f in n.Fields)
                if (seen.Add(f.Name))
                    order.Add(f.Name);
            if (initNode != null)
            {
                string self = initNode.Params.Positional.Count > 0 ? initNode.Params.Positional[0].Name : "self";
                CollectSelfSlots(initNode.Body, self, order, seen);
            }
            slotIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < order.Count; i++)
                slotIndex[order[i]] = i;
            allSlots = order;
        }

        private static void CollectSelfSlots(IReadOnlyList<StmtNode> body, string self, List<string> order, HashSet<string> seen)
        {
            for (int i = 0; i < body.Count; i++)
            {
                switch (body[i])
                {
                    case AssignNode a: foreach (var t in a.Targets)
                        AddSelfSlot(t, self, order, seen); break;
                    case AugAssignNode ag: AddSelfSlot(ag.Target, self, order, seen); break;
                    case IfNode f: CollectSelfSlots(f.Body, self, order, seen); CollectSelfSlots(f.OrElse, self, order, seen); break;
                    case WhileNode w: CollectSelfSlots(w.Body, self, order, seen); CollectSelfSlots(w.OrElse, self, order, seen); break;
                    case ForNode fr: CollectSelfSlots(fr.Body, self, order, seen); CollectSelfSlots(fr.OrElse, self, order, seen); break;
                    case TryNode t:
                        CollectSelfSlots(t.Body, self, order, seen);
                        foreach (var h in t.Handlers)
                            CollectSelfSlots(h.Body, self, order, seen);
                        CollectSelfSlots(t.OrElse, self, order, seen);
                        CollectSelfSlots(t.Finally, self, order, seen);
                        break;
                    case MatchNode m: foreach (var cs in m.Cases)
                        CollectSelfSlots(cs.Body, self, order, seen); break;
                    default: break;   // never descend into a nested def/class
                }
            }
        }

        private static void AddSelfSlot(ExprNode target, string self, List<string> order, HashSet<string> seen)
        {
            switch (target)
            {
                case AttributeNode at:
                    NameNode nm = at.Value as NameNode;
                    if (nm != null && nm.Id == self && seen.Add(at.Attr))
                        order.Add(at.Attr);
                    break;
                case TupleNode tup: foreach (var e in tup.Elts)
                    AddSelfSlot(e, self, order, seen); break;
                case ListNode l: foreach (var e in l.Elts)
                    AddSelfSlot(e, self, order, seen); break;
                case StarredNode s: AddSelfSlot(s.Value, self, order, seen); break;
            }
        }
    }
}
