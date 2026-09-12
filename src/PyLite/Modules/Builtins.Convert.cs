using System;
using System.Collections.Generic;
using System.Globalization;
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
                ["from_bytes"] = BuiltinFunctionValue.Make("from_bytes", (s, args, kw, c) => IntMethods.FromBytes(c, args, kw)),
            };
            var bytesStatics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["fromhex"] = BuiltinFunctionValue.Make("fromhex", (s, args, kw, c) => BytesFromHex(c, args)),
                ["maketrans"] = BuiltinFunctionValue.Make("maketrans", (s, args, kw, c) => BytesMethods.MakeTrans(c, args)),
            };
            var strStatics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["maketrans"] = BuiltinFunctionValue.Make("maketrans", (s, args, kw, c) => StrMakeTrans(c, args)),
            };
            var floatStatics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["fromhex"] = BuiltinFunctionValue.Make("fromhex", (s, args, kw, c) => FloatMethods.FromHex(c, args)),
            };
            AddType(d, "int", (self, args, kw, c) => IntBuiltin(c, args, kw), intStatics, IntValue.IntType);
            AddType(d, "float", (self, args, kw, c) => FloatBuiltin(c, args, kw), floatStatics, FloatValue.FloatType);
            AddType(d, "str", (self, args, kw, c) => StrBuiltin(c, args, kw), strStatics, StrValue.StrType);
            // bool inherits int's classmethod in CPython, and it answers with an int there too
            AddType(d, "bool", (self, args, kw, c) => BoolBuiltin(c, args, kw), intStatics);
            AddType(d, "bytes", (self, args, kw, c) => BytesBuiltin(c, args), bytesStatics, BytesValue.BytesType);
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
            int[] xs = CodePoints(x.Value), ys = CodePoints(y.Value);   // lengths in code points, as CPython counts
            if (xs.Length != ys.Length)
                throw Raise.ValueError(c, "the first two maketrans arguments must have equal length");
            for (int i = 0; i < xs.Length; i++)
                table.SetItem(c.Values.Int(xs[i]), c.Values.Int(ys[i]), c);
            if (args.Length == 3)
            {
                StrValue z = args[2] as StrValue;
                if (z == null)
                    throw Raise.TypeError(c, "maketrans arguments must be strings");
                foreach (int cp in CodePoints(z.Value))
                    table.SetItem(c.Values.Int(cp), c.Values.None, c);
            }
            return table;
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

        // bytes.fromhex('41 42') -> b'AB'  (ASCII spaces between byte pairs are ignored, CPython parity).
        private static ScriptValue BytesFromHex(EvalContext c, ScriptValue[] args)
        {
            StrValue s = args.Length >= 1 ? args[0] as StrValue : null;
            if (s == null)
                throw Raise.TypeError(c, "fromhex() argument must be str");
            string hex = s.Value;
            c.Budget.ChargeLinear(hex.Length);
            var outB = new List<byte>();
            int i = 0;
            while (i < hex.Length)
            {
                if (hex[i] == ' ')
                {
                    i++;
                    continue;
                }
                int hi = HexNibble(hex[i]);
                int lo = i + 1 < hex.Length ? HexNibble(hex[i + 1]) : -1;
                if (hi < 0 || lo < 0)
                    throw Raise.ValueError(c, "non-hexadecimal number found in fromhex() arg at position " + i);
                outB.Add((byte)(hi * 16 + lo));
                i += 2;
            }
            return c.Values.Bytes(outB.ToArray());
        }

        private static int HexNibble(char ch)
        {
            if (ch >= '0' && ch <= '9') return ch - '0';
            if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
            if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
            return -1;
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
            var list = new List<byte>();
            IScriptIterator it = PyOps.GetIterator(src, c);
            ScriptValue e;
            while (it.MoveNext(c, out e))
            {
                if (e.Kind != ValueKind.Int && e.Kind != ValueKind.Bool)
                    throw Raise.TypeError(c, "'" + e.PyTypeName + "' object cannot be interpreted as an integer");
                BigInteger v = NumericOps.AsBigInteger(e);
                if (v < 0 || v > 255)
                    throw Raise.ValueError(c, "bytes must be in range(0, 256)");
                list.Add((byte)v);
            }
            return c.Values.Bytes(list.ToArray());
        }

        private static ScriptValue IntBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
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
                StrValue s = xArg as StrValue;
                if (s == null)
                    throw Raise.TypeError(c, "int() can't convert non-string with explicit base");
                return c.Values.Int(IntParser.Parse(c, s.Value, BaseToInt(c, baseArg)));
            }

            IntValue iv = xArg as IntValue;
            if (iv != null)
                return xArg;
            BoolValue bv = xArg as BoolValue;
            if (bv != null)
                return c.Values.Int(bv.Value ? 1 : 0);
            FloatValue fv = xArg as FloatValue;
            if (fv != null)
                return c.Values.Int(IntFromFloat(c, fv.Value));
            StrValue sv = xArg as StrValue;
            if (sv != null)
                return c.Values.Int(IntParser.Parse(c, sv.Value, 10));
            FractionValue fr = xArg as FractionValue;   // __int__ truncates toward zero
            if (fr != null)
                return c.Values.Int(BigInteger.Divide(fr.Num, fr.Den));
            DecimalValue dv = xArg as DecimalValue;
            if (dv != null)
                return c.Values.Int(new BigInteger(Math.Truncate(dv.Value)));
            throw Raise.TypeError(c, "int() argument must be a string, a bytes-like object or a real number, not '" + xArg.PyTypeName + "'");
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

        private static ScriptValue FloatBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            if (kw.Count > 0)
                throw Raise.TypeError(c, "float() takes no keyword arguments");
            if (args.Length > 1)
                throw Raise.TypeError(c, "float expected at most 1 argument, got " + args.Length);
            if (args.Length == 0)
                return c.Values.Float(0.0);
            ScriptValue x = args[0];
            FloatValue fv = x as FloatValue;
            if (fv != null)
                return x;
            IntValue iv = x as IntValue;
            if (iv != null)
                return c.Values.Float(IntToDouble(c, iv.Value));
            BoolValue bv = x as BoolValue;
            if (bv != null)
                return c.Values.Float(bv.Value ? 1.0 : 0.0);
            StrValue sv = x as StrValue;
            if (sv != null)
                return c.Values.Float(ParseFloat(c, sv.Value));
            FractionValue fr = x as FractionValue;   // the other numeric-tower members' __float__
            if (fr != null)
                return c.Values.Float(fr.AsDouble(c));
            DecimalValue dv = x as DecimalValue;
            if (dv != null)
                return c.Values.Float((double)dv.Value);
            throw Raise.TypeError(c, "float() argument must be a string or a real number, not '" + x.PyTypeName + "'");
        }

        private static double IntToDouble(EvalContext c, BigInteger v)
        {
            double d = (double)v;
            if (double.IsInfinity(d))
                throw Raise.Overflow(c, "long int too large to convert to float");
            return d;
        }

        private static double ParseFloat(EvalContext c, string original)
        {
            int start = 0, end = original.Length;
            while (start < end && PyUnicode.IsPythonSpace(original[start]))
                start++;
            while (end > start && PyUnicode.IsPythonSpace(original[end - 1]))
                end--;
            string s = original.Substring(start, end - start);

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
                return sign > 0 ? double.PositiveInfinity : double.NegativeInfinity;
            if (rest.Equals("nan", StringComparison.OrdinalIgnoreCase))
                return double.NaN;

            if (!IsAsciiFloat(rest))
                throw Raise.ValueError(c, "could not convert string to float: " + c.Values.Str(Truncate200(original)).Repr(c));
            try
            {
                double d = double.Parse((sign < 0 ? "-" : "") + rest.Replace("_", ""), NumberStyles.Float, CultureInfo.InvariantCulture);
                return d;
            }
            catch (OverflowException)
            {
                return sign > 0 ? double.PositiveInfinity : double.NegativeInfinity;   // float('1e400') == inf
            }
            catch (FormatException)
            {
                throw Raise.ValueError(c, "could not convert string to float: " + c.Values.Str(Truncate200(original)).Repr(c));
            }
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

        private static ScriptValue StrBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            args = StrMethods.WithKw(c, args, kw, "str", "object", "encoding", "errors");
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
            if (x.Kind == ValueKind.Str)
                return x;   // the same object, no copy
            return c.Values.Str(x.Str(c));
        }

        private static ScriptValue BoolBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            if (kw.Count > 0)
                throw Raise.TypeError(c, "bool() takes no keyword arguments");
            if (args.Length > 1)
                throw Raise.TypeError(c, "bool expected at most 1 argument, got " + args.Length);
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
