using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    public sealed class StrValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("str", StrMethods.BuildSlots());
        internal static ScriptTypeInfo StrType { get { return Type; } }
        internal static readonly StrValue Empty = new StrValue("", true);

        public readonly string Value;
        private long _hashCache = long.MinValue;       // "not computed" sentinel (R8)
        private readonly bool _sharedNoHashCache;      // process-wide instances must not bake a seed in

        internal StrValue(string v) { Value = v; }

        // Shared-instance ctor (AsciiCache, Empty): the hash depends on the per-engine StrHashSeed, so a
        // process-wide instance must recompute it per call — a cached value from one engine/run poisons
        // set/dict lookups in every other one (found by the conformance suite, 2026-09).
        internal StrValue(string v, bool sharedNoHashCache)
        {
            Value = v;
            _sharedNoHashCache = sharedNoHashCache;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Str; } }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return Value.Length > 0; }
        protected internal override bool TryLengthCore(out long length) { length = Value.Length; return true; }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            if (_sharedNoHashCache)
                return NumericHash.HashString(Value, ctx.StrHashSeed);   // never cache on shared instances
            if (_hashCache != long.MinValue)
                return _hashCache;
            long h = NumericHash.HashString(Value, ctx.StrHashSeed);
            _hashCache = h;
            return h;
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            StrValue o = other as StrValue;
            return o != null && Value.Length == o.Value.Length && string.Equals(Value, o.Value, StringComparison.Ordinal);
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            StrValue o = other as StrValue;
            if (o == null)
            {
                cmp = 0;
                return false;
            }
            cmp = string.CompareOrdinal(Value, o.Value);
            return true;
        }

        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new StrIterator(this); }

        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            if (item.Kind != ValueKind.Str)
                throw Raise.TypeError(ctx, "'in <string>' requires string as left operand, not " + item.PyTypeName);
            string needle = ((StrValue)item).Value;
            found = PySearch.IndexOf(ctx, Value, needle, 0, Value.Length) >= 0;
            return true;
        }

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            switch (op)
            {
                case PyBinOp.Add:
                    if (reflected)
                        return null;                       // non-str + str -> generic TypeError
                    if (other.Kind == ValueKind.Str)
                        return Concat((StrValue)other, ctx);
                    throw Raise.TypeError(ctx, "can only concatenate str (not \"" + other.PyTypeName + "\") to str");
                case PyBinOp.Mul:
                    if (other.Kind == ValueKind.Int || other.Kind == ValueKind.Bool)
                        return Repeat(NumericOps.AsBigInteger(other), ctx);
                    if (other.Kind == ValueKind.Float)
                        throw Raise.TypeError(ctx, "can't multiply sequence by non-int of type 'float'");
                    return null;
                case PyBinOp.Mod:
                    if (reflected)
                        return null;
                    if (ctx.Format == null)
                        throw Raise.NotImplemented(ctx, "%-formatting is not available in this context");
                    return ctx.Format.PercentFormat(ctx, this, other);
                default:
                    return null;
            }
        }

        internal ScriptValue GetItem(ScriptValue index, EvalContext ctx)
        {
            if (index.Kind == ValueKind.Slice)
                return SliceOf((SliceValue)index, ctx);
            if (index.Kind == ValueKind.Int || index.Kind == ValueKind.Bool)
            {
                BigInteger i = NumericOps.AsBigInteger(index);
                if (i < 0)
                    i += Value.Length;
                if (i < 0 || i >= Value.Length)
                    throw Raise.IndexError(ctx, "string index out of range");
                return ctx.Values.StrFromChar(Value[(int)i]);
            }
            throw Raise.TypeError(ctx, "string indices must be integers");
        }

        private StrValue SliceOf(SliceValue sl, EvalContext ctx)
        {
            SliceIndices ind = sl.Indices(Value.Length, ctx);
            int start = (int)ind.Start, step = ind.StepInt;
            int len = (int)ind.Length;
            if (len == 0)
                return StrValue.Empty;
            ctx.Values.PreCharge(24 + 2L * len);
            if (step == 1)
                return ctx.Values.Str(Value.Substring(start, len));
            var chars = new char[len];
            int idx = start;
            for (int k = 0; k < len; k++)
            {
                chars[k] = Value[idx];
                idx += step;
            }
            return ctx.Values.Str(new string(chars));
        }

        internal StrValue Concat(StrValue other, EvalContext ctx)
        {
            long total = (long)Value.Length + other.Value.Length;
            if (total > ctx.Limits.MaxStrChars)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", ctx.Limits.MaxStrChars, total);
            return ctx.Values.Str(Value + other.Value);
        }

        internal StrValue Repeat(BigInteger n, EvalContext ctx)
        {
            if (n <= 0 || Value.Length == 0)
                return StrValue.Empty;   // '' * 10**30 is '': the count would not fit an int
            BigInteger total = (BigInteger)Value.Length * n;
            if (total > ctx.Limits.MaxStrChars)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", ctx.Limits.MaxStrChars,
                    total > long.MaxValue ? long.MaxValue : (long)total);
            int count = (int)n;
            if (Value.Length == 1)
                return ctx.Values.Str(new string (Value[0], count));
            var sb = new StringBuilder((int)total);
            for (int i = 0; i < count; i++)
                sb.Append(Value);
            return ctx.Values.Str(sb.ToString());
        }

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append(Value); }

        // Port of Objects/unicodeobject.c::unicode_repr, over UTF-16 code units.
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            string s = Value;
            char quote = '\'';
            if (s.IndexOf('\'') >= 0 && s.IndexOf('"') < 0)
                quote = '"';
            sb.Append(quote);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                int cp;
                int adv;
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, s[i + 1]);
                    adv = 2;
                }
                else
                {
                    cp = c;
                    adv = 1;
                }

                AppendReprChar(sb, cp, quote);
                i += adv;
            }
            sb.Append(quote);
        }

        private static void AppendReprChar(BudgetStringBuilder sb, int cp, char quote)
        {
            if (cp == '\\')
            {
                sb.Append("\\\\");
                return;
            }
            if (cp == quote)
            {
                sb.Append('\\');
                sb.Append(quote);
                return;
            }
            if (cp == '\n')
            {
                sb.Append("\\n");
                return;
            }
            if (cp == '\r')
            {
                sb.Append("\\r");
                return;
            }
            if (cp == '\t')
            {
                sb.Append("\\t");
                return;
            }
            if (cp < 0x20 || cp == 0x7F)
            {
                AppendHex(sb, "\\x", cp, 2);
                return;
            }
            if (cp < 0x7F)
            {
                sb.Append((char)cp);
                return;
            }
            if (IsPrintable(cp))
            {
                AppendCodePoint(sb, cp);
                return;
            }
            if (cp <= 0xFF)
                AppendHex(sb, "\\x", cp, 2);
            else if (cp <= 0xFFFF)
                AppendHex(sb, "\\u", cp, 4);
            else
                AppendHex(sb, "\\U", cp, 8);
        }

        private static void AppendCodePoint(BudgetStringBuilder sb, int cp)
        {
            if (cp <= 0xFFFF)
                sb.Append((char)cp);
            else
                sb.Append(char.ConvertFromUtf32(cp));
        }

        private static void AppendHex(BudgetStringBuilder sb, string prefix, int cp, int width)
        {
            sb.Append(prefix);
            sb.Append(cp.ToString("x" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
        }

        private static bool IsPrintable(int cp)
        {
            UnicodeCategory cat = cp <= 0xFFFF
                ? CharUnicodeInfo.GetUnicodeCategory((char)cp)
                : CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0);
            switch (cat)
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.SpaceSeparator:
                    return false;
                default:
                    return true;
            }
        }
    }

    // Iterates by UTF-16 code units; each element is a length-1 string (ASCII cache when possible).
    internal sealed class StrIterator : ScriptIteratorBase
    {
        private readonly StrValue _s;
        private int _pos;

        internal StrIterator(StrValue s) { _s = s; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_pos < _s.Value.Length)
            {
                value = ctx.Values.StrFromChar(_s.Value[_pos]);
                _pos++;
                return true;
            }
            value = null;
            return false;
        }
    }
}
