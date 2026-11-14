using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // unicodedata: normalize/is_normalized ride on the BCL's normalizer; name/lookup on the engine's own
    // Unicode 16.0 name table; category/numeric/decimal/digit on CharUnicodeInfo. A "character" is one
    // code point, so a surrogate pair is accepted as one.
    internal static class UnicodedataModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["unidata_version"] = ctx.Values.Str("16.0.0"),
                ["normalize"] = BuiltinFunctionValue.Make("normalize", Normalize),
                ["is_normalized"] = BuiltinFunctionValue.Make("is_normalized", IsNormalized),
                ["name"] = BuiltinFunctionValue.Make("name", Name),
                ["lookup"] = BuiltinFunctionValue.Make("lookup", Lookup),
                ["category"] = BuiltinFunctionValue.Make("category", Category),
                ["numeric"] = BuiltinFunctionValue.Make("numeric", Numeric),
                ["decimal"] = BuiltinFunctionValue.Make("decimal", DecimalValueOf),
                ["digit"] = BuiltinFunctionValue.Make("digit", Digit),
                ["east_asian_width"] = BuiltinFunctionValue.Make("east_asian_width", EastAsianWidth),
                ["combining"] = BuiltinFunctionValue.Make("combining", Combining),
                ["bidirectional"] = BuiltinFunctionValue.Make("bidirectional", Bidirectional),
                ["mirrored"] = BuiltinFunctionValue.Make("mirrored", Mirrored),
                ["decomposition"] = BuiltinFunctionValue.Make("decomposition", Decomposition),
            };
            return ctx.Values.Module("unicodedata", m);
        }

        private static void Arity(EvalContext ctx, ScriptValue[] args, KwArgs kw, string fn, int min, int max)
        {
            Args.Between(ctx, args, kw, fn, min, max);
        }

        private static NormalizationForm Form(EvalContext ctx, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "normalize() argument 1 must be str, not " + v.PyTypeName);
            switch (s.Value)
            {
                case "NFC": return NormalizationForm.FormC;
                case "NFKC": return NormalizationForm.FormKC;
                case "NFD": return NormalizationForm.FormD;
                case "NFKD": return NormalizationForm.FormKD;
            }
            throw Raise.ValueError(ctx, "invalid normalization form");
        }

        private static string Text(EvalContext ctx, ScriptValue v, string fn, int argNo)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, fn + "() argument " + argNo + " must be str, not " + v.PyTypeName);
            return s.Value;
        }

        private static ScriptValue Normalize(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "normalize", 2, 2);
            NormalizationForm form = Form(ctx, args[0]);
            string s = Text(ctx, args[1], "normalize", 2);
            ctx.Budget.Step();
            string r = SafeNormalize(ctx, s, form, ctx.Limits.MaxStrChars);
            if (r == null)
                ctx.Values.CheckStrLen((long)ctx.Limits.MaxStrChars + 1);
            return ReferenceEquals(r, s) ? args[1] : ctx.Values.Str(r);
        }

        private static ScriptValue IsNormalized(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "is_normalized", 2, 2);
            NormalizationForm form = Form(ctx, args[0]);
            string s = Text(ctx, args[1], "is_normalized", 2);
            ctx.Budget.Step();
            return ctx.Values.Bool(SafeNormalize(ctx, s, form, s.Length) == s);   // a form longer than s is not s
        }

        // Unicode 16.0.0 normalization (PyUnicodeNormalize), linear in the text; the result is checked against
        // limit as it grows (NFKD is up to 18 chars per char): null when it is longer.
        private static string SafeNormalize(EvalContext ctx, string s, NormalizationForm form, long limit)
        {
            ctx.Budget.ChargeLinear(s.Length);
            ctx.Budget.CheckDeadlineNow();
            bool compat = form == NormalizationForm.FormKC || form == NormalizationForm.FormKD;
            bool compose = form == NormalizationForm.FormC || form == NormalizationForm.FormKC;
            return PyUnicodeNormalize.Normalize(s, compat, compose, limit, ctx);
        }

        // NFKC of a name the lexer verified; compilation has no run to charge
        internal static string NfkcIdentifier(string s)
        {
            return PyUnicodeNormalize.Normalize(s, true, true);
        }

        // The single-character argument; a surrogate pair counts as one character, a lone surrogate too.
        private static string Char(EvalContext ctx, ScriptValue v, string fn)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, fn + "() argument 1 must be a unicode character, not " + v.PyTypeName);
            string t = s.Value;
            if (t.Length == 1 || (t.Length == 2 && char.IsHighSurrogate(t[0]) && char.IsLowSurrogate(t[1])))
                return t;
            throw Raise.TypeError(ctx, fn + "() argument 1 must be a unicode character, not str");
        }

        private static bool IsLone(string t)
        {
            return t.Length == 1 && char.IsSurrogate(t[0]);
        }

        private static ScriptValue Name(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "name", 1, 2);
            string t = Char(ctx, args[0], "name");
            string name;
            if (!IsLone(t) && PyUnicodeNames.TryName(char.ConvertToUtf32(t, 0), out name))
                return ctx.Values.Str(name);
            if (args.Length == 2)
                return args[1];
            throw Raise.ValueError(ctx, "no such name");
        }

        private static ScriptValue Lookup(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "lookup", 1, 1);
            string name = Text(ctx, args[0], "lookup", 1);
            if (name.Length > 256)
                throw Raise.Make(ctx, PyExceptionTypes.KeyError, "name too long");
            string value;
            if (PyUnicodeNames.TryLookup(name, out value))
                return ctx.Values.Str(value);
            throw Raise.Make(ctx, PyExceptionTypes.KeyError, "undefined character name '" + name + "'");
        }

        private static ScriptValue Category(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "category", 1, 1);
            string t = Char(ctx, args[0], "category");
            if (IsLone(t))
                return ctx.Values.Str("Cs");
            return ctx.Values.Str(CategoryCode(CharUnicodeInfo.GetUnicodeCategory(t, 0)));
        }

        private static ScriptValue Numeric(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "numeric", 1, 2);
            string t = Char(ctx, args[0], "numeric");
            double v = IsLone(t) ? -1 : CharUnicodeInfo.GetNumericValue(t, 0);
            if (v >= 0)
                return ctx.Values.Float(v);
            if (args.Length == 2)
                return args[1];
            throw Raise.ValueError(ctx, "not a numeric character");
        }

        private static ScriptValue DecimalValueOf(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "decimal", 1, 2);
            string t = Char(ctx, args[0], "decimal");
            int v = IsLone(t) ? -1 : CharUnicodeInfo.GetDecimalDigitValue(t, 0);
            if (v >= 0)
                return ctx.Values.Int(v);
            if (args.Length == 2)
                return args[1];
            throw Raise.ValueError(ctx, "not a decimal");
        }

        private static ScriptValue Digit(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "digit", 1, 2);
            string t = Char(ctx, args[0], "digit");
            int v = IsLone(t) ? -1 : CharUnicodeInfo.GetDigitValue(t, 0);
            if (v >= 0)
                return ctx.Values.Int(v);
            if (args.Length == 2)
                return args[1];
            throw Raise.ValueError(ctx, "not a digit");
        }

        private static ScriptValue EastAsianWidth(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "east_asian_width", 1, 1);
            string t = Char(ctx, args[0], "east_asian_width");
            return ctx.Values.Str(IsLone(t) ? "N" : PyUnicodeProps.EastAsianWidth(char.ConvertToUtf32(t, 0)));
        }

        private static ScriptValue Combining(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "combining", 1, 1);
            string t = Char(ctx, args[0], "combining");
            return ctx.Values.Int(IsLone(t) ? 0 : PyUnicodeProps.Combining(char.ConvertToUtf32(t, 0)));
        }

        // A lone surrogate is a code point of its own here: UnicodeData lists D800..DFFF with bidi class L.
        private static ScriptValue Bidirectional(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "bidirectional", 1, 1);
            string t = Char(ctx, args[0], "bidirectional");
            return ctx.Values.Str(PyUnicodeProps.Bidirectional(IsLone(t) ? t[0] : char.ConvertToUtf32(t, 0)));
        }

        private static ScriptValue Mirrored(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "mirrored", 1, 1);
            string t = Char(ctx, args[0], "mirrored");
            return ctx.Values.Int(!IsLone(t) && PyUnicodeProps.Mirrored(char.ConvertToUtf32(t, 0)) ? 1 : 0);
        }

        private static ScriptValue Decomposition(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Arity(ctx, args, kw, "decomposition", 1, 1);
            string t = Char(ctx, args[0], "decomposition");
            return ctx.Values.Str(IsLone(t) ? "" : PyUnicodeProps.Decomposition(char.ConvertToUtf32(t, 0)));
        }

        private static string CategoryCode(UnicodeCategory c)
        {
            switch (c)
            {
                case UnicodeCategory.UppercaseLetter: return "Lu";
                case UnicodeCategory.LowercaseLetter: return "Ll";
                case UnicodeCategory.TitlecaseLetter: return "Lt";
                case UnicodeCategory.ModifierLetter: return "Lm";
                case UnicodeCategory.OtherLetter: return "Lo";
                case UnicodeCategory.NonSpacingMark: return "Mn";
                case UnicodeCategory.SpacingCombiningMark: return "Mc";
                case UnicodeCategory.EnclosingMark: return "Me";
                case UnicodeCategory.DecimalDigitNumber: return "Nd";
                case UnicodeCategory.LetterNumber: return "Nl";
                case UnicodeCategory.OtherNumber: return "No";
                case UnicodeCategory.SpaceSeparator: return "Zs";
                case UnicodeCategory.LineSeparator: return "Zl";
                case UnicodeCategory.ParagraphSeparator: return "Zp";
                case UnicodeCategory.Control: return "Cc";
                case UnicodeCategory.Format: return "Cf";
                case UnicodeCategory.Surrogate: return "Cs";
                case UnicodeCategory.PrivateUse: return "Co";
                case UnicodeCategory.ConnectorPunctuation: return "Pc";
                case UnicodeCategory.DashPunctuation: return "Pd";
                case UnicodeCategory.OpenPunctuation: return "Ps";
                case UnicodeCategory.ClosePunctuation: return "Pe";
                case UnicodeCategory.InitialQuotePunctuation: return "Pi";
                case UnicodeCategory.FinalQuotePunctuation: return "Pf";
                case UnicodeCategory.OtherPunctuation: return "Po";
                case UnicodeCategory.MathSymbol: return "Sm";
                case UnicodeCategory.CurrencySymbol: return "Sc";
                case UnicodeCategory.ModifierSymbol: return "Sk";
                case UnicodeCategory.OtherSymbol: return "So";
                default: return "Cn";
            }
        }
    }
}
