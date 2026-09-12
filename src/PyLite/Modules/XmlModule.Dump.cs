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
    public static partial class XmlModule
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
                    textOnly.Append(((StrValue)root.Tail).Value);
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
                DumpFrame f = stack.Pop();
                ElementValue e = f.El;
                if (!f.Close)
                {
                    // A comment or a processing instruction: its tag is the factory, its text is written
                    // verbatim (CPython escapes neither), and it has no attributes or children.
                    if (ElementXPath.TagText(e.Tag) == null)
                    {
                        string body = e.Text.Kind == ValueKind.None ? "" : ((StrValue)e.Text).Value;
                        if (ReferenceEquals(e.Tag, ProcessingInstructionFn))
                            sb.Append("<?").Append(body).Append("?>");
                        else
                            sb.Append("<!--").Append(body).Append("-->");
                        AppendTail(sb, e);
                        continue;
                    }
                    sb.Append('<').Append(QName(ctx, e.Tag, nsmap));
                    if (!rootDone)
                    {
                        AppendXmlnsDeclarations(sb, nsmap);
                        rootDone = true;
                    }
                    AppendAttrs(ctx, sb, e, nsmap);
                    bool hasText = e.Text.Kind != ValueKind.None;
                    string plain = ElementXPath.TagText(e.Tag);
                    bool html = method == SerializeMethod.Html;
                    if (html && HtmlVoid.Contains(LocalName(plain)))
                    {
                        sb.Append('>');            // <br>, never closed and never self-closed
                        AppendTail(sb, e);
                        continue;
                    }
                    if (e.Children.Count == 0 && !hasText && shortEmpty && !html)
                    {
                        sb.Append(" />");
                        AppendTail(sb, e);
                    }
                    else
                    {
                        sb.Append('>');
                        if (hasText)
                        {
                            string body = ((StrValue)e.Text).Value;
                            sb.Append(html && HtmlRaw.Contains(LocalName(plain)) ? body : EscapeText(body));
                        }
                        stack.Push(new DumpFrame(e, true));
                        for (int i = e.Children.Count - 1; i >= 0; i--)
                            stack.Push(new DumpFrame((ElementValue)e.Children[i], false));
                    }
                }
                else
                {
                    sb.Append("</").Append(QName(ctx, e.Tag, nsmap)).Append('>');
                    AppendTail(sb, e);
                }
            }
            return Encode(ctx, sb.ToString(), enc, asBytes, codec, asciiOnly, declare, "utf-8");
        }

        // The tag without its {uri}, which is what the HTML rules key on.
        private static string LocalName(string tag)
        {
            if (tag == null)
                return "";
            int close = tag.IndexOf('}');
            return close >= 0 ? tag.Substring(close + 1) : tag;
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

        private static void AppendAttrs(EvalContext ctx, StringBuilder sb, ElementValue e, Dictionary<string, string> nsmap)
        {
            var t = e.Attrib.Table;
            for (int p = 0; p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (!t.TryGetEntryAt(p, out h, out k, out v))
                    continue;
                sb.Append(' ').Append(QName(ctx, k, nsmap)).Append("=\"").Append(EscapeAttr(v.Str(ctx))).Append('"');
            }
        }

        private static void AppendTail(StringBuilder sb, ElementValue e)
        {
            if (e.Tail.Kind != ValueKind.None)
                sb.Append(EscapeText(((StrValue)e.Tail).Value));
        }

        // Collect all namespace URIs (document order DFS), assign registered or ns0/ns1 prefixes.
        private static Dictionary<string, string> BuildNamespaceMap(EvalContext ctx, ElementValue root)
        {
            var uris = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<ElementValue>();
            stack.Push(root);
            var order = new List<ElementValue>();
            while (stack.Count > 0)
            {
                ElementValue e = stack.Pop();
                order.Add(e);
                for (int i = e.Children.Count - 1; i >= 0; i--)
                    stack.Push((ElementValue)e.Children[i]);
            }
            foreach (ElementValue e in order)
            {
                string tagText = ElementXPath.TagText(e.Tag);
                if (tagText != null)
                    CollectUri(uris, seen, tagText);
                var t = e.Attrib.Table;
                for (int p = 0; p < t.EntriesUsed; p++)
                {
                    long h;
                    ScriptValue k, v;
                    if (t.TryGetEntryAt(p, out h, out k, out v))
                        CollectUri(uris, seen, ((StrValue)k).Value);
                }
            }
            var nsmap = new Dictionary<string, string>(StringComparer.Ordinal);
            int counter = 0;
            Dictionary<string, string> registered = ctx.XmlUriToPrefix;
            foreach (string uri in uris)
            {
                string prefix;
                if (registered != null && registered.TryGetValue(uri, out prefix))
                    nsmap[uri] = prefix;
                else
                    nsmap[uri] = "ns" + counter++;
            }
            return nsmap;
        }

        private static void CollectUri(List<string> uris, HashSet<string> seen, string tag)
        {
            if (tag.Length > 0 && tag[0] == '{')
            {
                int end = tag.IndexOf('}');
                if (end > 0)
                {
                    string uri = tag.Substring(1, end - 1);
                    if (seen.Add(uri))
                        uris.Add(uri);
                }
            }
        }

        private static void AppendXmlnsDeclarations(StringBuilder sb, Dictionary<string, string> nsmap)
        {
            // Emit on the root, sorted by prefix for determinism.
            var items = new List<KeyValuePair<string, string>>(nsmap);
            items.Sort((x, y) => string.CompareOrdinal(x.Value, y.Value));
            foreach (KeyValuePair<string, string> kv in items)
                sb.Append(" xmlns:").Append(kv.Value).Append("=\"").Append(EscapeAttr(kv.Key)).Append('"');
        }

        // Clark {uri}local -> prefix:local; plain name stays as-is.
        private static string QName(EvalContext ctx, ScriptValue tag, Dictionary<string, string> nsmap)
        {
            string t = ((StrValue)tag).Value;
            if (t.Length > 0 && t[0] == '{')
            {
                int end = t.IndexOf('}');
                if (end > 0)
                {
                    string uri = t.Substring(1, end - 1);
                    string local = t.Substring(end + 1);
                    string prefix;
                    if (nsmap.TryGetValue(uri, out prefix))
                        return prefix + ":" + local;
                    return local;
                }
            }
            return t;
        }

        private static string EscapeText(string s)
        {
            var sb = new StringBuilder(s.Length);
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
            return sb.ToString();
        }

        private static string EscapeAttr(string s)
        {
            var sb = new StringBuilder(s.Length);
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
            return sb.ToString();
        }

        private static ScriptValue RegisterNamespace(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "register_namespace", 2);
            string prefix = AsStr(ctx, args[0]);
            string uri = AsStr(ctx, args[1]);
            if (prefix.StartsWith("xml", StringComparison.Ordinal))
                throw Raise.ValueError(ctx, "Prefix format reserved for internal use");
            if (ctx.XmlUriToPrefix == null)
                ctx.XmlUriToPrefix = new Dictionary<string, string>(StringComparer.Ordinal);
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
