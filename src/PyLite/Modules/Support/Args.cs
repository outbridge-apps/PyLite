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

        // Parameters a caller may pass positionally or by name (groups(default=...)): at most names.Length
        // of them, an unknown or repeated keyword refused; returns them positionally.
        public static ScriptValue[] Named(EvalContext c, ScriptValue[] a, KwArgs kw, string f, params string[] names)
        {
            ScriptValue[] r = StrMethods.WithKw(c, a, kw, f, names);
            AtMost(c, r, f, names.Length);
            return r;
        }

        // The three shapes of a function that a keyword must not pass unseen. These run before the body, so a refused
        // call changes nothing.
        public static BuiltinDelegate NoKeywords(string f, BuiltinDelegate fn)
        {
            return (self, a, kw, c) =>
            {
                NoKw(c, kw, f);
                return fn(self, a, kw, c);
            };
        }

        // The body takes its parameters by position: keywords are put in place first.
        public static BuiltinDelegate BoundKeywords(string f, BuiltinDelegate fn, params string[] names)
        {
            return BoundKeywords(f, 0, fn, names);
        }

        // As above, and the first `required` names have no default: one left out while a later one is given by name is
        // CPython's "missing" (repeat(times=2) ran with object None).
        public static BuiltinDelegate BoundKeywords(string f, int required, BuiltinDelegate fn, params string[] names)
        {
            return (self, a, kw, c) =>
            {
                for (int i = 0; i < kw.Count; i++)
                {
                    if (System.Array.IndexOf(names, kw.NameAt(i)) < 0)
                        throw KwReader.UnexpectedError(c, f, kw.NameAt(i));   // before "missing", as CPython checks
                }
                for (int i = a.Length; i < required && kw.Count > 0; i++)
                {
                    ScriptValue given;
                    if (!kw.TryGet(names[i], out given))
                        throw Raise.TypeError(c, f + "() missing required argument '" + names[i] + "' (pos " + (i + 1) + ")");
                }
                if (kw.Count == 0 && a.Length > names.Length)
                    throw AtMostError(c, f, names.Length, a.Length);   // an extra positional was dropped without a keyword
                return kw.Count == 0 ? fn(self, a, kw, c) : fn(self, StrMethods.WithKw(c, a, kw, f, names), KwArgs.Empty, c);
            };
        }

        // The body reads its keywords itself: only the names it knows may reach it.
        public static BuiltinDelegate KnownKeywords(string f, BuiltinDelegate fn, params string[] names)
        {
            return KnownKeywords(f, 0, fn, names);
        }

        // As above, and the first `positional` names are the parameters at positions 0, 1, ...: one given both ways is
        // CPython's "multiple values" (permutations-style drops kept the positional and lost the keyword silently).
        public static BuiltinDelegate KnownKeywords(string f, int positional, BuiltinDelegate fn, params string[] names)
        {
            return (self, a, kw, c) =>
            {
                if (positional > 0 && a.Length > positional)
                    throw AtMostError(c, f, positional, a.Length);   // the positional names are all of them
                for (int i = 0; i < kw.Count; i++)
                {
                    int at = System.Array.IndexOf(names, kw.NameAt(i));
                    if (at < 0)
                        throw KwReader.UnexpectedError(c, f, kw.NameAt(i));
                    if (at < positional && at < a.Length)
                        throw Raise.TypeError(c, f + "() got multiple values for argument '" + names[at] + "'");
                }
                return fn(self, a, kw, c);
            };
        }

        // As KnownKeywords, and the first `lead` names are the required parameters, which the body reads by position.
        public static BuiltinDelegate LeadKeywords(string f, BuiltinDelegate fn, int lead, params string[] names)
        {
            return (self, a, kw, c) =>
            {
                if (a.Length > names.Length)
                    throw AtMostError(c, f, names.Length, a.Length);   // Decimal(1, 2, 3) dropped the third
                if (kw.Count == 0)
                    return fn(self, a, kw, c);
                var leading = new ScriptValue[lead];
                var restNames = new System.Collections.Generic.List<string>();
                var restValues = new System.Collections.Generic.List<ScriptValue>();
                for (int i = 0; i < kw.Count; i++)
                {
                    string name = kw.NameAt(i);
                    int at = System.Array.IndexOf(names, name);
                    if (at < 0)
                        throw KwReader.UnexpectedError(c, f, name);
                    if (at >= lead && at < a.Length)
                        throw Raise.TypeError(c, f + "() got multiple values for argument '" + name + "'");   // the names follow the signature
                    if (at >= lead)
                    {
                        restNames.Add(name);
                        restValues.Add(kw.ValueAt(i));
                    }
                    else if (at < a.Length)
                    {
                        throw Raise.TypeError(c, f + "() got multiple values for argument '" + name + "'");
                    }
                    else
                    {
                        leading[at] = kw.ValueAt(i);
                    }
                }
                int filled = a.Length;
                while (filled < lead && leading[filled] != null)
                    filled++;
                for (int i = filled; i < lead; i++)
                {
                    if (leading[i] != null)
                        throw Raise.TypeError(c, f + "() missing required argument '" + names[filled] + "'");
                }
                ScriptValue[] all = a;
                if (filled > a.Length)
                {
                    all = new ScriptValue[filled];
                    System.Array.Copy(a, all, a.Length);
                    System.Array.Copy(leading, a.Length, all, a.Length, filled - a.Length);
                }
                return fn(self, all, KwArgs.FromArrays(restNames.ToArray(), restValues.ToArray()), c);
            };
        }

        // A signature of positional-or-keyword parameters `names`, in order, of which at most `max` come by position:
        // an unknown keyword, one given both ways, or one positional too many is refused before the body. The body
        // still reads its own keywords (they are not moved into place).
        public static BuiltinDelegate Signature(string f, int max, BuiltinDelegate fn, params string[] names)
        {
            return (self, a, kw, c) =>
            {
                if (a.Length > max)
                    Unpack(c, a, f, 0, max);
                for (int i = 0; i < kw.Count; i++)
                {
                    int at = System.Array.IndexOf(names, kw.NameAt(i));
                    if (at < 0)
                        throw KwReader.UnexpectedError(c, f, kw.NameAt(i));
                    if (at < a.Length)
                        throw Raise.TypeError(c, f + "() got multiple values for argument '" + names[at] + "'");
                }
                return fn(self, a, kw, c);
            };
        }

        // A builtin or method of min..max positional arguments and no keywords, registered outside Slots
        // (BuiltinFunctionValue.Make, SlotDescriptor.MakeMethod): both are checked before the body, so
        // neither a keyword nor an extra argument is dropped silently.
        public static BuiltinDelegate Positional(string f, int min, int max, BuiltinDelegate fn)
        {
            return (self, a, kw, c) =>
            {
                if (kw.Count != 0 || a.Length < min || a.Length > max)
                {
                    NoKw(c, kw, f);
                    CheckPositional(c, a, f, min, max);
                }
                return fn(self, a, kw, c);
            };
        }

        // A positional-only C function's arity in the wording Argument Clinic gives it: no parameter is
        // METH_NOARGS and one is METH_O ("f() takes no arguments / exactly one argument (N given)");
        // any other range is _PyArg_CheckPositional's (Unpack below).
        public static void CheckPositional(EvalContext c, ScriptValue[] a, string f, int min, int max)
        {
            if (max == 0)
                None(c, a, f);
            else if (min == 1 && max == 1)
                Exactly(c, a, f, 1);
            else
                Unpack(c, a, f, min, max);
        }

        // CPython's _PyArg_CheckPositional wording, used by builtins that unpack their own arguments:
        // "<f> expected [at least |at most ]N argument(s), got M", naming the bound actually crossed.
        public static void Unpack(EvalContext c, ScriptValue[] a, string f, int min, int max)
        {
            if (a.Length < min)
                throw UnpackError(c, f, min == max ? "" : "at least ", min, a.Length);
            if (a.Length > max)
                throw UnpackError(c, f, min == max ? "" : "at most ", max, a.Length);
        }

        public static ScriptException UnpackError(EvalContext c, string f, string bound, int n, int got)
        {
            return Raise.TypeError(c, f + " expected " + bound + n + " argument" + (n == 1 ? "" : "s") + ", got " + got);
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
