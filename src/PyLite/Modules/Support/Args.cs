using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // The arity checks of builtins and methods, with CPython's C-API texts in one place: "takes no
    // arguments" and "takes exactly one argument" (METH_NOARGS / METH_O), "takes exactly / at least /
    // at most N argument(s)" (PyArg_ParseTuple), each with "(M given)", and "takes no keyword
    // arguments". A signature with defaults or keyword-only parameters binds through ArgSpec instead.
    internal static class Args
    {
        public static void None(EvalContext c, ScriptValue[] a, string f)
        {
            if (a.Length != 0)
                throw NoneError(c, f, a.Length);
        }

        public static void None(EvalContext c, ScriptValue[] a, KwArgs kw, string f)
        {
            NoKw(c, kw, f);
            None(c, a, f);
        }

        // The one positional argument, or the METH_O text.
        public static ScriptValue One(EvalContext c, ScriptValue[] a, string f)
        {
            if (a.Length != 1)
                throw ExactlyError(c, f, 1, a.Length);
            return a[0];
        }

        public static ScriptValue One(EvalContext c, ScriptValue[] a, KwArgs kw, string f)
        {
            NoKw(c, kw, f);
            return One(c, a, f);
        }

        public static void Exactly(EvalContext c, ScriptValue[] a, string f, int n)
        {
            if (a.Length != n)
                throw ExactlyError(c, f, n, a.Length);
        }

        public static void Exactly(EvalContext c, ScriptValue[] a, KwArgs kw, string f, int n)
        {
            NoKw(c, kw, f);
            Exactly(c, a, f, n);
        }

        public static void AtLeast(EvalContext c, ScriptValue[] a, string f, int min)
        {
            if (a.Length < min)
                throw AtLeastError(c, f, min, a.Length);
        }

        public static void AtLeast(EvalContext c, ScriptValue[] a, KwArgs kw, string f, int min)
        {
            NoKw(c, kw, f);
            AtLeast(c, a, f, min);
        }

        public static void AtMost(EvalContext c, ScriptValue[] a, string f, int max)
        {
            if (a.Length > max)
                throw AtMostError(c, f, max, a.Length);
        }

        public static void AtMost(EvalContext c, ScriptValue[] a, KwArgs kw, string f, int max)
        {
            NoKw(c, kw, f);
            AtMost(c, a, f, max);
        }

        // Reports the bound that was crossed, as PyArg_ParseTuple does.
        public static void Between(EvalContext c, ScriptValue[] a, string f, int min, int max)
        {
            if (min == max)
                Exactly(c, a, f, min);
            else if (a.Length < min)
                throw AtLeastError(c, f, min, a.Length);
            else if (a.Length > max)
                throw AtMostError(c, f, max, a.Length);
        }

        public static void Between(EvalContext c, ScriptValue[] a, KwArgs kw, string f, int min, int max)
        {
            NoKw(c, kw, f);
            Between(c, a, f, min, max);
        }

        public static void NoKw(EvalContext c, KwArgs kw, string f)
        {
            if (kw.Count != 0)
                throw Raise.TypeError(c, f + "() takes no keyword arguments");
        }

        // The exceptions themselves, for a binder that finds the miss its own way (ArgSpec).
        public static ScriptException NoneError(EvalContext c, string f, int got)
        {
            return Raise.TypeError(c, f + "() takes no arguments (" + got + " given)");
        }

        public static ScriptException ExactlyError(EvalContext c, string f, int n, int got)
        {
            if (n == 0)
                return NoneError(c, f, got);
            if (n == 1)
                return Raise.TypeError(c, f + "() takes exactly one argument (" + got + " given)");
            return Raise.TypeError(c, f + "() takes exactly " + n + " arguments (" + got + " given)");
        }

        public static ScriptException AtLeastError(EvalContext c, string f, int min, int got)
        {
            return Raise.TypeError(c, f + "() takes at least " + min + Plural(min) + " (" + got + " given)");
        }

        public static ScriptException AtMostError(EvalContext c, string f, int max, int got)
        {
            return Raise.TypeError(c, f + "() takes at most " + max + Plural(max) + " (" + got + " given)");
        }

        private static string Plural(int n)
        {
            return n == 1 ? " argument" : " arguments";
        }
    }
}
