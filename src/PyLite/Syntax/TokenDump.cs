using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Outbridge.PyLite.Syntax
{
    // Deterministic text dump of the token stream: golden tests, CorpusScan, Token.ToString.
    // Never uses double.ToString for floats (repr-independence until the Ryū port).
    internal static class TokenDump
    {
        public static string DumpLine(Token t)
        {
            string head = t.Line.ToString(CultureInfo.InvariantCulture) + ":" +
                          t.Col.ToString(CultureInfo.InvariantCulture) + " " + t.Kind;
            string payload = Payload(t);
            return payload == null ? head : head + " " + payload;
        }

        public static string DumpAll(List<Token> tokens)
        {
            var sb = new StringBuilder();
            foreach (var t in tokens)
                sb.Append(DumpLine(t)).Append('\n');
            return sb.ToString();
        }

        private static string Payload(Token t)
        {
            switch (t.Kind)
            {
                case TokenKind.Name:
                case TokenKind.FStringStart:
                case TokenKind.FStringEnd:
                    return t.Text;
                case TokenKind.IntLiteral:
                    return ((BigInteger)t.Value).ToString(CultureInfo.InvariantCulture);
                case TokenKind.FloatLiteral:
                    return "0x" + BitConverter.DoubleToInt64Bits((double)t.Value).ToString("X16", CultureInfo.InvariantCulture);
                case TokenKind.StringLiteral:
                case TokenKind.FStringMiddle:
                    return Quote(t.Text);
                default:
                    return null;
            }
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20 || c >= 0x7F)
                            sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
