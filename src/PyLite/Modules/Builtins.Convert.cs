using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    internal static partial class Builtins
    {
        static partial void RegisterConvert(Dictionary<string, ScriptValue> d)
        {
            var intStatics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["from_bytes"] = BuiltinFunctionValue.Make("from_bytes", Args.LeadKeywords("from_bytes", (s, args, kw, c) => IntMethods.FromBytes(c, args, kw), 1, "bytes", "byteorder", "signed")),
            };
            var bytesStatics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["fromhex"] = BuiltinFunctionValue.Make("fromhex", Args.Positional("fromhex", 1, 1, (s, args, kw, c) => BytesFromHex(c, args))),
                ["maketrans"] = BuiltinFunctionValue.Make("maketrans", Args.Positional("maketrans", 0, int.MaxValue, (s, args, kw, c) => BytesMethods.MakeTrans(c, args))),
            };
            var strStatics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["maketrans"] = BuiltinFunctionValue.Make("maketrans", Args.Positional("maketrans", 0, int.MaxValue, (s, args, kw, c) => StrMakeTrans(c, args))),
            };
            var floatStatics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["fromhex"] = BuiltinFunctionValue.Make("fromhex", Args.Positional("fromhex", 1, 1, (s, args, kw, c) => FloatMethods.FromHex(c, args))),
            };
            AddQuietType(d, "int", IntOrError, intStatics, IntValue.IntType);
            AddQuietType(d, "float", FloatOrError, floatStatics, FloatValue.FloatType);
            AddType(d, "str", (self, args, kw, c) => StrBuiltin(c, args, kw), strStatics, StrValue.StrType);
            // bool inherits int's classmethod: cls(result), so bool.from_bytes answers a bool
            var boolStatics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["from_bytes"] = BuiltinFunctionValue.Make("from_bytes", Args.LeadKeywords("from_bytes", (s, args, kw, c) =>
                    c.Values.Bool(IntMethods.FromBytes(c, args, kw).IsTruthy(c)), 1, "bytes", "byteorder", "signed")),
            };
            AddType(d, "bool", (self, args, kw, c) => BoolBuiltin(c, args, kw), boolStatics);
            AddType(d, "bytes", (self, args, kw, c) => BytesBuiltin(c, StrMethods.WithKw(c, args, kw, "bytes", "source", "encoding", "errors")),
                bytesStatics, BytesValue.BytesType);
        }

        // str.maketrans(x[, y[, z]]) -> {ordinal: str|int|None}. One arg: a dict whose str keys must be
        // single characters (ints pass through); two strs of equal length map x[i] -> ord(y[i]); a third
        // str marks its characters deleted (None).
        private static ScriptValue StrMakeTrans(EvalContext c, ScriptValue[] args)
        {
            if (args.Length < 1 || args.Length > 3)
                throw Raise.TypeError(c, "maketrans expected 1 to 3 arguments, got " + args.Length);
            DictValue table = c.Values.Dict(8);
            if (args.Length == 1)
            {
                DictValue src = args[0] as DictValue;
                if (src == null)
                    throw Raise.TypeError(c, "if you give only one argument to maketrans it must be a dict");
                IScriptIterator it = PyOps.GetIterator(src, c);
                ScriptValue key;
                while (it.MoveNext(c, out key))
                {
                    ScriptValue v;
                    src.TryGet(key, c, out v);
                    if (key.Kind == ValueKind.Int || key.Kind == ValueKind.Bool)
                    {
                        table.SetItem(key, v, c);
                        continue;
                    }
                    StrValue sk = key as StrValue;
                    if (sk == null)
                        throw Raise.TypeError(c, "keys in translate table must be strings or integers");
                    int[] one = CodePoints(sk.Value);
                    if (one.Length != 1)
                        throw Raise.ValueError(c, "string keys in translate table must be of length 1");
                    table.SetItem(c.Values.Int(one[0]), v, c);
                }
                return table;
            }
            StrValue x = args[0] as StrValue;
            StrValue y = args[1] as StrValue;
            if (x == null || y == null)
                throw Raise.TypeError(c, "maketrans arguments must be strings");
            // Lengths in code points, as CPython counts; walked in place: arrays of them were 260 MB uncharged.
            c.Budget.ChargeLinear(x.Value.Length + y.Value.Length);
            if (CountCodePoints(x.Value) != CountCodePoints(y.Value))
                throw Raise.ValueError(c, "the first two maketrans arguments must have equal length");
            for (int i = 0, j = 0; i < x.Value.Length; )
            {
                c.Budget.Step();
                int from = NextCodePoint(x.Value, ref i), to = NextCodePoint(y.Value, ref j);
                table.SetItem(c.Values.Int(from), c.Values.Int(to), c);
            }
            if (args.Length == 3)
            {
                StrValue z = args[2] as StrValue;
                if (z == null)
                    throw Raise.TypeError(c, "maketrans arguments must be strings");
                for (int i = 0; i < z.Value.Length; )
                {
                    c.Budget.Step();
                    table.SetItem(c.Values.Int(NextCodePoint(z.Value, ref i)), c.Values.None, c);
                }
            }
            return table;
        }

        private static int NextCodePoint(string s, ref int i)
        {
            if (i + 1 < s.Length && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
            {
                i += 2;
                return char.ConvertToUtf32(s[i - 2], s[i - 1]);
            }
            return s[i++];
        }

        private static int CountCodePoints(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; n++)
                NextCodePoint(s, ref i);
            return n;
        }

        private static int[] CodePoints(string s)
        {
            var cps = new List<int>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (i + 1 < s.Length && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
                {
                    cps.Add(char.ConvertToUtf32(s[i], s[i + 1]));
                    i++;
                }
                else
                    cps.Add(s[i]);
            }
            return cps.ToArray();
        }

        // bytes.fromhex('41 42') -> b'AB': ASCII whitespace between the pairs is skipped, not inside one.
        private static ScriptValue BytesFromHex(EvalContext c, ScriptValue[] args)
        {
            StrValue s = args.Length >= 1 ? args[0] as StrValue : null;
            if (s == null)
                throw Raise.TypeError(c, "fromhex() argument must be str or bytes-like, not " + (args.Length >= 1 ? args[0].PyTypeName : "NoneType"));
            string hex = s.Value;
            c.Budget.ChargeLinear(hex.Length);
            for (int k = 0; k < hex.Length; k++)
            {
                if (hex[k] >= 128)
                    throw Raise.ValueError(c, "non-hexadecimal number found in fromhex() arg at position " + k);
            }
            var outB = new List<byte>();
            int i = 0;
            while (i < hex.Length)
            {
                if (hex[i] == ' ' || (hex[i] >= '\t' && hex[i] <= '\r'))
                {
                    i++;
                    continue;
                }
                int hi = Support.Hex.Nibble(hex[i]);
                if (hi < 0)
                    throw Raise.ValueError(c, "non-hexadecimal number found in fromhex() arg at position " + i);
                if (i + 1 >= hex.Length)
                    throw Raise.ValueError(c, "fromhex() arg must contain an even number of hexadecimal digits");
                int lo = Support.Hex.Nibble(hex[i + 1]);
                if (lo < 0)
                    throw Raise.ValueError(c, "non-hexadecimal number found in fromhex() arg at position " + (i + 1));
                outB.Add((byte)(hi * 16 + lo));
                i += 2;
            }
            return c.Values.Bytes(outB.ToArray());
        }

        // bytes()  bytes(n)->zeros  bytes(iterable of 0..255)  bytes(str, encoding[, errors])  bytes(bytes)
        private static ScriptValue BytesBuiltin(EvalContext c, ScriptValue[] args)
        {
            if (args.Length == 0)
                return ValueFactory.EmptyBytes;
            Args.AtMost(c, args, "bytes", 3);
            ScriptValue src = args[0];
            if (src.Kind == ValueKind.Str)
            {
                if (args.Length < 2)
                    throw Raise.TypeError(c, "string argument without an encoding");
                string enc = Support.BytesCodec.Normalize(c, args[1]);
                string errors = args.Length >= 3 && args[2] is StrValue ? ((StrValue)args[2]).Value : "strict";
                return c.Values.Bytes(Support.BytesCodec.Encode(c, ((StrValue)src).Value, enc, errors));
            }
            if (src.Kind == ValueKind.Bytes)
                return src;   // immutable -> the same object
            if (src.Kind == ValueKind.Int || src.Kind == ValueKind.Bool)
            {
                BigInteger n = NumericOps.AsBigInteger(src);
                if (n < 0)
                    throw Raise.ValueError(c, "negative count");
                if (n > c.Limits.MaxStrChars)
                    throw c.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", c.Limits.MaxStrChars,
                        n > long.MaxValue ? long.MaxValue : (long)n);   // report clamped: (long)10**100 would overflow
                return c.Values.Bytes(new byte[(int)n]);
            }
            IScriptIterator it = PyOps.TryGetIterator(src, c);   // once: __iter__ may be one-shot
            if (it == null)
                throw Raise.TypeError(c, "cannot convert '" + src.PyTypeName + "' object to bytes");
            return c.Values.Bytes(Support.Iter.ToByteArray(c, it));
        }

        // int(x) for a module whose CPython source says int(arg): the builtin's conversions and errors.
        internal static BigInteger IntOf(EvalContext c, ScriptValue x)
        {
            ScriptException error;
            ScriptValue v = IntOrError(new[] { x }, KwArgs.Empty, c, out error);
            if (error != null)
                throw error;
            return NumericOps.AsBigInteger(v);
        }

        // int(); a malformed literal's ValueError comes back in `error`
        private static ScriptValue IntOrError(ScriptValue[] args, KwArgs kw, EvalContext c, out ScriptException error)
        {
            error = null;
            Args.AtMost(c, args, "int", 2);
            ScriptValue xArg = args.Length >= 1 ? args[0] : null;
            ScriptValue baseArg = args.Length >= 2 ? args[1] : null;
            var r = new KwReader(c, kw, "int");
            ScriptValue baseKw;
            if (r.TryGet("base", out baseKw))
            {
                if (baseArg != null)
                    throw Raise.TypeError(c, "int() got multiple values for argument 'base'");
                baseArg = baseKw;
            }
            r.RejectInvalid();

            if (xArg == null)
            {
                if (baseArg != null)
                    throw Raise.TypeError(c, "int() missing string argument");
                return c.Values.Int(0);
            }

            if (baseArg != null)
            {
                string text = TextOf(xArg);
                if (text == null)
                    throw Raise.TypeError(c, "int() can't convert non-string with explicit base");
                return IntOfText(c, text, BaseToInt(c, baseArg), out error);
            }

            IntValue iv = xArg as IntValue;
            if (iv != null)
                return c.EnumValues != null && c.EnumValues.ContainsKey(iv) ? c.Values.Int(iv.Value) : xArg;   // int(IntEnum member) is a plain int
            BoolValue bv = xArg as BoolValue;
            if (bv != null)
                return c.Values.Int(bv.Value ? 1 : 0);
            FloatValue fv = xArg as FloatValue;
            if (fv != null)
                return c.Values.Int(IntFromFloat(c, fv.Value));
            string digits = TextOf(xArg);
            if (digits != null)
                return IntOfText(c, digits, 10, out error);
            FractionValue fr = xArg as FractionValue;   // __int__ truncates toward zero
            if (fr != null)
                return c.Values.Int(BigOps.Divide(c, fr.Num, fr.Den));
            DecimalValue dv = xArg as DecimalValue;
            if (dv != null)
                return c.Values.Int(new BigInteger(Math.Truncate(dv.Value)));
            throw Raise.TypeError(c, "int() argument must be a string, a bytes-like object or a real number, not '" + xArg.PyTypeName + "'");
        }

        private static ScriptValue IntOfText(EvalContext c, string text, int numBase, out ScriptException error)
        {
            BigInteger value;
            error = IntParser.ParseOrError(c, text, numBase, out value);
            return error == null ? c.Values.Int(value) : null;
        }

        // a str, or bytes read as ASCII (int(b'12'), float(b'1.5')); null for anything else
        private static string TextOf(ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s != null)
                return s.Value;
            BytesValue b = v as BytesValue;
            return b != null ? System.Text.Encoding.GetEncoding(28591).GetString(b.Data) : null;
        }

        private static int BaseToInt(EvalContext c, ScriptValue baseArg)
        {
            BigInteger b = Coerce.ToIndex(c, baseArg, "int() base");
            if (b < 0 || b > 36)
                return 999;   // out of range; IntParser raises the base error
            return (int)b;
        }

        internal static BigInteger IntFromFloat(EvalContext c, double d)
        {
            if (double.IsNaN(d))
                throw Raise.ValueError(c, "cannot convert float NaN to integer");
            if (double.IsInfinity(d))
                throw Raise.Overflow(c, "cannot convert float infinity to integer");
            bool neg;
            ulong mant;
            int e2;
            FloatRepr.Decompose(d, out neg, out mant, out e2);
            if (mant == 0)
                return BigInteger.Zero;
            BigInteger m = mant;
            BigInteger r = e2 >= 0 ? m << e2 : m >> (-e2);   // truncation toward zero
            return neg ? -r : r;
        }

        // float(); a malformed literal's ValueError comes back in `error`
        private static ScriptValue FloatOrError(ScriptValue[] args, KwArgs kw, EvalContext c, out ScriptException error)
        {
            error = null;
            if (kw.Count > 0)
                throw Raise.TypeError(c, "float() takes no keyword arguments");
            Args.Unpack(c, args, "float", 0, 1);
            if (args.Length == 0)
                return c.Values.Float(0.0);
            ScriptValue x = args[0];
            FloatValue fv = x as FloatValue;
            if (fv != null)
                return x;
            IntValue iv = x as IntValue;
            if (iv != null)
                return c.Values.Float(NumericOps.ToDoubleChecked(iv.Value, c));
            BoolValue bv = x as BoolValue;
            if (bv != null)
                return c.Values.Float(bv.Value ? 1.0 : 0.0);
            string text = TextOf(x);
            if (text != null)
            {
                double d;
                error = ParseFloatOrError(c, text, out d);
                return error == null ? c.Values.Float(d) : null;
            }
            FractionValue fr = x as FractionValue;   // the other numeric-tower members' __float__
            if (fr != null)
                return c.Values.Float(fr.AsDouble(c));
            DecimalValue dv = x as DecimalValue;
            if (dv != null)
                return c.Values.Float(NearestDouble.FromDecimal(dv.Value));
            throw Raise.TypeError(c, "float() argument must be a string or a real number, not '" + x.PyTypeName + "'");
        }

        // _PyUnicode_TransformDecimalAndSpaceToASCII: any Unicode decimal digit reads as its ASCII twin
        internal static string AsciiDigits(string s)
        {
            char[] chars = null;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < 0x80)
                    continue;
                int d = PyUnicode.DecimalDigitValue(s[i]);
                if (d < 0)
                    continue;
                if (chars == null)
                    chars = s.ToCharArray();
                chars[i] = (char)('0' + d);
            }
            return chars == null ? s : new string(chars);
        }

        internal static double ParseFloat(EvalContext c, string original)
        {
            double d;
            ScriptException error = ParseFloatOrError(c, original, out d);
            if (error != null)
                throw error;
            return d;
        }

        private static ScriptException ParseFloatOrError(EvalContext c, string original, out double value)
        {
            value = 0.0;
            int start = 0, end = original.Length;
            while (start < end && PyUnicode.IsPythonSpace(original[start]))
                start++;
            while (end > start && PyUnicode.IsPythonSpace(original[end - 1]))
                end--;
            string s = AsciiDigits(original.Substring(start, end - start));

            int sign = 1;
            int i = 0;
            if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            {
                if (s[i] == '-')
                    sign = -1;
                i++;
            }
            string rest = s.Substring(i);
            if (rest.Equals("inf", StringComparison.OrdinalIgnoreCase) || rest.Equals("infinity", StringComparison.OrdinalIgnoreCase))
            {
                value = sign > 0 ? double.PositiveInfinity : double.NegativeInfinity;
                return null;
            }
            if (rest.Equals("nan", StringComparison.OrdinalIgnoreCase))
            {
                value = sign > 0 ? NumericOps.PyNaN : -NumericOps.PyNaN;
                return null;
            }

            double d;
            if (!IsAsciiFloat(rest) || !NearestDouble.TryParse(rest.Replace("_", ""), out d))
                return Raise.ValueError(c, "could not convert string to float: " + c.Values.Str(Truncate200(original)).Repr(c));
            value = sign < 0 ? -d : d;   // float('1e400') is inf, as in CPython
            return null;
        }

        // Accept only the ASCII float grammar: digits/./e with an optional exponent sign; '_' and hex are rejected.
        private static bool IsAsciiFloat(string s)
        {
            if (s.Length == 0)
                return false;
            int i = 0;
            bool digits = false, dot = false, exp = false;
            while (i < s.Length)
            {
                char ch = s[i];
                if (ch >= '0' && ch <= '9')
                {
                    digits = true;
                    i++;
                }
                else if (ch == '.' && !dot && !exp)
                {
                    dot = true;
                    i++;
                }
                else if ((ch == 'e' || ch == 'E') && !exp && digits)
                {
                    exp = true;
                    digits = false;   // require digits after exponent
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                        i++;
                }
                else if (ch == '_' && i > 0 && i + 1 < s.Length
                    && s[i - 1] >= '0' && s[i - 1] <= '9' && s[i + 1] >= '0' && s[i + 1] <= '9')
                {
                    i++;   // PEP 515: '_' between digits
                }
                else
                {
                    return false;
                }
            }
            return digits;
        }

        private static readonly string[] StrNames = { "object", "encoding", "errors" };   // built once

        private static ScriptValue StrBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            args = StrMethods.WithKw(c, args, kw, "str", StrNames);
            Args.AtMost(c, args, "str", 3);
            if (args.Length == 0)
                return c.Values.Str("");
            ScriptValue x = args[0];
            if (args.Length >= 2)
            {
                BytesValue data = x as BytesValue;   // str(bytes, encoding[, errors]) is bytes.decode
                if (data == null)
                    throw Raise.TypeError(c, "decoding to str: need a bytes-like object, " + x.PyTypeName + " found");
                string enc = args[1].Kind == ValueKind.None ? "utf-8" : BytesCodec.Normalize(c, args[1]);
                string errors = "strict";
                if (args.Length == 3)
                {
                    StrValue es = args[2] as StrValue;
                    if (es == null)
                        throw Raise.TypeError(c, "str() argument 'errors' must be str, not " + args[2].PyTypeName);
                    errors = es.Value;
                }
                return c.Values.Str(BytesCodec.Decode(c, data.Data, enc, errors));
            }
            if (x.Kind == ValueKind.Str && (c.EnumValues == null || !c.EnumValues.ContainsKey(x)))
                return x;   // the same object, no copy (a StrEnum member gives its plain value or __str__)
            return c.Values.Str(x.Str(c));
        }

        private static ScriptValue BoolBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            if (kw.Count > 0)
                throw Raise.TypeError(c, "bool() takes no keyword arguments");
            Args.Unpack(c, args, "bool", 0, 1);
            if (args.Length == 0)
                return c.Values.Bool(false);
            return c.Values.Bool(args[0].IsTruthy(c));
        }

        internal static string Truncate200(string s)
        {
            return s.Length <= 200 ? s : s.Substring(0, 200);
        }
    }
}
