using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // xml tostring: our own writer over an explicit stack; attributes in insertion order
    // (3.8+). Namespaced Clark tags render with generated ns0/ns1 prefixes honoring
    // register_namespace. Only encoding='unicode' yields str; the default (us-ascii) and utf-8 give bytes.
    internal static partial class XmlModule
    {
        private struct DumpFrame
        {
            public ElementValue El;
            public bool Close;
            public DumpFrame(ElementValue el, bool close) { El = el; Close = close; }
        }

        private static ScriptValue ToStringFn(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length < 1)
                throw Raise.TypeError(ctx, "tostring() missing required argument 'element'");
            ElementValue root = args[0] as ElementValue;
            if (root == null)
                throw Raise.TypeError(ctx, "tostring() argument must be an Element, not " + args[0].PyTypeName);
            string enc = "us-ascii";   // CPython's default: bytes, non-ASCII as character references
            if (args.Length >= 2 && args[1].Kind != ValueKind.None)
                enc = AsStr(ctx, args[1]);
            // Every keyword is read, and an unknown one is refused: a serializer option that is accepted
            // and dropped is a wrong document with no signal.
            KwReader.RejectInvalidExcept(ctx, kw, "tostring",
                new[] { "encoding", "xml_declaration", "method", "short_empty_elements", "default_namespace" });
            ScriptValue ekw;
            if (kw.TryGet("encoding", out ekw) && ekw.Kind != ValueKind.None)
                enc = AsStr(ctx, ekw);
            bool? declareOpt = null;
            ScriptValue dkw;
            if (kw.TryGet("xml_declaration", out dkw) && dkw.Kind != ValueKind.None)
                declareOpt = dkw.IsTruthy(ctx);
            ScriptValue nskw;
            if (kw.TryGet("default_namespace", out nskw) && nskw.Kind != ValueKind.None)
                throw Raise.TypeError(ctx, "tostring(default_namespace=...) is not supported in this dialect");
            bool shortEmpty = true;
            ScriptValue skw;
            if (kw.TryGet("short_empty_elements", out skw) && skw.Kind != ValueKind.None)
                shortEmpty = skw.IsTruthy(ctx);
            return Serialize(ctx, root, enc, declareOpt, Method(ctx, kw), shortEmpty);
        }

        internal enum SerializeMethod { Xml, Html, Text }

        internal static SerializeMethod Method(EvalContext ctx, KwArgs kw)
        {
            ScriptValue v;
            if (!kw.TryGet("method", out v) || v.Kind == ValueKind.None)
                return SerializeMethod.Xml;
            StrValue s = v as StrValue;
            switch (s == null ? null : s.Value)
            {
                case "xml": return SerializeMethod.Xml;
                case "html": return SerializeMethod.Html;
                case "text": return SerializeMethod.Text;
                default: throw Raise.ValueError(ctx, "unknown method " + v.Repr(ctx));
            }
        }

        // 'unicode' -> str. Any other name is a codec and yields bytes; utf-8 and us-ascii get no XML
        // declaration (CPython omits it for exactly these two), every other codec gets one and has its
        // unencodable characters written as character references, as the CPython serializer does.
        // xmlDeclaration True/False forces the declaration either way (ElementTree.write shares this).
        internal static ScriptValue Serialize(EvalContext ctx, ElementValue root, string enc, bool? xmlDeclaration)
        {
            return Serialize(ctx, root, enc, xmlDeclaration, SerializeMethod.Xml, true);
        }

        // HTML void elements: written open, never closed and never self-closed (CPython's HTML_EMPTY).
        private static readonly HashSet<string> HtmlVoid = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "basefont", "br", "col", "embed", "frame", "hr", "img", "input",
            "isindex", "link", "meta", "param", "source", "track", "wbr",
        };

        // HTML elements whose content is CDATA: written verbatim, not escaped.
        private static readonly HashSet<string> HtmlRaw = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style",
        };

        internal static ScriptValue Serialize(EvalContext ctx, ElementValue root, string enc, bool? xmlDeclaration,
            SerializeMethod method, bool shortEmpty)
        {
            bool asBytes = !string.Equals(enc, "unicode", StringComparison.Ordinal);
            string codec = asBytes ? BytesCodec.Normalize(ctx, ctx.Values.Str(enc)) : null;
            bool asciiOnly = codec == "ascii";
            bool declare = xmlDeclaration ?? (asBytes && codec != "utf-8" && !asciiOnly);
            if (declare && !asBytes)
                enc = "utf-8";   // CPython declares the locale's preferred encoding for a str result; utf-8 here

            // method='text' writes no markup at all: the complete text content plus this element's tail,
            // which is what CPython's _serialize_text does.
            if (method == SerializeMethod.Text)
            {
                var textOnly = new StringBuilder();
                ElementXPath.AppendText(ctx, root, textOnly);
                if (root.Tail.Kind != ValueKind.None)
                    textOnly.Append(SerialStr(ctx, root.Tail));
                return Encode(ctx, textOnly.ToString(), enc, asBytes, codec, asciiOnly, false, null);
            }

            Dictionary<string, string> nsmap = BuildNamespaceMap(ctx, root);
            var sb = new StringBuilder();
            var stack = new Stack<DumpFrame>();
            stack.Push(new DumpFrame(root, false));
            bool rootDone = false;
            while (stack.Count > 0)
            {
                ctx.Budget.Step();
                ctx.Values.CheckStrLen(sb.Length);   // capped as it grows: one large text may be shared by many elements
                DumpFrame f = stack.Pop();
                ElementValue e = f.El;
                if (!f.Close)
                {
                    // A comment or a processing instruction: its tag is the factory, its text is written
                    // verbatim (CPython escapes neither), and it has no attributes or children.
                    if (e.Tag.Kind == ValueKind.None)
                    {
                        // Tag None: only what it holds is written.
                        if (PyOps.Truth(e.Text, ctx))
                            AppendText(ctx, sb, SerialStr(ctx, e.Text));
                        stack.Push(new DumpFrame(e, true));
                        for (int i = e.Children.Count - 1; i >= 0; i--)
                            stack.Push(new DumpFrame((ElementValue)e.Children[i], false));
                        continue;
                    }
                    if (ElementXPath.TagText(e.Tag) == null)
                    {
                        bool isPi = ReferenceEquals(e.Tag, ProcessingInstructionFn);
                        sb.Append(isPi ? "<?" : "<!--");
                        if (method == SerializeMethod.Html)   // "<!--%s-->" % _escape_cdata(text); xml writes it as it is
                            AppendText(ctx, sb, SerialStr(ctx, e.Text));
                        else
                            sb.Append(e.Text.Str(ctx));
                        sb.Append(isPi ? "?>" : "-->");
                        AppendTail(ctx, sb, e);
                        continue;
                    }
                    sb.Append('<').Append(QName(ctx, e.Tag, nsmap));
                    if (!rootDone)
                    {
                        AppendXmlnsDeclarations(ctx, sb, nsmap);
                        rootDone = true;
                    }
                    bool html = method == SerializeMethod.Html;
                    AppendAttrs(ctx, sb, e, nsmap, html);
                    bool hasText = PyOps.Truth(e.Text, ctx);
                    string written = QName(ctx, e.Tag, nsmap);   // the HTML rules key on it, prefix included
                    if (e.Children.Count == 0 && !hasText && shortEmpty && !html)

                    {
                        sb.Append(" />");
                        AppendTail(ctx, sb, e);
                    }
                    else
                    {
                        sb.Append('>');
                        if (hasText)
                        {
                            string body = SerialStr(ctx, e.Text);
                            if (html && HtmlRaw.Contains(written))
                                sb.Append(body);
                            else
                                AppendText(ctx, sb, body);
                        }
                        stack.Push(new DumpFrame(e, true));
                        for (int i = e.Children.Count - 1; i >= 0; i--)
                            stack.Push(new DumpFrame((ElementValue)e.Children[i], false));
                    }
                }
                else
                {
                    if (e.Tag.Kind != ValueKind.None)
                    {
                        string closing = QName(ctx, e.Tag, nsmap);
                        if (method != SerializeMethod.Html || !HtmlVoid.Contains(closing))
                            sb.Append("</").Append(closing).Append('>');
                    }
                    AppendTail(ctx, sb, e);

                }
            }
            return Encode(ctx, sb.ToString(), enc, asBytes, codec, asciiOnly, declare, "utf-8");
        }

        private static ScriptValue Encode(EvalContext ctx, string body, string enc, bool asBytes, string codec,
            bool asciiOnly, bool declare, string declEnc)
        {
            if (!asBytes)
                return ctx.Values.Str(declare ? "<?xml version='1.0' encoding='" + declEnc + "'?>\n" + body : body);
            string text = body;
            if (asciiOnly)
            {
                var ascii = new StringBuilder(text.Length);
                for (int i = 0; i < text.Length; i++)
                {
                    if ((i & 4094) == 4094)   // i steps by 1 or 2 (a surrogate pair): one of the two is hit
                    {
                        ctx.Budget.Step();
                        if (ascii.Length > ctx.Limits.MaxStrChars)
                            ctx.Values.CheckStrLen(ascii.Length);   // a char reference is up to 8 chars of one
                    }
                    char ch = text[i];
                    if (ch < 128)
                        ascii.Append(ch);
                    else if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                        ascii.Append("&#").Append(char.ConvertToUtf32(ch, text[++i])).Append(';');
                    else
                        ascii.Append("&#").Append((int)ch).Append(';');
                }
                text = ascii.ToString();
            }
            if (declare)
                text = "<?xml version='1.0' encoding='" + enc + "'?>\n" + text;
            return ctx.Values.Bytes(BytesCodec.Encode(ctx, text, codec, "xmlcharrefreplace"));
        }

        private static void AppendAttrs(EvalContext ctx, StringBuilder sb, ElementValue e, Dictionary<string, string> nsmap, bool html)
        {
            DictValue attrib = e.AttribOrNull;
            if (attrib == null)
                return;
            var t = attrib.Table;
            for (int p = t.WalkStart(ctx); p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (!t.TryGetEntryAt(p, out h, out k, out v))
                    continue;
                sb.Append(' ').Append(QName(ctx, k, nsmap)).Append("=\"");
                if (html)
                    AppendHtmlAttr(ctx, sb, SerialStr(ctx, v));
                else
                    AppendAttr(ctx, sb, SerialStr(ctx, v));
                sb.Append('"');
            }
        }

        private static void AppendTail
