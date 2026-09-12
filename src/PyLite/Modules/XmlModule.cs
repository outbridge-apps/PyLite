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
    public static partial class XmlModule
    {
        private const string XmlnsNs = "http://www.w3.org/2000/xmlns/";

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["Element"] = ctx.Values.Type("Element", ElementCtor, null, null);
            m["SubElement"] = BuiltinFunctionValue.Make("SubElement", SubElement);
            m["fromstring"] = BuiltinFunctionValue.Make("fromstring", FromString);
            m["XML"] = BuiltinFunctionValue.Make("XML", FromString);
            m["tostring"] = BuiltinFunctionValue.Make("tostring", ToStringFn);
            m["indent"] = BuiltinFunctionValue.Make("indent", Indent);
            m["ElementTree"] = ctx.Values.Type("xml.etree.ElementTree.ElementTree", ElementTreeCtor, ElementTreeValue.TreeType, null);
            m["iselement"] = BuiltinFunctionValue.Make("iselement", IsElement);
            m["register_namespace"] = BuiltinFunctionValue.Make("register_namespace", RegisterNamespace);
            m["Comment"] = CommentFn;
            m["ProcessingInstruction"] = ProcessingInstructionFn;
            m["PI"] = ProcessingInstructionFn;
            m["dump"] = BuiltinFunctionValue.Make("dump", Dump);
            m["tostringlist"] = BuiltinFunctionValue.Make("tostringlist", ToStringList);
            m["ParseError"] = ParseErrorType();
            AddSources(m, ctx);
            return ctx.Values.Module("xml.etree.ElementTree", m);
        }

        private static TypeValue ParseErrorType()
        {
            BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(PyExceptionTypes.XmlParseError, args);
            return TypeValue.Make("ParseError", ctor, null, null, PyExceptionTypes.XmlParseError);
        }

        // ParseError carries `position` as CPython's does: the (line, column) the reader stopped at.
        // `code` is expat's error number and has no counterpart here, so it is left absent rather than
        // invented. The DTD refusal gets its own wording: the reader's own sentence advises setting an
        // XmlReaderSettings property, which is addressed to whoever embeds the engine, not to the script.
        private static ScriptException ParseFailed(EvalContext ctx, XmlException xe)
        {
            // The reader leaves both at 0 for a failure it spots before positioning (the DTD refusal);
            // a position is meant to point somewhere, so it starts at the first character instead.
            int line = xe.LineNumber < 1 ? 1 : xe.LineNumber;
            int col = xe.LinePosition < 1 ? 0 : xe.LinePosition - 1;
            string what = xe.Message.IndexOf("DTD is prohibited", StringComparison.Ordinal) >= 0
                ? "DTD is prohibited in this environment"
                : xe.Message;
            ScriptException ex = Raise.Make(ctx, PyExceptionTypes.XmlParseError,
                what + ": line " + line + ", column " + col);
            ex.Value.Data = new Dictionary<string, ScriptValue>(1, StringComparer.Ordinal)
            {
                ["position"] = ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(line), ctx.Values.Int(col) }),
            };
            return ex;
        }

        // Element(tag, attrib={}, **extra): attrib is COPIED (not shared with the argument).
        private static ScriptValue ElementCtor(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "Element", 1, 2);
            return BuildElement(ctx, args[0], args.Length == 2 ? args[1] : null, kw);
        }

        private static ElementValue BuildElement(EvalContext ctx, ScriptValue tag, ScriptValue attribArg, KwArgs kw)
        {
            DictValue attrib = ctx.Values.Dict(4);
            ScriptValue attribKw;
            if (kw.TryGet("attrib", out attribKw))
            {
                // Element(tag, attrib={...}) spelled by keyword is the dict, not an attribute named 'attrib'
                if (attribArg != null && attribArg.Kind != ValueKind.None)
                    throw Raise.TypeError(ctx, "Element() got multiple values for argument 'attrib'");
                attribArg = attribKw;
            }
            if (attribArg != null && attribArg.Kind != ValueKind.None)
            {
                DictValue src = attribArg as DictValue;
                if (src == null)
                    throw Raise.TypeError(ctx, "attrib must be a dict");
                CopyAttribs(ctx, attrib, src);
            }
            for (int j = 0; j < kw.Count; j++)
            {
                if (kw.NameAt(j) != "attrib")
                    attrib.SetItem(ctx.Values.Str(kw.NameAt(j)), kw.ValueAt(j), ctx);
            }
            return new ElementValue(UnQName(tag), attrib, ctx);   // a QName tag is stored as its text
        }

        private static void CopyAttribs(EvalContext ctx, DictValue dst, DictValue src)
        {
            var t = src.Table;
            for (int p = 0; p < t.EntriesUsed; p++)
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
            ElementValue el = BuildElement(ctx, args[1], args.Length == 3 ? args[2] : null, kw);
            parent.Children.Add(el);
            parent.Version++;
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
                if (IsBlank(e.Text))
                    e.Text = ctx.Values.Str(child);
                ElementValue last = null;
                foreach (ScriptValue c in e.Children)
                {
                    ElementValue ce = c as ElementValue;
                    if (ce == null)
                        continue;
                    if (ce.Children.Count > 0)
                        stack.Push(new KeyValuePair<ElementValue, int>(ce, lvl + 1));
                    if (IsBlank(ce.Tail))
                        ce.Tail = ctx.Values.Str(child);
                    last = ce;
                }
                if (last != null && IsBlank(last.Tail))
                    last.Tail = ctx.Values.Str(own);
                e.Version++;
            }
            return ctx.Values.None;
        }

        private static bool IsBlank(ScriptValue text)
        {
            StrValue s = text as StrValue;
            return s == null || s.Value.Trim().Length == 0;
        }

        // The indentation of one level: n copies of the space string, sized against the string cap before
        // anything is built (a level of 10**9 would otherwise allocate gigabytes off budget).
        private static string Repeat(EvalContext ctx, string s, int n)
        {
            long total = (long)s.Length * n;
            if (total > ctx.Limits.MaxStrChars)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", ctx.Limits.MaxStrChars, total);
            ctx.Values.PreCharge(2 * total + 24);
            var sb = new StringBuilder((int)total);
            for (int i = 0; i < n; i++)
                sb.Append(s);
            return sb.ToString();
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
            BytesValue b = args[0] as BytesValue;   // a UTF-8 document (the only encoding the sandbox decodes)
            if (b != null)
                return Parse(ctx, new System.Text.UTF8Encoding(false, true).GetString(b.Data));
            throw Raise.TypeError(ctx, "fromstring() argument must be str or bytes, not " + args[0].PyTypeName);
        }

        // ---- loader ----

        private static ElementValue Parse(EvalContext ctx, string text)
        {
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
                                    stack.Peek().Children.Add(el);
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
                        throw Raise.Make(ctx, PyExceptionTypes.XmlParseError, "no element found");
                    return root;
                }
            }
            catch (XmlException xe)
            {
                throw ParseFailed(ctx, xe);
            }
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
            DictValue attrib = ctx.Values.Dict(4);
            if (reader.HasAttributes && reader.MoveToFirstAttribute())
            {
                do
                {
                    if (reader.Name == "xmlns" || reader.Prefix == "xmlns" || reader.NamespaceURI == XmlnsNs)
                        continue;   // namespace declarations are not ET attributes
                    string key = string.IsNullOrEmpty(reader.NamespaceURI)
                        ? reader.LocalName
                        : "{" + reader.NamespaceURI + "}" + reader.LocalName;
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
