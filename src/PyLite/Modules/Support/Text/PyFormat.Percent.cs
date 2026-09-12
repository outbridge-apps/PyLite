using System.Numerics;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // printf-style % operator. Argument errors -> TypeError; format-syntax -> ValueError.
    internal static partial class PyFormat
    {
        internal static StrValue PercentFormat(EvalContext ctx, StrValue format, ScriptValue rhs)
        {
            return (StrValue)ctx.Values.Str(PercentCore(ctx, format.Value, rhs, false));
        }

        // bytes % args (CPython 3.5+). Latin-1 maps every byte to the same-numbered char and back, so the
        // format and the result round-trip through the ONE parser below byte for byte; only the
        // conversions differ, and `bytesMode` selects those.
        internal static ScriptValue PercentFormatBytes(EvalContext ctx, BytesValue format, ScriptValue rhs)
        {
            byte[] data = format.Data;
            var chars = new char[data.Length];
            for (int k = 0; k < data.Length; k++)
                chars[k] = (char)data[k];
            string text = PercentCore(ctx, new string(chars), rhs, true);
            var outBytes = new byte[text.Length];
            for (int k = 0; k < text.Length; k++)
            {
                if (text[k] > 0xFF)
                    throw Raise.TypeError(ctx, "%b requires a bytes-like object, "
                        + "or an object that implements __bytes__, not 'str'");
                outBytes[k] = (byte)text[k];
            }
            return ctx.Values.Bytes(outBytes);
        }

        private static string PercentCore(EvalContext ctx, string fmt, ScriptValue rhs, bool bytesMode)
        {
            bool useTuple = rhs.Kind == ValueKind.Tuple;
            ScriptValue[] args = useTuple ? ((TupleValue)rhs).Items : new[] { rhs };
            int argIndex = 0;

            var sb = new StringBuilder(fmt.Length + 16);
            int i = 0, n = fmt.Length;
            while (i < n)
            {
                char c = fmt[i];
                if (c != '%')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }
                i++;
                if (i >= n)
                    throw Raise.ValueError(ctx, "incomplete format");

                // %(key)conv mapping substitution: the key (parens-nested, up to the matching ')') is
                // looked up in the right-hand mapping; the resolved value drives this conversion instead
                // of the next positional argument.
                ScriptValue mapped = null;
                bool hasKey = false;
                if (fmt[i] == '(')
                {
                    int keyStart = ++i, depth = 1;
                    while (i < n && depth > 0)
                    {
                        if (fmt[i] == '(') depth++;
                        else if (fmt[i] == ')') depth--;
                        if (depth > 0) i++;
                    }
                    if (i >= n || depth != 0)
                        throw Raise.ValueError(ctx, "incomplete format key");
                    string key = fmt.Substring(keyStart, i - keyStart);
                    i++;   // skip ')'
                    DictValue mapping = rhs as DictValue;
                    if (mapping == null)
                        throw Raise.TypeError(ctx, "format requires a mapping");
                    mapped = mapping.GetItemOrThrow(ctx.Values.Str(key), ctx);
                    hasKey = true;
                }

                bool left = false, plus = false, space = false, alt = false, zero = false;
                while (i < n)
                {
                    char fc = fmt[i];
                    if (fc == '-') left = true;
                    else if (fc == '+') plus = true;
                    else if (fc == ' ') space = true;
                    else if (fc == '#') alt = true;
                    else if (fc == '0') zero = true;
                    else break;
                    i++;
                }

                int width = -1;
                if (i < n && fmt[i] == '*')
                {
                    i++;
                    int wv = IntArg(ctx, Next(ctx, args, ref argIndex));
                    if (wv < 0) { left = true; width = -wv; }
                    else width = wv;
                }
                else
                {
                    int w;
                    if (TryReadInt(fmt, ref i, out w))
                    {
                        if (w < 0)
                            throw Raise.ValueError(ctx, "width too big");
                        width = w;
                    }
                }

                int prec = -1;
                if (i < n && fmt[i] == '.')
                {
                    i++;
                    if (i < n && fmt[i] == '*')
                    {
                        i++;
                        int pv = IntArg(ctx, Next(ctx, args, ref argIndex));
                        prec = pv < 0 ? 0 : pv;
                    }
                    else
                    {
                        int p;
                        prec = TryReadInt(fmt, ref i, out p) ? p : 0;
                    }
                }

                while (i < n && (fmt[i] == 'h' || fmt[i] == 'l' || fmt[i] == 'L'))
                    i++;
                if (i >= n)
                    throw Raise.ValueError(ctx, "incomplete format");
                char conv = fmt[i];
                int convPos = i;
                i++;

                if (conv == '%')
                {
                    if (left || plus || space || alt || zero || width >= 0 || prec >= 0)
                        throw Raise.ValueError(ctx, "unsupported format character '%' (0x25) at index " + convPos);
                    sb.Append('%');
                    continue;
                }

                ScriptValue value = hasKey ? mapped : Next(ctx, args, ref argIndex);
                sb.Append(RenderConv(ctx, conv, convPos, value,
                    left, plus, space, alt, zero, width, prec, bytesMode));
            }

            // In mapping mode (rhs is a dict) leftover keys are fine; the leftover-args check applies to
            // positional (tuple / single-arg) formatting only.
            if (rhs.Kind != ValueKind.Dict && argIndex < args.Length)
                throw Raise.TypeError(ctx, "not all arguments converted during "
                    + (bytesMode ? "bytes" : "string") + " formatting");
            return sb.ToString();
        }

        private static ScriptValue Next(EvalContext ctx, ScriptValue[] args, ref int argIndex)
        {
            if (argIndex < args.Length)
                return args[argIndex++];
            throw Raise.TypeError(ctx, "not enough arguments for format string");
        }

        private static int IntArg(EvalContext ctx, ScriptValue v)
        {
            if (v.Kind == ValueKind.Int || v.Kind == ValueKind.Bool)
                return Coerce.ToSsize(ctx, v, "*");
            throw Raise.TypeError(ctx, "* wants int");
        }

        private static string RenderConv(EvalContext ctx, char conv, int convPos, ScriptValue v,
            bool left, bool plus, bool space, bool alt, bool zero, int width, int prec, bool bytesMode)
        {
            switch (conv)
            {
                case 'b':
                    // bytes-only spelling; in a str format it is not a conversion at all
                    if (!bytesMode)
                        break;
                    return PadStr(ctx, BytesOperand(ctx, v), left, width, prec);
                case 's':
                    return PadStr(ctx, bytesMode ? BytesOperand(ctx, v) : v.Str(ctx), left, width, prec);
                case 'r':
                    // for bytes, %r is the (deprecated) alias of %a -- the repr must stay ASCII
                    return PadStr(ctx, bytesMode ? AsciiEscape(v.Repr(ctx)) : v.Repr(ctx), left, width, prec);
                case 'a':
                    return PadStr(ctx, AsciiEscape(v.Repr(ctx)), left, width, prec);
                case 'd':
                case 'i':
                case 'u':
                    return RenderInt(ctx, ToIntTrunc(ctx, v, "d"), 10, "", left, plus, space, zero, width, prec);
                case 'x':
                    return RenderInt(ctx, IntOnly(ctx, v, "x"), 16, alt ? "0x" : "", left, plus, space, zero, width, prec);
                case 'X':
                    return RenderInt(ctx, IntOnly(ctx, v, "X"), 16, alt ? "0X" : "", left, plus, space, zero, width, prec).ToUpperInvariant();
                case 'o':
                    return RenderInt(ctx, IntOnly(ctx, v, "o"), 8, alt ? "0o" : "", left, plus, space, zero, width, prec);
                case 'e':
                case 'E':
                case 'f':
                case 'F':
                case 'g':
                case 'G':
                    return RenderFloatPct(ctx, ToDoublePct(ctx, v), conv, left, plus, space, alt, zero, width, prec);
                case 'c':
                    return PadStr(ctx, bytesMode ? ByteCharArg(ctx, v) : CharArg(ctx, v), left, width, -1);
            }
            throw Raise.ValueError(ctx, "unsupported format character '" + conv + "' (0x"
                + ((int)conv).ToString("x", System.Globalization.CultureInfo.InvariantCulture) + ") at index " + convPos);
        }

        // %b / %s over bytes: the operand must BE bytes. Its content is carried as latin-1 chars so the
        // caller can write it back out byte for byte.
        private static string BytesOperand(EvalContext ctx, ScriptValue v)
        {
            BytesValue b = v as BytesValue;
            if (b == null)
                throw Raise.TypeError(ctx, "%b requires a bytes-like object, "
                    + "or an object that implements __bytes__, not '" + v.PyTypeName + "'");
            var chars = new char[b.Data.Length];
            for (int k = 0; k < b.Data.Length; k++)
                chars[k] = (char)b.Data[k];
            return new string(chars);
        }

        // %c over bytes: one byte, given either as an int in range(256) or as a bytes of length 1.
        private static string ByteCharArg(EvalContext ctx, ScriptValue v)
        {
            BytesValue b = v as BytesValue;
            if (b != null)
            {
                if (b.Data.Length != 1)
                    throw Raise.TypeError(ctx, "%c requires an integer in range(256) or a single byte");
                return ((char)b.Data[0]).ToString();
            }
            if (v.Kind != ValueKind.Int && v.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "%c requires an integer in range(256) or a single byte");
            BigInteger code = IntOnly(ctx, v, "c");
            if (code < 0 || code > 255)
                throw Raise.TypeError(ctx, "%c requires an integer in range(256) or a single byte");
            return ((char)(int)code).ToString();
        }

        // %c: an int code point or a single-character str (a surrogate pair counts as one character).
        private static string CharArg(EvalContext ctx, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s != null)
            {
                string t = s.Value;
                if (t.Length == 1 || (t.Length == 2 && char.IsHighSurrogate(t[0]) && char.IsLowSurrogate(t[1])))
                    return t;
                throw Raise.TypeError(ctx, "%c requires int or char");
            }
            if (v.Kind != ValueKind.Int && v.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "%c requires int or char");
            BigInteger cp = IntOnly(ctx, v, "c");
            if (cp < 0 || cp > 0x10FFFF)
                throw Raise.Overflow(ctx, "%c arg not in range(0x110000)");
            int code = (int)cp;
            return code <= 0xFFFF ? ((char)code).ToString() : char.ConvertFromUtf32(code);
        }

        private static string PadStr(EvalContext ctx, string s, bool left, int width, int prec)
        {
            if (prec >= 0 && s.Length > prec)
                s = s.Substring(0, prec);
            return Pad(ctx, s, ' ', left ? '<' : '>', width, '<');
        }

        private static BigInteger ToIntTrunc(EvalContext ctx, ScriptValue v, string conv)
        {
            if (v.Kind == ValueKind.Int)
                return ((IntValue)v).Value;
            if (v.Kind == ValueKind.Bool)
                return ((BoolValue)v).Value ? BigInteger.One : BigInteger.Zero;
            if (v.Kind == ValueKind.Float)
                return Builtins.IntFromFloat(ctx, ((FloatValue)v).Value);
            throw Raise.TypeError(ctx, "%" + conv + " format: a number is required, not " + v.PyTypeName);
        }

        private static BigInteger IntOnly(EvalContext ctx, ScriptValue v, string conv)
        {
            if (v.Kind == ValueKind.Int)
                return ((IntValue)v).Value;
            if (v.Kind == ValueKind.Bool)
                return ((BoolValue)v).Value ? BigInteger.One : BigInteger.Zero;
            throw Raise.TypeError(ctx, "%" + conv + " format: an integer is required, not " + v.PyTypeName);
        }

        private static double ToDoublePct(EvalContext ctx, ScriptValue v)
        {
            if (v.Kind == ValueKind.Float)
                return ((FloatValue)v).Value;
            if (v.Kind == ValueKind.Int)
            {
                double d = (double)((IntValue)v).Value;
                if (double.IsInfinity(d))
                    throw Raise.Overflow(ctx, "long int too large to convert to float");
                return d;
            }
            if (v.Kind == ValueKind.Bool)
                return ((BoolValue)v).Value ? 1.0 : 0.0;
            throw Raise.TypeError(ctx, "a float is required");
        }

        private static string RenderInt(EvalContext ctx, BigInteger v, int radix, string prefix,
            bool left, bool plus, bool space, bool zero, int width, int prec)
        {
            string digits = radix == 10
                ? BigInteger.Abs(v).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : InBase(BigInteger.Abs(v), radix);
            if (prec >= 0 && digits.Length < prec)
                digits = new string('0', prec - digits.Length) + digits;
            string sign = v.Sign < 0 ? "-" : plus ? "+" : space ? " " : "";
            string head = sign + prefix;
            if (zero && !left && prec < 0 && width > head.Length + digits.Length)
                digits = new string('0', width - head.Length - digits.Length) + digits;
            return Pad(ctx, head + digits, ' ', left ? '<' : '>', width, '>');
        }

        private static string RenderFloatPct(EvalContext ctx, double v, char conv,
            bool left, bool plus, bool space, bool alt, bool zero, int width, int prec)
        {
            FormatSpec f = default(FormatSpec);
            f.Fill = zero && !left ? '0' : ' ';
            f.Align = left ? '<' : zero ? '=' : '\0';
            f.ZeroPad = zero && !left;
            f.Sign = plus ? '+' : space ? ' ' : '\0';
            f.AltForm = alt;
            f.Width = width;
            f.Precision = prec;
            f.Type = conv;
            return FormatFloat(ctx, v, f);
        }
    }
}
