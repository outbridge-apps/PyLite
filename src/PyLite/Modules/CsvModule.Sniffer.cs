using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // csv.Sniffer and the dialect object it answers with - a port of Lib/csv.py's Sniffer: the
    // quote-and-delimiter regexes first, the per-character frequency consistency second, and the
    // has_header column-type heuristic.
    public static partial class CsvModule
    {
        // A dialect as a value: what sniff() returns, what csv.excel is, what reader()/writer() accept in
        // place of a name. Read-only; the fmtparams of a reader call override a COPY of it.
        private sealed class DialectValue : ScriptValue
        {
            private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("csv.Dialect", BuildSlots());
            internal readonly Dialect D;

            internal DialectValue(Dialect d) { D = d; }

            public override ScriptTypeInfo TypeInfo { get { return Type; } }
            internal override ValueKind Kind { get { return ValueKind.Opaque; } }

            protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
            {
                sb.Append("<csv.Dialect delimiter=" + ctx.Values.Str(D.Delimiter.ToString()).Repr(ctx)
                    + " quotechar=" + ctx.Values.Str(D.QuoteChar.ToString()).Repr(ctx) + ">");
            }

            private static ScriptValue Chr(EvalContext c, char ch)
            {
                if (ch == (char)0)
                    return c.Values.None;
                return c.Values.Str(ch.ToString());
            }

            private static Dictionary<string, SlotDescriptor> BuildSlots()
            {
                return new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
                {
                    ["delimiter"] = SlotDescriptor.MakeProperty("delimiter", (self, c) => Chr(c, ((DialectValue)self).D.Delimiter)),
                    ["quotechar"] = SlotDescriptor.MakeProperty("quotechar", (self, c) => Chr(c, ((DialectValue)self).D.QuoteChar)),
                    ["escapechar"] = SlotDescriptor.MakeProperty("escapechar", (self, c) => Chr(c, ((DialectValue)self).D.EscapeChar)),
                    ["doublequote"] = SlotDescriptor.MakeProperty("doublequote", (self, c) => c.Values.Bool(((DialectValue)self).D.DoubleQuote)),
                    ["skipinitialspace"] = SlotDescriptor.MakeProperty("skipinitialspace", (self, c) => c.Values.Bool(((DialectValue)self).D.SkipInitialSpace)),
                    ["lineterminator"] = SlotDescriptor.MakeProperty("lineterminator", (self, c) => c.Values.Str(((DialectValue)self).D.LineTerminator)),
                    ["quoting"] = SlotDescriptor.MakeProperty("quoting", (self, c) => c.Values.Int(((DialectValue)self).D.Quoting)),
                    ["strict"] = SlotDescriptor.MakeProperty("strict", (self, c) => c.Values.Bool(((DialectValue)self).D.Strict)),
                };
            }
        }

        private sealed class SnifferValue : ScriptValue
        {
            private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("csv.Sniffer", BuildSlots());

            public override ScriptTypeInfo TypeInfo { get { return Type; } }
            internal override ValueKind Kind { get { return ValueKind.Opaque; } }

            private static Dictionary<string, SlotDescriptor> BuildSlots()
            {
                var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
                d["sniff"] = SlotDescriptor.MakeMethod("sniff", (self, a, kw, c) => SniffFn(c, a, kw));
                d["has_header"] = SlotDescriptor.MakeMethod("has_header", (self, a, kw, c) => HasHeaderFn(c, a));
                return d;
            }
        }

        private static readonly char[] Preferred = { ',', '\t', ';', ' ', ':' };

        private static ScriptValue SniffFn(EvalContext ctx, ScriptValue[] a, KwArgs kw)
        {
            Args.Between(ctx, a, "sniff", 1, 2);
            StrValue sample = a[0] as StrValue;
            if (sample == null)
                throw Raise.TypeError(ctx, "sniff() sample must be a str, not " + a[0].PyTypeName);
            ScriptValue delimsV = a.Length == 2 ? a[1] : null;
            ScriptValue kwv;
            if (kw.TryGet("delimiters", out kwv))
                delimsV = kwv;
            string delimiters = null;
            if (delimsV != null && delimsV.Kind != ValueKind.None)
            {
                StrValue ds = delimsV as StrValue;
                if (ds == null)
                    throw Raise.TypeError(ctx, "delimiters must be a str or None");
                delimiters = ds.Value;
            }
            return new DialectValue(Sniff(ctx, sample.Value, delimiters));
        }

        private static Dialect Sniff(EvalContext ctx, string sample, string delimiters)
        {
            ctx.Budget.Step(1 + sample.Length / 64);
            char quotechar;
            bool doublequote, skipinitialspace;
            char delimiter;
            GuessQuoteAndDelimiter(ctx, sample, delimiters, out quotechar, out doublequote, out delimiter, out skipinitialspace);
            if (delimiter == '\0')
                GuessDelimiter(ctx, sample, delimiters, out delimiter, out skipinitialspace);
            if (delimiter == '\0')
                throw Raise.Make(ctx, PyExceptionTypes.CsvError, "Could not determine delimiter");
            return new Dialect
            {
                Delimiter = delimiter,
                QuoteChar = quotechar == '\0' ? '"' : quotechar,
                DoubleQuote = doublequote,
                SkipInitialSpace = skipinitialspace,
                LineTerminator = "\r\n",
                Quoting = QuoteMinimal,
            };
        }

        private static readonly Regex[] QuotePatterns =
        {
            new Regex("(?<delim>[^\\w\\n\"'])(?<space> ?)(?<quote>[\"']).*?\\k<quote>\\k<delim>", RegexOptions.Singleline | RegexOptions.Multiline),
            new Regex("(?:^|\\n)(?<quote>[\"']).*?\\k<quote>(?<delim>[^\\w\\n\"'])(?<space> ?)", RegexOptions.Singleline | RegexOptions.Multiline),
            new Regex("(?<delim>[^\\w\\n\"'])(?<space> ?)(?<quote>[\"']).*?\\k<quote>(?:$|\\n)", RegexOptions.Singleline | RegexOptions.Multiline),
            new Regex("(?:^|\\n)(?<quote>[\"']).*?\\k<quote>(?:$|\\n)", RegexOptions.Singleline | RegexOptions.Multiline),
        };

        // Lib/csv.py _guess_quote_and_delimiter: quoted fields give the quote away, and the character
        // right next to the quotes is the delimiter. The most frequent pair wins, first seen on a tie.
        private static void GuessQuoteAndDelimiter(EvalContext ctx, string data, string delimiters,
            out char quotechar, out bool doublequote, out char delimiter, out bool skipinitialspace)
        {
            ctx.Budget.CheckDeadlineNow();
            // Lib/csv.py runs these passes over the whole sample. None of them can be interrupted, so a
            // multi-megabyte sample would outlive the deadline; the head answers the same for any real
            // file. The delimiter frequency scan below still reads all of it.
            const int QuoteScanCap = 64 * 1024;
            if (data.Length > QuoteScanCap)
            {
                int cut = data.LastIndexOf('\n', QuoteScanCap - 1);
                data = data.Substring(0, cut > 0 ? cut + 1 : QuoteScanCap);
            }
            quotechar = '\0';
            doublequote = false;
            delimiter = '\0';
            skipinitialspace = false;
            MatchCollection matches = null;
            foreach (Regex re in QuotePatterns)
            {
                matches = re.Matches(data);
                if (matches.Count > 0)
                    break;
            }
            if (matches == null || matches.Count == 0)
                return;
            var quotes = new Counter();
            var delims = new Counter();
            int spaces = 0;
            foreach (Match m in matches)
            {
                Group q = m.Groups["quote"];
                if (q.Success && q.Value.Length > 0)
                    quotes.Add(q.Value[0]);
                Group d = m.Groups["delim"];
                if (!d.Success)
                    continue;
                if (d.Value.Length > 0 && (delimiters == null || delimiters.IndexOf(d.Value[0]) >= 0))
                    delims.Add(d.Value[0]);
                Group sp = m.Groups["space"];
                if (sp.Success && sp.Value.Length > 0)
                    spaces++;
            }
            quotechar = quotes.Max();
            if (delims.Count > 0)
            {
                delimiter = delims.Max();
                skipinitialspace = delims.Of(delimiter) == spaces;
                if (delimiter == '\n')
                    delimiter = '\0';
            }
            // an extra quote between two delimiters means the quote is doubled inside fields
            string dq = "((" + Regex.Escape(delimiter == '\0' ? "" : delimiter.ToString()) + ")|^)\\W*" + Regex.Escape(quotechar.ToString())
                + "[^" + Regex.Escape(delimiter == '\0' ? "" : delimiter.ToString()) + "\\n]*" + Regex.Escape(quotechar.ToString())
                + "[^" + Regex.Escape(delimiter == '\0' ? "" : delimiter.ToString()) + "\\n]*" + Regex.Escape(quotechar.ToString())
                + "\\W*((" + Regex.Escape(delimiter == '\0' ? "" : delimiter.ToString()) + ")|$)";
            doublequote = Regex.IsMatch(data, dq, RegexOptions.Multiline);
        }

        // Insertion-ordered counts, so max() breaks ties the way a dict-backed max(key=get) does.
        private sealed class Counter
        {
            private readonly List<char> _keys = new List<char>();
            private readonly List<int> _counts = new List<int>();

            public int Count { get { return _keys.Count; } }

            public void Add(char c)
            {
                int i = _keys.IndexOf(c);
                if (i < 0)
                {
                    _keys.Add(c);
                    _counts.Add(1);
                }
                else
                    _counts[i]++;
            }

            public int Of(char c)
            {
                int i = _keys.IndexOf(c);
                return i < 0 ? 0 : _counts[i];
            }

            public char Max()
            {
                int best = -1;
                for (int i = 0; i < _keys.Count; i++)
                    if (best < 0 || _counts[i] > _counts[best])
                        best = i;
                return best < 0 ? '\0' : _keys[best];
            }
        }

        // Lib/csv.py _guess_delimiter: in chunks of ten lines, for every 7-bit character build the
        // "how many lines have it N times" table, take each character's dominant count (minus the
        // others), and keep the characters that hit that count on 90%+ of the lines - lowering the bar
        // a point at a time until something qualifies. One survivor is the delimiter; several go to the
        // preferred list, then to the most consistent.
        private static void GuessDelimiter(EvalContext ctx, string data, string delimiters, out char delimiter, out bool skipinitialspace)
        {
            delimiter = '\0';
            skipinitialspace = false;
            var lines = new List<string>();
            foreach (string line in data.Split('\n'))
            {
                ctx.Budget.Step();
                if (line.Length > 0)
                    lines.Add(line);
            }
            if (lines.Count == 0)
                return;
            int chunk = Math.Min(10, lines.Count);
            int iteration = 0;
            // per character: ordered (count-per-line -> lines) pairs
            var freq = new List<KeyValuePair<int, int>>[127];
            var modes = new KeyValuePair<int, int>[127];
            var hasMode = new bool[127];
            var delims = new List<char>();
            var counts = new int[127];
            int start = 0, end = chunk;
            while (start < lines.Count)
            {
                iteration++;
                // One pass per line fills all 127 counts; Lib/csv.py rescans the line per character, and
                // on a sample the loop cannot resolve early that difference is the whole cost.
                for (int li = start; li < end && li < lines.Count; li++)
                {
                    ctx.Budget.Step();
                    string line = lines[li];
                    Array.Clear(counts, 0, counts.Length);
                    foreach (char ch in line)
                        if (ch < 127)
                            counts[ch]++;
                    for (int c = 0; c < 127; c++)
                    {
                        int n = counts[c];
                        List<KeyValuePair<int, int>> meta = freq[c] ?? (freq[c] = new List<KeyValuePair<int, int>>());
                        int at = -1;
                        for (int i = 0; i < meta.Count; i++)
                        {
                            if (meta[i].Key == n)
                            {
                                at = i;
                                break;
                            }
                        }
                        if (at < 0)
                            meta.Add(new KeyValuePair<int, int>(n, 1));
                        else
                            meta[at] = new KeyValuePair<int, int>(n, meta[at].Value + 1);
                    }
                }
                for (int c = 0; c < 127; c++)
                {
                    List<KeyValuePair<int, int>> items = freq[c];
                    if (items == null || (items.Count == 1 && items[0].Key == 0))
                        continue;
                    if (items.Count > 1)
                    {
                        int best = 0;
                        for (int i = 1; i < items.Count; i++)
                            if (items[i].Value > items[best].Value)
                                best = i;
                        int others = 0;
                        for (int i = 0; i < items.Count; i++)
                            if (i != best)
                                others += items[i].Value;
                        modes[c] = new KeyValuePair<int, int>(items[best].Key, items[best].Value - others);
                    }
                    else
                        modes[c] = items[0];
                    hasMode[c] = true;
                }
                double total = Math.Min(chunk * iteration, lines.Count);
                double consistency = 1.0;
                const double threshold = 0.9;
                while (delims.Count == 0 && consistency >= threshold)
                {
                    for (int c = 0; c < 127; c++)
                    {
                        if (!hasMode[c] || modes[c].Key <= 0 || modes[c].Value <= 0)
                            continue;
                        if (modes[c].Value / total >= consistency && (delimiters == null || delimiters.IndexOf((char)c) >= 0))
                            delims.Add((char)c);
                    }
                    consistency -= 0.01;
                }
                if (delims.Count == 1)
                {
                    delimiter = delims[0];
                    skipinitialspace = CountOf(lines[0], delimiter) == CountOf(lines[0], delimiter + " ");
                    return;
                }
                start = end;
                end += chunk;
            }
            if (delims.Count == 0)
                return;
            foreach (char p in Preferred)
            {
                if (delims.Contains(p))
                {
                    delimiter = p;
                    skipinitialspace = CountOf(lines[0], p) == CountOf(lines[0], p + " ");
                    return;
                }
            }
            // nothing preferred: the most consistent character, the highest one on a tie
            char pick = delims[0];
            foreach (char c in delims)
                if (modes[c].Value > modes[pick].Value || (modes[c].Value == modes[pick].Value && c > pick))
                    pick = c;
            delimiter = pick;
            skipinitialspace = CountOf(lines[0], pick) == CountOf(lines[0], pick + " ");
        }

        private static int CountOf(string s, char c)
        {
            int n = 0;
            foreach (char ch in s)
                if (ch == c)
                    n++;
            return n;
        }

        private static int CountOf(string s, string sub)
        {
            int n = 0, at = 0;
            while ((at = s.IndexOf(sub, at, StringComparison.Ordinal)) >= 0)
            {
                n++;
                at += sub.Length;
            }
            return n;
        }

        // Lib/csv.py has_header: read the sample with the sniffed dialect; for each column find the one
        // type (int, float, or a fixed length) every data row agrees on; the first row is a header when
        // it disagrees with more of those columns than it agrees with.
        private static ScriptValue HasHeaderFn(EvalContext ctx, ScriptValue[] a)
        {
            Args.Exactly(ctx, a, "has_header", 1);
            StrValue sample = a[0] as StrValue;
            if (sample == null)
                throw Raise.TypeError(ctx, "has_header() sample must be a str, not " + a[0].PyTypeName);
            Dialect d = Sniff(ctx, sample.Value, null);
            var rows = new CsvRowIterator(PyOps.GetIterator(new StringIOValue(ctx, sample.Value), ctx), d);
            ScriptValue first;
            if (!rows.MoveNext(ctx, out first))
                return ctx.Values.Bool(false);
            List<ScriptValue> header = ((ListValue)first).Items;
            int columns = header.Count;
            const int TInt = -1, TFloat = -2;               // a column type: int, float, or a length >= 0
            var colType = new int?[columns];
            var alive = new bool[columns];
            for (int i = 0; i < columns; i++)
                alive[i] = true;
            int checkedRows = 0;
            ScriptValue rowV;
            while (rows.MoveNext(ctx, out rowV))
            {
                if (checkedRows > 20)
                    break;
                checkedRows++;
                List<ScriptValue> row = ((ListValue)rowV).Items;
                if (row.Count != columns)
                    continue;
                for (int col = 0; col < columns; col++)
                {
                    if (!alive[col])
                        continue;
                    string cell = ((StrValue)row[col]).Value;
                    int thisType = IsInt(cell) ? TInt : IsFloat(cell) ? TFloat : cell.Length;
                    if (colType[col] == null)
                        colType[col] = thisType;
                    else if (colType[col].Value != thisType)
                        alive[col] = false;                 // inconsistent: out of consideration
                }
            }
            int hasHeader = 0;
            for (int col = 0; col < columns; col++)
            {
                if (!alive[col] || colType[col] == null)
                    continue;
                string cell = ((StrValue)header[col]).Value;
                int t = colType[col].Value;
                bool agrees = t == TInt ? IsInt(cell) : t == TFloat ? IsFloat(cell) : cell.Length == t;
                hasHeader += agrees ? -1 : 1;
            }
            return ctx.Values.Bool(hasHeader > 0);
        }

        private static bool IsInt(string s)
        {
            long v;
            return long.TryParse(s.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);
        }

        private static bool IsFloat(string s)
        {
            double v;
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
