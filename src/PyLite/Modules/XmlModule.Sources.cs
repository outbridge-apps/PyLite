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
    // Reading a document from a source object, QName, and the two list-shaped entry points
    // (closed 2026-09-11). There are no files in the sandbox, so a "source" is anything with a read()
    // method — io.StringIO is the one the engine ships — and a filename is refused rather than guessed at.
    public static partial class XmlModule
    {
        internal static void AddSources(Dictionary<string, ScriptValue> m, EvalContext ctx)
        {
            m["QName"] = ctx.Values.Type("xml.etree.ElementTree.QName", QNameCtor, QNameValue.QNameType, null);
            m["fromstringlist"] = BuiltinFunctionValue.Make("fromstringlist", FromStringList);
            m["parse"] = BuiltinFunctionValue.Make("parse", ParseSource);
            m["iterparse"] = BuiltinFunctionValue.Make("iterparse", IterParse);
            m["XMLID"] = BuiltinFunctionValue.Make("XMLID", XmlId);
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
                if (e.Attrib.TryGet(ctx.Values.Str("id"), ctx, out key))
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
            return new ElementTreeValue(Parse(ctx, ReadSource(ctx, a[0], "parse")));
        }

        // iterparse(source, events=('end',)) -> an iterator of (event, element).
        //
        // The document is parsed up front and the events are replayed from the finished tree. That is not
        // incremental, which is the whole point of iterparse in CPython — but it cannot be here anyway: a
        // source is an in-memory object, so the text is already materialised before the first event, and
        // reading it twice would cost more than the tree. Documented in docs/deviations.md.
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
            return ctx.Values.Iterator(new IterParseIterator(root, start, end), "xml.etree.ElementTree.iterparse");
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
            BytesValue b = text as BytesValue;   // a UTF-8 document, the only encoding the sandbox decodes
            if (b != null)
                return new UTF8Encoding(false, true).GetString(b.Data);
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

        // ('start', el) on the way down and ('end', el) on the way up, in document order, over an explicit
        // stack so a deep tree costs no C# frames.
        private sealed class IterParseIterator : ScriptIteratorBase
        {
            private readonly bool _start, _end;
            private readonly Stack<Frame> _stack = new Stack<Frame>();
            private bool _done;

            private sealed class Frame
            {
                public ElementValue El;
                public int Child;
                public bool Opened;
            }

            internal IterParseIterator(ElementValue root, bool start, bool end)
            {
                _start = start;
                _end = end;
                if (root != null)
                    _stack.Push(new Frame { El = root });
            }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                value = null;
                while (!_done && _stack.Count > 0)
                {
                    Frame f = _stack.Peek();
                    if (!f.Opened)
                    {
                        f.Opened = true;
                        if (_start)
                        {
                            value = Pair(ctx, "start", f.El);
                            return true;
                        }
                        continue;
                    }
                    if (f.Child < f.El.Children.Count)
                    {
                        ElementValue kid = f.El.Children[f.Child++] as ElementValue;
                        if (kid != null)
                            _stack.Push(new Frame { El = kid });
                        continue;
                    }
                    _stack.Pop();
                    if (_end)
                    {
                        value = Pair(ctx, "end", f.El);
                        return true;
                    }
                }
                _done = true;
                return false;
            }

            private static ScriptValue Pair(EvalContext ctx, string ev, ElementValue el)
            {
                return ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Str(ev), el });
            }
        }
    }
}
