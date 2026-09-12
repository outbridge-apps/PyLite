using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // string: the constants and capwords(). Template and Formatter are not here - the record-class
    // dialect has no class to subclass them with, and str.format covers the formatting.
    public static class StringModule
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
                ["capwords"] = BuiltinFunctionValue.Make("capwords", Capwords),
            };
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
            string[] words = sep == null
                ? sv.Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                : sv.Value.Split(new[] { sep }, StringSplitOptions.None);
            var sb = new StringBuilder(sv.Value.Length);
            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0)
                    sb.Append(sep ?? " ");
                sb.Append(PyUnicodeCase.Capitalize(words[i]));
            }
            ctx.Values.EnsureStrLen(sb.Length);
            return ctx.Values.Str(sb.ToString());
        }
    }
}
