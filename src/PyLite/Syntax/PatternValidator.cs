using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    // CPython's compiler checks on match patterns (codegen_pattern_*): an irrefutable pattern before the last
    // case or alternative, a name bound twice, alternatives binding different names, two starred names, a
    // repeated class-pattern attribute, a repeated literal mapping key. Pattern depth is bounded by the parser.
    internal static class PatternValidator
    {
        public static void Validate(MatchNode m)
        {
            for (int i = 0; i < m.Cases.Count; i++)
            {
                MatchCase mc = m.Cases[i];
                Check(mc.Pattern, mc.Guard != null || i == m.Cases.Count - 1, new HashSet<string>(System.StringComparer.Ordinal));
            }
        }

        private static void Check(PatternNode p, bool allowIrrefutable, HashSet<string> stores)
        {
            switch (p)
            {
                case CapturePattern cp:
                    if (!allowIrrefutable)
                        throw Err(p.Src, cp.Target != null ? "name capture '" + cp.Target.Id + "' makes remaining patterns unreachable"
                            : "wildcard makes remaining patterns unreachable");
                    Store(cp.Target, p.Src, stores);
                    break;
                case AsPattern a:
                    Check(a.Inner, allowIrrefutable, stores);
                    Store(a.Target, p.Src, stores);
                    break;
                case StarPattern st:
                    Store(st.Target, p.Src, stores);
                    break;
                case SequencePattern seq:
                {
                    int stars = 0;
                    foreach (PatternNode e in seq.Elements)
                    {
                        if (e is StarPattern && ++stars > 1)
                            throw Err(p.Src, "multiple starred names in sequence pattern");
                    }
                    foreach (PatternNode e in seq.Elements)
                        Check(e, true, stores);
                    break;
                }
                case MappingPattern mp:
                {
                    int dup = FirstDuplicateKey(mp);
                    if (dup >= 0)
                        throw Err(p.Src, "mapping pattern checks duplicate key (" + ReprKey(mp.Keys[dup]) + ")");
                    foreach (PatternNode v in mp.Values)
                        Check(v, true, stores);
                    Store(mp.Rest, p.Src, stores);
                    break;
                }
                case ClassPattern cls:
                {
                    // CPython 3.14: a __debug__ keyword anywhere first, then the first name repeated later, at the repeat
                    for (int i = 0; i < cls.KwNames.Count; i++)
                    {
                        if (cls.KwNames[i] == "__debug__")
                            throw Err(cls.KwPatterns[i].Src, "cannot assign to __debug__");
                    }
                    var first = new Dictionary<string, int>(System.StringComparer.Ordinal);
                    var second = new Dictionary<string, int>(System.StringComparer.Ordinal);
                    for (int i = 0; i < cls.KwNames.Count; i++)
                    {
                        if (!first.ContainsKey(cls.KwNames[i]))
                            first[cls.KwNames[i]] = i;
                        else if (!second.ContainsKey(cls.KwNames[i]))
                            second[cls.KwNames[i]] = i;
                    }
                    for (int i = 0; i < cls.KwNames.Count; i++)
                    {
                        string n = cls.KwNames[i];
                        int j;
                        if (first[n] == i && second.TryGetValue(n, out j))
                            throw Err(cls.KwPatterns[j].Src, "attribute name repeated in class pattern: " + n);
                    }
                    foreach (PatternNode q in cls.Positional)
                        Check(q, true, stores);
                    foreach (PatternNode q in cls.KwPatterns)
                        Check(q, true, stores);
                    break;
                }
                case OrPattern o:
                {
                    HashSet<string> control = null;
                    for (int i = 0; i < o.Alternatives.Count; i++)
                    {
                        var alt = new HashSet<string>(System.StringComparer.Ordinal);
                        Check(o.Alternatives[i], i == o.Alternatives.Count - 1 && allowIrrefutable, alt);
                        if (control == null)
                            control = alt;
                        else if (!alt.SetEquals(control))
                            throw Err(p.Src, "alternative patterns bind different names");
                    }
                    foreach (string name in control)
                    {
                        if (!stores.Add(name))
                            throw Err(p.Src, "multiple assignments to name '" + name + "' in pattern");
                    }
                    break;
                }
            }
        }

        private static void Store(NameNode target, SourceInfo at, HashSet<string> stores)
        {
            if (target != null && !stores.Add(target.Id))
                throw Err(at, "multiple assignments to name '" + target.Id + "' in pattern");
        }

        // The index of the key that first repeats an earlier one, or -1. By sorting, not hashing: the hashes of BigInteger
        // and of a string are the same in every process on net472, so a source can be written to collide.
        private static int FirstDuplicateKey(MappingPattern mp)
        {
            var keys = new List<KeyValuePair<object, int>>(mp.Keys.Count);
            for (int i = 0; i < mp.Keys.Count; i++)
            {
                object k = ConstKey(mp.Keys[i]);
                if (k != null)
                    keys.Add(new KeyValuePair<object, int>(k, i));
            }
            keys.Sort((a, b) =>
            {
                int byKey = CompareKeys(a.Key, b.Key);
                return byKey != 0 ? byKey : a.Value.CompareTo(b.Value);
            });
            int first = -1;
            for (int i = 1; i < keys.Count; i++)
            {
                if (CompareKeys(keys[i - 1].Key, keys[i].Key) == 0 && (first < 0 || keys[i].Value < first))
                    first = keys[i].Value;
            }
            return first;
        }

        private static int KeyRank(object k)
        {
            return k is BigInteger ? 0 : k is double ? 1 : 2;
        }

        private static int CompareKeys(object a, object b)
        {
            int rank = KeyRank(a).CompareTo(KeyRank(b));
            if (rank != 0)
                return rank;
            if (a is BigInteger)
                return ((BigInteger)a).CompareTo((BigInteger)b);
            if (a is double)
                return ((double)a).CompareTo((double)b);
            return string.CompareOrdinal((string)a, (string)b);
        }

        // A literal key's value as Python compares it: 1, 1.0 and True are one key; null for a value pattern. By value,
        // never through text: BigInteger.ToString is quadratic, and a radix literal has no digit cap.
        private static object ConstKey(ExprNode key)
        {
            double sign = 1;
            UnaryOpNode u = key as UnaryOpNode;
            if (u != null)
            {
                sign = u.Op == UnaryOp.USub ? -1 : 1;
                key = u.Operand;
            }
            LiteralNode lit = key as LiteralNode;
            if (lit == null)
                return null;
            switch (lit.Kind)
            {
                case LiteralKind.Int:
                    return (BigInteger)lit.Value * (int)sign;
                case LiteralKind.Float:
                    double d = (double)lit.Value * sign;
                    if (d == System.Math.Floor(d) && !double.IsInfinity(d))
                        return new BigInteger(d);
                    return d;
                case LiteralKind.Bool:
                    return (bool)lit.Value ? BigInteger.One : BigInteger.Zero;
                case LiteralKind.Str:
                    return "s" + (string)lit.Value;
                case LiteralKind.Bytes:
                    return "b" + System.Convert.ToBase64String((byte[])lit.Value);
                case LiteralKind.None:
                    return "None";
                default:
                    return null;
            }
        }

        private static string ReprKey(ExprNode key)
        {
            string sign = "";
            UnaryOpNode u = key as UnaryOpNode;
            if (u != null)
            {
                sign = u.Op == UnaryOp.USub ? "-" : "";
                key = u.Operand;
            }
            LiteralNode lit = (LiteralNode)key;
            switch (lit.Kind)
            {
                case LiteralKind.Int:   // past the 4,300 digits an int has no text in CPython either
                    return sign + (((BigInteger)lit.Value).ToByteArray().Length > 1800 ? "a long int" : ((BigInteger)lit.Value).ToString(CultureInfo.InvariantCulture));
                case LiteralKind.Float: return sign + Modules.Support.FloatRepr.Repr((double)lit.Value);
                case LiteralKind.Bool: return (bool)lit.Value ? "True" : "False";
                case LiteralKind.None: return "None";
                case LiteralKind.Str: return QuoteStr((string)lit.Value);
                default: return "b" + QuoteStr(Encoding.GetEncoding("ISO-8859-1").GetString((byte[])lit.Value));
            }
        }

        private static string QuoteStr(string s)
        {
            char q = s.IndexOf('\'') >= 0 && s.IndexOf('"') < 0 ? '"' : '\'';
            var sb = new StringBuilder().Append(q);
            foreach (char c in s)
            {
                if (c == q || c == '\\')
                    sb.Append('\\').Append(c);
                else if (c == '\n')
                    sb.Append("\\n");
                else if (c == '\r')
                    sb.Append("\\r");
                else if (c == '\t')
                    sb.Append("\\t");
                else if (c < 0x20 || c == 0x7f)
                    sb.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                else
                    sb.Append(c);
            }
            return sb.Append(q).ToString();
        }

        private static PySyntaxErrorException Err(SourceInfo s, string msg) { return PySyntaxErrorException.Syntax(msg, s.Line, s.Col); }
    }
}
