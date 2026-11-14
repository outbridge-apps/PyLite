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
    // Reading a document from a source object, QName, and the two list-shaped entry points.
    // There are no files in the sandbox, so a "source" is anything with a read()
    // method — io.StringIO is the one the engine ships — and a filename is refused rather than guessed at.
    internal static partial class XmlModule
    {
        internal static void AddSources(Dictionary<string, ScriptValue> m, EvalContext ctx)
        {
            m["QName"] = ctx.Values.EngineType("xml.etree.ElementTree.QName", QNameCtor, QNameValue.QNameType, null);
            m["fromstringlist"] = BuiltinFunctionValue.Make("fromstringlist", Args.LeadKeywords("fromstringlist", FromStringList, 1, "sequence", "parser"));
            m["parse"] = BuiltinFunctionValue.Make("parse", Args.LeadKeywords("parse", ParseSource, 1, "source", "parser"));
            m["iterparse"] = BuiltinFunctionValue.Make("iterparse", IterParse);
            m["XMLID"] = BuiltinFunctionValue.Make("XMLID", Args.LeadKeywords("XMLID", XmlId, 1, "text", "parser"));
        }

        // XMLID(text) -> (root, {id_value: element}) over every element carrying an `id` attribute.
        private static ScriptValue XmlId(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "XMLID", 1, 2);
            RefuseParser(ctx, a.Length > 1 ? a[1] : null, kw, "XMLID");
            var root = (ElementValue)FromString(self, new[] { a[0] }, KwArgs.Empty, ctx);
            DictValue ids = ctx.Values.Dict(8);
            var stack = new Stack<ElementValue>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                ctx.Budget.Step();
                ElementValue e = stack.Pop();
                ScriptValue key;
                if (e.AttribOrNull != null && e.AttribOrNull.TryGet(ctx.Values.Str("id"), ctx, out key))
                    ids.SetItem(key, e, ctx);
                for (int i = e.Children.Count - 1; i >= 0; i--)
                    stack.Push((ElementValue)e.Children[i]);
            }
            return ctx.Values.Tuple(new ScriptValue[] { root, ids });
        }

        // ElementTree.parse(source): reads the source and becomes that tree, returning its root — the
        // method form of the module function above, and CPython's own contract for it.
        internal static ScriptValue TreeParse(EvalContext ctx, ElementTreeValue tree, ScriptValue[] a, KwArgs kw)
        {
            Args.Between(ctx, a, "parse", 1, 2);
            RefuseParser(ctx, a.Length > 1 ? a[1] : null, kw, "parse");
            ElementValue root = Parse(ctx, ReadSource(ctx, a[0], "parse"));
            tree.Root = root;
            return root;
        }

        // QName(text_or_uri, tag=None): tag given => '{uri}tag', else the text as written. It carries the
        // text and nothing else, which is what makes it usable as a tag, an attribute name or a dict key.
        private static ScriptValue QNameCtor(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, kw, "QName", 1, 2);
            string head = QNamePart(ctx, a[0]);
            ctx.Values.ChargeObject(1);
            if (a.Length == 1)
                return new QNameValue(ctx.Values.Str(head));
            return new QNameValue(ctx.Values.Str("{" + head + "}" + QNamePart(ctx, a[1])));
        }

        private static string QNamePart(EvalContext ctx, ScriptValue v)
        {
            QNameValue q = v as QNameValue;
            if (q != null)
                return q.Text.Value;
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "QName() argument must be str, not " + v.PyTypeName);
            return s.Value;
        }

        // A tag or attribute name written as a QName is stored as its text, so everything downstream —
        // comparison, find(), serialization — sees the plain '{uri}local' string it already understands.
        internal static ScriptValue UnQName(ScriptValue v)
        {
            QNameValue q = v as QNameValue;
            return q != null ? q.Text : v;
        }

        private static ScriptValue FromStringList(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "fromstringlist", 1, 2);
            RefuseParser(ctx, a.Length > 1 ? a[1] : null, kw, "fromstringlist");
            var sb = new StringBuilder();
            IScriptIterator it = PyOps.GetIterator(a[0], ctx);
            ScriptValue chunk;
            while (it.MoveNext(ctx, out chunk))
            {
                StrValue s = chunk as StrValue;
                if (s == null)
                    throw Raise.TypeError(ctx, "fromstringlist() items must be str, not " + chunk.PyTypeName);
                ctx.Values.PreCharge(2L * s.Value.Length);
                sb.Append(s.Value);
            }
            return Parse(ctx, sb.ToString());
        }

        // parse(source) -> ElementTree. `source` is an object with read(); a filename cannot be one,
        // since the sandbox has no filesystem, and saying so beats parsing the name as a document.
        private static ScriptValue ParseSource(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "parse", 1, 2);
            RefuseParser(ctx, a.Length > 1 ? a[1] : null, kw, "parse");
            ElementValue root = Parse(ctx, ReadSource(ctx, a[0], "parse"));
            ctx.Values.ChargeObject(1);
            return new ElementTreeValue(root);
        }

        // iterparse(source, events=('end',)) -> an iterator of (event, element).
        //
        // The document is parsed up front and its events are recorded before the first is handed out. That is not
        // incremental, which is the whole point of iterparse in CPython — but it cannot be here anyway: a
        // source is an in-memory object, so the text is already materialised before the first event, and
        // reading it twice would cost more than the tree. The record is CPython's queue of events for a document
        // read in one chunk: a script changing the tree meanwhile (`root.clear()` after each record, the streaming
        // idiom) changes no event.
        private static ScriptValue IterParse(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "iterparse", 1, 2);
            ScriptValue eventsArg = a.Length > 1 ? a[1] : null;
            ScriptValue v;
            if (kw.TryGet("events", out v))
            {
                if (eventsArg != null)
                    throw Raise.TypeError(ctx, "got multiple values for argument 'events'");
                eventsArg = v;
            }
            KwReader.RejectInvalidExcept(ctx, kw, "iterparse", new[] { "events", "parser" });
            RefuseParser(ctx, null, kw, "iterparse");

            bool start = false, end = true;
            if (eventsArg != null && eventsArg.Kind != ValueKind.None)
            {
                start = false;
                end = false;
                IScriptIterator ev = PyOps.GetIterator(eventsArg, ctx);
                ScriptValue e;
                while (ev.MoveNext(ctx, out e))
                {
                    StrValue s = e as StrValue;
                    string name = s == null ? null : s.Value;
                    if (name == "start")
                        start = true;
                    else if (name == "end")
                        end = true;
                    else if (name == "start-ns" || name == "end-ns")
                        throw Raise.ValueError(ctx, "unsupported event '" + name + "' in this dialect");
                    else
                        throw Raise.ValueError(ctx, "unknown event '" + (name ?? e.PyTypeName) + "'");
                }
            }
            ElementValue root = Parse(ctx, ReadSource(ctx, a[0], "iterparse"));
            return ctx.Values.EngineIterator(IterParseIterator.Record(ctx, root, start, end), "xml.etree.ElementTree.iterparse");
        }

        // A byte document read the way expat reads it: a BOM, a zero in the first two bytes (UTF-16), the declaration's
        // encoding= (any codec the engine has, multi-byte ones refused), else UTF-8. Bytes the encoding
        // cannot read are a ParseError at their position, as expat reports an invalid token.
        internal static string DecodeDocument(EvalContext ctx, byte[] d)
        {
            string enc = "utf-8";
            if (d.Length >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF)
            {
                enc = "utf-8-sig";
            }
            else if (d.Length >= 2 && ((d[0] == 0xFF && d[1] == 0xFE) || (d[0] == 0xFE && d[1] == 0xFF)))
            {
                enc = "utf-16";
            }
            else if (d.Length >= 2 && d[0] != 0 && d[1] == 0)
            {
                enc = "utf-16-le";
            }
            else if (d.Length >= 2 && d[0] == 0 && d[1] != 0)
            {
                enc = "utf-16-be";
            }
            else
            {
                int valueAt;
                string declared = DeclaredEncoding(d, out valueAt);
                if (declared != null)
                {
                    if (!IsEncName(declared))
                        throw PositionedError(ctx, "XML declaration not well-formed", 1, valueAt);   // encoding="", " utf-8"
                    string lower = declared.ToLowerInvariant().Replace('_', '-');
                    if (lower == "shift-jis" || lower == "euc-jp" || lower == "gbk" || lower == "gb2312"
                        || lower == "big5" || lower == "euc-kr" || lower == "cp932" || lower == "gb18030"
                        || lower.StartsWith("utf-32", StringComparison.Ordinal) || lower.StartsWith("utf32", StringComparison.Ordinal))
                        throw Raise.ValueError(ctx, "multi-byte encodings are not supported");
                    // a CPython single-byte codec the sandbox lacks decodes as UTF-8, as 1.1.0 read every document: an
                    // ASCII one still parses; any other name is CPython's LookupError
                    enc = !BytesCodec.IsKnown(lower) && IsPythonSingleByte(lower)
                        ? "utf-8" : BytesCodec.Normalize(ctx, ctx.Values.Str(declared));
                    if (enc.StartsWith("utf-16", StringComparison.Ordinal))
                        throw PositionedError(ctx, "encoding specified in XML declaration is incorrect", 1, valueAt);
                    if (enc == "utf-8-sig")
                        enc = "utf-8";
                }
            }
            try
            {
                return BytesCodec.Decode(ctx, d, enc, "strict");
            }
            catch (ScriptException se) when (se.Value.ExcType == PyExceptionTypes.UnicodeDecodeError)
            {
                IntValue start = se.Value.Args.Length > 2 ? se.Value.Args[2] as IntValue : null;
                long at = (start != null && start.IsSmall ? start.Small : 0) + (enc == "utf-8-sig" ? 3 : 0);
                int line = 1;
                long lineStart = 0;
                for (long i = 0; i < at && i < d.Length; i++)
                {
                    if (d[i] == (byte)'\n')
                    {
                        line++;
                        lineStart = i + 1;
                    }
                }
                throw PositionedError(ctx, "not well-formed (invalid token)", line, (int)(at - lineStart));
            }
        }

        // encoding="..." of an ASCII XML declaration at the very start, and where its value begins.
        private static readonly HashSet<string> PythonCodePages = new HashSet<string>(StringComparer.Ordinal)
        {
            "037", "273", "424", "437", "500", "720", "737", "775", "850", "852", "855", "856", "857", "858", "860", "861",
            "862", "863", "864", "865", "869", "874", "875", "1006", "1026", "1125", "1140", "1250", "1253", "1254", "1255",
            "1256", "1257", "1258",
        };

        private static readonly HashSet<string> PythonSingleByteNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "mac-roman", "macroman", "macintosh", "mac-cyrillic", "maccyrillic", "mac-greek", "macgreek", "mac-iceland",
            "maciceland", "mac-latin2", "maclatin2", "maccentraleurope", "mac-centeuro", "mac-turkish", "macturkish",
            "mac-arabic", "mac-farsi", "mac-croatian", "mac-romanian", "koi8-u", "koi8-t", "kz1048", "ptcp154", "pt154",
            "hp-roman8", "roman8", "tis-620", "tis620", "palmos", "greek", "hebrew", "arabic", "thai",
        };

        // lower: lower-cased, `_` as `-`. A codec CPython reads byte by byte: its expat takes it for any document.
        private static bool IsPythonSingleByte(string lower)
        {
            if (PythonSingleByteNames.Contains(lower))
                return true;
            foreach (string prefix in new[] { "iso-8859-", "iso8859-", "iso-8859", "iso8859" })
            {
                if (lower.StartsWith(prefix, StringComparison.Ordinal))
                {
                    int n;
                    return int.TryParse(lower.Substring(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n)
                        && n >= 2 && n <= 16 && n != 12;
                }
            }
            foreach (string prefix in new[] { "latin", "l" })
            {
                int n;
                if (lower.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(lower.Substring(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n))
                    return n >= 2 && n <= 10;
            }
            foreach (string prefix in new[] { "windows-", "cp", "ibm", "" })
            {
                if (lower.StartsWith(prefix, StringComparison.Ordinal) && PythonCodePages.Contains(lower.Substring(prefix.Length)))
                    return true;
            }
            return false;
        }

        // XML's EncName: [A-Za-z] ([A-Za-z0-9._] | '-')*
        private static bool IsEncName(string s)
        {
            if (s.Length == 0 || !((s[0] >= 'A' && s[0] <= 'Z') || (s[0] >= 'a' && s[0] <= 'z')))
                return false;
            foreach (char c in s)
            {
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-'))
                    return false;
            }
            return true;
        }

        private static string DeclaredEncoding(byte[] d, out int valueAt)
        {
            valueAt = 0;
            if (d.Length < 6 || d[0] != '<' || d[1] != '?' || d[2] != 'x' || d[3] != 'm' || d[4] != 'l')
                return null;
            int end = Math.Min(d.Length, 1024);
            var decl = new StringBuilder();
            for (int i = 0; i < end && d[i] < 0x80; i++)
            {
                decl.Append((char)d[i]);
                if (d[i] == '>')
                    break;
            }
            string s = decl.ToString();
            int at = s.IndexOf("encoding", StringComparison.Ordinal);
            if (at < 0)
                return null;
            int p = at + 8;
            while (p < s.Length && (s[p] == ' ' || s[p] == '\t' || s[p] == '\r' || s[p] == '\n'))
                p++;
            if (p >= s.Length || s[p] != '=')
                return null;
            p++;
            while (p < s.Length && (s[p] == ' ' || s[p] == '\t' || s[p] == '\r' || s[p] == '\n'))
                p++;
            if (p >= s.Length || (s[p] != '"' && s[p] != '\''))
                return null;
            int close = s.IndexOf(s[p], p + 1);
            if (close < 0)
                return null;
            valueAt = p + 1;
            return s.Substring(p + 1, close - p - 1);
        }

        private static string ReadSource(EvalContext ctx, ScriptValue source, string who)
        {
            if (source is StrValue || source is BytesValue)
                throw Raise.TypeError(ctx, who + "() takes an object with a read() method, not a filename"
                    + " (there is no filesystem in this environment; use fromstring() for a document you hold)");
            ScriptValue read = PyOps.GetAttr(source, "read", ctx);
            ScriptValue text = ctx.CallHook(read, Array.Empty<ScriptValue>(), KwArgs.Empty);
            StrValue s = text as StrValue;
            if (s != null)
                return s.Value;
            BytesValue b = text as BytesValue;
            if (b != null)
                return DecodeDocument(ctx, b.Data);
            throw Raise.TypeError(ctx, who + "(): read() must return str or bytes, not " + text.PyTypeName);
        }

        // parser= is accepted as None and refused otherwise: XMLParser/TreeBuilder are not in this dialect.
        private static void RefuseParser(EvalContext ctx, ScriptValue positional, KwArgs kw, string who)
        {
            ScriptValue v = positional;
            ScriptValue k;
            if (kw.TryGet("parser", out k))
                v = k;
            if (v != null && v.Kind != ValueKind.None)
                throw Raise.TypeError(ctx, who + "(parser=...) is not supported in this dialect");
        }

        // ('start', el) on the way down and ('end', el) on the way up, in document order: recorded from the fresh tree
        // over an explicit stack (a deep tree costs no C# frames), then handed out whatever the script does to the tree.
        private sealed class IterParseIterator : ScriptIteratorBase
        {
            private const long EventBytes = 9;   // a recorded event: the element and its kind, in arrays of the exact size
            private readonly ElementValue[] _els;
            private readonly bool[] _isEnd;
            private int _next;

            private IterParseIterator(ElementValue[] els, bool[] isEnd)
            {
                _els = els;
                _isEnd = isEnd;
            }

            internal static IterParseIterator Record(EvalContext ctx, ElementValue root, bool start, bool end)
            {
                long count = 0;   // the fresh tree's elements, each in one list
                var todo = new Stack<ElementValue>();
                if (root != null)
                    todo.Push(root);
                while (todo.Count > 0)
                {
                    ctx.Budget.StepTreeNode();
                    ElementValue el = todo.Pop();
                    count++;
                    foreach (ScriptValue c in el.Children)
                        todo.Push((ElementValue)c);
                }
                long events = count * ((start ? 1 : 0) + (end ? 1 : 0));
                ctx.Values.PreCharge(EventBytes * events);
                var els = new ElementValue[events];
                var isEnd = new bool[events];
                int n = 0;
                var stack = new Stack<KeyValuePair<ElementValue, int>>();   // an element, and its next child (-1: not opened)
                if (root != null)
                    stack.Push(new KeyValuePair<ElementValue, int>(root, -1));
                while (stack.Count > 0)
                {
                    KeyValuePair<ElementValue, int> f = stack.Pop();
                    ElementValue el = f.Key;
                    int child = f.Value;
                    if (child < 0)
                    {
                        ctx.Budget.StepTreeNode();
                        if (start)
                            els[n++] = el;
                        child = 0;
                    }
                    if (child < el.Children.Count)
                    {
                        stack.Push(new KeyValuePair<ElementValue, int>(el, child + 1));
                        stack.Push(new KeyValuePair<ElementValue, int>((ElementValue)el.Children[child], -1));
                        continue;
                    }
                    if (end)
                    {
                        isEnd[n] = true;
                        els[n++] = el;
                    }
                }
                return new IterParseIterator(els, isEnd);
            }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                value = null;
                if (_next >= _els.Length)
                    return false;
                ElementValue el = _els[_next];
                bool atEnd = _isEnd[_next];
                _els[_next++] = null;   // a handed-out event holds its element no longer, as CPython's queue
                value = Pair(ctx, atEnd ? "end" : "start", el);
                return true;
            }

            private static ScriptValue Pair(EvalContext ctx, string ev, ElementValue el)
            {
                return ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Str(ev), el });
            }
        }
    }
}
