using Newtonsoft.Json;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // The fast path of the grammar check, run on the token stream. The reader enforces the structure
    // itself — a missing comma or colon, a stray character, a bad escape all throw — so what is left to
    // catch is what it accepts: a comma before a closer, a number spelled the JavaScript way, a comment,
    // a hole in an array, a quote character that is not a double quote. Each of those is visible from
    // the token's kind, its quote character, or the raw text at its end, which the reader's line
    // position locates; a string's interior, the one part that can be long, is never walked again.
    //
    // Nothing is decided here. Whatever does not line up hands the document to JsonScan, which either
    // raises CPython's error or proves the document valid, after which the guard switches off. So a
    // valid document pays a few comparisons per token, an invalid one pays a scan, and the answer never
    // depends on the reader's position arithmetic being what this code expects.
    //
    // The one thing the reader hides is a raw control character other than CR and LF inside a string:
    // it keeps the character without a trace. Those two move its line number, which is compared.
    //
    // A by-product: for a number token the guard knows the exact span of its text, which is what the
    // parse_float / parse_int hooks are handed.
    internal sealed class JsonGuard
    {
        private readonly EvalContext _ctx;
        private readonly string _s;
        private readonly StrValue _doc;   // the document as the script's own value, for the error's doc
        private readonly bool _strict;
        private readonly int _maxDepth;
        private int _end;         // just past the previous token
        private int _line = 1;    // the reader's line arithmetic, kept in step through the whitespace
        private int _lineStart;
        private bool _on = true;

        internal int NumberStart;   // the span of the last number token, valid while On
        internal int NumberEnd;

        internal JsonGuard(EvalContext ctx, string s, StrValue doc, bool strict, int maxDepth)
        {
            _ctx = ctx;
            _s = s;
            _doc = doc;
            _strict = strict;
            _maxDepth = maxDepth;
        }

        internal bool On { get { return _on; } }

        // Called once per token, before it is consumed.
        internal void Token(JsonTextReader r)
        {
            if (!_on)
                return;
            JsonToken t = r.TokenType;
            string s = _s;
            int line = r.LineNumber;
            if (line != _line && !Resync(line, t == JsonToken.PropertyName))
            {
                Verdict();
                return;
            }
            int end = _lineStart + r.LinePosition;
            if (end <= _end || end > s.Length)
            {
                Verdict();
                return;
            }
            char last = s[end - 1];
            switch (t)
            {
                case JsonToken.StartObject:
                case JsonToken.StartArray:
                    if (last != (t == JsonToken.StartObject ? '{' : '['))
                        break;
                    _end = end;
                    return;

                case JsonToken.EndObject:
                case JsonToken.EndArray:
                    // the reader takes "[1,]": the comma is in the gap before the closer
                    if (last != (t == JsonToken.EndObject ? '}' : ']') || !Blank(_end, end - 1))
                        break;
                    _end = end;
                    return;

                case JsonToken.PropertyName:
                    if (last != ':' || r.QuoteChar != '"')
                        break;
                    _end = end;
                    return;

                case JsonToken.String:
                    if (last != '"' || r.QuoteChar != '"')
                        break;
                    _end = end;
                    return;

                case JsonToken.Integer:
                case JsonToken.Float:
                    // the text from the gap to the reader's end must be exactly one number of ours
                    int start = _end;
                    while (start < end && (s[start] == ' ' || s[start] == ',' || s[start] == '\t'
                        || s[start] == '\n' || s[start] == '\r'))
                        start++;
                    if (JsonScan.Number(s, start) != end && !Literal(start, end))
                        break;
                    NumberStart = start;
                    NumberEnd = end;
                    _end = end;
                    return;

                case JsonToken.Boolean:
                case JsonToken.Null:
                    _end = end;
                    return;
            }
            Verdict();
        }

        // The reader stopped or threw: the scan says why, in CPython's words, when the text is at fault.
        internal void Verdict()
        {
            JsonScan.Validate(_ctx, _s, _doc, _strict, _maxDepth);
            _on = false;   // proved valid: nothing left to check
        }

        // An error of our own at a position the scan did not reach.
        internal ScriptException Fail(string msg, int pos)
        {
            return JsonScan.Error(_ctx, _s, _doc, msg, pos);
        }

        // The reader moved to line; the breaks must lie in the whitespace after the previous token — one
        // inside a token is a raw newline inside a string. CR LF counts once, as it does for the reader.
        // A key's token runs to its colon, so for one the walk may pass over the name itself.
        private bool Resync(int line, bool key)
        {
            string s = _s;
            int n = s.Length;
            int i = _end;
            while (_line < line)
            {
                if (i >= n)
                    return false;
                char c = s[i++];
                if (c == '\n')
                {
                    _line++;
                    _lineStart = i;
                }
                else if (c == '\r')
                {
                    if (i < n && s[i] == '\n')
                        i++;
                    _line++;
                    _lineStart = i;
                }
                else if (c == '"' && key)
                {
                    key = false;
                    i = PastString(i);
                }
                else if (c != ' ' && c != '\t' && c != ',')
                {
                    return false;
                }
            }
            return true;
        }

        // Where the next token begins, for an error the reader raised on it; -1 once the guard is off.
        internal int NextStart
        {
            get
            {
                if (!_on)
                    return -1;
                string s = _s;
                int i = _end;
                while (i < s.Length && (s[i] == ' ' || s[i] == ',' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
                    i++;
                return i;
            }
        }

        // Just past the closing quote of the string whose opening quote is at i - 1 (or n).
        private int PastString(int i)
        {
            string s = _s;
            int n = s.Length;
            while (i < n)
            {
                char c = s[i++];
                if (c == '"')
                    return i;
                if (c == '\\')
                    i++;
            }
            return n;
        }

        private bool Blank(int from, int to)
        {
            string s = _s;
            for (int i = from; i < to; i++)
            {
                char c = s[i];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    return false;
            }
            return true;
        }

        // CPython's decoder takes NaN and the infinities as well, so they are not a leniency.
        private bool Literal(int start, int end)
        {
            string s = _s;
            int len = end - start;
            return (len == 3 && string.CompareOrdinal(s, start, "NaN", 0, 3) == 0)
                || (len == 8 && string.CompareOrdinal(s, start, "Infinity", 0, 8) == 0)
                || (len == 9 && string.CompareOrdinal(s, start, "-Infinity", 0, 9) == 0);
        }
    }
}
