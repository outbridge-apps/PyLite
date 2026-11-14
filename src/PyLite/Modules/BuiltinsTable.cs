using System;
using System.Collections.Generic;
using System.Threading;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // The immutable default builtins template. Built once, lazily; HostFunctionTable
    // .CreateDefault() hands out mutable copies. The exception classes are wired here; the functions and
    // type constructors come from the Builtins partial class (Builtins.RegisterAll).
    internal static class BuiltinsTable
    {
        // Built once under a lock, not a Lazy: a Lazy caches the exception, so a host thread aborted (an ASP.NET
        // timeout) mid-build made every run of every engine an engine fault until restart. A build cut short
        // publishes nothing and the next caller builds again (the classes it records are all overwritten).
        private static Dictionary<string, ScriptValue> _template;
        private static readonly object TemplateLock = new object();

        private static Dictionary<string, ScriptValue> Template
        {
            get
            {
                Dictionary<string, ScriptValue> t = Volatile.Read(ref _template);
                if (t != null)
                    return t;
                lock (TemplateLock)
                {
                    if (_template == null)
                        Volatile.Write(ref _template, BuildDefaultTemplate());
                    return _template;
                }
            }
        }

        // Builds the template if no run has yet: it records the class of every exception in the builtins tree.
        internal static void EnsureBuilt()
        {
            Dictionary<string, ScriptValue> ignored = Template;
        }

        // A fresh mutable copy of the immutable template (the source of HostFunctionTable.CreateDefault).
        internal static Dictionary<string, ScriptValue> CreateCopy()
        {
            return new Dictionary<string, ScriptValue>(Template, StringComparer.Ordinal);
        }

        private static Dictionary<string, ScriptValue> BuildDefaultTemplate()
        {
            var d = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);

            // Exception classes: calling one constructs an ExceptionValue. BaseException takes no keywords, as in CPython;
            // ImportError, AttributeError and NameError take their attribute keywords (see ConstructException). The TypeValue carries the PyExceptionType for except/isinstance.
            foreach (PyExceptionType pt in PyExceptionTypes.All)
            {
                PyExceptionType captured = pt;
                BuiltinDelegate ctor = ReferenceEquals(captured, PyExceptionTypes.JSONDecodeError)
                    ? JsonModule.DecodeErrorCtor
                    : captured.IsUnicodeCodecError
                        ? (self, args, kw, c) =>
                        {
                            Args.NoKw(c, kw, captured.Name);
                            return c.Values.Exception(captured, ExceptionValue.CheckedCodecArgs(c, args));
                        }
                        : (BuiltinDelegate)((self, args, kw, c) => ConstructException(c, captured, args, kw));
                TypeValue cls = TypeValue.Make(pt.Name, ctor, null, null, captured);
                captured.ClassValue = cls;
                d[pt.Name] = cls;
            }
            d["IOError"] = d["OSError"];
            d["EnvironmentError"] = d["OSError"];
            d["__debug__"] = BoolValue.True;

            Builtins.RegisterAll(d);
            return d;
        }

        // The attributes CPython's constructors keep, read back through ExceptionValue.Data: ImportError name, path
        // and msg; AttributeError name and obj; NameError name; re.error msg, pattern, pos, lineno and colno, whose
        // text gains the position as CPython's __init__ builds it. What is not given reads None (PyOps.GetAttr).
        internal static ScriptValue ConstructException(EvalContext c, PyExceptionType t, ScriptValue[] args, KwArgs kw)
        {
            RefuseExceptionKeywords(c, t, kw);
            if (t.IsSubtypeOf(PyExceptionTypes.ReError))
                return ReErrorValue(c, t, args, kw);
            ExceptionValue ev = c.Values.Exception(t, args);
            if (kw.Count > 0)
            {
                ev.Data = new Dictionary<string, ScriptValue>(kw.Count, StringComparer.Ordinal);
                for (int i = 0; i < kw.Count; i++)
                    ev.Data[kw.NameAt(i)] = kw.ValueAt(i);
            }
            return ev;
        }

        private static ScriptValue ReErrorValue(EvalContext c, PyExceptionType t, ScriptValue[] args, KwArgs kw)
        {
            if (args.Length > 3)
                throw Raise.TypeError(c, t.Name + "() takes from 1 to 3 positional arguments but " + args.Length + " were given");
            if (args.Length == 0)
                throw Raise.TypeError(c, t.Name + "() missing 1 required positional argument: 'msg'");
            ScriptValue pattern = args.Length > 1 ? args[1] : c.Values.None;
            ScriptValue pos = args.Length > 2 ? args[2] : c.Values.None;
            ScriptValue v;
            if (kw.TryGet("pattern", out v))
                pattern = v;
            if (kw.TryGet("pos", out v))
                pos = v;
            ScriptValue text = args[0];
            ScriptValue lineno = c.Values.None, colno = c.Values.None;
            // CPython's re.error.__init__: count and rfind of the newline over pattern[0:pos] (slice rules), str or bytes
            string ps = pattern is StrValue ? ((StrValue)pattern).Value
                : pattern is BytesValue ? System.Text.Encoding.GetEncoding(28591).GetString(((BytesValue)pattern).Data) : null;
            long p = 0;
            bool havePos = false;
            if (pos is IntValue && ((IntValue)pos).IsSmall)
            {
                p = ((IntValue)pos).Small;
                havePos = true;
            }
            else if (pos is BoolValue)
            {
                p = ((BoolValue)pos).Value ? 1 : 0;
                havePos = true;
            }
            if (ps != null && havePos)
            {
                long end = p < 0 ? Math.Max(0, p + ps.Length) : Math.Min(p, ps.Length);
                long line = 1;
                for (int k = 0; k < end; k++)
                {
                    if (ps[k] == '\n')
                        line++;
                }
                long col = p - (end > 0 ? ps.LastIndexOf('\n', (int)end - 1) : -1);
                lineno = c.Values.Int(line);
                colno = c.Values.Int(col);
                string full = args[0].Str(c) + " at position " + p.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (ps.IndexOf('\n') >= 0)
                    full += " (line " + line.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", column " + col.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
                text = c.Values.Str(full);
            }
            ExceptionValue ev = c.Values.Exception(t, new[] { text });
            ev.Data = new Dictionary<string, ScriptValue>(5, StringComparer.Ordinal)
            {
                ["msg"] = args[0],
                ["pattern"] = pattern,
                ["pos"] = pos,
                ["lineno"] = lineno,
                ["colno"] = colno,
            };
            return ev;
        }

        // BaseException.__init__ takes no keywords (ValueError(zzz=1) dropped it); a script subclass, which cannot
        // write its own __init__ here, has its base's rule.
        internal static void RefuseExceptionKeywords(EvalContext c, PyExceptionType t, KwArgs kw)
        {
            if (kw.Count == 0)
                return;
            string[] known = t.IsSubtypeOf(PyExceptionTypes.ImportError) ? new[] { "name", "path", "name_from" }
                : t.IsSubtypeOf(PyExceptionTypes.ReError) ? new[] { "pattern", "pos" }
                : t.IsSubtypeOf(PyExceptionTypes.IllegalMonthError) ? new[] { "month" }
                : t.IsSubtypeOf(PyExceptionTypes.IllegalWeekdayError) ? new[] { "weekday" }
                : t.IsSubtypeOf(PyExceptionTypes.AttributeError) ? new[] { "name", "obj" }
                : t.IsSubtypeOf(PyExceptionTypes.NameError) ? new[] { "name" }
                : null;
            for (int i = 0; i < kw.Count; i++)
            {
                if (known == null)
                    throw Raise.TypeError(c, t.Name + "() takes no keyword arguments");
                if (Array.IndexOf(known, kw.NameAt(i)) < 0)
                    throw KwReader.UnexpectedError(c, t.Name, kw.NameAt(i));
            }
        }

        // --- "no" builtins: present syntactically, refused with a specific NameError ----

        private static readonly HashSet<string> Unsupported = new HashSet<string>(StringComparer.Ordinal)
        {
            "eval", "exec", "compile", "open", "input", "globals", "locals", "vars", "delattr",
            "bytearray", "memoryview", "complex", "staticmethod", "classmethod", "property",
            "__import__", "help", "id", "object"
            // A name registered as a real builtin is resolved before this list (Environment.GetGlobal), so it
            // must not be listed here.
        };

        internal static bool TryGetUnsupportedMessage(string name, out string message)
        {
            if (Unsupported.Contains(name))
            {
                message = "name '" + name + "' is not defined: this builtin is not supported in this environment";
                return true;
            }
            message = null;
            return false;
        }

        // --- hint tails for known-absent v1 attributes ----

        private static readonly Dictionary<string, string> _hints = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // NOTE: "encode"/"decode" hints removed once str.encode / bytes.decode became real.
            // format_map/translate/maketrans/isidentifier/isprintable/fromkeys removed — real since the
            // The static methods of str and dict (StrMethods / dict statics).
        };

        internal static IReadOnlyDictionary<string, string> KnownMissingAttributeHints { get { return _hints; } }
    }
}