(EvalContext ctx, StringBuilder sb, ElementValue e)
        {
            if (PyOps.Truth(e.Tail, ctx))
                AppendText(ctx, sb, SerialStr(ctx, e.Tail));
        }

        // A text, tail, tag or attribute name the script set to a non-str: CPython's TypeError, not a cast fault.
        private static string SerialStr(EvalContext ctx, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "cannot serialize " + v.Repr(ctx) + " (type " + v.PyTypeName + ")");
            return s.Value;
        }

        // Collect all namespace URIs (document order DFS), assign registered or ns0/ns1 prefixes.
        private static Dictionary<string, string> BuildNamespaceMap(EvalContext ctx, ElementValue root)
        {
            var stack = new Stack<ElementValue>();
            stack.Push(root);
            var order = new List<ElementValue>();
            while (stack.Count > 0)
            {
                ctx.Budget.StepTreeNode();   // a tree that contains itself (a.append(a)) has no end: capped below
                ElementValue e = stack.Pop();
                order.Add(e);
                ctx.Values.EnsureCollectionSize((long)order.Count + stack.Count + e.Children.Count);
                for (int i = e.Children.Count - 1; i >= 0; i--)
                    stack.Push((ElementValue)e.Children[i]);
            }
            var nsmap = new Dictionary<string, string>(StringComparer.Ordinal);
            int declared = 0;
            Dictionary<string, string> registered = ctx.XmlUriToPrefix ?? WellKnownPrefixes;
            foreach (ElementValue e in order)
            {
                string tagText = ElementXPath.TagText(e.Tag);
                if (tagText != null)
                    AddUri(ctx, nsmap, registered, ref declared, tagText);
                else if (e.Tag.Kind != ValueKind.None && !ReferenceEquals(e.Tag, CommentFn) && !ReferenceEquals(e.Tag, ProcessingInstructionFn))
                    SerialStr(ctx, e.Tag);   // e.tag = 5: CPython's _namespaces refuses it before writing
                DictValue attrib = e.AttribOrNull;
                if (attrib == null)
                    continue;
                var t = attrib.Table;
                ctx.Budget.StepTreeNodes(t.EntriesUsed);   // 2,000 attributes on 2M shared copies walked 87 s on no step
                for (int p = t.WalkStart(ctx); p < t.EntriesUsed; p++)
                {
                    long h;
                    ScriptValue k, v;
                    if (t.TryGetEntryAt(p, out h, out k, out v))
                        AddUri(ctx, nsmap, registered, ref declared, SerialStr(ctx, k));
                }
            }
            return nsmap;
        }

        // _namespaces.add_qname: a prefix in the map, or ns<how many are declared so far>. The xml prefix is never declared.
        private static void AddUri(EvalContext ctx, Dictionary<string, string> nsmap, Dictionary<string, string> registered, ref int declared, string tag)
        {
            if (tag.Length == 0 || tag[0] != '{')
                return;
            // Scanned, then the uri copied and hashed, per element: a 4M-char tag on 600 elements leaked the runner.
            // By the character, not a step a name: that was 2.4 times the steps of a namespaced tree (review 12, row 19).
            ctx.Budget.ChargeChars(tag.Length);
            int end = tag.IndexOf('}');
            if (end <= 0)
                return;
            string uri = tag.Substring(1, end - 1), prefix;
            if (nsmap.ContainsKey(uri))
                return;
            if (!registered.TryGetValue(uri, out prefix))
                prefix = "ns" + declared.ToString(System.Globalization.CultureInfo.InvariantCulture);
            nsmap[uri] = prefix;
            if (prefix != "xml")
                declared++;
        }

        private static readonly Dictionary<string, string> WellKnownPrefixes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["http://www.w3.org/XML/1998/namespace"] = "xml",
            ["http://www.w3.org/1999/xhtml"] = "html",
            ["http://www.w3.org/1999/02/22-rdf-syntax-ns#"] = "rdf",
            ["http://schemas.xmlsoap.org/wsdl/"] = "wsdl",
            ["http://www.w3.org/2001/XMLSchema"] = "xs",
            ["http://www.w3.org/2001/XMLSchema-instance"] = "xsi",
            ["http://purl.org/dc/elements/1.1/"] = "dc",
        };

        private static void AppendXmlnsDeclarations(EvalContext ctx, StringBuilder sb, Dictionary<string, string> nsmap)
        {
            // Emit on the root, sorted by prefix for determinism.
            var items = new List<KeyValuePair<string, string>>(nsmap);
            items.Sort((x, y) => string.CompareOrdinal(x.Value, y.Value));
            foreach (KeyValuePair<string, string> kv in items)
            {
                if (kv.Value == "xml")
                    continue;
                sb.Append(kv.Value.Length == 0 ? " xmlns" : " xmlns:").Append(kv.Value).Append("=\"");
                AppendAttr(ctx, sb, kv.Key);
                sb.Append('"');
            }
        }

        // Clark {uri}local -> prefix:local; plain name stays as-is.
        private static string QName(EvalContext ctx, ScriptValue tag, Dictionary<string, string> nsmap)
        {
            string t = SerialStr(ctx, tag);
            if (t.Length > 0 && t[0] == '{')
            {
                ctx.Budget.ChargeChars(t.Length);   // three per element, each scanning for the brace and copying the uri and the local name
                int end = t.IndexOf('}');
                if (end > 0)
                {
                    string uri = t.Substring(1, end - 1);
                    string local = t.Substring(end + 1);
                    string prefix;
                    if (nsmap.TryGetValue(uri, out prefix) && prefix.Length > 0)
                        return prefix + ":" + local;
                    return local;
                }
            }
            return t;
        }

        // Escaped straight into the document: no builder and string per text, and a text with nothing
        // to escape is appended whole.
        private static void AppendText(EvalContext ctx, StringBuilder sb, string s)
        {
            if (s.IndexOfAny(TextSpecials) < 0)
            {
                ctx.Values.CheckStrLen((long)sb.Length + s.Length);
                sb.Append(s);
                return;
            }
            CheckEscaped(ctx, sb, s, false);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    default: sb.Append(ch); break;
                }
            }
        }

        private static void AppendAttr(EvalContext ctx, StringBuilder sb, string s)
        {
            if (s.IndexOfAny(AttrSpecials) < 0)
            {
                ctx.Values.CheckStrLen((long)sb.Length + s.Length);
                sb.Append(s);
                return;
            }
            CheckEscaped(ctx, sb, s, true);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\n': sb.Append("&#10;"); break;
                    case '\t': sb.Append("&#09;"); break;
                    case '\r': sb.Append("&#13;"); break;
                    default: sb.Append(ch); break;
                }
            }
        }

        // _escape_attrib_html: a < and the line ends stay as they are.
        private static void AppendHtmlAttr(EvalContext ctx, StringBuilder sb, string s)
        {
            CheckEscaped(ctx, sb, s, true);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(ch); break;
                }
            }
        }

        // The escaped length, measured before the append: a 15M-char attribute of quotes became 90M chars
        // before the document's length was next checked.
        // The escaped length is counted only when the worst case (every char escaped) could pass the cap.
        private static void CheckEscaped(EvalContext ctx, StringBuilder sb, string s, bool attr)
        {
            if (sb.Length + (attr ? 6L : 5L) * s.Length > ctx.Limits.MaxStrChars)
                ctx.Values.CheckStrLen(sb.Length + EscapedLength(s, attr));
        }

        private static long EscapedLength(string s, bool attr)
        {
            long n = s.Length;
            foreach (char ch in s)
            {
                if (ch == '&')
                    n += 4;
                else if (ch == '<' || ch == '>')
                    n += 3;
                else if (attr && ch == '"')
                    n += 5;
                else if (attr && (ch == '\n' || ch == '\t' || ch == '\r'))
                    n += 4;
            }
            return n;
        }

        private static readonly char[] TextSpecials = { '&', '<', '>' };
        private static readonly char[] AttrSpecials = { '&', '<', '>', '"', '\n', '\t', '\r' };

        // re.match(r"ns\d+$", prefix)
        private static bool IsGeneratedPrefix(string prefix)
        {
            int end = prefix.EndsWith("\n", StringComparison.Ordinal) ? prefix.Length - 1 : prefix.Length;
            if (end < 3 || !prefix.StartsWith("ns", StringComparison.Ordinal))
                return false;
            for (int i = 2; i < end; i++)
            {
                if (!char.IsDigit(prefix[i]))
                    return false;
            }
            return true;
        }

        private static ScriptValue RegisterNamespace(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "register_namespace", 2);
            string prefix = AsStr(ctx, args[0]);
            string uri = AsStr(ctx, args[1]);
            if (IsGeneratedPrefix(prefix))
                throw Raise.ValueError(ctx, "Prefix format reserved for internal use");
            if (ctx.XmlUriToPrefix == null)
                ctx.XmlUriToPrefix = new Dictionary<string, string>(WellKnownPrefixes, StringComparer.Ordinal);

            // Overwrite this uri's prefix; drop any other uri already mapped to this prefix (_namespace_map parity).
            var toRemove = new List<string>();
            foreach (KeyValuePair<string, string> kv in ctx.XmlUriToPrefix)
                if (string.Equals(kv.Value, prefix, StringComparison.Ordinal))
                    toRemove.Add(kv.Key);
            foreach (string u in toRemove)
                ctx.XmlUriToPrefix.Remove(u);
            ctx.XmlUriToPrefix[uri] = prefix;
            return ctx.Values.None;
        }
    }
}
