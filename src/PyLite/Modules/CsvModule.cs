using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // csv (corpus-gated): reader/writer + DictReader/DictWriter, QUOTE_* constants
    // csv.Error. The reader accepts any iterable of strings (a list of lines, io.StringIO, ...); quoted
    // fields may span lines (the parser pulls more lines mid-record). The writer targets any object with
    // a write() attribute (io.StringIO or user record classes).
    public static partial class CsvModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["reader"] = BuiltinFunctionValue.Make("reader", Reader);
            m["writer"] = BuiltinFunctionValue.Make("writer", Writer);
            m["DictReader"] = BuiltinFunctionValue.Make("DictReader", DictReader);
            m["DictWriter"] = BuiltinFunctionValue.Make("DictWriter", DictWriter);
            m["QUOTE_MINIMAL"] = ctx.Values.Int(0);
            m["QUOTE_ALL"] = ctx.Values.Int(1);
            m["QUOTE_NONNUMERIC"] = ctx.Values.Int(2);
            m["QUOTE_NONE"] = ctx.Values.Int(3);
            m["QUOTE_STRINGS"] = ctx.Values.Int(4);      // 3.12
            m["QUOTE_NOTNULL"] = ctx.Values.Int(5);
            m["Error"] = ErrorType();
            // The three CPython registers at import, as objects; get_dialect / list_dialects /
            // register_dialect / unregister_dialect over those plus whatever this RUN registered
            // (EvalContext.Csv — the module dictionary is shared between runs and cannot hold it).
            m["excel"] = new DialectValue(new Dialect());
            m["excel_tab"] = new DialectValue(new Dialect { Delimiter = '\t' });
            m["unix_dialect"] = new DialectValue(new Dialect { LineTerminator = "\n", Quoting = QuoteAll });
            m["get_dialect"] = BuiltinFunctionValue.Make("get_dialect", (s, a, k, c) =>
            {
                Args.Exactly(c, a, "get_dialect", 1);
                return new DialectValue(NamedDialect(c, a[0]));
            });
            m["list_dialects"] = BuiltinFunctionValue.Make("list_dialects", (s, a, k, c) =>
            {
                ListValue names = c.Values.List(4);
                foreach (string n in Builtin)
                    names.Add(c.Values.Str(n), c);
                foreach (string n in c.Csv.Names)
                {
                    if (Array.IndexOf(Builtin, n) < 0)
                        names.Add(c.Values.Str(n), c);
                }
                return names;
            });
            m["register_dialect"] = BuiltinFunctionValue.Make("register_dialect", RegisterDialect);
            m["unregister_dialect"] = BuiltinFunctionValue.Make("unregister_dialect", (s, a, k, c) =>
            {
                Args.Exactly(c, a, k, "unregister_dialect", 1);
                StrValue n = a[0] as StrValue;
                if (n == null || !c.Csv.Unregister(n.Value))
                    throw Raise.Make(c, PyExceptionTypes.CsvError, "unknown dialect");
                return c.Values.None;
            });
            m["field_size_limit"] = BuiltinFunctionValue.Make("field_size_limit", FieldSizeLimit);
            m["__version__"] = ctx.Values.Str("1.0");
            m["Sniffer"] = ctx.Values.Type("Sniffer", (s, a, k, c) => new SnifferValue(), null, null);
            return ctx.Values.Module("csv", m);
        }

        private static TypeValue ErrorType()
        {
            BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(PyExceptionTypes.CsvError, args);
            return TypeValue.Make("Error", ctor, null, null, PyExceptionTypes.CsvError);
        }

        private const int QuoteMinimal = 0, QuoteAll = 1, QuoteNonNumeric = 2, QuoteNone = 3;
        private const int QuoteStrings = 4, QuoteNotNull = 5;   // 3.12

        // CPython validates the range: anything outside it is a TypeError, not a silent QUOTE_MINIMAL.
        private static int Quoting(EvalContext ctx, ScriptValue v)
        {
            int q = Support.Coerce.ToInt32(ctx, v, "quoting must be an integer");
            if (q < QuoteMinimal || q > QuoteNotNull)
                throw Raise.TypeError(ctx, "bad \"quoting\" value");
            return q;
        }

        private sealed class Dialect
        {
            public char Delimiter = ',';
            public char QuoteChar = '"';
            public string LineTerminator = "\r\n";
            public int Quoting = QuoteMinimal;
            public bool SkipInitialSpace;
            public char EscapeChar;        // '\0' => none
            public bool DoubleQuote = true;
            public bool Strict;

            public Dialect Clone() { return (Dialect)MemberwiseClone(); }
        }

        private static readonly string[] Builtin = { "excel", "excel-tab", "unix" };

        // A name resolves against this RUN's registry first, so register_dialect can shadow a preset the
        // way it does in CPython's single dict, then against the three presets CPython registers at import.
        private static Dialect NamedDialect(EvalContext ctx, ScriptValue v)
        {
            DialectValue dv = v as DialectValue;
            if (dv != null)
                return dv.D.Clone();   // a copy: the caller's fmtparams override it without touching the object
            StrValue s = v as StrValue;
            if (s == null)
                return DuckDialect(ctx, v);
            object registered;
            if (ctx.Csv.TryGetDialect(s.Value, out registered))
                return ((Dialect)registered).Clone();
            switch (s.Value)
            {
                case "excel":
                    return new Dialect();
                case "excel-tab":
                    return new Dialect { Delimiter = '\t' };
                case "unix":
                    return new Dialect { LineTerminator = "\n", Quoting = QuoteAll };
                default:
                    throw Raise.Make(ctx, PyExceptionTypes.CsvError, "unknown dialect");
            }
        }

        // Anything that is not a name or one of our dialect objects is read for the eight attributes, each
        // absent one falling back to the default. That is exactly what CPython's dialect_init does — it
        // PyErr_Clear()s a missing attribute — so csv.Dialect there is a carrier, not a requirement, and a
        // record class with the fields set in __init__ serves as a dialect here without any subclassing
        // (which the class model does not have: a base must be a record class, and a class body holds no
        // attributes). An object with none of the eight is therefore the default dialect, as it is there.
        private static Dialect DuckDialect(EvalContext ctx, ScriptValue v)
        {
            var d = new Dialect();
            ScriptValue a;
            if (TryAttr(ctx, v, "delimiter", out a))
                d.Delimiter = OneChar(ctx, a, "delimiter");
            if (TryAttr(ctx, v, "quotechar", out a))
                d.QuoteChar = OneChar(ctx, a, "quotechar");
            if (TryAttr(ctx, v, "escapechar", out a) && a.Kind != ValueKind.None)
                d.EscapeChar = OneChar(ctx, a, "escapechar");
            if (TryAttr(ctx, v, "doublequote", out a))
                d.DoubleQuote = PyOps.Truth(a, ctx);
            if (TryAttr(ctx, v, "skipinitialspace", out a))
                d.SkipInitialSpace = PyOps.Truth(a, ctx);
            if (TryAttr(ctx, v, "lineterminator", out a))
            {
                StrValue lt = a as StrValue;
                if (lt == null)
                    throw Raise.TypeError(ctx, "\"lineterminator\" must be a string");
                d.LineTerminator = lt.Value;
            }
            if (TryAttr(ctx, v, "quoting", out a))
                d.Quoting = Quoting(ctx, a);
            if (TryAttr(ctx, v, "strict", out a))
                d.Strict = PyOps.Truth(a, ctx);
            return d;
        }

        // The attribute, or false when the object does not carry one. A record instance (the usual
        // carrier: a class with the fields set in __init__) answers without an exception, because seven
        // misses per construction at exception cost made a reader in a loop crawl; anything else goes
        // through GetAttr, where a raised AttributeError reads as absent — CPython's PyErr_Clear() too.
        private static bool TryAttr(EvalContext ctx, ScriptValue v, string name, out ScriptValue value)
        {
            RecordInstanceValue r = v as RecordInstanceValue;
            if (r != null)
            {
                if (!r.TryGetAttribute(name, ctx, out value))
                    return false;
            }
            else
            {
                try
                {
                    value = PyOps.GetAttr(v, name, ctx);
                }
                catch (ScriptException ex) when (ex.Value.ExcType.IsSubtypeOf(PyExceptionTypes.AttributeError))
                {
                    value = null;
                    return false;
                }
            }
            return value.Kind != ValueKind.None || name == "escapechar";
        }

        // register_dialect(name, dialect=None, **fmtparams). The base may be a registered name, one of our
        // dialect objects or anything shaped like one; the fmtparams override it, exactly as they do for
        // reader()/writer().
        private static ScriptValue RegisterDialect(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "register_dialect", 1, 2);
            StrValue name = a[0] as StrValue;
            if (name == null)
                throw Raise.TypeError(ctx, "dialect name must be a string");
            ctx.Csv.Register(name.Value, ReadDialect(ctx, kw, a.Length > 1 ? a[1] : null));
            return ctx.Values.None;
        }

        // field_size_limit([new_limit]) -> the limit in force BEFORE the call, as in CPython. It bounds
        // one field, not the document: a runaway quoted field stops here with csv.Error instead of
        // growing until the allocation budget notices.
        private static ScriptValue FieldSizeLimit(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.AtMost(ctx, a, kw, "field_size_limit", 1);
            int old = ctx.Csv.FieldLimit;
            if (a.Length == 1)
                ctx.Csv.FieldLimit = Support.Coerce.ToInt32(ctx, a[0], "limit must be an integer");
            return ctx.Values.Int(old);
        }

        private static readonly string[] DialectKeywords =
        {
            "dialect", "delimiter", "quotechar", "lineterminator", "quoting", "skipinitialspace",
            "escapechar", "doublequote", "strict",
        };

        // `dialect` (positional or keyword) picks the base preset; the fmtparams below override it, as in
        // CPython. extraAllowed: the caller's own keywords (fieldnames, restkey, ...). Anything else is
        // refused rather than silently ignored — a dropped option is a wrong answer with no signal.
        private static Dialect ReadDialect(EvalContext ctx, KwArgs kw, ScriptValue dialect, params string[] extraAllowed)
        {
            KwReader.RejectInvalidExcept(ctx, kw, null, DialectKeywords, extraAllowed);
            ScriptValue v;
            if (kw.TryGet("dialect", out v))
            {
                if (dialect != null)
                    throw Raise.TypeError(ctx, "got multiple values for argument 'dialect'");
                dialect = v;
            }
            Dialect d = dialect == null || dialect.Kind == ValueKind.None ? new Dialect() : NamedDialect(ctx, dialect);
            if (kw.TryGet("delimiter", out v))
                d.Delimiter = OneChar(ctx, v, "delimiter");
            if (kw.TryGet("quotechar", out v))
                d.QuoteChar = OneChar(ctx, v, "quotechar");
            if (kw.TryGet("lineterminator", out v) && v.Kind == ValueKind.Str)
                d.LineTerminator = ((StrValue)v).Value;
            if (kw.TryGet("quoting", out v))
                d.Quoting = Quoting(ctx, v);
            if (kw.TryGet("skipinitialspace", out v))
                d.SkipInitialSpace = PyOps.Truth(v, ctx);
            if (kw.TryGet("escapechar", out v) && v.Kind != ValueKind.None)
                d.EscapeChar = OneChar(ctx, v, "escapechar");
            if (kw.TryGet("doublequote", out v))
                d.DoubleQuote = PyOps.Truth(v, ctx);
            if (kw.TryGet("strict", out v))
                d.Strict = PyOps.Truth(v, ctx);
            return d;
        }

        private static char OneChar(EvalContext ctx, ScriptValue v, string name)
        {
            StrValue s = v as StrValue;
            if (s == null || s.Value.Length != 1)
                throw Raise.TypeError(ctx, "\"" + name + "\" must be a 1-character string");
            return s.Value[0];
        }

        // ---- reader ----

        private static ScriptValue Reader(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "reader", 1, 2);
            IScriptIterator lines = PyOps.GetIterator(a[0], ctx);
            Dialect rd = ReadDialect(ctx, kw, a.Length > 1 ? a[1] : null);
            return ctx.Values.Iterator(new CsvRowIterator(lines, rd), "csv.reader", ReaderSlots);
        }

        private static ScriptValue DictReader(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, "DictReader", 1);
            IScriptIterator lines = PyOps.GetIterator(a[0], ctx);
            Dialect d = ReadDialect(ctx, kw, null, "fieldnames", "restkey", "restval");
            ScriptValue fieldnames = null;
            ScriptValue fv;
            if (kw.TryGet("fieldnames", out fv) && fv.Kind != ValueKind.None)
                fieldnames = fv;
            ScriptValue restkey, restval;
            if (!kw.TryGet("restkey", out restkey))
                restkey = ctx.Values.None;
            if (!kw.TryGet("restval", out restval))
                restval = ctx.Values.None;
            var rows = new CsvRowIterator(lines, d);
            return ctx.Values.Iterator(new CsvDictRowIterator(rows, fieldnames, restkey, restval),
                "csv.DictReader", DictReaderSlots);
        }

        // The iterator type cache is keyed by NAME, so one shared table serves every csv.reader and each
        // getter has to reach its own iterator through self. A closure over one reader would answer for
        // all of them.
        private static readonly IDictionary<string, SlotDescriptor> ReaderSlots = BuildReaderSlots();

        private static IDictionary<string, SlotDescriptor> BuildReaderSlots()
        {
            var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            // line_num counts SOURCE lines, so a row holding an embedded newline advances it by more than
            // one - the same number CPython reports.
            s["line_num"] = SlotDescriptor.MakeProperty("line_num", (self, ctx) =>
                ctx.Values.Int(((CsvRowIterator)((IteratorValue)self).Iterator).LineNum));
            s["dialect"] = SlotDescriptor.MakeProperty("dialect", (self, ctx) =>
                new DialectValue(((CsvRowIterator)((IteratorValue)self).Iterator).Dialect));
            return s;
        }

        // DictReader.fieldnames reads the header lazily, exactly as the first next() would; the rest are
        // what the reader was built with, plus the inner csv.reader CPython exposes as .reader.
        private static readonly IDictionary<string, SlotDescriptor> DictReaderSlots = BuildDictReaderSlots();

        private static CsvDictRowIterator Dict(ScriptValue self)
        {
            return (CsvDictRowIterator)((IteratorValue)self).Iterator;
        }

        private static IDictionary<string, SlotDescriptor> BuildDictReaderSlots()
        {
            var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            s["fieldnames"] = SlotDescriptor.MakeProperty("fieldnames", (self, ctx) => Dict(self).FieldNames(ctx));
            s["restkey"] = SlotDescriptor.MakeProperty("restkey", (self, ctx) => Dict(self).RestKey);
            s["restval"] = SlotDescriptor.MakeProperty("restval", (self, ctx) => Dict(self).RestVal);
            s["line_num"] = SlotDescriptor.MakeProperty("line_num", (self, ctx) => ctx.Values.Int(Dict(self).Rows.LineNum));
            s["dialect"] = SlotDescriptor.MakeProperty("dialect", (self, ctx) => new DialectValue(Dict(self).Rows.Dialect));
            // .reader is the csv.reader underneath, which CPython hands out as an ordinary reader
            s["reader"] = SlotDescriptor.MakeProperty("reader", (self, ctx) =>
                ctx.Values.Iterator(Dict(self).Rows, "csv.reader", ReaderSlots));
            return s;
        }

        // The row parser: a state machine over lines; quoted fields may consume additional lines.
        private sealed class CsvRowIterator : ScriptIteratorBase
        {
            private readonly IScriptIterator _lines;
            private readonly Dialect _d;

            internal CsvRowIterator(IScriptIterator lines, Dialect d) { _lines = lines; _d = d; }

            internal int LineNum;

            internal Dialect Dialect { get { return _d; } }

            private string NextLine(EvalContext ctx)
            {
                ScriptValue v;
                if (!_lines.MoveNext(ctx, out v))
                    return null;
                LineNum++;
                StrValue s = v as StrValue;
                if (s == null)
                    throw Raise.Make(ctx, PyExceptionTypes.CsvError,
                        "iterator should return strings, not " + v.PyTypeName);
                return s.Value.TrimEnd('\r', '\n');
            }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                string line = NextLine(ctx);
                if (line == null)
                {
                    value = null;
                    return false;
                }
                ListValue row = ctx.Values.List(4);
                var field = new StringBuilder();
                State st = State.StartField;
                bool fieldQuoted = false, any = line.Length > 0;
                int i = 0;
                while (true)
                {
                    ctx.Budget.Step();
                    if (i >= line.Length)
                    {
                        if (st != State.InQuotedField && st != State.EscapeInQuotedField)
                            break;
                        // a quoted field spans lines: the newline belongs to the field
                        string more = NextLine(ctx);
                        if (more == null)
                            throw Raise.Make(ctx, PyExceptionTypes.CsvError, "unexpected end of data");
                        field.Append('\n');
                        line = more;
                        i = 0;
                        st = State.InQuotedField;
                        continue;
                    }
                    char c = line[i++];
                    bool esc = _d.EscapeChar != '\0' && c == _d.EscapeChar;
                    bool quote = c == _d.QuoteChar && _d.Quoting != QuoteNone;
                    switch (st)
                    {
                        case State.StartField:
                            if (quote)
                            {
                                st = State.InQuotedField;
                                fieldQuoted = true;
                            }
                            else if (esc)
                            {
                                st = State.EscapedChar;
                            }
                            else if (c == ' ' && _d.SkipInitialSpace)
                            {
                                // leading spaces are not part of the field
                            }
                            else if (c == _d.Delimiter)
                            {
                                AddField(ctx, row, field, fieldQuoted);
                                fieldQuoted = false;
                                any = true;
                            }
                            else
                            {
                                field.Append(c);
                                st = State.InField;
                            }
                            break;
                        case State.EscapedChar:
                            field.Append(c);
                            st = State.InField;
                            break;
                        case State.InField:
                            if (esc)
                                st = State.EscapedChar;
                            else if (c == _d.Delimiter)
                            {
                                AddField(ctx, row, field, fieldQuoted);
                                fieldQuoted = false;
                                any = true;
                                st = State.StartField;
                            }
                            else
                                field.Append(c);
                            break;
                        case State.InQuotedField:
                            if (esc)
                                st = State.EscapeInQuotedField;
                            else if (quote)
                                st = _d.DoubleQuote ? State.QuoteInQuotedField : State.InField;
                            else
                                field.Append(c);
                            break;
                        case State.EscapeInQuotedField:
                            field.Append(c);
                            st = State.InQuotedField;
                            break;
                        default:   // QuoteInQuotedField: the char after a closing quote
                            if (quote)
                            {
                                field.Append(_d.QuoteChar);   // "" -> literal quote
                                st = State.InQuotedField;
                            }
                            else if (c == _d.Delimiter)
                            {
                                AddField(ctx, row, field, fieldQuoted);
                                fieldQuoted = false;
                                any = true;
                                st = State.StartField;
                            }
                            else if (!_d.Strict)
                            {
                                field.Append(c);
                                st = State.InField;
                            }
                            else
                                throw Raise.Make(ctx, PyExceptionTypes.CsvError,
                                    "'" + _d.Delimiter + "' expected after '" + _d.QuoteChar + "'");
                            break;
                    }
                }
                if (any || field.Length > 0 || fieldQuoted)
                    AddField(ctx, row, field, fieldQuoted);
                value = row;   // an empty input line yields an empty row (CPython parity)
                return true;
            }

            // QUOTE_NONNUMERIC turns every UNQUOTED field into a float; an empty unquoted field stays
            // a string (CPython never enters the numeric state for it).
            private void AddField(EvalContext ctx, ListValue row, StringBuilder field, bool quoted)
            {
                if (field.Length > ctx.Csv.FieldLimit)
                    throw Raise.Make(ctx, PyExceptionTypes.CsvError,
                        "field larger than field limit (" + ctx.Csv.FieldLimit + ")");
                string s = field.ToString();
                field.Clear();
                // QUOTE_STRINGS and QUOTE_NOTNULL (3.12) read an EMPTY unquoted field back as None, which
                // is the other half of writing a None bare; past that, STRINGS behaves as NONNUMERIC and
                // NOTNULL as ALL.
                if (!quoted && s.Length == 0 && (_d.Quoting == QuoteStrings || _d.Quoting == QuoteNotNull))
                {
                    row.Add(ctx.Values.None, ctx);
                    return;
                }
                if ((_d.Quoting == QuoteNonNumeric || _d.Quoting == QuoteStrings) && !quoted && s.Length > 0)
                {
                    double num;
                    if (!double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out num))
                        throw Raise.ValueError(ctx, "could not convert string to float: " + ctx.Values.Str(s).Repr(ctx));
                    row.Add(ctx.Values.Float(num), ctx);
                    return;
                }
                row.Add(ctx.Values.Str(s), ctx);
            }
        }

        // The reader states, exactly CPython's (Modules/_csv.c).
        private enum State { StartField, EscapedChar, InField, InQuotedField, EscapeInQuotedField, QuoteInQuotedField }

        // DictReader: the first row is the header unless fieldnames= was given.
        private sealed class CsvDictRowIterator : ScriptIteratorBase
        {
            private readonly CsvRowIterator _rows;
            private readonly ScriptValue _restKey;    // key for the columns past the header (default None)
            private readonly ScriptValue _restVal;    // filler for the columns the row is missing

            internal CsvRowIterator Rows { get { return _rows; } }
            internal ScriptValue RestKey { get { return _restKey; } }
            internal ScriptValue RestVal { get { return _restVal; } }
            private ScriptValue _fieldnamesArg;
            private List<ScriptValue> _names;

            internal CsvDictRowIterator(CsvRowIterator rows, ScriptValue fieldnames, ScriptValue restKey, ScriptValue restVal)
            {
                _rows = rows;
                _fieldnamesArg = fieldnames;
                _restKey = restKey;
                _restVal = restVal;
            }

            // null only when the header row is asked for and the input is empty.
            private List<ScriptValue> EnsureNames(EvalContext ctx)
            {
                if (_names != null)
                    return _names;
                var names = new List<ScriptValue>();
                if (_fieldnamesArg != null)
                {
                    IScriptIterator it = PyOps.GetIterator(_fieldnamesArg, ctx);
                    ScriptValue n;
                    while (it.MoveNext(ctx, out n))
                        names.Add(n);
                    _fieldnamesArg = null;
                }
                else
                {
                    ScriptValue header;
                    if (!_rows.MoveNext(ctx, out header))
                        return null;
                    foreach (ScriptValue n in ((ListValue)header).Items)
                        names.Add(n);
                }
                _names = names;
                return _names;
            }

            internal ScriptValue FieldNames(EvalContext ctx)
            {
                List<ScriptValue> names = EnsureNames(ctx);
                if (names == null)
                    return ctx.Values.None;
                ListValue l = ctx.Values.List(names.Count);
                foreach (ScriptValue n in names)
                    l.Add(n, ctx);
                return l;
            }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                List<ScriptValue> names = EnsureNames(ctx);
                if (names == null)
                {
                    value = null;
                    return false;
                }
                ScriptValue rowVal;
                if (!_rows.MoveNext(ctx, out rowVal))
                {
                    value = null;
                    return false;
                }
                ListValue row = (ListValue)rowVal;
                DictValue d = ctx.Values.Dict(names.Count + 1);
                for (int i = 0; i < names.Count; i++)
                    d.SetItem(names[i], i < row.Items.Count ? row.Items[i] : _restVal, ctx);
                if (row.Items.Count > names.Count)
                {
                    ListValue rest = ctx.Values.List(row.Items.Count - names.Count);
                    for (int i = names.Count; i < row.Items.Count; i++)
                        rest.Add(row.Items[i], ctx);
                    d.SetItem(_restKey, rest, ctx);   // restkey defaults to None, the key itself
                }
                value = d;
                return true;
            }
        }

        // ---- writer ----

        private static ScriptValue Writer(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "writer", 1, 2);
            ctx.Values.PreCharge(48);
            return new CsvWriterValue(a[0], ReadDialect(ctx, kw, a.Length > 1 ? a[1] : null), null);
        }

        private static ScriptValue DictWriter(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Dialect d = ReadDialect(ctx, kw, null, "fieldnames", "restval", "extrasaction");
            ScriptValue fieldnames;
            if (a.Length == 2)
                fieldnames = a[1];
            else if (a.Length == 1 && kw.TryGet("fieldnames", out fieldnames))
            {
                // fieldnames by keyword
            }
            else
                throw Args.ExactlyError(ctx, "DictWriter", 2, a.Length);
            IScriptIterator it = PyOps.GetIterator(fieldnames, ctx);
            var names = new List<ScriptValue>();
            ScriptValue n;
            while (it.MoveNext(ctx, out n))
                names.Add(n);
            ScriptValue restval;
            if (!kw.TryGet("restval", out restval))
                restval = ctx.Values.Str("");
            ScriptValue extras;
            string extrasaction = kw.TryGet("extrasaction", out extras) && extras.Kind == ValueKind.Str
                ? ((StrValue)extras).Value : "raise";
            if (extrasaction != "raise" && extrasaction != "ignore")
                throw Raise.ValueError(ctx, "extrasaction (" + extrasaction + ") must be 'raise' or 'ignore'");
            ctx.Values.PreCharge(48);
            return new CsvWriterValue(a[0], d, names, restval, extrasaction == "raise");
        }

        // csv.writer / csv.DictWriter instance: writerow/writerows (+writeheader for Dict).
        private sealed class CsvWriterValue : ScriptValue
        {
            private static readonly ScriptTypeInfo WType = new ScriptTypeInfo("csv.writer", BuildWriterSlots());
            private static readonly ScriptTypeInfo DType = new ScriptTypeInfo("csv.DictWriter", BuildWriterSlots());

            private readonly ScriptValue _file;
            private readonly Dialect _d;
            private readonly List<ScriptValue> _fieldnames;   // non-null => DictWriter
            private readonly ScriptValue _restVal;            // filler for keys the row omits
            private readonly bool _raiseOnExtras;             // extrasaction='raise' (the default)

            internal CsvWriterValue(ScriptValue file, Dialect d, List<ScriptValue> fieldnames)
                : this(file, d, fieldnames, null, true)
            {
            }

            internal CsvWriterValue(ScriptValue file, Dialect d, List<ScriptValue> fieldnames,
                ScriptValue restVal, bool raiseOnExtras)
            {
                _file = file;
                _d = d;
                _fieldnames = fieldnames;
                _restVal = restVal;
                _raiseOnExtras = raiseOnExtras;
            }

            public override ScriptTypeInfo TypeInfo { get { return _fieldnames != null ? DType : WType; } }
            internal override ValueKind Kind { get { return ValueKind.Opaque; } }

            // CPython returns whatever the sink's write() returned (3.5 for writerow, 3.8 for
            // writeheader), which is how a caller counts what it wrote.
            private ScriptValue Emit(EvalContext ctx, string text)
            {
                ScriptValue write = PyOps.GetAttr(_file, "write", ctx);
                return ctx.CallHook1(write, ctx.Values.Str(text));   // per-row hot path
            }

            private string FormatField(EvalContext ctx, ScriptValue v)
            {
                bool isNumber = v.Kind == ValueKind.Int || v.Kind == ValueKind.Float;
                string s = v.Kind == ValueKind.None ? "" : v.Str(ctx);
                bool mustQuote;
                switch (_d.Quoting)
                {
                    case 1: mustQuote = true; break;                                    // QUOTE_ALL
                    // QUOTE_NONNUMERIC quotes anything PyNumber_Check refuses, None included: _csv.c computes
                    // quoted = !PyNumber_Check(field) BEFORE the None branch, and 3.12 added QUOTE_NOTNULL
                    // precisely because this one does not leave a None bare.
                    case 2: mustQuote = !isNumber; break;                               // QUOTE_NONNUMERIC
                    case 3: mustQuote = false; break;                                   // QUOTE_NONE
                    case 4: mustQuote = v.Kind == ValueKind.Str; break;                 // QUOTE_STRINGS (3.12)
                    case 5: mustQuote = v.Kind != ValueKind.None; break;                // QUOTE_NOTNULL (3.12)
                    default:
                        mustQuote = s.IndexOf(_d.Delimiter) >= 0 || s.IndexOf(_d.QuoteChar) >= 0
                            || s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0;
                        break;
                }
                if (!mustQuote)
                {
                    if (NeedsQuoting(s))
                    {
                        if (_d.EscapeChar == '\0')
                            throw Raise.Make(ctx, PyExceptionTypes.CsvError, "need to escape, but no escapechar set");
                        return Escaped(s);   // QUOTE_NONE with an escapechar: escape instead of quoting
                    }
                    return s;
                }
                var sb = new StringBuilder(s.Length + 2);
                sb.Append(_d.QuoteChar);
                foreach (char c in s)
                {
                    if (c == _d.QuoteChar)
                    {
                        // doublequote=False needs an escapechar to protect the quote
                        if (!_d.DoubleQuote)
                        {
                            if (_d.EscapeChar == '\0')
                                throw Raise.Make(ctx, PyExceptionTypes.CsvError, "need to escape, but no escapechar set");
                            sb.Append(_d.EscapeChar);
                        }
                        else
                            sb.Append(_d.QuoteChar);
                    }
                    sb.Append(c);
                }
                sb.Append(_d.QuoteChar);
                return sb.ToString();
            }

            private bool NeedsQuoting(string s)
            {
                return s.IndexOf(_d.Delimiter) >= 0 || s.IndexOf(_d.QuoteChar) >= 0
                    || s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0
                    || (_d.EscapeChar != '\0' && s.IndexOf(_d.EscapeChar) >= 0);
            }

            private string Escaped(string s)
            {
                var sb = new StringBuilder(s.Length + 4);
                foreach (char c in s)
                {
                    if (c == _d.Delimiter || c == _d.QuoteChar || c == _d.EscapeChar || c == '\r' || c == '\n')
                        sb.Append(_d.EscapeChar);
                    sb.Append(c);
                }
                return sb.ToString();
            }

            private ScriptValue WriteRow(EvalContext ctx, ScriptValue seq)
            {
                var sb = new StringBuilder();
                IScriptIterator it = PyOps.GetIterator(seq, ctx);
                ScriptValue v;
                bool first = true;
                while (it.MoveNext(ctx, out v))
                {
                    if (!first)
                        sb.Append(_d.Delimiter);
                    first = false;
                    sb.Append(FormatField(ctx, v));
                }
                sb.Append(_d.LineTerminator);
                return Emit(ctx, sb.ToString());
            }

            private ScriptValue WriteDictRow(EvalContext ctx, ScriptValue rowVal)
            {
                DictValue row = rowVal as DictValue;
                if (row == null)
                    throw Raise.TypeError(ctx, "DictWriter.writerow() argument must be a dict, not " + rowVal.PyTypeName);
                // extra keys not in fieldnames -> ValueError, unless extrasaction='ignore'
                if (_raiseOnExtras)
                {
                    List<string> wrong = null;
                    var it = new DictKeyIterator(row.Table);
                    ScriptValue k;
                    while (it.MoveNext(ctx, out k))
                    {
                        bool found = false;
                        foreach (ScriptValue f in _fieldnames)
                        {
                            if (PyOps.Equals(k, f, ctx, 0))
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                            (wrong ?? (wrong = new List<string>())).Add(k.Repr(ctx));
                    }
                    if (wrong != null)
                        throw Raise.ValueError(ctx, "dict contains fields not in fieldnames: " + string.Join(", ", wrong));
                }
                ScriptValue restVal = _restVal ?? ctx.Values.Str("");
                ListValue vals = ctx.Values.List(_fieldnames.Count);
                foreach (ScriptValue f in _fieldnames)
                {
                    ScriptValue v;
                    vals.Add(row.TryGet(f, ctx, out v) ? v : restVal, ctx);
                }
                return WriteRow(ctx, vals);
            }

            private static IDictionary<string, SlotDescriptor> BuildWriterSlots()
            {
                var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
                s["writerow"] = SlotDescriptor.MakeMethod("writerow", (self, a, kw, ctx) =>
                {
                    CsvWriterValue w = (CsvWriterValue)self;
                    Args.Exactly(ctx, a, "writerow", 1);
                    return w._fieldnames != null ? w.WriteDictRow(ctx, a[0]) : w.WriteRow(ctx, a[0]);
                });
                s["writerows"] = SlotDescriptor.MakeMethod("writerows", (self, a, kw, ctx) =>
                {
                    CsvWriterValue w = (CsvWriterValue)self;
                    Args.Exactly(ctx, a, "writerows", 1);
                    IScriptIterator it = PyOps.GetIterator(a[0], ctx);
                    ScriptValue row;
                    while (it.MoveNext(ctx, out row))
                    {
                        if (w._fieldnames != null)
                            w.WriteDictRow(ctx, row);
                        else
                            w.WriteRow(ctx, row);
                    }
                    return ctx.Values.None;
                });
                s["writeheader"] = SlotDescriptor.MakeMethod("writeheader", (self, a, kw, ctx) =>
                {
                    CsvWriterValue w = (CsvWriterValue)self;
                    if (w._fieldnames == null)
                        throw Raise.Attribute(ctx, "csv.writer", "writeheader");
                    ListValue names = ctx.Values.List(w._fieldnames.Count);
                    foreach (ScriptValue f in w._fieldnames)
                        names.Add(f, ctx);
                    return w.WriteRow(ctx, names);
                });
                s["dialect"] = SlotDescriptor.MakeProperty("dialect", (self, ctx) =>
                    new DialectValue(((CsvWriterValue)self)._d));
                // The three a DictWriter was built with; a plain writer has none of them, as in CPython.
                s["fieldnames"] = SlotDescriptor.MakeProperty("fieldnames", (self, ctx) =>
                {
                    CsvWriterValue w = (CsvWriterValue)self;
                    if (w._fieldnames == null)
                        throw Raise.Attribute(ctx, "csv.writer", "fieldnames");
                    ListValue names = ctx.Values.List(w._fieldnames.Count);
                    foreach (ScriptValue f in w._fieldnames)
                        names.Add(f, ctx);
                    return names;
                });
                s["restval"] = SlotDescriptor.MakeProperty("restval", (self, ctx) =>
                {
                    CsvWriterValue w = (CsvWriterValue)self;
                    if (w._fieldnames == null)
                        throw Raise.Attribute(ctx, "csv.writer", "restval");
                    return w._restVal ?? ctx.Values.Str("");
                });
                s["extrasaction"] = SlotDescriptor.MakeProperty("extrasaction", (self, ctx) =>
                {
                    CsvWriterValue w = (CsvWriterValue)self;
                    if (w._fieldnames == null)
                        throw Raise.Attribute(ctx, "csv.writer", "extrasaction");
                    return ctx.Values.Str(w._raiseOnExtras ? "raise" : "ignore");
                });
                return s;
            }
        }
    }

    // io: StringIO only — enough for csv writers and text-buffer patterns.
    public static class IoModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["StringIO"] = ctx.Values.Type("StringIO", (self, a, kw, c) =>
            {
                string initial = "";
                if (a.Length >= 1 && a[0].Kind != ValueKind.None)
                {
                    StrValue s = a[0] as StrValue;
                    if (s == null)
                        throw Raise.TypeError(c, "initial_value must be str or None, not " + a[0].PyTypeName);
                    initial = s.Value;
                }
                return new StringIOValue(c, initial);
            }, null, null);
            return ctx.Values.Module("io", m);
        }
    }
}
