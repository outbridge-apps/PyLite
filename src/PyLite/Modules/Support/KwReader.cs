using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // Reads a builtin's keyword arguments by name and then rejects what was not read. Get/TryGet/Bool
    // mark a name as consumed, Accept marks one that is taken and ignored, and RejectUnknown (Argument
    // Clinic's "f() got an unexpected keyword argument 'x'") or RejectInvalid (PyArg_ParseTupleAndKeywords'
    // "'x' is an invalid keyword argument for f()") names the first leftover. A keyword spelling of a
    // positional parameter maps through StrMethods.WithKw; a signature with defaults binds through ArgSpec.
    internal struct KwReader
    {
        private readonly EvalContext _c;
        private readonly KwArgs _kw;
        private readonly string _f;
        private ulong _seen;       // bit i: NameAt(i) consumed (a call carries far fewer than 64 keywords)
        private bool[] _seenWide;  // the same beyond 64

        public KwReader(EvalContext c, KwArgs kw, string f)
        {
            _c = c;
            _kw = kw;
            _f = f;
            _seen = 0;
            _seenWide = kw.Count > 64 ? new bool[kw.Count] : null;
        }

        public bool TryGet(string name, out ScriptValue value)
        {
            for (int i = 0; i < _kw.Count; i++)
            {
                if (_kw.NameAt(i) == name)
                {
                    Mark(i);
                    value = _kw.ValueAt(i);
                    return true;
                }
            }
            value = null;
            return false;
        }

        // The value, or null when the keyword was not given.
        public ScriptValue Get(string name)
        {
            ScriptValue v;
            return TryGet(name, out v) ? v : null;
        }

        public ScriptValue Get(string name, ScriptValue dflt)
        {
            ScriptValue v;
            return TryGet(name, out v) ? v : dflt;
        }

        // The value, or null when it was not given or was None: the common "None means default" knob.
        public ScriptValue GetOrNull(string name)
        {
            ScriptValue v;
            return TryGet(name, out v) && v.Kind != ValueKind.None ? v : null;
        }

        public bool Bool(string name, bool dflt)
        {
            ScriptValue v;
            return TryGet(name, out v) ? v.IsTruthy(_c) : dflt;
        }

        public void Accept(string name)
        {
            ScriptValue ignored;
            TryGet(name, out ignored);
        }

        public void RejectUnknown()
        {
            int i = FirstUnread();
            if (i >= 0)
                throw UnexpectedError(_c, _f, _kw.NameAt(i));
        }

        public void RejectInvalid()
        {
            int i = FirstUnread();
            if (i >= 0)
                throw InvalidError(_c, _f, _kw.NameAt(i));
        }

        // For a site that reads its keywords elsewhere and only validates the names here.
        public static void RejectUnknownExcept(EvalContext c, KwArgs kw, string f, params string[] names)
        {
            for (int i = 0; i < kw.Count; i++)
            {
                if (System.Array.IndexOf(names, kw.NameAt(i)) < 0)
                    throw UnexpectedError(c, f, kw.NameAt(i));
            }
        }

        public static void RejectInvalidExcept(EvalContext c, KwArgs kw, string f, string[] names, string[] alsoNames = null)
        {
            for (int i = 0; i < kw.Count; i++)
            {
                string k = kw.NameAt(i);
                if (System.Array.IndexOf(names, k) < 0 && (alsoNames == null || System.Array.IndexOf(alsoNames, k) < 0))
                    throw InvalidError(c, f, k);
            }
        }

        // The two texts, for a switch-shaped reader's default branch.
        public static ScriptException UnexpectedError(EvalContext c, string f, string name)
        {
            return Raise.TypeError(c, f + "() got an unexpected keyword argument '" + name + "'");
        }

        // f == null is PyArg's anonymous form, "for this function" (csv's dialect keywords keep it).
        public static ScriptException InvalidError(EvalContext c, string f, string name)
        {
            return Raise.TypeError(c, "'" + name + "' is an invalid keyword argument for " + (f == null ? "this function" : f + "()"));
        }

        private void Mark(int i)
        {
            if (_seenWide != null)
                _seenWide[i] = true;
            else
                _seen |= 1UL << i;
        }

        private int FirstUnread()
        {
            for (int i = 0; i < _kw.Count; i++)
            {
                bool seen = _seenWide != null ? _seenWide[i] : (_seen & (1UL << i)) != 0;
                if (!seen)
                    return i;
            }
            return -1;
        }
    }
}
