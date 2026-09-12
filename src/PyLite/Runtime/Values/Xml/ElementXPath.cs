using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // Mini-XPath for Element.find/findall/findtext + a lazy iter (adds positional
    // predicates [N]/[last()]/[last()-N] and the namespaces= prefix map). Grammar: tag, path (tag1/tag2),
    // '*', '.', '//'+'.//' descendant, {uri}tag, prefix:tag, predicates [@a], [@a='v'], [tag], [N].
    // The matcher uses explicit stacks/lists (no CLR recursion). Positional predicates apply per context
    // node on the child/self axes; descendant+positional is rejected (invalid path).
    internal static class ElementXPath
    {
        internal static void AddSlots(IDictionary<string, SlotDescriptor> s)
        {
            s["find"] = SlotDescriptor.MakeMethod("find", Find);
            s["findall"] = SlotDescriptor.MakeMethod("findall", FindAll);
            s["findtext"] = SlotDescriptor.MakeMethod("findtext", FindText);
            s["iter"] = SlotDescriptor.MakeMethod("iter", Iter);
            s["iterfind"] = SlotDescriptor.MakeMethod("iterfind", IterFind);
            s["itertext"] = SlotDescriptor.MakeMethod("itertext", IterText);
        }

        // itertext(): this element's text, then each descendant's text and tail in document order. The
        // element's OWN tail is not part of it, and a comment or PI contributes nothing (its tag is not
        // a string). Computed up front, as iterfind is.
        private static ScriptValue IterText(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.None(ctx, a, kw, "itertext");
            var texts = new List<ScriptValue>();
            // An explicit stack of "descend into this node" and "emit this tail", so a deep tree stays
            // off the C# stack. Pushed in reverse, the pops read child, its subtree, its tail, next child.
            var work = new Stack<TextWork>();
            work.Push(new TextWork { Node = (ElementValue)self });
            while (work.Count > 0)
            {
                ctx.Budget.Step();
                TextWork item = work.Pop();
                if (item.Node == null)
                {
                    texts.Add(item.Emit);
                    continue;
                }
                if (TagText(item.Node.Tag) == null)
                    continue;                       // a comment or processing instruction has no text
                if (item.Node.Text.Kind != ValueKind.None)
                    texts.Add(item.Node.Text);
                List<ScriptValue> kids = item.Node.Children;
                for (int i = kids.Count - 1; i >= 0; i--)
                {
                    var child = (ElementValue)kids[i];
                    if (child.Tail.Kind != ValueKind.None)
                        work.Push(new TextWork { Emit = child.Tail });
                    work.Push(new TextWork { Node = child });
                }
            }
            return ctx.Values.Iterator(new ListIterator(texts), "xml.etree.ElementTree.Element");
        }

        private struct TextWork
        {
            public ElementValue Node;   // null when this item just emits Emit
            public ScriptValue Emit;
        }

        // The tag as text: null for a comment or a processing instruction, whose tag is the factory
        // itself, exactly as CPython marks them.
        internal static string TagText(ScriptValue tag)
        {
            StrValue s = tag as StrValue;
            return s == null ? null : s.Value;
        }

        // The same four over an ElementTree: the tree delegates to its root (CPython does the same).
        internal static ScriptValue TreeFind(ElementValue root, ScriptValue[] a, KwArgs kw, EvalContext ctx) { return Find(root, a, kw, ctx); }
        internal static ScriptValue TreeFindAll(ElementValue root, ScriptValue[] a, KwArgs kw, EvalContext ctx) { return FindAll(root, a, kw, ctx); }
        internal static ScriptValue TreeFindText(ElementValue root, ScriptValue[] a, KwArgs kw, EvalContext ctx) { return FindText(root, a, kw, ctx); }
        internal static ScriptValue TreeIter(ElementValue root, ScriptValue[] a, KwArgs kw, EvalContext ctx) { return Iter(root, a, kw, ctx); }
        internal static ScriptValue TreeIterFind(ElementValue root, ScriptValue[] a, KwArgs kw, EvalContext ctx) { return IterFind(root, a, kw, ctx); }

        // iterfind(path): the findall matches as an iterator (the match set is computed up front, so a
        // mutation during iteration does not disturb it - CPython's is lazy but over the same result).
        private static ScriptValue IterFind(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            List<ElementValue> matches = Match((ElementValue)self, PathArg(ctx, a, "iterfind"), Namespaces(ctx, a, kw), ctx);
            var items = new List<ScriptValue>(matches.Count);
            foreach (ElementValue e in matches)
                items.Add(e);
            return ctx.Values.Iterator(new ListIterator(items), "generator");
        }

        private enum Axis { Child, Descendant, Self, Parent }

        private sealed class Step
        {
            public Axis Axis;
            public string Test;                     // tag, '*', '.', or {uri}tag (prefixes pre-expanded)
            public List<Predicate> Predicates = new List<Predicate>();
        }

        private sealed class Predicate
        {
            public string Attr;      // non-null => attribute predicate
            public string Value;     // non-null (with Attr) => equality; null => presence
            public string ChildTag;  // non-null => a direct child with this tag must exist
            public int Position;     // 0 => not positional; >0 => 1-based index; <0 => from end (-1 = last())
            public bool Negated;     // [@a!='v'] / [tag!='text']
        }

        // A path the grammar has no reading for. CPython raises SyntaxError here; answering with an empty
        // match set instead would turn a typo into "nothing found", which is the quietest kind of wrong.
        private static ScriptException BadPath(EvalContext ctx, string what, string path)
        {
            return Raise.Make(ctx, PyExceptionTypes.SyntaxError, what + ": " + path);
        }

        // ---- method slots ----

        private static ScriptValue Find(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            List<ElementValue> matches = Match((ElementValue)self, PathArg(ctx, a, "find"), Namespaces(ctx, a, kw), ctx);
            return matches.Count > 0 ? (ScriptValue)matches[0] : ctx.Values.None;
        }

        private static ScriptValue FindAll(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            List<ElementValue> matches = Match((ElementValue)self, PathArg(ctx, a, "findall"), Namespaces(ctx, a, kw), ctx);
            ListValue list = ctx.Values.List(matches.Count);
            foreach (ElementValue e in matches)
                list.Add(e, ctx);
            return list;
        }

        private static ScriptValue FindText(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "findtext", 1, 3);
            string path = StrArg(ctx, a[0], "findtext");
            // findtext(path, default=None, namespaces=None) — default is the SECOND positional here.
            ScriptValue deflt = a.Length >= 2 ? a[1] : ctx.Values.None;
            ScriptValue dkw;
            if (kw.TryGet("default", out dkw))
                deflt = dkw;
            Dictionary<string, string> ns = ReadNsArg(ctx, a.Length >= 3 ? a[2] : null, kw);
            List<ElementValue> matches = Match((ElementValue)self, path, ns, ctx);
            if (matches.Count == 0)
                return deflt;
            ScriptValue text = matches[0].Text;
            return text.Kind == ValueKind.None ? ctx.Values.Str("") : text;
        }

        private static ScriptValue Iter(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            string tag = null;
            if (a.Length == 1 && a[0].Kind != ValueKind.None)
                tag = StrArg(ctx, a[0], "iter");
            ScriptValue tkw;
            if (kw.TryGet("tag", out tkw) && tkw.Kind != ValueKind.None)
                tag = StrArg(ctx, tkw, "iter");
            return ctx.Values.Iterator(new ElementIterIterator((ElementValue)self, tag), "xml.etree.ElementTree.Element");
        }

        private static string PathArg(EvalContext ctx, ScriptValue[] a, string name)
        {
            if (a.Length < 1)
                throw Raise.TypeError(ctx, name + "() missing required argument 'path'");
            return StrArg(ctx, a[0], name);
        }

        // find/findall: namespaces is the 2nd positional or the namespaces= kwarg.
        private static Dictionary<string, string> Namespaces(EvalContext ctx, ScriptValue[] a, KwArgs kw)
        {
            return ReadNsArg(ctx, a.Length >= 2 ? a[1] : null, kw);
        }

        private static Dictionary<string, string> ReadNsArg(EvalContext ctx, ScriptValue positional, KwArgs kw)
        {
            ScriptValue nsArg = positional;
            ScriptValue nkw;
            if (kw.TryGet("namespaces", out nkw))
                nsArg = nkw;
            if (nsArg == null || nsArg.Kind == ValueKind.None)
                return null;
            DictValue d = nsArg as DictValue;
            if (d == null)
                throw Raise.TypeError(ctx, "namespaces must be a dict, not " + nsArg.PyTypeName);
            var map = new Dictionary<string, string>(System.StringComparer.Ordinal);
            var t = d.Table;
            for (int p = 0; p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (!t.TryGetEntryAt(p, out h, out k, out v))
                    continue;
                if (k.Kind != ValueKind.Str || v.Kind != ValueKind.Str)
                    throw Raise.TypeError(ctx, "namespaces keys and values must be str");
                map[((StrValue)k).Value] = ((StrValue)v).Value;
            }
            return map;
        }

        private static string StrArg(EvalContext ctx, ScriptValue v, string name)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, name + "() path must be str, not " + v.PyTypeName);
            return s.Value;
        }

        // ---- matcher ----

        private static List<ElementValue> Match(ElementValue start, string path, Dictionary<string, string> ns, EvalContext ctx)
        {
            List<Step> steps = ParsePath(ctx, path, ns);
            var current = new List<ElementValue> { start };
            Dictionary<ElementValue, ElementValue> parents = null;
            foreach (Step step in steps)
            {
                if (step.Axis == Axis.Parent && parents == null)
                    parents = ParentMap(ctx, start);
                var next = new List<ElementValue>();
                // each parent appears once per step, as in CPython; a set, because a chain of 200k
                // nodes asking for '..' is 200k lookups, not 200k list scans
                HashSet<ElementValue> seenParents = step.Axis == Axis.Parent ? new HashSet<ElementValue>() : null;
                foreach (ElementValue node in current)
                {
                    ctx.Budget.Step();
                    List<ElementValue> cands = step.Axis == Axis.Parent
                        ? ParentOf(parents, node, seenParents)
                        : TagFiltered(ctx, node, step);
                    foreach (Predicate p in step.Predicates)
                    {
                        if (p.Position != 0)
                        {
                            int idx = p.Position > 0 ? p.Position : cands.Count + p.Position + 1;   // 1-based
                            ElementValue picked = idx >= 1 && idx <= cands.Count ? cands[idx - 1] : null;
                            cands = new List<ElementValue>();
                            if (picked != null)
                                cands.Add(picked);
                        }
                        else
                        {
                            var kept = new List<ElementValue>(cands.Count);
                            foreach (ElementValue c in cands)
                            {
                                ctx.Budget.Step();
                                if (MatchesPredicate(ctx, c, p))
                                    kept.Add(c);
                            }
                            cands = kept;
                        }
                    }
                    next.AddRange(cands);
                }
                current = next;
            }
            return current;
        }

        // '..' resolves through a map built from the START element's subtree, so a parent above the node
        // the search began at is simply not there — which is CPython's documented answer for it.
        private static Dictionary<ElementValue, ElementValue> ParentMap(EvalContext ctx, ElementValue start)
        {
            var map = new Dictionary<ElementValue, ElementValue>();   // reference identity, as CPython's is
            var stack = new Stack<ElementValue>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                ctx.Budget.Step();
                ElementValue n = stack.Pop();
                foreach (ScriptValue c in n.Children)
                {
                    var e = (ElementValue)c;
                    map[e] = n;
                    stack.Push(e);
                }
            }
            return map;
        }

        private static List<ElementValue> ParentOf(Dictionary<ElementValue, ElementValue> parents,
            ElementValue node, HashSet<ElementValue> seen)
        {
            var result = new List<ElementValue>(1);
            ElementValue parent;
            if (parents.TryGetValue(node, out parent) && seen.Add(parent))
                result.Add(parent);
            return result;
        }

        private static List<ElementValue> TagFiltered(EvalContext ctx, ElementValue node, Step step)
        {
            var result = new List<ElementValue>();
            if (step.Axis == Axis.Self)
            {
                result.Add(node);
                return result;
            }
            if (step.Axis == Axis.Child)
            {
                foreach (ScriptValue c in node.Children)
                {
                    ctx.Budget.Step();
                    ElementValue e = (ElementValue)c;
                    if (TagMatches(e, step.Test))
                        result.Add(e);
                }
                return result;
            }
            var stack = new Stack<ElementValue>();
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push((ElementValue)node.Children[i]);
            while (stack.Count > 0)
            {
                ctx.Budget.Step();
                ElementValue n = stack.Pop();
                if (TagMatches(n, step.Test))
                    result.Add(n);
                for (int i = n.Children.Count - 1; i >= 0; i--)
                    stack.Push((ElementValue)n.Children[i]);
            }
            return result;
        }

        private static bool TagMatches(ElementValue e, string test)
        {
            if (test == "*" || test == ".")
                return true;
            return TagText(e.Tag) == test;
        }

        private static bool MatchesPredicate(EvalContext ctx, ElementValue e, Predicate p)
        {
            if (p.Attr != null)
            {
                ScriptValue got;
                bool has = e.Attrib.TryGet(ctx.Values.Str(p.Attr), ctx, out got);
                if (p.Value == null)
                    return has;
                // [@a!='v'] is `elem.get(a) != v`, so an element WITHOUT the attribute is selected —
                // CPython's asymmetry between the attribute form and the tag form below.
                if (p.Negated)
                    return !has || ((StrValue)got).Value != p.Value;
                return has && ((StrValue)got).Value == p.Value;
            }
            if (p.ChildTag == "." && p.Value != null)
            {
                bool same = TextOf(ctx, e) == p.Value;   // [.='text'] over the COMPLETE text content
                return p.Negated ? !same : same;
            }
            foreach (ScriptValue c in e.Children)
            {
                ElementValue child = (ElementValue)c;
                if (TagText(child.Tag) != p.ChildTag)
                    continue;
                if (p.Value == null)
                    return true;                                     // [tag]: presence
                // [tag='t'] / [tag!='t'] both need SUCH A CHILD to exist; with none, neither selects.
                if ((TextOf(ctx, child) == p.Value) != p.Negated)
                    return true;
            }
            return false;
        }

        // The complete text content, which is what CPython compares in [tag='text'] and [.='text']:
        // "".join(e.itertext()), not e.text alone.
        private static string TextOf(EvalContext ctx, ElementValue e)
        {
            var sb = new System.Text.StringBuilder();
            AppendText(ctx, e, sb);
            return sb.ToString();
        }

        // elem.text, then each descendant's text and tail in document order (the element's own tail is
        // not part of it). An explicit stack, so a deep subtree stays off the C# stack.
        internal static void AppendText(EvalContext ctx, ElementValue root, System.Text.StringBuilder sb)
        {
            var work = new Stack<TextWork>();
            work.Push(new TextWork { Node = root });
            while (work.Count > 0)
            {
                ctx.Budget.Step();
                TextWork item = work.Pop();
                if (item.Node == null)
                {
                    sb.Append(((StrValue)item.Emit).Value);
                    continue;
                }
                if (TagText(item.Node.Tag) == null)
                    continue;                       // a comment or processing instruction has no text
                StrValue t = item.Node.Text as StrValue;
                if (t != null)
                    sb.Append(t.Value);
                List<ScriptValue> kids = item.Node.Children;
                for (int i = kids.Count - 1; i >= 0; i--)
                {
                    var child = (ElementValue)kids[i];
                    if (child.Tail.Kind != ValueKind.None)
                        work.Push(new TextWork { Emit = child.Tail });
                    work.Push(new TextWork { Node = child });
                }
            }
        }

        // ---- path parsing ----

        private static List<Step> ParsePath(EvalContext ctx, string path, Dictionary<string, string> ns)
        {
            var steps = new List<Step>();
            string[] parts = path.Split('/');
            bool descendant = false;
            for (int idx = 0; idx < parts.Length; idx++)
            {
                string part = parts[idx];
                if (part.Length == 0)
                {
                    if (idx == 0)
                        continue;   // leading '/'
                    descendant = true;
                    continue;
                }
                steps.Add(ParseStep(ctx, part, descendant ? Axis.Descendant : Axis.Child, ns));
                descendant = false;
            }
            if (steps.Count == 0)
                throw Raise.ValueError(ctx, "invalid path");
            return steps;
        }

        private static Step ParseStep(EvalContext ctx, string part, Axis axis, Dictionary<string, string> ns)
        {
            var step = new Step { Axis = axis };
            int br = part.IndexOf('[');
            string test = br < 0 ? part : part.Substring(0, br);
            if (test == ".")
                step.Axis = Axis.Self;
            else if (test == "..")
                step.Axis = Axis.Parent;
            else if (test.IndexOf('(') >= 0 || test.IndexOf(')') >= 0)
                throw BadPath(ctx, "unsupported path syntax", part);   // text(), node(), ...
            step.Test = step.Axis == Axis.Parent ? "*" : ExpandPrefix(ctx, test, ns, true);
            int i = br;
            while (i >= 0 && i < part.Length && part[i] == '[')
            {
                int close = part.IndexOf(']', i);
                if (close < 0)
                    throw Raise.ValueError(ctx, "invalid predicate");
                Predicate p = ParsePredicate(ctx, part.Substring(i + 1, close - i - 1), ns);
                if (p.Position != 0 && step.Axis == Axis.Descendant)
                    throw Raise.ValueError(ctx, "invalid path");   // positional-on-descendant: out of scope
                step.Predicates.Add(p);
                i = close + 1;
            }
            return step;
        }

        private static Predicate ParsePredicate(EvalContext ctx, string body, Dictionary<string, string> ns)
        {
            body = body.Trim();
            if (body.Length == 0)
                throw BadPath(ctx, "invalid predicate", "[]");
            if (body[0] == '@')
            {
                bool neg;
                int eq = FindComparison(body, out neg);
                if (eq < 0)
                {
                    string only = body.Substring(1).Trim();
                    if (only.Length == 0)
                        throw BadPath(ctx, "invalid predicate", "[" + body + "]");
                    return new Predicate { Attr = ExpandPrefix(ctx, only, ns, false) };
                }
                string attr = body.Substring(1, eq - 1 - (neg ? 1 : 0)).Trim();
                if (attr.Length == 0)
                    throw BadPath(ctx, "invalid predicate", "[" + body + "]");
                return new Predicate
                {
                    Attr = ExpandPrefix(ctx, attr, ns, false),
                    Value = Unquote(body.Substring(eq + 1)),
                    Negated = neg,
                };
            }
            int pos;
            if (TryParsePosition(body, out pos))
                return new Predicate { Position = pos };
            if (body.IndexOf('(') >= 0 || body.IndexOf(')') >= 0)
                throw BadPath(ctx, "unsupported predicate", "[" + body + "]");   // position(), text(), ...
            bool tneg;
            int teq = FindComparison(body, out tneg);
            if (teq > 0)
            {
                // [tag='text'] / [tag!='text'] / [.='text']: a child (or the node itself) whose complete
                // text content compares to the literal
                string tag = body.Substring(0, teq - (tneg ? 1 : 0)).Trim();
                if (tag.Length == 0)
                    throw BadPath(ctx, "invalid predicate", "[" + body + "]");
                return new Predicate
                {
                    ChildTag = tag == "." ? "." : ExpandPrefix(ctx, tag, ns, true),
                    Value = Unquote(body.Substring(teq + 1)),
                    Negated = tneg,
                };
            }
            if (teq == 0)
                throw BadPath(ctx, "invalid predicate", "[" + body + "]");
            return new Predicate { ChildTag = ExpandPrefix(ctx, body, ns, true) };
        }

        // The index of the '=' of an '=' or '!=' comparison; negated says which. -1 when there is none.
        private static int FindComparison(string body, out bool negated)
        {
            negated = false;
            int eq = body.IndexOf('=');
            if (eq < 0)
                return -1;
            negated = eq > 0 && body[eq - 1] == '!';
            return eq;
        }

        private static string Unquote(string v)
        {
            v = v.Trim();
            if (v.Length >= 2 && (v[0] == '\'' || v[0] == '"') && v[v.Length - 1] == v[0])
                return v.Substring(1, v.Length - 2);
            return v;
        }

        // [N] -> N (1-based); [last()] -> -1; [last()-K] -> -(K+1).
        private static bool TryParsePosition(string body, out int pos)
        {
            pos = 0;
            if (body.Length > 0 && AllDigits(body))
            {
                pos = int.Parse(body, System.Globalization.CultureInfo.InvariantCulture);
                return pos != 0;
            }
            if (!body.StartsWith("last()", System.StringComparison.Ordinal))
                return false;
            string rest = body.Substring(6).Trim();
            if (rest.Length == 0)
            {
                pos = -1;
                return true;
            }
            if (rest[0] == '-' && AllDigits(rest.Substring(1).Trim()))
            {
                pos = -1 - int.Parse(rest.Substring(1).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            return false;
        }

        private static bool AllDigits(string s)
        {
            if (s.Length == 0)
                return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9')
                    return false;
            return true;
        }

        // 'prefix:name' -> '{uri}name' via the namespaces map; a default '' entry applies to bare TAGS only.
        // Unknown prefix -> KeyError (the engine has no SyntaxError; documented deviation).
        private static string ExpandPrefix(EvalContext ctx, string test, Dictionary<string, string> ns, bool isTag)
        {
            if (test.Length == 0 || test[0] == '{' || test == "*" || test == ".")
                return test;
            int colon = test.IndexOf(':');
            if (colon >= 0)
            {
                string prefix = test.Substring(0, colon);
                string uri;
                if (ns == null || !ns.TryGetValue(prefix, out uri))
                    throw Raise.KeyError(ctx, ctx.Values.Str(prefix));
                return "{" + uri + "}" + test.Substring(colon + 1);
            }
            string defUri;
            if (isTag && ns != null && ns.TryGetValue("", out defUri))
                return "{" + defUri + "}" + test;
            return test;
        }
    }

    // Lazy pre-order DFS over an element subtree, including self. tag=null/'*' => all.
    internal sealed class ElementIterIterator : ScriptIteratorBase
    {
        private readonly Stack<ElementValue> _stack;
        private readonly string _tagFilter;

        internal ElementIterIterator(ElementValue root, string tagFilter)
        {
            _stack = new Stack<ElementValue>();
            _stack.Push(root);
            _tagFilter = tagFilter;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            while (_stack.Count > 0)
            {
                ElementValue e = _stack.Pop();
                for (int i = e.Children.Count - 1; i >= 0; i--)
                    _stack.Push((ElementValue)e.Children[i]);
                if (_tagFilter == null || _tagFilter == "*" || ElementXPath.TagText(e.Tag) == _tagFilter)
                {
                    value = e;
                    return true;
                }
            }
            value = null;
            return false;
        }
    }
}
