using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // string: the constants, capwords() and Template. Template is written in the dialect itself
    // (Modules/Python/string_template.py, compiled once per process, run in each run that imports string),
    // so a script subclasses it like any class. No Formatter: str.format covers the formatting.
    internal static class StringModule
    {
        private const string Lower = "abcdefghijklmnopqrstuvwxyz";
        private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string Digits = "0123456789";
        private const string Punctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
        private const string Whitespace = " \t\n\r\x0b\x0c";

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["ascii_lowercase"] = ctx.Values.Str(Lower),
                ["ascii_uppercase"] = ctx.Values.Str(Upper),
                ["ascii_letters"] = ctx.Values.Str(Lower + Upper),
                ["digits"] = ctx.Values.Str(Digits),
                ["hexdigits"] = ctx.Values.Str(Digits + "abcdef" + "ABCDEF"),
                ["octdigits"] = ctx.Values.Str("01234567"),
                ["punctuation"] = ctx.Values.Str(Punctuation),
                ["printable"] = ctx.Values.Str(Digits + Lower + Upper + Punctuation + Whitespace),
                ["whitespace"] = ctx.Values.Str(Whitespace),
                ["capwords"] = BuiltinFunctionValue.Make("capwords", Args.LeadKeywords("capwords", Capwords, 1, "s", "sep")),
            };
            ModuleValue template = PythonSourceModules.Load(ctx, "Outbridge.PyLite.string_template.py", "string");
            m["Template"] = template.Members["Template"];
            return ctx.Values.Module("string", m);
        }

        // capwords(s, sep=None): split on sep (or runs of whitespace), capitalize each word, join with sep
        // (or a single space) - so with sep=None the surrounding and repeated whitespace collapses.
        private static ScriptValue Capwords(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length < 1 || args.Length > 2)
                throw Raise.TypeError(ctx, "capwords() takes from 1 to 2 positional arguments but " + args.Length + " were given");
            StrValue sv = args[0] as StrValue;
            if (sv == null)
                throw Raise.TypeError(ctx, "capwords() argument 1 must be str, not " + args[0].PyTypeName);
            ScriptValue sepArg = args.Length == 2 ? args[1] : null;
            ScriptValue sepKw;
            if (kw.TryGet("sep", out sepKw))
                sepArg = sepKw;
            string sep = null;
            if (sepArg != null && sepArg.Kind != ValueKind.None)
            {
                StrValue ss = sepArg as StrValue;
                if (ss == null)
                    throw Raise.TypeError(ctx, "sep must be str or None, not " + sepArg.PyTypeName);
                sep = ss.Value;
            }
            if (sep != null && sep.Length == 0)
                throw Raise.ValueError(ctx, "empty separator");
            // Word by word with a charge for each: the whitespace split made all 8M words at once, uncharged,
            // and the loop over them charged nothing either.
            var sb = new BudgetStringBuilder(ctx);
            if (sep == null)
            {
                string s = sv.Value;
                int i = 0;
                while (true)
                {
                    while (i < s.Length && Support.PyUnicode.IsPythonSpace(s[i]))
                        i++;   // str.split()'s whitespace: \x1c-\x1f and \x85 too, as CPython's
                    if (i >= s.Length)
                        break;
                    int j = i;
                    while (j < s.Length && !Support.PyUnicode.IsPythonSpace(s[j]))
                        j++;
                    ctx.Budget.ChargeLinear(j - i);
                    if (sb.Length > 0)
                        sb.Append(' ');
                    sb.Append(PyUnicodeCase.Capitalize(s.Substring(i, j - i)));
                    i = j;
                }
            }
            else
            {
                // piece by piece through the bounded search, with no list of the pieces
                string s = sv.Value;
                int start = 0;
                while (true)
                {
                    int at = PySearch.IndexOf(ctx, s, sep, start, s.Length - start);
                    int end = at < 0 ? s.Length : at;
                    ctx.Budget.ChargeLinear(end - start);
                    if (start > 0)
                        sb.Append(sep);
                    sb.Append(PyUnicodeCase.Capitalize(s.Substring(start, end - start)));
                    if (at < 0)
                        break;
                    start = at + sep.Length;
                }
            }
            return ctx.Values.Str(sb.ToString());
        }
    }
}
