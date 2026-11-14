using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // xml.etree.ElementTree subset: Element/SubElement/fromstring/XML/tostring/
    // iselement/ParseError. Anti-XXE is hard-wired (DTD prohibited, no resolver). Tree mapping and
    // serialization use explicit stacks (no CLR recursion).
    internal static partial class XmlModule
    {
        private const string XmlnsNs = "http://www.w3.org/2000/xmlns/";

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["Element"] = ctx.Values.EngineType("xml.etree.ElementTree.Element", ElementCtor, null, null);
            m["SubElement"] = BuiltinFunctionValue.Make("SubElement", SubElement);
            m["fromstring"] = BuiltinFunctionValue.Make("fromstring", Args.LeadKeywords("fromstring", FromString, 1, "text", "parser"));
            m["XML"] = BuiltinFunctionValue.Make("XML", Args.LeadKeywords("XML", FromString, 1, "text", "parser"));
            m["tostring"] = BuiltinFunctionValue.Make("tostring", ToStringFn);
            m["indent"] = BuiltinFunctionValue.Make("indent", Args.LeadKeywords("indent", Indent, 1, "tree", "space", "level"));
            m["ElementTree"] = ctx.Values.EngineType("xml.etree.ElementTree.ElementTree", Args.Signature("ElementTree", 2, ElementTreeCtor, "element", "file"), ElementTreeValue.TreeType, null);
            m["iselement"] = BuiltinFunctionValue.Make("iselement", Args.BoundKeywords("iselement", 1, IsElement, "element"));
            m["register_namespace"] = BuiltinFunctionValue.Make("register_namespace", Args.BoundKeywords("register_namespace", 2, RegisterNamespace, "prefix", "uri"));
            m["Comment"] = CommentFn;
            m["ProcessingInstruction"] = ProcessingInstructionFn;
            m["PI"] = ProcessingInstructionFn;
            m["dump"] = BuiltinFunctionValue.Make("dump", Dump);
            m["tostringlist"] = BuiltinFunctionValue.Make("tostringlist", ToStringList);
            m["ParseError"] = ModuleKit.ExceptionClass(PyExceptionTypes.XmlParseError);
            AddSources(m, ctx);
            return ctx.Values.Module("xml.etree.ElementTree", m);
        }

        // ParseError carries `position` and `code` as CPython's does. The reader's sentence is mapped to the
        // expat text and number of the same fault, and the column moved to where expat points (the `&` of a
        // reference, the `<` of a second root). The DTD refusal keeps its own wording: the reader's sentence
        // advises an XmlReaderSettings property, which is addressed to the host, not to the script.
        private static ScriptException ParseFailed(EvalContext ctx, XmlException xe, string text)
        {
            // The reader leaves both at 0 for a failure it spots before positioning (the DTD refusal);
            // a position is meant to point somewhere, so it starts at the first character instead.
            int line = xe.LineNumber < 1 ? 1 : xe.LineNumber;
            int col = xe.LinePosition < 1 ? 0 : xe.LinePosition - 1;
            string m = xe.Message;
            if (m.IndexOf("DTD is prohibited", StringComparison.Ordinal) >= 0)
                return PositionedError(ctx, "DTD is prohibited in this environment", line, col, 0);
            if (m.StartsWith(TooManyAttributes, StringComparison.Ordinal))
                return PositionedError(ctx, m, line, col, 0);   // the sandbox's own limit, in its own words
            int lineAt = LineOffset(text, line);
            int at = Math.Min(lineAt + col, text.Length);
            string what;
            int code;
            if (Has(m, "does not match the end tag"))
            {
                what = "mismatched tag";
                code = 7;
            }
            else if (Has(m, "Root element is missing"))
            {
                return AtEnd(ctx, text, "no element found", 3);
            }
            else if (Has(m, "Unexpected end of file"))
            {
                // the input ends inside a tag (unclosed token at its `<`) or between tags (no element found at the end)
                int lt = text.LastIndexOf('<');
                if (lt >= 0 && text.IndexOf('>', lt) < 0)
                    return PositionedAt(ctx, text, lt, "unclosed token", 5);
                return AtEnd(ctx, text, "no element found", 3);
            }
            else if (Has(m, "Reference to undeclared entity"))
            {
                what = "undefined entity";
                code = 11;
                col = Back(text, lineAt, at, '&') - lineAt;
            }
            else if (Has(m, "duplicate attribute"))
            {
                what = "duplicate attribute";
                code = 8;
            }
            else if (Has(m, "multiple root elements"))
            {
                what = "junk after document element";
                code = 9;
                col = Back(text, lineAt, at, '<') - lineAt;
            }
            else if (Has(m, "invalid character") && at > lineAt && Back(text, lineAt, at, '&') + 1 < text.Length
                && text[Back(text, lineAt, at, '&') + 1] == '#' && text.IndexOf(';', Back(text, lineAt, at, '&')) >= at - 1)
            {
                what = "reference to invalid character number";
                code = 14;
                col = Back(text, lineAt, at, '&') - lineAt;
            }
            else if (Has(m, "Data at the root level is invalid"))
            {
                if (at < text.Length && text[at] == '<')
                {
                    // a tag the reader stopped at: never closed, or closed and then the input ended inside its element
                    if (text.IndexOf('>', at) < 0)
                        return PositionedAt(ctx, text, at, "unclosed token", 5);
                    return AtEnd(ctx, text, "no element found", 3);
                }
                // text outside the root: after an element it is junk, before any it is a syntax error
                bool afterElement = text.LastIndexOf("/>", Math.Max(0, at - 1), StringComparison.Ordinal) >= 0
                    || text.LastIndexOf("</", Math.Max(0, at - 1), StringComparison.Ordinal) >= 0;
                what = afterElement ? "junk after document element" : "syntax error";
                code = afterElement ? 9 : 2;
            }
            else
            {
                what = "not well-formed (invalid token)";
                code = 4;
                if (Has(m, "']]>' is not allowed"))
                    col += 2;   // expat points at the `>`
            }
            return PositionedError(ctx, what, line, col, code);
        }

        // An error at a character offset: its line and column counted as the reader counts them.
        private static ScriptException PositionedAt(EvalContext ctx, string text, int offset, string what, int code)
        {
            int line = 1, start = 0;
            for (int i = 0; i < offset && i < text.Length; i++)
            {
                if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
                {
                    line++;
                    start = i + 1;
                }
            }
            return PositionedError(ctx, what, line, offset - start, code);
        }

        private static ScriptException AtEnd(EvalContext ctx, string text, string what, int code)
        {
            int line = 1, start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
                {
                    line++;
                    start = i + 1;
                }
            }
            return PositionedError(ctx, what, line, text.Length - start, code);
        }

        private static bool Has(string s, string part)
        {
            return s.IndexOf(part, StringComparison.Ordinal) >= 0;
        }

        // The offset of the first character of a 1-based line (CR LF, CR and LF each end one, as for the reader).
        private static int LineOffset(string text, int line)
        {
            int i = 0;
            for (int l = 1; l < line && i < text.Length; i++)
            {
                if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
                    l++;
            }
            return i;
        }

        // The last `c` at or before `at` on the line that starts at lineAt; `at` itself when there is none.
        private static int Back(string text, int lineAt, int at, char c)
        {
            for (int i = Math.Min(at, text.Length - 1); i >= lineAt; i--)
            {
                if (text[i] == c)
                    return i;
            }
            return at;
        }

        private static ScriptException PositionedError(EvalContext ctx, string what, int line, int col)
        {
            return PositionedError(ctx, what, line, col, 4);   // the Sources checks: an encoding or token fault
        }

        private static ScriptException PositionedError(EvalContext ctx, string what, int line, int col, int code)
        {
            ScriptException ex = Raise.Make(ctx, PyExceptionTypes.XmlParseError,
                what + ": line " + line + ", column " + col);
            ex.Value.Data = new Dictionary<string, ScriptValue>(2, StringComparer.Ordinal)
            {
                ["position"] = ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(line), ctx.Values.Int(col) }),
                ["code"] = ctx.Values.Int(code),
            };
            return ex;
        }

        // Element(tag, attrib={}, **extra): attrib is COPIED (not shared with the argument).
        private static ScriptValue ElementCtor(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "Element", 1, 2);
            return BuildElement(ctx, args[0], args.Length == 2 ? args[1] : null, kw, "Element() argument 2");
        }

        // attribArg: the positional attrib, null when not given; `where` names it in the error, as CPython does.
        private static ElementValue BuildElement(EvalContext ctx, ScriptValue tag, ScriptValue attribArg, KwArgs kw, string where)
        {
            DictValue attrib = null;   // none unless there are attributes, as CPython keeps none
            // Element(tag, attrib={...}) spelled by keyword is the dict; after a positional one it is an attribute
            // named 'attrib', as _elementtree merges the keywords into the dict it parsed
            ScriptValue attribKw = null;
            bool attribByName = attribArg == null && kw.TryGet("attrib", out attribKw);
            if (attribByName)
            {
                attribArg = attribKw;
                where = null;
            }
            if (attribArg != null)
            {
                // None is refused too: CPython takes only a dict here
                DictValue src = attribArg as DictValue;
                if (src == null)
                {
                    throw Raise.TypeError(ctx, where == null
                        ? "attrib must be dict, not " + attribArg.PyTypeName
                        : where + " must be dict, not " + (attribArg.Kind == ValueKind.None ? "None" : attribArg.PyTypeName));
                }
                if (src.Count > 0)
                {
                    attrib = ctx.Values.Dict(4);
                    CopyAttribs(ctx, attrib, src);
                }
            }
            for (int j = 0; j < kw.Count; j++)
            {
                if (attribByName && kw.NameAt(j) == "attrib")
                    continue;
                if (attrib == null)
                    attrib = ctx.Values.Dict(4);
                attrib.SetItem(ctx.Values.Str(kw.NameAt(j)), kw.ValueAt(j), ctx);
            }
            return new ElementValue(UnQName(tag), attrib, ctx);   // a QName tag is stored as its text
        }

        private static void CopyAttribs(EvalContext ctx, DictValue dst, DictValue src)
        {
            var t = src.Table;
            for (int p = t.WalkStart(ctx); p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (t.TryGetEntryAt(p, out h, out k, out v))
                    dst.SetItem(k, v, ctx);
            }
        }

        private static ScriptValue SubElement(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "SubElement", 2, 3);
            ElementValue parent = args[0] as ElementValue;
            if (parent == null)
                throw Raise.TypeError(ctx, "SubElement() argument 1 must be an Element");
            ElementValue el = BuildElement(ctx, args[1], args.Length == 3 ? args[2] : null, kw, "SubElement() argument 3");
            parent.Grow(ctx);   // the item cap, as append has it: SubElement used to pass it
            ElementValue.Placing(ctx, el);
            parent.Children.Add(el);
            parent.AppendedLeaf(ctx, el);   // just built: no children, in no list
            return el;
        }

        // ElementTree(element=None): a root holder; the file argument is not taken (no files here).
        private static ScriptValue ElementTreeCtor(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ScriptValue el = args.Length >= 1 ? args[0] : null;
            ScriptValue ekw;
            if (kw.TryGet("element", out ekw))
                el = ekw;
            ScriptValue file;
            if (args.Length >= 2 || (kw.TryGet("file", out file) && file.Kind != ValueKind.None))
                throw Raise.TypeError(ctx, "ElementTree(file=...) is not supported in this dialect");
            ElementValue root = null;
            if (el != null && el.Kind != ValueKind.None)
            {
                root = el as ElementValue;
                if (root == null)
                    throw Raise.TypeError(ctx, "ElementTree() argument 'element' must be an Element, not " + el.PyTypeName);
            }
            ctx.Values.ChargeObject(1);
            return new ElementTreeValue(root);
        }

        // indent(tree, space='  ', level=0): CPython 3.9's pretty-printer, with an explicit stack. A blank
        // text/tail (None or whitespace) becomes the indentation; real text is left alone.
        private static ScriptValue Indent(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "indent", 1, 3);
            ElementValue root = args[0] as ElementValue;
            ElementTreeValue tree = args[0] as ElementTreeValue;
            if (tree != null)
                root = tree.Root;
            if (root == null && tree == null)
                throw Raise.TypeError(ctx, "indent() argument 1 must be an Element or ElementTree, not " + args[0].PyTypeName);
            ScriptValue spaceV = args.Length >= 2 ? args[1] : null;
            ScriptValue levelV = args.Length >= 3 ? args[2] : null;
            ScriptValue v;
            if (kw.TryGet("space", out v))
                spaceV = v;
            if (kw.TryGet("level", out v))
                levelV = v;
            string space = spaceV == null ? "  " : AsStr(ctx, spaceV);
            int level = levelV == null ? 0 : Coerce.ToInt32(ctx, levelV, "level");
            if (level < 0)
                throw Raise.ValueError(ctx, "Initial indentation level must be >= 0");
            if (root == null || root.Children.Count == 0)
                return ctx.Values.None;

            var stack = new Stack<KeyValuePair<ElementValue, int>>();
            stack.Push(new KeyValuePair<ElementValue, int>(root, level));
            while (stack.Count > 0)
            {
                ctx.Budget.Step();
                KeyValuePair<ElementValue, int> item = stack.Pop();
                ElementValue e = item.Key;
                int lvl = item.Value;
                string own = "\n" + Repeat(ctx, space, lvl);
                string child = own + space;
                if (IsBlank(ctx, e.Text))
                    e.Text = ctx.Values.Str(child);
                ElementValue last = null;
                foreach (ScriptValue c in e.Children)
                {
                    ElementValue ce = c as ElementValue;
                    if (ce == null)
                        continue;
                    if (ce.Children.Count > 0)
                        stack.Push(new KeyValuePair<ElementValue, int>(ce, lvl + 1));
                    if (IsBlank(ctx, ce.Tail))
                        ce.Tail = ctx.Values.Str(child);
                    last = ce;
                }
                if (last != null && IsBlank(ctx, last.Tail))
                    last.Tail = ctx.Values.Str(own);
                e.Changed(ctx);
            }
            return ctx.Values.None;
        }

        // `not text.strip()`, charged for what it reads: a blank 4M-char tail on 400 children leaked the runner
        private static bool IsBlank(EvalContext ctx, ScriptValue text)
        {
            StrValue s = text as StrValue;
            if (s == null)
                return true;
            int i = 0;
            while (i < s.Value.Length && PyUnicode.IsPythonSpace(s.Value[i]))
                i++;
            ctx.Budget.ChargeLinear(i);
            return i == s.Value.Length;
        }

        // The indentation of one level: n copies of the space string, sized against the string cap before
        // anything is built (a level of 10**9 would otherwise allocate gigabytes off budget).
        private static string Repeat(EvalContext ctx, string s, int n)
        {
            long total = (long)s.Length * n;
            if (total > ctx.Limits.MaxStrChars)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", ctx.Limits.MaxStrChars, total);
            if (total == 0)
                return "";   // an empty space string: a level of 2e9 was 2e9 appends of nothing, on no step
            ctx.Values.PreCharge(2 * total + 24);
            return new StringBuilder((int)total).Insert(0, s, n).ToString();
        }

        private static ScriptValue IsElement(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "iselement", 1);
            return ctx.Values.Bool(args[0] is ElementValue);
        }

        private static ScriptValue FromString(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "fromstring", 1, 2);
            // CPython's signature is fromstring(text, parser=None): the slot exists, and only a parser
            // object is refused, so a caller passing None explicitly is not turned away for it.
            RefuseParser(ctx, args.Length > 1 ? args[1] : null, kw, "fromstring");
            StrValue s = args[0] as StrValue;
            if (s != null)
                return Parse(ctx, s.Value);
            BytesValue b = args[0] as BytesValue;
            if (b != null)
                return Parse(ctx, DecodeDocument(ctx, b.Data));
            throw Raise.TypeError(ctx, "fromstring() argument must be str or bytes, not " + args[0].PyTypeName);
        }

        // ---- loader ----

        private static ElementValue Parse(EvalContext ctx, string text)
        {
            if (text.Length > 0 && text[0] == '﻿')
                text = text.Substring(1);   // expat skips a leading byte order mark
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                CheckCharacters = true,
                ConformanceLevel = ConformanceLevel.Document,
                CloseInput = true,
                IgnoreProcessingInstructions = true,
                IgnoreComments = true,
                MaxCharactersFromEntities = 1024,
            };
            try
            {
                CheckAttributeCounts(ctx, text);
                using (var sr = new StringReader(text))
                using (var reader = XmlReader.Create(sr, settings))
                {
                    var stack = new Stack<ElementValue>();
                    ElementValue root = null;
                    ElementValue last = null;
                    bool tail = false;
                    var pending = new StringBuilder();

                    while (reader.Read())
                    {
                        ctx.Budget.Step();
                        switch (reader.NodeType)
                        {
                            case XmlNodeType.Element:
                                Flush(ctx, ref last, tail, pending);
                                ElementValue el = new ElementValue(ctx.Values.Str(MapTag(reader)), ReadAttribs(ctx, reader), ctx);
                                if (stack.Count > 0)
                                {
                                    stack.Peek().Grow(ctx);
                                    ElementValue.Placing(ctx, el);
                                    stack.Peek().Children.Add(el);
                                    stack.Peek().Changed(ctx);
                                }
                                else
                                    root = el;
                                if (!reader.IsEmptyElement)
                                {
                                    stack.Push(el);
                                    last = el;
                                    tail = false;
                                }
                                else
                                {
                                    last = el;
                                    tail = true;
                                }
                                break;
                            case XmlNodeType.Text:
                            case XmlNodeType.CDATA:
                            case XmlNodeType.SignificantWhitespace:
                            case XmlNodeType.Whitespace:
                                pending.Append(reader.Value);
                                break;
                            case XmlNodeType.EndElement:
                                Flush(ctx, ref last, tail, pending);
                                last = stack.Pop();
                                tail = true;
                                break;
                        }
                    }
                    if (root == null)
                        throw AtEnd(ctx, text, "no element found", 3);   // expat reports the end of the input
                    return root;
                }
            }
            catch (XmlException xe)
            {
                throw ParseFailed(ctx, xe, text);
            }
        }

        // .NET's reader is quadratic in the attributes of one start tag, inside one call the budget cannot
        // interrupt (400K namespace declarations ran past the deadline + 5 s). Expat has no such limit; a tag
        // with more than this many attributes is refused before the reader sees it.
        private const int MaxAttributesPerTag = 10000;
        private const string TooManyAttributes = "too many attributes in one element";

        private static void CheckAttributeCounts(EvalContext ctx, string text)
        {
            ctx.Budget.ChargeLinear(text.Length);
            int line = 1, count = 0;
            bool inTag = false;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                    line++;
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                    continue;
                }
                if (!inTag)
                {
                    if (c != '<' || i + 1 >= text.Length)
                        continue;
                    char next = text[i + 1];
                    if (next == '!' || next == '?')
                        i = SkipMarkup(text, i) - 1;   // comment, CDATA, PI: no attributes, may hold '<' and quotes
                    else if (next != '/')
                    {
                        inTag = true;
                        count = 0;
                    }
                    continue;
                }
                if (c == '"' || c == '\'')
                    quote = c;
                else if (c == '>')
                    inTag = false;
                else if (c == '=' && ++count > MaxAttributesPerTag)
                    throw new XmlException(TooManyAttributes + " (limit " + MaxAttributesPerTag + ")", null, line, 1);
            }
        }

        // The index just past the comment, CDATA section or processing instruction starting at `at`.
        private static int SkipMarkup(string text, int at)
        {
            string end = string.CompareOrdinal(text, at, "<!--", 0, 4) == 0 ? "-->"
                : string.CompareOrdinal(text, at, "<![CDATA[", 0, 9) == 0 ? "]]>"
                : text[at + 1] == '?' ? "?>" : ">";
            int close = text.IndexOf(end, at + 2, StringComparison.Ordinal);
            return close < 0 ? text.Length : close + end.Length;
        }

        private static void Flush(EvalContext ctx, ref ElementValue last, bool tail, StringBuilder pending)
        {
            if (pending.Length == 0 || last == null)
            {
                pending.Clear();
                return;
            }
            string s = pending.ToString();
            pending.Clear();
            if (tail)
                last.Tail = Concat(ctx, last.Tail, s);
            else
                last.Text = Concat(ctx, last.Text, s);
        }

        private static ScriptValue Concat(EvalContext ctx, ScriptValue existing, string s)
        {
            if (existing.Kind == ValueKind.None)
                return ctx.Values.Str(s);
            return ctx.Values.Str(((StrValue)existing).Value + s);
        }

        private static string MapTag(XmlReader reader)
        {
            string uri = reader.NamespaceURI;
            return string.IsNullOrEmpty(uri) ? reader.LocalName : "{" + uri + "}" + reader.LocalName;
        }

        private static DictValue ReadAttribs(EvalContext ctx, XmlReader reader)
        {
            DictValue attrib = null;   // none for an element without attributes, as CPython keeps none
            if (reader.HasAttributes && reader.MoveToFirstAttribute())
            {
                do
                {
                    if (reader.Name == "xmlns" || reader.Prefix == "xmlns" || reader.NamespaceURI == XmlnsNs)
                        continue;   // namespace declarations are not ET attributes
                    string key = string.IsNullOrEmpty(reader.NamespaceURI)
                        ? reader.LocalName
                        : "{" + reader.NamespaceURI + "}" + reader.LocalName;
                    if (attrib == null)
                        attrib = ctx.Values.Dict(4);
                    attrib.SetItem(ctx.Values.Str(key), ctx.Values.Str(reader.Value), ctx);
                }
                while (reader.MoveToNextAttribute());
                reader.MoveToElement();
            }
            return attrib;
        }

        private static string AsStr(EvalContext ctx, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "expected str, not " + v.PyTypeName);
            return s.Value;
        }
    }
}
