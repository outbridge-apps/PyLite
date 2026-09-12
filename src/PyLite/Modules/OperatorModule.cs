using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // operator: the operators as functions, over the same PyOps entry points the evaluator uses, plus
    // itemgetter / attrgetter / methodcaller. The in-place i* forms, matmul, length_hint and call are
    // not here.
    public static class OperatorModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            Bin(m, "add", PyBinOp.Add);
            Bin(m, "concat", PyBinOp.Add);
            Bin(m, "sub", PyBinOp.Sub);
            Bin(m, "mul", PyBinOp.Mul);
            Bin(m, "truediv", PyBinOp.TrueDiv);
            Bin(m, "floordiv", PyBinOp.FloorDiv);
            Bin(m, "mod", PyBinOp.Mod);
            Bin(m, "pow", PyBinOp.Pow);
            Bin(m, "lshift", PyBinOp.LShift);
            Bin(m, "rshift", PyBinOp.RShift);
            Bin(m, "and_", PyBinOp.BitAnd);
            Bin(m, "or_", PyBinOp.BitOr);
            Bin(m, "xor", PyBinOp.BitXor);
            Un(m, "neg", PyUnaryOp.Neg);
            Un(m, "pos", PyUnaryOp.Pos);
            Un(m, "invert", PyUnaryOp.Invert);
            Un(m, "inv", PyUnaryOp.Invert);
            Cmp(m, "lt", PyCmpOp.Lt);
            Cmp(m, "le", PyCmpOp.Le);
            Cmp(m, "eq", PyCmpOp.Eq);
            Cmp(m, "ne", PyCmpOp.Ne);
            Cmp(m, "ge", PyCmpOp.Ge);
            Cmp(m, "gt", PyCmpOp.Gt);
            m["abs"] = BuiltinFunctionValue.Make("abs", (s, a, k, c) => Builtins.AbsBuiltin(c, Exactly(c, a, 1, "abs")));
            m["not_"] = BuiltinFunctionValue.Make("not_", (s, a, k, c) => c.Values.Bool(!PyOps.Truth(Exactly(c, a, 1, "not_")[0], c)));
            m["truth"] = BuiltinFunctionValue.Make("truth", (s, a, k, c) => c.Values.Bool(PyOps.Truth(Exactly(c, a, 1, "truth")[0], c)));
            m["is_"] = BuiltinFunctionValue.Make("is_", (s, a, k, c) => c.Values.Bool(ReferenceEquals(Exactly(c, a, 2, "is_")[0], a[1])));
            m["is_not"] = BuiltinFunctionValue.Make("is_not", (s, a, k, c) => c.Values.Bool(!ReferenceEquals(Exactly(c, a, 2, "is_not")[0], a[1])));
            m["contains"] = BuiltinFunctionValue.Make("contains", (s, a, k, c) => c.Values.Bool(PyOps.Contains(Exactly(c, a, 2, "contains")[1], a[0], c)));
            m["countOf"] = BuiltinFunctionValue.Make("countOf", (s, a, k, c) => CountOf(c, Exactly(c, a, 2, "countOf")));
            m["indexOf"] = BuiltinFunctionValue.Make("indexOf", (s, a, k, c) => IndexOf(c, Exactly(c, a, 2, "indexOf")));
            m["getitem"] = BuiltinFunctionValue.Make("getitem", (s, a, k, c) => PyOps.GetItem(Exactly(c, a, 2, "getitem")[0], a[1], c));
            m["setitem"] = BuiltinFunctionValue.Make("setitem", (s, a, k, c) =>
            {
                PyOps.SetItem(Exactly(c, a, 3, "setitem")[0], a[1], a[2], c);
                return c.Values.None;
            });
            m["delitem"] = BuiltinFunctionValue.Make("delitem", (s, a, k, c) =>
            {
                PyOps.DelItem(Exactly(c, a, 2, "delitem")[0], a[1], c);
                return c.Values.None;
            });
            m["index"] = BuiltinFunctionValue.Make("index", (s, a, k, c) =>
            {
                ScriptValue v = Exactly(c, a, 1, "index")[0];
                if (v.Kind != ValueKind.Int && v.Kind != ValueKind.Bool)
                    throw Raise.TypeError(c, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
                return v;
            });
            m["itemgetter"] = BuiltinFunctionValue.Make("itemgetter", ItemGetter);
            m["attrgetter"] = BuiltinFunctionValue.Make("attrgetter", AttrGetter);
            m["methodcaller"] = BuiltinFunctionValue.Make("methodcaller", MethodCaller);
            return ctx.Values.Module("operator", m);
        }

        private static ScriptValue[] Exactly(EvalContext ctx, ScriptValue[] a, int n, string name)
        {
            Args.Exactly(ctx, a, name, n);
            return a;
        }

        private static void Bin(Dictionary<string, ScriptValue> m, string name, PyBinOp op)
        {
            m[name] = BuiltinFunctionValue.Make(name, (s, a, k, c) => PyOps.BinaryOp(op, Exactly(c, a, 2, name)[0], a[1], c));
        }

        private static void Un(Dictionary<string, ScriptValue> m, string name, PyUnaryOp op)
        {
            m[name] = BuiltinFunctionValue.Make(name, (s, a, k, c) => PyOps.UnaryOp(op, Exactly(c, a, 1, name)[0], c));
        }

        private static void Cmp(Dictionary<string, ScriptValue> m, string name, PyCmpOp op)
        {
            m[name] = BuiltinFunctionValue.Make(name, (s, a, k, c) => PyOps.RichCompare(op, Exactly(c, a, 2, name)[0], a[1], c));
        }

        private static ScriptValue CountOf(EvalContext ctx, ScriptValue[] a)
        {
            int n = 0;
            IScriptIterator it = PyOps.GetIterator(a[0], ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
            {
                ctx.Budget.Step();
                if (ReferenceEquals(v, a[1]) || PyOps.Equals(v, a[1], ctx, 0))
                    n++;
            }
            return ctx.Values.Int(n);
        }

        private static ScriptValue IndexOf(EvalContext ctx, ScriptValue[] a)
        {
            int i = 0;
            IScriptIterator it = PyOps.GetIterator(a[0], ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
            {
                ctx.Budget.Step();
                if (ReferenceEquals(v, a[1]) || PyOps.Equals(v, a[1], ctx, 0))
                    return ctx.Values.Int(i);
                i++;
            }
            throw Raise.ValueError(ctx, "sequence.index(x): x not in sequence");
        }

        // itemgetter(item, ...): one item -> f(obj) = obj[item]; several -> the tuple of them.
        private static ScriptValue ItemGetter(ScriptValue self, ScriptValue[] items, KwArgs kw, EvalContext ctx)
        {
            if (items.Length == 0)
                throw Raise.TypeError(ctx, "itemgetter expected 1 argument, got 0");
            ScriptValue[] keys = (ScriptValue[])items.Clone();
            return BuiltinFunctionValue.Make("itemgetter", (s, a, k, c) =>
            {
                ScriptValue obj = Exactly(c, a, 1, "itemgetter")[0];
                if (keys.Length == 1)
                    return PyOps.GetItem(obj, keys[0], c);
                var got = new ScriptValue[keys.Length];
                for (int i = 0; i < keys.Length; i++)
                    got[i] = PyOps.GetItem(obj, keys[i], c);
                return c.Values.Tuple(got);
            });
        }

        // attrgetter(name, ...): dotted names walk; one name -> the attribute, several -> the tuple.
        private static ScriptValue AttrGetter(ScriptValue self, ScriptValue[] names, KwArgs kw, EvalContext ctx)
        {
            if (names.Length == 0)
                throw Raise.TypeError(ctx, "attrgetter expected 1 argument, got 0");
            var paths = new string[names.Length][];
            for (int i = 0; i < names.Length; i++)
            {
                StrValue s = names[i] as StrValue;
                if (s == null)
                    throw Raise.TypeError(ctx, "attribute name must be a string");
                paths[i] = s.Value.Split('.');
            }
            return BuiltinFunctionValue.Make("attrgetter", (s, a, k, c) =>
            {
                ScriptValue obj = Exactly(c, a, 1, "attrgetter")[0];
                if (paths.Length == 1)
                    return Walk(c, obj, paths[0]);
                var got = new ScriptValue[paths.Length];
                for (int i = 0; i < paths.Length; i++)
                    got[i] = Walk(c, obj, paths[i]);
                return c.Values.Tuple(got);
            });
        }

        private static ScriptValue Walk(EvalContext ctx, ScriptValue obj, string[] path)
        {
            foreach (string step in path)
                obj = PyOps.GetAttr(obj, step, ctx);
            return obj;
        }

        // methodcaller(name, *args, **kwargs): f(obj) = obj.name(*args, **kwargs).
        private static ScriptValue MethodCaller(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            if (a.Length == 0)
                throw Raise.TypeError(ctx, "methodcaller needs at least one argument, the method name");
            StrValue name = a[0] as StrValue;
            if (name == null)
                throw Raise.TypeError(ctx, "method name must be a string");
            var rest = new ScriptValue[a.Length - 1];
            Array.Copy(a, 1, rest, 0, rest.Length);
            return BuiltinFunctionValue.Make("methodcaller", (s, args, k, c) =>
            {
                ScriptValue obj = Exactly(c, args, 1, "methodcaller")[0];
                return c.CallHook(PyOps.GetAttr(obj, name.Value, c), rest, kw);
            });
        }
    }
}
