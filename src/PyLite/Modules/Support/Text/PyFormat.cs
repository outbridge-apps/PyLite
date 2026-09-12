using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    internal struct FormatSpec
    {
        public char Fill;
        public char Align;     // '\0' = unset
        public char Sign;      // '\0' | '+' | '-' | ' '
        public bool AltForm;
        public bool ZeroPad;
        public bool AlignImplied;   // Align '=' came from the '0' flag, not from the spec
        public int Width;      // -1 = none
        public char GroupChar; // '\0' = none | ',' | '_' (PEP 515)
        public int GroupSize;  // digits between separators: 3 (decimal/float) or 4 (underscore + b/o/x/X)
        public int Precision;  // -1 = none
        public char Type;      // '\0' = none
    }

    // The format mini-language. Numeric printing goes through FloatRepr; no second
    // number-printing path. StrDotFormat/PercentFormat are in the sibling partial files.
    internal static partial class PyFormat
    {
        internal static bool IsAlign(char c) { return c == '<' || c == '>' || c == '^' || c == '='; }

        internal static FormatSpec ParseSpec(EvalContext ctx, string spec)
        {
            FormatSpec f = default(FormatSpec);
            f.Fill = ' ';
            f.Width = -1;
            f.Precision = -1;
            f.GroupSize = 3;
            int pos = 0, n = spec.Length;
            bool fillSpecified = false;

            if (n - pos >= 2 && IsAlign(spec[pos + 1]))
            {
                f.Fill = spec[pos];
                f.Align = spec[pos + 1];
                fillSpecified = true;
                pos += 2;
            }
            else if (n - pos >= 1 && IsAlign(spec[pos]))
            {
                f.Align = spec[pos];
                pos += 1;
            }

            if (pos < n && (spec[pos] == '+' || spec[pos] == '-' || spec[pos] == ' '))
            {
                f.Sign = spec[pos];
                pos++;
            }
            if (pos < n && spec[pos] == '#')
            {
                f.AltForm = true;
                pos++;
            }
            // CPython: a '0' flag (only when no explicit fill) enables sign-aware zero padding — it sets
            // fill '0' and align '=' UNLESS an explicit alignment was given. On strings the implied '='
            // means the default (left) alignment with '0' fill (3.10+); an explicit '=' still raises there.
            // When a fill WAS given, the '0' is not a flag — it falls through as a leading width digit.
            if (!fillSpecified && pos < n && spec[pos] == '0')
            {
                f.ZeroPad = true;
                f.Fill = '0';
                if (f.Align == '\0')
                {
                    f.Align = '=';
                    f.AlignImplied = true;
                }
                pos++;
            }

            int width;
            if (TryReadInt(spec, ref pos, out width))
            {
                if (width < 0)
                    throw Raise.ValueError(ctx, "Too many decimal digits in format string");
                f.Width = width;
            }
            if (pos < n && (spec[pos] == ',' || spec[pos] == '_'))
            {
                f.GroupChar = spec[pos];
                pos++;
            }
            if (pos < n && spec[pos] == '.')
            {
                pos++;
                int prec;
                if (!TryReadInt(spec, ref pos, out prec) || prec < 0)
                    throw Raise.ValueError(ctx, "Format specifier missing precision");
                f.Precision = prec;
            }
            if (pos < n)
            {
                f.Type = spec[pos];
                pos++;
            }
            if (pos != n)
                throw Raise.ValueError(ctx, "Invalid format specifier");
            return f;
        }

        // Reads ASCII or Nd digits; returns false if no digit; sets value=-1 on int overflow.
        private static bool TryReadInt(string s, ref int pos, out int value)
        {
            int start = pos;
            long acc = 0;
            bool overflow = false;
            while (pos < s.Length)
            {
                int dv = DigitOf(s[pos]);
                if (dv < 0)
                    break;
                acc = acc * 10 + dv;
                if (acc > int.MaxValue)
                    overflow = true;
                pos++;
            }
            if (pos == start)
            {
                value = 0;
                return false;
            }
            value = overflow ? -1 : (int)acc;
            return true;
        }

        private static int DigitOf(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            return PyUnicode.DecimalDigitValue(c);
        }

        // format(value, spec) dispatcher.
        internal static ScriptValue FormatValue(EvalContext ctx, ScriptValue value, string spec)
        {
            if (spec.Length == 0)
                return ctx.Values.Str(value.Str(ctx));
            switch (value.Kind)
            {
                case ValueKind.Str:
                {
                    FormatSpec f = ParseSpec(ctx, spec);
                    if (f.Type != '\0' && f.Type != 's')
                        throw Raise.ValueError(ctx, "Unknown format code '" + f.Type + "' for object of type 'str'");
                    return ctx.Values.StrPrecharged(ApplyStringSpec(ctx, ((StrValue)value).Value, f));
                }
                case ValueKind.Bool:
                {
                    FormatSpec f = ParseSpec(ctx, spec);
                    if (f.Type == '\0' || f.Type == 's')
                        return ctx.Values.StrPrecharged(ApplyStringSpec(ctx, ((BoolValue)value).Value ? "True" : "False", f));
                    return ctx.Values.StrPrecharged(FormatInt(ctx, ((BoolValue)value).Value ? BigInteger.One : BigInteger.Zero, f));
                }
                case ValueKind.Int:
                    return ctx.Values.StrPrecharged(FormatInt(ctx, ((IntValue)value).Value, ParseSpec(ctx, spec)));
                case ValueKind.Float:
                    return ctx.Values.StrPrecharged(FormatFloat(ctx, ((FloatValue)value).Value, ParseSpec(ctx, spec)));
                default:
                    ScriptValue viaHook = value.FormatSpecCore(spec, ctx);
                    if (viaHook != null)
                        return viaHook;
                    throw Raise.TypeError(ctx, "non-empty format string passed to object.__format__");
            }
        }

        // ---- string path ----

        internal static string ApplyStringSpec(EvalContext ctx, string s, FormatSpec f)
        {
            if (f.Sign != '\0')
                throw Raise.ValueError(ctx, "Sign not allowed in string format specifier");
            if (f.Align == '=' && f.AlignImplied)
                f.Align = '<';
            if (f.Align == '=')
                throw Raise.ValueError(ctx, "'=' alignment not allowed in string format specifier");
            if (f.GroupChar != '\0')
                throw Raise.ValueError(ctx, "Cannot specify '" + f.GroupChar + "' with 's'.");
            if (f.AltForm)
                throw Raise.ValueError(ctx, "Alternate form (#) not allowed in string format specifier");
            if (f.Precision >= 0 && s.Length > f.Precision)
                s = s.Substring(0, f.Precision);
            return Pad(ctx, s, f.Fill, f.Align, f.Width, '<');
        }

        internal static string Pad(EvalContext ctx, string body, char fill, char align, int width, char defaultAlign)
        {
            if (align == '\0')
                align = defaultAlign;
            if (width <= body.Length)
                return body;
            ctx.Values.EnsureStrLen(width);
            int pad = width - body.Length;
            if (align == '<')
                return body + new string(fill, pad);
            if (align == '>' || align == '=')
                return new string(fill, pad) + body;
            int left = pad / 2;
            return new string(fill, left) + body + new string(fill, pad - left);
        }

        // ---- integer path ----

        internal static string FormatInt(EvalContext ctx, BigInteger v, FormatSpec f)
        {
            char type = f.Type;
            if (type == 'e' || type == 'E' || type == 'f' || type == 'F' || type == 'g' || type == 'G' || type == '%')
            {
                double d = (double)v;   // 'f'/'e'/'g'/'%' on an int: precision belongs to the float path
                if (double.IsInfinity(d))
                    throw Raise.Overflow(ctx, "long int too large to convert to float");
                return FormatFloat(ctx, d, f);
            }
            if (f.Precision >= 0)
                throw Raise.ValueError(ctx, "Precision not allowed in integer format specifier");
            if (type == 'c')
            {
                if (f.Sign != '\0')
                    throw Raise.ValueError(ctx, "Sign not allowed with integer format specifier 'c'");
                if (v < 0 || v > 0x10FFFF)
                    throw Raise.Overflow(ctx, "%c arg not in range(0x110000)");
                int cp = (int)v;
                string ch = cp <= 0xFFFF ? ((char)cp).ToString() : char.ConvertFromUtf32(cp);
                return Pad(ctx, ch, f.Fill, f.Align, f.Width, '<');
            }

            string prefix = "";
            string digits;
            switch (type)
            {
                case '\0':
                case 'd':
                case 'n':
                    NumericOps.ChargeIntToStr(v, ctx);
                    digits = BigInteger.Abs(v).ToString(CultureInfo.InvariantCulture);
                    break;
                case 'b':
                    digits = InBase(BigInteger.Abs(v), 2);
                    prefix = f.AltForm ? "0b" : "";
                    break;
                case 'o':
                    digits = InBase(BigInteger.Abs(v), 8);
                    prefix = f.AltForm ? "0o" : "";
                    break;
                case 'x':
                    digits = InBase(BigInteger.Abs(v), 16);
                    prefix = f.AltForm ? "0x" : "";
                    break;
                case 'X':
                    digits = InBase(BigInteger.Abs(v), 16).ToUpperInvariant();
                    prefix = f.AltForm ? "0X" : "";
                    break;
                default:
                    throw Raise.ValueError(ctx, "Unknown format code '" + type + "' for object of type 'int'");
            }

            if (f.GroupChar != '\0')
            {
                bool decimalType = type == 'd' || type == '\0';
                bool baseType = type == 'b' || type == 'o' || type == 'x' || type == 'X';
                // ',' groups decimal only; '_' (PEP 515) also groups b/o/x/X, every 4 digits.
                if (!decimalType && !(f.GroupChar == '_' && baseType))
                    throw Raise.ValueError(ctx, "Cannot specify '" + f.GroupChar + "' with '" + type + "'.");
                f.GroupSize = (f.GroupChar == '_' && baseType) ? 4 : 3;
                digits = Group(digits, f.GroupChar, f.GroupSize);
            }

            string sign = v.Sign < 0 ? "-" : f.Sign == '+' ? "+" : f.Sign == ' ' ? " " : "";
            return PadNumeric(ctx, sign, prefix, digits, f);
        }

        // ---- float path ----

        internal static string FormatFloat(EvalContext ctx, double v, FormatSpec f)
        {
            char type = f.Type;
            if (type != '\0' && "eEfFgGn%".IndexOf(type) < 0)
                throw Raise.ValueError(ctx, "Unknown format code '" + type + "' for object of type 'float'");
            bool upper = type == 'E' || type == 'F' || type == 'G';
            string body;
            bool special = double.IsNaN(v) || double.IsInfinity(v);
            if (special)
            {
                body = double.IsNaN(v) ? "nan" : "inf";
                if (upper)
                    body = body.ToUpperInvariant();
                if (type == '%')
                    body += "%";
            }
            else
            {
                switch (type)
                {
                    case 'f':
                    case 'F':
                        body = FloatRepr.FormatFixed(ctx, Math.Abs(v), f.Precision < 0 ? 6 : f.Precision, f.AltForm);
                        break;
                    case 'e':
                    case 'E':
                        body = FloatRepr.FormatScientific(ctx, Math.Abs(v), f.Precision < 0 ? 6 : f.Precision, upper, f.AltForm);
                        break;
                    case 'g':
                    case 'G':
                    case 'n':
                        body = FloatRepr.FormatGeneral(ctx, Math.Abs(v), f.Precision < 0 ? 6 : f.Precision, upper, f.AltForm, false);
                        break;
                    case '%':
                        body = FloatRepr.FormatFixed(ctx, Math.Abs(v) * 100.0, f.Precision < 0 ? 6 : f.Precision, f.AltForm) + "%";
                        break;
                    default:   // '\0'
                        if (f.Precision >= 0)
                            body = FloatRepr.FormatGeneral(ctx, Math.Abs(v), f.Precision, upper, f.AltForm, false);
                        else
                            body = FloatRepr.Repr(Math.Abs(v));
                        break;
                }
            }

            bool neg = SignBit(v) && !double.IsNaN(v);   // NaN prints unsigned (CPython parity)
            string sign = neg ? "-" : f.Sign == '+' ? "+" : f.Sign == ' ' ? " " : "";
            if (f.GroupChar != '\0' && !special)
                body = GroupFloat(body, f.GroupChar);
            if (special)
            {
                // nan/inf: ZeroPad ignored, aligned right by default
                return Pad(ctx, sign + body, ' ', f.Align == '\0' ? '>' : f.Align, f.Width, '>');
            }
            return PadNumeric(ctx, sign, "", body, f);
        }

        private static bool SignBit(double v) { return BitConverter.DoubleToInt64Bits(v) < 0L; }

        // ---- shared numeric padding + grouping + base ----

        internal static string PadNumeric(EvalContext ctx, string sign, string prefix, string body, FormatSpec f)
        {
            // '=' pads BETWEEN the sign/prefix and the digits (sign-aware); ParseSpec maps a bare '0'
            // flag to align '=' + fill '0', so this branch covers both the explicit and the flag forms.
            char align = f.Align == '\0' && f.ZeroPad ? '=' : f.Align;
            if (align == '=')
            {
                if (f.Width > 0)
                    ctx.Values.EnsureStrLen(f.Width);   // bounds minBody by MaxStrChars before we allocate
                int minBody = f.Width - sign.Length - prefix.Length;
                if (f.Fill == '0' && f.GroupChar != '\0')
                {
                    if (body.Length < minBody)
                        body = ZeroPadGrouped(body, minBody, f.GroupChar, f.GroupSize);
                }
                else if (body.Length < minBody)
                {
                    body = new string(f.Fill, minBody - body.Length) + body;
                }
                return sign + prefix + body;
            }
            return Pad(ctx, sign + prefix + body, f.Fill, align, f.Width, '>');
        }

        // Zero-pad a numeric body to a visible width of >= minBody, keeping the separators. Computes the
        // needed integer-digit count once and groups a single time — O(minBody), not the O(n^2) of a
        // prepend-one-then-regroup loop (which a large field width could turn into an uninterruptible hang).
        private static string ZeroPadGrouped(string body, int minBody, char sep, int size)
        {
            string stripped = StripGroup(body);
            int dot = stripped.IndexOf('.');
            string intPart = dot < 0 ? stripped : stripped.Substring(0, dot);
            string tail = dot < 0 ? "" : stripped.Substring(dot);
            int k = intPart.Length;
            while (k + (k - 1) / size + tail.Length < minBody)   // add integer digits until wide enough
                k++;
            if (k > intPart.Length)
                intPart = new string('0', k - intPart.Length) + intPart;
            return Group(intPart + tail, sep, size);
        }

        private static string StripGroup(string s) { return s.Replace(",", "").Replace("_", ""); }

        // ascii() / !a / %a: repr text with every non-ASCII char escaped (\xhh / \uhhhh / \Uhhhhhhhh).
        // The repr pass has already escaped controls and quotes; this only rewrites chars >= 0x7F.
        internal static string AsciiEscape(string repr)
        {
            StringBuilder sb = null;
            for (int i = 0; i < repr.Length; i++)
            {
                char c = repr[i];
                if (c < 0x7F)
                {
                    if (sb != null)
                        sb.Append(c);
                    continue;
                }
                if (sb == null)
                    sb = new StringBuilder(repr.Length + 8).Append(repr, 0, i);
                if (char.IsHighSurrogate(c) && i + 1 < repr.Length && char.IsLowSurrogate(repr[i + 1]))
                {
                    sb.Append("\\U").Append(char.ConvertToUtf32(c, repr[i + 1]).ToString("x8", CultureInfo.InvariantCulture));
                    i++;
                }
                else if (c <= 0xFF)
                {
                    sb.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
            }
            return sb == null ? repr : sb.ToString();
        }

        // Group the integer part of a positive decimal body (Decimal.__format__ reuse; comma, /3).
        internal static string GroupNumber(string bodyWithDot, char sep) { return Group(bodyWithDot, sep, 3); }

        // Insert `sep` every `size` digits in the integer part (leaves any '.'/frac untouched).
        private static string Group(string digits, char sep, int size)
        {
            int dot = digits.IndexOf('.');
            string intPart = dot < 0 ? digits : digits.Substring(0, dot);
            string rest = dot < 0 ? "" : digits.Substring(dot);
            var sb = new StringBuilder();
            int count = 0;
            for (int i = intPart.Length - 1; i >= 0; i--)
            {
                sb.Insert(0, intPart[i]);
                if (++count % size == 0 && i > 0)
                    sb.Insert(0, sep);
            }
            return sb.ToString() + rest;
        }

        private static string GroupFloat(string body, char sep)
        {
            int e = body.IndexOfAny(new[] { 'e', 'E' });
            if (e >= 0)
                return Group(body.Substring(0, e), sep, 3) + body.Substring(e);
            return Group(body, sep, 3);
        }

        private static string InBase(BigInteger a, int radix)
        {
            if (a.IsZero)
                return "0";
            string hex = a.ToString("x", CultureInfo.InvariantCulture);
            hex = TrimZeros(hex);
            if (radix == 16)
                return hex;
            var bits = new StringBuilder(hex.Length * 4);
            for (int i = 0; i < hex.Length; i++)
            {
                int nib = hex[i] <= '9' ? hex[i] - '0' : hex[i] - 'a' + 10;
                for (int b = 3; b >= 0; b--)
                    bits.Append((char)('0' + ((nib >> b) & 1)));
            }
            string bin = TrimZeros(bits.ToString());
            if (radix == 2)
                return bin;
            int pad = (3 - bin.Length % 3) % 3;
            if (pad > 0)
                bin = new string('0', pad) + bin;
            var oct = new StringBuilder(bin.Length / 3);
            for (int i = 0; i < bin.Length; i += 3)
                oct.Append((char)('0' + ((bin[i] - '0') * 4 + (bin[i + 1] - '0') * 2 + (bin[i + 2] - '0'))));
            return TrimZeros(oct.ToString());
        }

        private static string TrimZeros(string s)
        {
            int i = 0;
            while (i < s.Length - 1 && s[i] == '0')
                i++;
            return s.Substring(i);
        }
    }
}
