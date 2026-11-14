using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // Mini-XPath for Element.find/findall/findtext + a lazy iter (adds positional
    // predicates [N]/[last()]/[last()-N] and the namespaces= prefix map). Grammar: tag, path (tag1/tag2),
    // '*', '.', '//'+'.//' descendant, {uri}tag, prefix:tag, predicates [@a], [@a='v'], [tag], [N].
    // The matcher uses explicit stacks/lists (no CLR recursion). A positional predicate keeps an element
    // that is the N-th of its parent's children with its tag, on any axis, as ElementPath does.
    internal static class ElementXPath
    {
        internal static void AddSlots(IDictionary<string, SlotDescriptor> s)
        {
            s["find"] = SlotDescriptor.MakeMethod("find", Args.BoundKeywords("find", 1, Find, "path", "namespaces"));
            s["findall"] = SlotDescriptor.MakeMethod("findall", Args.BoundKeywords("findall", 1, FindAll, "path", "namespaces"));
            s["findtext"] = SlotDescriptor.MakeMethod("findtext", Args.BoundKeywords("findtext", 1, FindText, "path", "default", "namespaces"));
            s["iter"] = SlotDescriptor.MakeMethod("iter", Args.BoundKeywords("iter", Iter, "tag"));
            s["iterfind"] = SlotDescriptor.MakeMethod("iterfind", Args.BoundKeywords("iterfind", 1, IterFind, "path", "namespaces"));
            s["itertext"] = SlotDescriptor.MakeMethod("itertext", IterText);
        }

        // itertext(): this element's text, then each descendant's text and tail in document order. The
        // element's OWN tail is not part of it. A comment's or PI's text is part of it: CPython's C iterator
        // reads no tag (the pure-Python one it replaces skips them). Lazy, as CPython's is: 900 shared copies
        // of a 900-child element made 810K texts up front, past the item cap.
        private static ScriptValue IterText(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.None(ctx, a, kw, "itertext");
            return ctx.Values.EngineIterator(new TextIterator((ElementValue)self), "xml.etree.ElementTree.Element");
        }

        // Each element's text on the way down, its tail once its subtree is done, whatever its tag
        // (_elementtree's iterator, on an ElementWalk).
        private sealed class TextIterator : ScriptIteratorBase
        {
            private readonly ElementValue _root;
            private readonly bool _ownsWalk;   // itertext(): lent at the start, given back at the end
            private ElementWalk _walk;
            private bool _started;

            internal TextIterator(ElementValue root)
            {
                _root = root;
                _ownsWalk = true;
            }

            // On a walk the caller rented, for a text read and done within one call.
            internal TextIterator(ElementValue root, ElementWalk walk)
            {
                _root = root;
                _walk = walk;
            }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                value = NextText(ctx);
                return value != null;
            }

            // The next text or tail, or null at the end.
            internal ScriptValue NextText(EvalContext ctx)
            {
                if (!_started)
                {
                    _started = true;
                    if (_ownsWalk)
                        _walk = RentWalk(ctx);
                    _walk.Reset(ctx, _root);
                    if (Yields(ctx, _root.Text))
                        return _root.Text;
                }
                while (_walk != null && _walk.Depth > 0)
                {
                    ElementValue c = _walk.NextChild();
                    if (c == null)
                    {
                        ElementValue done = _walk.Ascend();
                        if (_walk.Depth > 0 && Yields(ctx, done.Tail))   // the root's own tail is not part of it
                            return done.Tail;
                        continue;
                    }
                    ctx.Budget.StepTreeNode();   // leaving a node is bounded by the depth
                    _walk.Descend(ctx, c);
                    if (Yields(ctx, c.Text))
                        return c.Text;
                }
                if (_ownsWalk && _walk != null)
                {
                    ReturnWalk(ctx, _walk);   // a drained itertext() reads nothing more; a dropped one keeps its walk
                    _walk = null;
                }
                return null;
            }

            // CPython yields a text or tail only when it is true: '' is skipped as None is.
            private static bool Yields(EvalContext ctx, ScriptValue t)
            {
                return t.Kind != ValueKind.None && t.IsTruthy(ctx);
            }
        }

        // The tag as text: null for a comment or a processing instruction, whose tag is the factory
        // itself, exactly as CPython marks them.
        internal static string TagText(ScriptValue tag)
        {
            StrValue s = tag as StrValue;
            return s == null ? null : s.Value;
        }

        // The same four over an ElementTree: the tree delegates to its root (CPython does the same).
        internal static ScriptValue TreeFind(ElementValue root, ScriptValue[] a, KwArgs kw, EvalContext ctx) { return Find(root, TreePath(ctx, a), kw, ctx); }
        internal static ScriptValue TreeFindAll(ElementValue root, ScriptValue[] a, KwArgs kw, EvalContext ctx) { return FindAll(root, TreePath(ctx, a), kw, ctx); }
        internal static ScriptValue TreeFindText(ElementValue root, ScriptValue[] a, KwArgs kw, EvalContext ctx) { return FindText(root, TreePath(ctx, a), kw, ctx); }
        internal static ScriptValue TreeIter(ElementValue root, ScriptValue[] a, KwArgs kw, EvalContext ctx) { return Iter(root, a, kw, ctx); }
        internal static ScriptValue TreeIterFind(ElementValue root, ScriptValue[] a, KwArgs kw, EvalContext ctx) { return IterFind(root, TreePath(ctx, a), kw, ctx); }

        // On a tree, a path from the root ('/a') reads as './a' (CPython warns, then does so); an element refuses it.
        private static ScriptValue[] TreePath(EvalContext ctx, ScriptValue[] a)
        {
            StrValue s = a.Length > 0 ? a[0] as StrValue : null;
            if (s == null || !s.Value.StartsWith("/", System.StringComparison.Ordinal))
                return a;
            var copy = (ScriptValue[])a.Clone();
            copy[0] = ctx.Values.Str("." + s.Value);
            return copy;
        }

        // iterfind(path): the matches one at a time, as CPython's generator yields them; children are read
        // live, so a mutation during the iteration is seen as CPython sees it.
        private static ScriptValue IterFind(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            string path = PathArg(ctx, a, "iterfind");
            Dictionary<string, string> ns = Namespaces(ctx, a, kw);
            if (path.Length == 0)
                return ctx.Values.None;   // ElementPath.iterfind returns before it yields: None, not an empty generator
            return ctx.Values.EngineIterator(new PathIterator(Walk((ElementValue)self, path, ns, ctx, true)), "generator");
        }

        private sealed class PathIterator : ScriptIteratorBase
        {
            private readonly PathWalk _walk;

            internal PathIterator(PathWalk walk)
            {
                _walk = walk;
            }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                _walk.ForgetSiblings(ctx);   // the script ran since the last item
                value = _walk.Next(ctx);
                _walk.ForgetSiblings(ctx);   // rebuilt by the next item anyway: a dropped iterfind keeps none (review 10, B-2)
                if (value == null)
                    _walk.Close(ctx);   // an iterator dropped before its end keeps its map, charged as kept
                return value != null;
            }
        }

        internal enum Axis { Child, Descendant, Self, Parent }

        // Charged per parsed step and predicate (the object, its list and slots), and per walk source opened:
        // the source, and the descent stack only a descendant step has (review 10, B-2: every source paid for one).
        private const long StepBytes = 120, PredicateBytes = 64, SourceBytes = 64, DescentBytes = 136;
        private const int FreeEagerSources = 16;

        internal sealed class Step
        {
            public Axis Axis;
            public string Test;                     // tag, '*', '.', or {uri}tag (prefixes pre-expanded)
            public List<Predicate> Predicates = new List<Predicate>();
        }

        internal sealed class Predicate
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
            string path = PathArg(ctx, a, "find");
            Dictionary<string, string> ns = Namespaces(ctx, a, kw);
            RefuseEmptyWithMap(ctx, path, ns, "an iterator");
            ElementValue first = First(ctx, Walk((ElementValue)self, path, ns, ctx));
            return first ?? (ScriptValue)ctx.Values.None;
        }

        // An eager search's first match; its scratch (parent map, sibling groups) is given back when it ends.
        private static ElementValue First(EvalContext ctx, PathWalk walk)
        {
            try
            {
                return walk.Next(ctx);
            }
            finally
            {
                walk.Close(ctx);
            }
        }

        private static ScriptValue FindAll(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            string path = PathArg(ctx, a, "findall");
            Dictionary<string, string> ns = Namespaces(ctx, a, kw);
            RefuseEmptyWithMap(ctx, path, ns, "iterable");
            PathWalk walk = Walk((ElementValue)self, path, ns, ctx);
            ListValue list = ctx.Values.List(4);
            try
            {
                ElementValue e;
                while ((e = walk.Next(ctx)) != null)
                    list.Add(e, ctx);   // charged and capped as it grows
            }
            finally
            {
                walk.Close(ctx);
            }
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
            RefuseEmptyWithMap(ctx, path, ns, "an iterator");
            ElementValue first = First(ctx, Walk((ElementValue)self, path, ns, ctx));
            if (first == null)
                return deflt;
            ScriptValue text = first.Text;
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
            return ctx.Values.EngineIterator(new ElementIterIterator((ElementValue)self, tag), "xml.etree.ElementTree.Element");
        }

        // With a map CPython's find, findall and findtext read ElementPath.iterfind, which returns None for the
        // empty path: they fail on that None.
        private static void RefuseEmptyWithMap(EvalContext ctx, string path, Dictionary<string, string> ns, string what)
        {
            if (path.Length == 0 && ns != null)
                throw Raise.TypeError(ctx, "'NoneType' object is not " + what);
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
            for (int p = t.WalkStart(ctx); p < t.EntriesUsed; p++)
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

        private static string StrArg(EvalContext ctx, ScriptValue v, string name) { return Coerce.ToStr(ctx, v, name + "() path"); }

        // ---- matcher ----

        // lazy: iterfind's walk, which its iterator keeps; find, findall and findtext end theirs before they return.
        private static PathWalk Walk(ElementValue start, string path, Dictionary<string, string> ns, EvalContext ctx, bool lazy = false)
        {
            bool asTag = !lazy && ns == null && (path.Length == 0 || IsPrefixedPlainTag(path));
            return new PathWalk(start, CachedPath(ctx, path, ns, asTag), lazy);
        }

        // CPython's find, findall and findtext do not parse a path that has none of / * [ @ . outside braces when no
        // map is given: the children's tags are compared with it as written, so 'p:b' finds nothing there, where
        // iterfind and a call with a map refuse the undefined prefix. The empty path is such a name: no child's tag
        // unless one was given '' (1.1.0 raised ValueError, 1.2.0 before review 12 SyntaxError). So is a name with
        // whitespace, which ParsePath reads as steps of their own (review 13, row 35): find(' b') is a child tagged ' b'.
        // A '{}' or '{*}' prefix is a wildcard, which checkpath sends to ElementPath.
        private static bool IsPrefixedPlainTag(string path)
        {
            if (path.Length == 0 || (path.Length >= 3 && path[0] == '{' && (path[1] == '}' || (path[1] == '*' && path[2] == '}'))))
                return false;
            bool prefixed = path[0] != '{' && path.IndexOf(':') >= 0;
            bool spaced = false;
            bool check = true;
            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];
                if (c == '{')
                    check = false;
                else if (c == '}')
                    check = true;
                else if (check && (c == '/' || c == '*' || c == '[' || c == '@' || c == '.'))
                    return false;
                if (PyUnicode.IsPythonSpace(c))
                    spaced = true;
            }
            return prefixed || spaced;
        }

        // ElementPath's _cache: a run parses each (path, namespaces) once and keeps up to 100 of them, cleared when full,
        // as CPython does. The parsed steps are never changed by a walk. 800,000 find() calls parsed and charged 800,000
        // paths (250 MB) where 1.1.0 charged nothing.
        internal sealed class PathCache
        {
            internal const int MaxEntries = 100;
            private readonly Dictionary<string, List<Step>> _paths = new Dictionary<string, List<Step>>(System.StringComparer.Ordinal);

            private Dictionary<string, string> _lastNs;
            private string _lastNsKey;

            internal bool TryGet(string key, out List<Step> steps) { return _paths.TryGetValue(key, out steps); }

            // The key of a namespace map: the last call's when the map holds the same entries (a loop passes the same
            // dict: compared entry by entry, no sort, no new string).
            internal string NsKey(EvalContext ctx, Dictionary<string, string> ns)
            {
                ctx.Budget.ChargeLinear(ns.Count);
                if (_lastNs != null && _lastNs.Count == ns.Count)
                {
                    bool same = true;
                    foreach (KeyValuePair<string, string> kv in ns)
                    {
                        string v;
                        if (!_lastNs.TryGetValue(kv.Key, out v) || !string.Equals(v, kv.Value, System.StringComparison.Ordinal))
                        {
                            same = false;
                            break;
                        }
                    }
                    if (same)
                        return _lastNsKey;
                }
                var keys = new List<string>(ns.Keys);
                keys.Sort(System.StringComparer.Ordinal);
                var sb = new System.Text.StringBuilder(16 * keys.Count + 1);
                sb.Append('+');
                foreach (string k in keys)
                {
                    string v = ns[k];
                    sb.Append(k.Length).Append(':').Append(k).Append(v.Length).Append(':').Append(v);
                }
                sb.Append('|');
                _lastNs = ns;
                _lastNsKey = sb.ToString();
                return _lastNsKey;
            }

            internal void Add(string key, List<Step> steps)
            {
                if (_paths.Count >= MaxEntries)
                    _paths.Clear();
                _paths[key] = steps;
            }
        }

        // asTag: the whole path is one child tag, compared as written (IsPrefixedPlainTag).
        private static List<Step> CachedPath(EvalContext ctx, string path, Dictionary<string, string> ns, bool asTag)
        {
            PathCache cache = ctx.XmlPaths;
            if (cache == null)
                ctx.XmlPaths = cache = new PathCache();
            string key = asTag ? "=" + path : ns == null ? "-" + path : cache.NsKey(ctx, ns) + path;
            List<Step> steps;
            if (cache.TryGet(key, out steps))
            {
                ctx.Budget.Step();
                return steps;
            }
            if (asTag)
            {
                ctx.Values.PreCharge(StepBytes + 2L * path.Length);
                steps = new List<Step>(1) { new Step { Axis = Axis.Child, Test = path } };
            }
            else
                steps = ParsePath(ctx, path, ns);   // a path that does not parse raises and is not kept
            ctx.Values.PreCharge(2L * key.Length);
            cache.Add(key, steps);
            return steps;
        }


        // The path evaluated lazily, as CPython's chain of generators: the steps are walked depth-first with
        // one candidate source per step, so find() stops at the first match and iterfind() holds no match
        // set (find('*/*') over 6000 shared copies of a 6000-child element built all 36M pairs first, 589 MB
        // charged as nothing). The order is the step-by-step order: every match under the first candidate
        // comes before the second's.
        private sealed class PathWalk
        {
            private readonly ElementValue _start;
            private readonly List<Step> _steps;
            private readonly Source[] _src;
            private readonly bool _lazy;   // kept by an iterator: its sources are charged
            private int _depth = -1;   // the step being drained; -1 before the start and after the end
            private bool _started;
            private ParentMap _map;
            private long _mapVersion;   // EvalContext.XmlTreeVersion when the walk took _map: it sees no later entry
            private ParentMapCache.MapLease _lease;   // a lazy walk's hold on _map, given back when the walk is collected
            private bool _needsMap;   // a parent axis or a positional predicate
            private int _unshared;   // FastParents: 0 not known yet, 1 no element of the start's subtree is in two lists, 2 some may be
            private ElementValue _skipped, _skippedOwner;   // SkipToPosition's candidate, at _skippedAt among its owner's children
            private int _skippedAt;
            private Dictionary<ElementValue, long> _groupBytes;   // each parent's share of _siblingBytes
            private long _startVersion;   // EvalContext.XmlTreeVersion when the search began
            private Dictionary<ElementValue, Dictionary<string, List<ElementValue>>> _siblings;   // AtPosition's, per pull
            private long _siblingBytes;   // _siblings' share of the run's held scratch
            // the last parent's groups and the last group read: the next candidate is mostly the same parent's, same tag
            private ElementValue _lastParent;
            private Dictionary<string, List<ElementValue>> _lastByTag;
            private Dictionary<string, List<ElementValue>> _lastGroupOf;   // the groups _lastGroup is one of
            private string _lastTag;
            private List<ElementValue> _lastGroup;

            private sealed class Source
            {
                public ElementValue Node;   // the context node
                public int Index;           // into Node.Children (child axis)
                public bool Done;           // self and parent axes: the one candidate was taken
                public bool NoSkip;         // child axis: Node's children are not one str tag's, SkipToPosition has no say
                public ElementWalk Walk;    // descendant axis only; an eager search's is lent by the scratch
                public HashSet<ElementValue> SeenParents;   // each parent once per step, as in CPython
                public ElementValue Owner;  // descendant axis: the element whose children list held the last candidate
                public long Paid;           // List/SeenParents entries charged: the high-water mark, not every add

                public Source(ElementWalk walk)
                {
                    Walk = walk;
                }
            }

            internal PathWalk(ElementValue start, List<Step> steps, bool lazy)
            {
                _start = start;
                _steps = steps;
                _lazy = lazy;
                _src = new Source[steps.Count];   // each built when the walk first reaches its step
            }

            private static bool NeedsParentMap(List<Step> steps)
            {
                foreach (Step s in steps)
                {
                    if (s.Axis == Axis.Parent)
                        return true;
                    foreach (Predicate p in s.Predicates)
                    {
                        if (p.Position != 0)
                            return true;
                    }
                }
                return false;
            }

            private static bool HasParentAxis(List<Step> steps)
            {
                foreach (Step s in steps)
                {
                    if (s.Axis == Axis.Parent)
                        return true;
                }
                return false;
            }

            // Before script code can run in the search (a predicate's comparison, a tag's __eq__): the map CPython took
            // at the start is the one built now, while no list has changed since.
            private void BeforeScript(EvalContext ctx)
            {
                if (_needsMap)
                    TakeParents(ctx);
            }

            internal void ForgetSiblings(EvalContext ctx)
            {
                if (_siblings == null)
                    return;
                _siblings = null;
                _lastParent = null;
                _lastByTag = null;
                _lastGroupOf = null;
                _lastTag = null;
                _lastGroup = null;
                _groupBytes = null;
                Scratch(ctx).Release(_siblingBytes);
                _siblingBytes = 0;
            }

            // A parent the walk has left (a child step never comes back to it, a descendant walk not once it ascended past
            // it): its groups go back. One positional search kept every parent's groups to its end (review 10, A-1).
            private void DropGroups(EvalContext ctx, ElementValue parent)
            {
                long bytes;
                if (_siblings == null || _groupBytes == null || parent == null || !_groupBytes.TryGetValue(parent, out bytes))
                    return;
                _groupBytes.Remove(parent);
                Dictionary<string, List<ElementValue>> byTag;
                if (_siblings.TryGetValue(parent, out byTag))
                {
                    _siblings.Remove(parent);
                    if (byTag != null && ReferenceEquals(byTag, _lastGroupOf))
                    {
                        _lastGroupOf = null;
                        _lastTag = null;
                        _lastGroup = null;
                    }
                }
                if (ReferenceEquals(parent, _lastParent))
                {
                    _lastParent = null;
                    _lastByTag = null;
                }
                _siblingBytes -= bytes;
                Scratch(ctx).Release(bytes);
            }

            // The search's parent map, taken once (CPython's get_parent_map) and held until Close.
            private void TakeParents(EvalContext ctx)
            {
                if (_map != null)
                    return;
                _map = SharedParentMap(ctx, _start);
                _mapVersion = ctx.XmlTreeVersion;
                if (_lazy)
                    _lease = Scratch(ctx).Lease(this, _map);   // an iterfind may be dropped before its end
            }

            // The search is over: its scratch goes back. An iterfind dropped part way gives its map back once collected.
            internal void Close(EvalContext ctx)
            {
                ForgetSiblings(ctx);
                if (!_lazy)
                    ReturnWalks(ctx);
                if (_map != null)
                {
                    if (_lease != null)
                    {
                        _lease.Done = true;
                        _lease = null;
                    }
                    Scratch(ctx).Drop(_map);
                    _map = null;
                }
            }

            private void ReturnWalks(EvalContext ctx)
            {
                for (int i = 0; i < _src.Length; i++)
                {
                    if (_src[i] != null && _src[i].Walk != null)
                    {
                        Scratch(ctx).ReturnWalk(_src[i].Walk);
                        _src[i].Walk = null;
                    }
                }
            }

            // The next match, or null once there are no more.
            internal ElementValue Next(EvalContext ctx)
            {
                if (!_started)
                {
                    _started = true;
                    _startVersion = ctx.XmlTreeVersion;
                    _needsMap = NeedsParentMap(_steps);
                    // get_parent_map runs when the select starts. An eager search runs no script code until a predicate
                    // compares (BeforeScript), so its map is the start's until then, and a child step's positions need
                    // none while each element is in one list (AtPosition).
                    if (_needsMap && (_lazy || HasParentAxis(_steps)))
                        TakeParents(ctx);
                    if (_steps.Count == 0)
                        return _start;
                    _depth = 0;
                    Open(ctx, 0, _start);
                }
                while (_depth >= 0)
                {
                    ElementValue c = Pull(ctx, _depth);
                    if (c == null)
                    {
                        _depth--;
                        continue;
                    }
                    if (_depth == _steps.Count - 1)
                        return c;
                    _depth++;
                    Open(ctx, _depth, c);
                }
                return null;
            }

            private void Open(EvalContext ctx, int i, ElementValue node)
            {
                ctx.Budget.Step();
                Step step = _steps[i];
                Source s = _src[i];
                if (s == null)
                {
                    // an eager search's first sources end with the call: scratch (800,000 finds paid 160 MB); a path of
                    // a million steps pays for each past them, as an iterfind's walk pays for all
                    bool descends = step.Axis == Axis.Descendant;
                    if (_lazy || i >= FreeEagerSources)
                        ctx.Values.PreCharge(descends ? SourceBytes + DescentBytes : SourceBytes);
                    s = _src[i] = new Source(!descends ? null : _lazy ? new ElementWalk() : Scratch(ctx).RentWalk());
                }
                if (step.Axis == Axis.Child && !ReferenceEquals(s.Node, node))
                    DropGroups(ctx, s.Node);
                s.Node = node;
                s.Index = 0;
                s.Done = false;
                s.NoSkip = false;
                if (step.Axis == Axis.Parent)
                    TakeParents(ctx);
                if (step.Axis == Axis.Descendant)
                    s.Walk.Reset(ctx, node);
            }

            // The next candidate of step i that passes its predicates, or null when the source is drained.
            private ElementValue Pull(EvalContext ctx, int i)
            {
                Step step = _steps[i];
                Source s = _src[i];
                if (step.Predicates.Count == 0)
                    return Raw(ctx, step, s);

                ElementValue c;
                if (step.Axis == Axis.Child && step.Predicates[0].Position != 0)
                    SkipToPosition(ctx, step, s, s.Node);
                while ((c = Raw(ctx, step, s)) != null)
                {
                    // the element whose children list the candidate was read from
                    ElementValue owner = step.Axis == Axis.Child ? s.Node : step.Axis == Axis.Descendant ? s.Owner : null;
                    if (Passes(ctx, c, step, owner))
                        return c;
                }
                return null;
            }

            // A child step whose first predicate is a position, while AtPosition answers from the owner (no list changed,
            // no element is in two lists): every candidate before the one at that position fails it, running nothing, so
            // the scan resumes there (row[last()] checked every row before the last).
            private void SkipToPosition(EvalContext ctx, Step step, Source s, ElementValue owner)
            {
                _skipped = null;   // counted for this pull only: a tag may be renamed between an iterfind's items
                _skippedOwner = null;
                bool star = step.Test == "*";
                if (s.NoSkip || !(star || PlainTag(step.Test)) || !FastParents(ctx))
                    return;
                List<ScriptValue> kids = owner.Children;
                int position = step.Predicates[0].Position;
                // one pass over the owner's children: every tag a str (CPython's findall would run a script tag's __eq__),
                // and the matches counted; no groups built. `*` over children of one tag is that tag's step (add a row,
                // find *[last()]: every row was a candidate); over several tags each has its own N-th and nothing is skipped
                string wanted = star ? null : step.Test;
                int count = 0, target = -1;
                for (int i = 0; i < kids.Count && !s.NoSkip; i++)
                {
                    ctx.Budget.StepTreeNode();
                    StrValue t = ((ElementValue)kids[i]).Tag as StrValue;
                    if (wanted == null && t != null)
                        wanted = t.Value;
                    bool same = t != null && string.Equals(t.Value, wanted, StringComparison.Ordinal);
                    // remembered for the node: `*` over a run of one tag and then others rescanned the run per item
                    if (t == null || (star && !same))
                        s.NoSkip = true;
                    else if (same && ++count == position)
                        target = i;
                }
                if (s.NoSkip)
                    return;
                if (position < 0 && count + position >= 0)
                {
                    for (int i = kids.Count - 1, seen = 0; i >= 0; i--)
                    {
                        if (string.Equals(((StrValue)((ElementValue)kids[i]).Tag).Value, wanted, StringComparison.Ordinal) && --seen == position)
                        {
                            target = i;
                            break;
                        }
                    }
                }
                if (target < s.Index)
                {
                    s.Index = kids.Count;   // no child is at that position, or the scan has passed it already
                    return;
                }
                s.Index = target;
                _skipped = (ElementValue)kids[target];
                _skippedOwner = owner;
                _skippedAt = position;
            }

            // AtPosition may take a candidate's parent from the list it was read from: no list has changed since the search
            // began and no element of the start's subtree is in two lists, so the start's map gives that parent, taken or
            // not (an iterfind takes it at once: its add-a-row + row[last()] loop still checked every row, review 10, A-3).
            private bool FastParents(EvalContext ctx)
            {
                if (ctx.XmlTreeVersion != _startVersion)
                    return false;
                if (_unshared == 0)
                    _unshared = ctx.XmlShared == 0 || Scratch(ctx).Unshared(ctx, _start, _startVersion) ? 1 : 2;
                return _unshared == 1;
            }

            // A tag test that only that very tag passes (TagMatches is then `tag == test`).
            private static bool PlainTag(string test)
            {
                return test != "." && test.IndexOf('*') < 0 && !test.StartsWith("{}", StringComparison.Ordinal);
            }

            private bool Passes(EvalContext ctx, ElementValue c, Step step, ElementValue owner)
            {
                foreach (Predicate p in step.Predicates)
                {
                    ctx.Budget.Step();
                    if (p.Position != 0)
                    {
                        if (!AtPosition(ctx, c, p.Position, owner))
                            return false;
                        continue;
                    }
                    BeforeScript(ctx);
                    ForgetSiblings(ctx);   // a text predicate can run a script __bool__ that changes children
                    if (!MatchesPredicate(ctx, c, p))
                        return false;
                }
                return true;
            }

            // ElementPath's [N] / [last()-K]: the element is the N-th of its parent's children with ITS tag
            // (parent.findall(elem.tag)[N - 1] is elem), whatever the step selected
            //
            // owner: the element whose children list e was read from (a child or descendant step), or null. While no
            // element is in two lists (EvalContext.XmlShared) and none changed since the start, the start's map would
            // give owner: a loop that adds a row and finds row[last()] built a map of every row per search.
            private bool AtPosition(EvalContext ctx, ElementValue e, int position, ElementValue owner)
            {
                ElementValue parent;
                if (owner != null && FastParents(ctx))
                {
                    if (ReferenceEquals(e, _skipped) && ReferenceEquals(owner, _skippedOwner) && position == _skippedAt)
                    {
                        // SkipToPosition counted it there, for the check that follows it at once: a later step may
                        // meet the same child after a rename, which no version counts
                        _skipped = null;
                        _skippedOwner = null;
                        return true;
                    }
                    parent = owner;
                }
                else
                {
                    TakeParents(ctx);
                    if (!_map.TryGetParent(e, _mapVersion, out parent))
                        return false;
                }
                StrValue tag = e.Tag as StrValue;
                if (tag == null)
                {
                    BeforeScript(ctx);
                    ForgetSiblings(ctx);   // a tag's __getitem__ can change any parent's children
                    if (!ComparesAsTag(ctx, e.Tag))
                        return false;
                }
                if (tag != null && parent.Children.Count <= SmallParent)
                {
                    int r = SmallPosition(ctx, parent, tag.Value, e, position);
                    if (r >= 0)
                        return r == 1;
                }
                Dictionary<string, List<ElementValue>> byTag = tag == null || parent.Children.Count <= SmallParent ? null : SiblingsByTag(ctx, parent);
                if (byTag != null)
                {
                    List<ElementValue> group;
                    if (ReferenceEquals(byTag, _lastGroupOf) && ReferenceEquals(tag.Value, _lastTag))
                        group = _lastGroup;
                    else if (!byTag.TryGetValue(tag.Value, out group))
                        return false;
                    else
                    {
                        _lastGroupOf = byTag;
                        _lastTag = tag.Value;
                        _lastGroup = group;
                    }
                    int at = position > 0 ? position - 1 : group.Count + position;
                    return at >= 0 && at < group.Count && ReferenceEquals(group[at], e);
                }
                BeforeScript(ctx);
                ForgetSiblings(ctx);   // the __eq__ below can change any parent's children
                var same = new List<ElementValue>();
                // By index: a tag's __eq__ can change the children.
                for (int i = 0; i < parent.Children.Count; i++)
                {
                    ctx.Budget.Step();
                    ElementValue ce = (ElementValue)parent.Children[i];
                    if (PyOps.Equals(ce.Tag, e.Tag, ctx, 0))
                        same.Add(ce);
                }
                int idx = position > 0 ? position - 1 : same.Count + position;
                return idx >= 0 && idx < same.Count && ReferenceEquals(same[idx], e);
            }

            // A small parent's children counted in place, no groups: a dictionary for two children held 240 B a parent
            // (270,000 records past 256 MB) and was slower than 1.1.0's scan. 1 e is at position, 0 it is not, -1 a tag is
            // not a str (its __eq__ may run).
            private const int SmallParent = 16;

            private static int SmallPosition(EvalContext ctx, ElementValue parent, string tag, ElementValue e, int position)
            {
                List<ScriptValue> kids = parent.Children;
                int count = 0;
                for (int i = 0; i < kids.Count; i++)
                {
                    ctx.Budget.StepTreeNode();
                    StrValue t = ((ElementValue)kids[i]).Tag as StrValue;
                    if (t == null)
                        return -1;
                    if (string.Equals(t.Value, tag, StringComparison.Ordinal))
                        count++;
                }
                int want = position > 0 ? position - 1 : count + position;   // among the children with e's tag
                if (want < 0 || want >= count)
                    return 0;
                for (int i = 0, seen = 0; i < kids.Count; i++)
                {
                    if (!string.Equals(((StrValue)((ElementValue)kids[i]).Tag).Value, tag, StringComparison.Ordinal))
                        continue;
                    if (seen++ == want)
                        return ReferenceEquals(kids[i], e) ? 1 : 0;
                }
                return 0;
            }

            // The parent's children grouped by tag, built once per parent while no script code runs (rescanning them
            // per candidate made row[last()] quadratic). Null when a child's tag is not a str: its __eq__ may run.
            private Dictionary<string, List<ElementValue>> SiblingsByTag(EvalContext ctx, ElementValue parent)
            {
                if (_siblings == null)
                    _siblings = new Dictionary<ElementValue, Dictionary<string, List<ElementValue>>>();
                else if (ReferenceEquals(parent, _lastParent))
                    return _lastByTag;
                Dictionary<string, List<ElementValue>> byTag;
                if (!_siblings.TryGetValue(parent, out byTag))
                {
                    long before = _siblingBytes;
                    byTag = _siblings[parent] = GroupByTag(ctx, parent);
                    if (_groupBytes == null)
                        _groupBytes = new Dictionary<ElementValue, long>();
                    _groupBytes[parent] = _siblingBytes - before;
                }
                _lastParent = parent;
                _lastByTag = byTag;
                return byTag;
            }

            private const long GroupsBytes = 240, TagGroupBytes = 96;   // a parent's Dictionary and entry; a tag's List

            private void Hold(EvalContext ctx, long bytes)
            {
                Scratch(ctx).Hold(ctx, bytes);
                _siblingBytes += bytes;
            }

            private Dictionary<string, List<ElementValue>> GroupByTag(EvalContext ctx, ElementValue parent)
            {
                List<ScriptValue> kids = parent.Children;
                // given back with the groups (rebuilt per search, they added up): the dictionary, a list per tag, a slot a child
                Hold(ctx, GroupsBytes + 16L * kids.Count);
                var byTag = new Dictionary<string, List<ElementValue>>(System.StringComparer.Ordinal);
                string lastTag = null;
                List<ElementValue> group = null;
                for (int i = 0; i < kids.Count; i++)
                {
                    ctx.Budget.Step();
                    var ce = (ElementValue)kids[i];
                    StrValue t = ce.Tag as StrValue;
                    if (t == null)
                        return null;
                    if (!ReferenceEquals(t.Value, lastTag))   // a run of one tag (one string, as parsed names are) hashes once
                    {
                        lastTag = t.Value;
                        if (!byTag.TryGetValue(lastTag, out group))
                        {
                            Hold(ctx, TagGroupBytes);
                            byTag[lastTag] = group = new List<ElementValue>();
                        }
                    }
                    group.Add(ce);
                }
                return byTag;
            }

            // The next node on the step's axis that passes its tag test, predicates not applied.
            private ElementValue Raw(EvalContext ctx, Step step, Source s)
            {
                switch (step.Axis)
                {
                    case Axis.Self:
                        if (s.Done)
                            return null;
                        s.Done = true;
                        return s.Node;
                    case Axis.Parent:
                        if (s.Done)
                            return null;
                        s.Done = true;
                        if (s.SeenParents == null)
                            s.SeenParents = new HashSet<ElementValue>();
                        ElementValue parent;
                        if (!_map.TryGetParent(s.Node, _mapVersion, out parent) || !s.SeenParents.Add(parent))
                            return null;
                        Pay(ctx, s, s.SeenParents.Count);
                        return parent;
                    case Axis.Child:
                        // read live by index, as CPython iterates an element: a child removed meanwhile ends it early
                        List<ScriptValue> kids = s.Node.Children;
                        while (s.Index < kids.Count)
                        {
                            ctx.Budget.StepTreeNode();
                            var e = (ElementValue)kids[s.Index++];
                            if (e.Tag is StrValue ? TagMatches(e, step.Test) : OddTagMatches(ctx, e.Tag, step.Test))
                                return e;
                        }
                        return null;
                    default:   // descendant, pre-order, the context node itself excluded
                        if (!s.Done)
                        {
                            s.Done = true;   // elem.iter(tag) compares the context node's tag before it is left out
                            if (!(s.Node.Tag is StrValue) && !IsWildcard(step.Test))
                                OddTagMatches(ctx, s.Node.Tag, step.Test);
                        }
                        while (s.Walk.Depth > 0)
                        {
                            ElementValue n = s.Walk.NextChild();
                            if (n == null)
                            {
                                DropGroups(ctx, s.Walk.Ascend());
                                continue;
                            }
                            ctx.Budget.StepTreeNode();
                            s.Owner = s.Walk.Top;
                            s.Walk.Descend(ctx, n);
                            if (n.Tag is StrValue ? TagMatches(n, step.Test) : OddTagMatches(ctx, n.Tag, step.Test))
                                return n;
                        }
                        return null;
                }
            }

            // A tag that is not a str against a step's test: its own __eq__ runs, as in CPython's `e.tag == tag`, and can
            // change any list, so the map is taken and the groups go first (review 9, C-5).
            private bool OddTagMatches(EvalContext ctx, ScriptValue tag, string test)
            {
                if (test == "*" || test == ".")
                    return true;
                if (InertTag(tag) || ComparedTag(test) == null)
                    return false;
                BeforeScript(ctx);
                ForgetSiblings(ctx);
                return OddTagEquals(ctx, tag, test);
            }

            private static void Pay(EvalContext ctx, Source s, long entries)
            {
                if (entries <= s.Paid)
                    return;
                ctx.Values.PreCharge(8L * (entries - s.Paid));
                s.Paid = entries;
            }
        }

        // A search's parent map: {child: parent} over the start's subtree, held by the cache slot and by each walk
        // that took it.
        internal sealed class ParentMap
        {
            internal const long EntryBytes = 48, LateBytes = 64;
            internal readonly Dictionary<ElementValue, ElementValue> Parents = new Dictionary<ElementValue, ElementValue>();   // reference identity, as CPython's is
            internal long Bytes;    // held in the run's scratch while Holders > 0
            internal int Holders;

            // The leaves appended since the build (ParentMapCache.ListChanged), each with the tree version it came with:
            // Parents is as built, and a walk sees the entries that were there when it took the map.
            private struct LateParent
            {
                internal ElementValue Parent;
                internal long Since;
            }

            private Dictionary<ElementValue, LateParent> _late;
            internal int LateUnused;   // added since a search last took the map
            internal int Carried;      // changes outside the subtree since a search last took the map

            internal bool TryGetParent(ElementValue e, long version, out ElementValue parent)
            {
                if (Parents.TryGetValue(e, out parent))
                    return true;
                LateParent late;
                if (_late == null || !_late.TryGetValue(e, out late) || late.Since > version)
                    return false;
                parent = late.Parent;
                return true;
            }

            // Whether e has a parent in the map as it is now.
            internal bool Has(ElementValue e)
            {
                return Parents.ContainsKey(e) || (_late != null && _late.ContainsKey(e));
            }

            internal void AddLate(ElementValue leaf, ElementValue parent, long since)
            {
                if (_late == null)
                    _late = new Dictionary<ElementValue, LateParent>();
                _late[leaf] = new LateParent { Parent = parent, Since = since };
                LateUnused++;
            }
        }

        // CPython builds a search's parent map when the search first needs it; the run keeps its last few, and one is
        // reused while no children list in the run has changed since it was built (EvalContext.XmlTreeVersion): it is
        // then the map a new build would give. 80 rounds of row[1] over 62K elements built 160 maps, out of MaxAllocBytes.
        //
        // The maps and a search's sibling groups are scratch: what is held at once is charged, at its high-water mark,
        // and a map given back by every holder is held no longer. Charging each build cumulatively refused a loop that
        // adds a row and finds row[last()] at 2,900 rows (1.1.0 ran 6,000 in a second); a map an unfinished iterfind
        // keeps is never given back, so a thousand of them are charged a thousand times.
        internal sealed class ParentMapCache
        {
            private const int Size = 4;
            private readonly ElementValue[] _start = new ElementValue[Size];
            private readonly long[] _version = new long[Size];
            private readonly ParentMap[] _map = new ParentMap[Size];
            private int _next;
            private long _held, _paid;
            private long _current = -1;   // the last tree version a slot was current for

            internal ParentMap Get(ElementValue start, long version)
            {
                for (int i = 0; i < Size; i++)
                {
                    if (_map[i] != null && ReferenceEquals(_start[i], start) && _version[i] == version)
                    {
                        _map[i].LateUnused = 0;
                        _map[i].Carried = 0;
                        return _map[i];
                    }
                }
                return null;
            }

            // A children list has just changed (the run's version is one more). A map of a subtree the owner is not in
            // is still the map a new build would give: it stays current. So does a map that has the owner when the change
            // only appended leaf, a childless element that was in no list: it has one entry more, which a walk that took
            // the map earlier does not see. A loop that adds a row and searches, or builds one tree while it searches
            // another, built a map per search (review 10, A-3; review 9). Any other change leaves the slot stale.
            private const int MaxLateUnused = 64;   // leaves added for no search: a tree built after one search adds no more
            private const int MaxCarried = 4096;    // changes elsewhere for no search: a tree the script is done with goes

            internal void ListChanged(EvalContext ctx, ElementValue owner, ElementValue leaf)
            {
                long now = ctx.XmlTreeVersion;
                if (_current != now - 1)
                    return;
                for (int i = 0; i < Size; i++)
                {
                    ParentMap map = _map[i];
                    if (map == null || _version[i] != now - 1)
                        continue;
                    if (ReferenceEquals(_start[i], owner) || map.Has(owner))
                    {
                        if (leaf == null || map.LateUnused >= MaxLateUnused || map.Has(leaf))
                            continue;
                        Hold(ctx, ParentMap.LateBytes);   // refused: the slot stays stale
                        map.Bytes += ParentMap.LateBytes;
                        map.AddLate(leaf, owner, now);
                    }
                    else if (++map.Carried > MaxCarried)
                    {
                        continue;
                    }
                    _version[i] = now;
                    _current = now;
                }
            }

            // The run's version only grows: a map of an older one is never asked for again. A map kept current across
            // changes outside its tree that no search has taken it since goes too (four trees, each changed and searched
            // in turn, held four maps, out of MaxAllocBytes); one that only gained leaves of its own stays.
            internal void DropStale(long version)
            {
                for (int i = 0; i < Size; i++)
                {
                    if (_map[i] != null && (_version[i] != version || _map[i].Carried > 0))
                    {
                        Drop(_map[i]);
                        _map[i] = null;
                        _start[i] = null;
                    }
                }
            }

            internal void Put(ElementValue start, long version, ParentMap map)
            {
                if (_map[_next] != null)
                    Drop(_map[_next]);
                _start[_next] = start;
                _version[_next] = version;
                _current = version;
                _map[_next] = map;
                map.Holders++;
                _next = (_next + 1) % Size;
            }

            internal void Drop(ParentMap map)
            {
                if (--map.Holders == 0)
                    Release(map.Bytes);
            }

            // Walks that end with their call, kept for the next one: a lent walk is no other caller's until it is
            // returned, so a search nested in a predicate's script code takes another.
            private const int SpareWalks = 4;
            private readonly ElementWalk[] _spareWalks = new ElementWalk[SpareWalks];
            private int _spares;

            internal ElementWalk RentWalk()
            {
                if (_spares == 0)
                    return new ElementWalk();
                ElementWalk w = _spareWalks[--_spares];
                _spareWalks[_spares] = null;
                return w;
            }

            internal void ReturnWalk(ElementWalk walk)
            {
                walk.Clear();
                if (_spares < SpareWalks)
                    _spareWalks[_spares++] = walk;
            }

            // A lazy walk's hold on a map. An iterfind dropped before its end kept its map held, and charged, to the end of
            // the run: 1,500 rounds of add-a-row + next(iterfind('row[last()]')) charged 54 MB (1.1.0: 1 MB). Once the CLR
            // has collected the walk, its map goes back (swept before a new map is built, after a collection).
            internal sealed class MapLease
            {
                internal readonly WeakReference Walk;
                internal readonly ParentMap Map;
                internal bool Done;   // the walk closed it itself

                internal MapLease(object walk, ParentMap map)
                {
                    Walk = new WeakReference(walk);
                    Map = map;
                }
            }

            private readonly List<MapLease> _leases = new List<MapLease>();
            private int _leaseGc = -1;
            private int _pruneLeasesAt = 256;

            internal MapLease Lease(object walk, ParentMap map)
            {
                // the list is pruned as it doubles, so leases of walks that ended (Done) never pile up while the tree
                // stays unchanged and no new map is built (5M iterfinds over one map kept 5M leases)
                if (_leases.Count >= _pruneLeasesAt)
                {
                    PruneLeases();
                    _pruneLeasesAt = Math.Max(256, 2 * _leases.Count);
                }
                var lease = new MapLease(walk, map);
                _leases.Add(lease);
                return lease;
            }

            internal void SweepLeases()
            {
                if (_leases.Count == 0 || GC.CollectionCount(0) == _leaseGc)
                    return;
                _leaseGc = GC.CollectionCount(0);
                PruneLeases();
            }

            private void PruneLeases()
            {
                _leases.RemoveAll(l =>
                {
                    if (l.Done)
                        return true;
                    if (l.Walk.IsAlive)
                        return false;
                    Drop(l.Map);
                    return true;
                });
            }

            // More scratch held: charged when the total passes the most held before.
            internal void Hold(EvalContext ctx, long bytes)
            {
                long held = _held + bytes;
                if (held > _paid)
                {
                    ctx.Values.PreCharge(held - _paid);
                    _paid = held;
                }
                _held = held;
            }

            internal void Release(long bytes)
            {
                _held -= bytes;
            }

            // Whether no element of start's subtree is in two lists (FastParents), known for the last few (start, version)
            // as the maps are: walked only while some element of the run is (EvalContext.XmlShared), one walk per change.
            private readonly ElementValue[] _uStart = new ElementValue[Size];
            private readonly long[] _uVersion = new long[Size];
            private readonly bool[] _uAnswer = new bool[Size];
            private int _uNext;

            internal bool Unshared(EvalContext ctx, ElementValue start, long version)
            {
                for (int i = 0; i < Size; i++)
                {
                    if (ReferenceEquals(_uStart[i], start) && _uVersion[i] == version)
                        return _uAnswer[i];
                }
                bool answer = true;
                ElementWalk walk = RentWalk();
                try
                {
                    walk.Reset(ctx, start);
                    while (walk.Depth > 0)
                    {
                        ElementValue n = walk.NextChild();
                        if (n == null)
                        {
                            walk.Ascend();
                            continue;
                        }
                        ctx.Budget.StepTreeNode();
                        if (n.InSeveralLists)
                        {
                            answer = false;
                            break;
                        }
                        walk.Descend(ctx, n);
                    }
                }
                finally
                {
                    ReturnWalk(walk);
                }
                _uStart[_uNext] = start;
                _uVersion[_uNext] = version;
                _uAnswer[_uNext] = answer;
                _uNext = (_uNext + 1) % Size;
                return answer;
            }
        }

        // A walk lent by the run's scratch, for one that is given back when it ends (ElementWalk).
        internal static ElementWalk RentWalk(EvalContext ctx) { return Scratch(ctx).RentWalk(); }

        internal static void ReturnWalk(EvalContext ctx, ElementWalk walk) { Scratch(ctx).ReturnWalk(walk); }

        private static ParentMapCache Scratch(EvalContext ctx)
        {
            ParentMapCache cache = ctx.XmlParentMaps;
            if (cache == null)
                ctx.XmlParentMaps = cache = new ParentMapCache();
            return cache;
        }

        // The map for a walk to hold (Holders counts it): the cached one, or a new build.
        private static ParentMap SharedParentMap(EvalContext ctx, ElementValue start)
        {
            ParentMapCache cache = Scratch(ctx);
            long version = ctx.XmlTreeVersion;
            ParentMap map = cache.Get(start, version);
            if (map == null)
            {
                cache.SweepLeases();
                cache.DropStale(version);
                map = BuildParentMap(ctx, cache, start);   // runs no script code: nothing can change the tree meanwhile
                cache.Put(start, version, map);
            }
            map.Holders++;
            return map;
        }

        // '..' resolves through a map built from the START element's subtree, so a parent above the node
        // the search began at is simply not there — which is CPython's documented answer for it.
        private static ParentMap BuildParentMap(EvalContext ctx, ParentMapCache cache, ElementValue start)
        {
            // {c: p for p in start.iter() for c in p}: every parent in document order sets all its children,
            // so a child shared by several parents maps to the last of them, as in CPython
            var map = new ParentMap();
            ElementWalk walk = cache.RentWalk();
            try
            {
                walk.Reset(ctx, start);
                MapChildren(ctx, cache, map, start);
                while (walk.Depth > 0)
                {
                    ElementValue n = walk.NextChild();
                    if (n == null)
                    {
                        walk.Ascend();
                        continue;
                    }
                    ctx.Budget.StepTreeNode();
                    walk.Descend(ctx, n);
                    MapChildren(ctx, cache, map, n);
                }
            }
            finally
            {
                cache.ReturnWalk(walk);
            }
            return map;
        }

        private static void MapChildren(EvalContext ctx, ParentMapCache cache, ParentMap map, ElementValue p)
        {
            foreach (ScriptValue c in p.Children)
            {
                int before = map.Parents.Count;
                map.Parents[(ElementValue)c] = p;
                if (map.Parents.Count > before)
                {
                    cache.Hold(ctx, ParentMap.EntryBytes);   // one entry per element of the subtree, held as it is made
                    map.Bytes += ParentMap.EntryBytes;
                }
            }
        }

        // parent.findall(elem.tag) for a tag that is not a str: CPython reads it as a path, and `path[-1:]`, the cache
        // key or the tokenizer refuses it. True for bytes that read as a plain name, which are compared as a tag;
        // false for a dict, whose KeyError goes the way of the IndexError (review 9, C-5).
        private static bool ComparesAsTag(EvalContext ctx, ScriptValue tag)
        {
            BytesValue bytes = tag as BytesValue;
            if (bytes != null)
            {
                if (!PlainName(bytes.Data))
                    throw Raise.TypeError(ctx, "cannot use a string pattern on a bytes-like object");
                return true;
            }
            if (tag.Kind == ValueKind.Dict)
                return false;
            if (tag is QNameValue || ReferenceEquals(tag, Modules.XmlModule.CommentFn) || ReferenceEquals(tag, Modules.XmlModule.ProcessingInstructionFn))
                throw Raise.TypeError(ctx, "'" + (tag is QNameValue ? "QName" : "function") + "' object is not subscriptable");
            PyOps.GetItem(tag, ctx.Values.Slice(ctx.Values.Int(-1), ctx.Values.None, ctx.Values.None), ctx);
            PyOps.Hash(tag, ctx, 0);
            throw Raise.TypeError(ctx, "expected string or bytes-like object, got '" + tag.PyTypeName + "'");
        }

        // _elementtree's checkpath over bytes: no path character outside a {namespace}, no {} or {*} prefix.
        private static bool PlainName(byte[] name)
        {
            bool plain = !(name.Length >= 3 && name[0] == '{' && (name[1] == '}' || (name[1] == '*' && name[2] == '}')));
            bool check = true;
            for (int i = 0; i < name.Length && plain; i++)
            {
                byte b = name[i];
                if (b == '{')
                    check = false;
                else if (b == '}')
                    check = true;
                else if (check && (b == '/' || b == '*' || b == '[' || b == '@' || b == '.'))
                    plain = false;
            }
            return plain;
        }

        // ElementPath's _is_wildcard_tag: a step over these compares nothing with the context node.
        private static bool IsWildcard(string test)
        {
            return test == "*" || test.StartsWith("{*}", StringComparison.Ordinal) || test.EndsWith("}*", StringComparison.Ordinal);
        }

        // The tag a test compares with `==` ({}name and {*}name compare name), null where it compares nothing.
        private static string ComparedTag(string test)
        {
            if (test == "*" || test == "." || test.EndsWith("}*", StringComparison.Ordinal))
                return null;
            if (test.StartsWith("{}", StringComparison.Ordinal))
                return test.Substring(2);
            return test.StartsWith("{*}", StringComparison.Ordinal) ? test.Substring(3) : test;
        }

        // None and a function (Comment, ProcessingInstruction) equal no str, and no script runs to say so.
        private static bool InertTag(ScriptValue tag)
        {
            return tag.Kind == ValueKind.None || tag is BuiltinFunctionValue;
        }

        // `tag == text` for a tag that is not a str: a QName equals its text, a script class answers with its __eq__.
        internal static bool OddTagIs(EvalContext ctx, ScriptValue tag, string text)
        {
            return !InertTag(tag) && PyOps.Equals(tag, ctx.Values.Str(text), ctx, 0);
        }

        private static bool OddTagEquals(EvalContext ctx, ScriptValue tag, string test)
        {
            if (test == "*" || test == ".")
                return true;
            string compared = ComparedTag(test);
            return compared != null && OddTagIs(ctx, tag, compared);
        }

        private static bool HasOddTag(ElementValue e)
        {
            bool odd = false;
            for (int k = 0; k < e.Children.Count && !odd; k++)
                odd = !(((ElementValue)e.Children[k]).Tag is StrValue);
            return odd;
        }

        // [tag='text'] over children with a tag that is not a str: elem.findall(tag) first, as in CPython, so every
        // tag's __eq__ has run before a text is read.
        private static bool OddTextPredicate(EvalContext ctx, ElementValue e, Predicate p)
        {
            var found = new List<ElementValue>();
            for (int k = 0; k < e.Children.Count; k++)
            {
                ElementValue child = (ElementValue)e.Children[k];
                if (child.Tag is StrValue ? TagMatches(child, p.ChildTag) : OddTagEquals(ctx, child.Tag, p.ChildTag))
                    found.Add(child);
            }
            bool matched = false;
            for (int k = 0; k < found.Count && !matched; k++)
                matched = TextEquals(ctx, found[k], p.Value) != p.Negated;
            return matched;
        }

        private static bool TagMatches(ElementValue e, string test)
        {
            if (test == "*" || test == ".")
                return true;
            string tag = TagText(e.Tag);
            if (tag == test)
                return true;
            return test.Length > 2 && test[0] == '{' && tag != null && WildcardMatches(tag, test);
        }

        // ElementPath's _prepare_tag: {*}* any tag, {}* one in no namespace, {*}name that local name in any
        // namespace or none, {ns}* any tag in ns; {}name is plain name.
        private static bool WildcardMatches(string tag, string test)
        {
            if (test == "{*}*")
                return true;
            if (test == "{}*")
                return tag.Length > 0 && tag[0] != '{';
            if (test.StartsWith("{*}", StringComparison.Ordinal))
                return tag == test.Substring(3) || tag.EndsWith(test.Substring(2), StringComparison.Ordinal);
            if (test.EndsWith("}*", StringComparison.Ordinal))
                return tag.StartsWith(test.Substring(0, test.Length - 1), StringComparison.Ordinal);
            return test.StartsWith("{}", StringComparison.Ordinal) && tag == test.Substring(2);
        }

        private static bool MatchesPredicate(EvalContext ctx, ElementValue e, Predicate p)
        {
            if (p.Attr != null)
            {
                ScriptValue got = null;
                DictValue attrib = e.AttribOrNull;
                bool has = attrib != null && attrib.TryGet(ctx.Values.Str(p.Attr), ctx, out got);
                if (p.Value == null)
                    return has;
                // [@a!='v'] is `elem.get(a) is not None and elem.get(a) != v`: an element without the
                // attribute is not selected, as with the tag form below.
                StrValue gs = got as StrValue;   // a non-str value (e.set('a', 5)) never equals the text
                bool equal = has && gs != null && gs.Value == p.Value;
                return p.Negated ? has && got.Kind != ValueKind.None && !equal : equal;
            }
            if (p.ChildTag == "." && p.Value != null)
            {
                bool same = TextEquals(ctx, e, p.Value);   // [.='text'] over the COMPLETE text content
                return p.Negated ? !same : same;
            }
            if (p.Value != null && HasOddTag(e))
                return OddTextPredicate(ctx, e, p);
            // by index: the text walk can run a script __bool__ that changes e's children, and so can a tag's __eq__
            for (int k = 0; k < e.Children.Count; k++)
            {
                ElementValue child = (ElementValue)e.Children[k];
                if (!(child.Tag is StrValue ? TagMatches(child, p.ChildTag) : OddTagEquals(ctx, child.Tag, p.ChildTag)))
                    continue;
                if (p.Value == null)
                    return true;                                     // [tag]: presence
                // [tag='t'] / [tag!='t'] both need SUCH A CHILD to exist; with none, neither selects.
                if (TextEquals(ctx, child, p.Value) != p.Negated)
                    return true;
            }
            return false;
        }

        // The complete text content, which is what CPython compares in [tag='text'] and [.='text']:
        // "".join(e.itertext()), not e.text alone.
        // Compared part by part, never joined: a large text shared by many candidates is no copy per candidate.
        // The walk still runs to the end, as the join raises for a non-str part or a text past MaxStrChars.
        private static bool TextEquals(EvalContext ctx, ElementValue e, string value)
        {
            ParentMapCache scratch = Scratch(ctx);
            ElementWalk walk = scratch.RentWalk();
            long at = 0;
            bool same = true;
            try
            {
                var texts = new TextIterator(e, walk);
                ScriptValue part;
                for (int index = 0; (part = texts.NextText(ctx)) != null; index++)
                {
                    string s = PartText(ctx, part, index);
                    long end = at + s.Length;
                    ctx.Values.CheckStrLen(end);
                    if (same)
                    {
                        if (end > value.Length)
                        {
                            same = false;
                        }
                        else
                        {
                            ctx.Budget.ChargeLinear(s.Length);
                            same = string.CompareOrdinal(value, (int)at, s, 0, s.Length) == 0;
                        }
                    }
                    at = end;
                }
            }
            finally
            {
                scratch.ReturnWalk(walk);
            }
            return same && at == value.Length;
        }

        // elem.text, then each descendant's text and tail in document order (the element's own tail is
        // not part of it): itertext() joined, so a deep subtree stays off the C# stack.
        internal static void AppendText(EvalContext ctx, ElementValue root, System.Text.StringBuilder sb)
        {
            ParentMapCache scratch = Scratch(ctx);
            ElementWalk walk = scratch.RentWalk();
            try
            {
                var texts = new TextIterator(root, walk);
                ScriptValue part;
                while ((part = texts.NextText(ctx)) != null)
                    AppendPart(ctx, sb, part);
            }
            finally
            {
                scratch.ReturnWalk(walk);
            }
        }

        // CPython joins itertext() here, so a text or tail the script set to a non-str is its TypeError.
        private static void AppendPart(EvalContext ctx, System.Text.StringBuilder sb, ScriptValue part)
        {
            string s = PartText(ctx, part, -1);
            ctx.Values.CheckStrLen((long)sb.Length + s.Length);   // one large text may be shared by many elements
            ctx.Budget.ChargeLinear(s.Length);
            sb.Append(s);
        }

        // index: the join's item number in its message, as CPython's; -1 leaves it out
        private static string PartText(EvalContext ctx, ScriptValue part, int index)
        {
            StrValue s = part as StrValue;
            if (s == null)
            {
                throw Raise.TypeError(ctx, "sequence item" + (index < 0 ? "" : " " + index) + ": expected str instance, "
                    + part.PyTypeName + " found");
            }
            return s.Value;
        }

        // ---- path parsing ----

        private static List<Step> ParsePath(EvalContext ctx, string path, Dictionary<string, string> ns)
        {
            if (path.StartsWith("/", System.StringComparison.Ordinal))
                throw Raise.Make(ctx, PyExceptionTypes.SyntaxError, "cannot use absolute path on element");
            if (path.EndsWith("/", System.StringComparison.Ordinal))
                path += "*";   // ElementPath's implicit all
            var steps = new List<Step>();
            ctx.Budget.ChargeLinear(path.Length);
            bool descendant = false;
            var closers = new Closers(path);
            // part by part, each charged as it is made: a split of 'x/' * 4M built 4M strings and steps uncharged
            for (int start = 0, idx = 0; ; idx++)
            {
                int slash = NextSlash(path, start, closers);
                int end = slash < 0 ? path.Length : slash;
                if (end == start)
                {
                    if (idx != 0)
                        descendant = true;   // '//' (a leading '/' is skipped)
                }
                else
                {
                    ctx.Budget.Step();
                    ctx.Values.PreCharge(StepBytes + 2L * (end - start));
                    string part = path.Substring(start, end - start);
                    Axis axis = descendant ? Axis.Descendant : Axis.Child;
                    List<Step> spaced = SpacedSteps(ctx, part, axis, ns);
                    if (spaced == null)
                        steps.Add(ParseStep(ctx, part, axis, ns));
                    else
                        steps.AddRange(spaced);
                    descendant = false;
                }
                if (slash < 0)
                    break;
                start = slash + 1;
            }
            if (steps.Count == 0)
                throw Raise.Make(ctx, PyExceptionTypes.SyntaxError, "invalid path");
            return steps;
        }

        // A part's steps when it has whitespace outside a {uri} or a predicate, else null. CPython's tokenizer reads
        // such a run as a step of its own, the children tagged '' (ops['']): 'a b' is a, then '', then b, and a
        // predicate right after the whitespace is that step's (review 13, row 35). Each step past the part's first is
        // charged as ParsePath charges a part.
        private static List<Step> SpacedSteps(EvalContext ctx, string part, Axis axis, Dictionary<string, string> ns)
        {
            int lastBrace = part.LastIndexOf('}');
            int space = NextSpace(part, 0, lastBrace);
            if (space < 0)
                return null;
            var steps = new List<Step>();
            int from = 0;
            while (space >= 0)
            {
                if (space > from)
                {
                    ChargeAnother(ctx, steps);
                    steps.Add(ParseStep(ctx, part.Substring(from, space - from), axis, ns));
                    axis = Axis.Child;
                }
                from = space;
                while (from < part.Length && PyUnicode.IsPythonSpace(part[from]))
                    from++;
                space = NextSpace(part, from, lastBrace);
                int subEnd = space < 0 ? part.Length : space;
                ChargeAnother(ctx, steps);
                Step empty;
                int predicates = PredicatesEnd(part, from, subEnd);
                if (predicates > from)
                {
                    // its predicates; a name after them is a step of its own, as the tokenizer reads it
                    empty = ParseStep(ctx, part.Substring(from, predicates - from), axis, ns);
                    from = predicates;
                }
                else
                {
                    empty = new Step { Axis = axis };
                }
                empty.Test = "";   // no default namespace for it: the tokenizer qualifies only a tag that is not empty
                steps.Add(empty);
                axis = Axis.Child;
            }
            if (from < part.Length)
            {
                ChargeAnother(ctx, steps);
                steps.Add(ParseStep(ctx, part.Substring(from), axis, ns));
            }
            return steps;
        }

        // A step past a part's first, before it is made: ParsePath charged the part for one.
        private static void ChargeAnother(EvalContext ctx, List<Step> steps)
        {
            if (steps.Count > 0)
            {
                ctx.Budget.Step();
                ctx.Values.PreCharge(StepBytes);
            }
        }

        // The end of the closed predicates that start at `from`, or `from` when none does. An unclosed one is taken whole,
        // for ParseStep to refuse.
        private static int PredicatesEnd(string part, int from, int end)
        {
            int at = from;
            while (at < end && part[at] == '[')
            {
                int close = PredicateEnd(part, at);
                at = close < 0 || close >= end ? end : close + 1;
            }
            return at;
        }

        // The next whitespace of a part at or after start (where a name may begin) that is not inside a {uri} or a
        // predicate, or -1.
        private static int NextSpace(string part, int start, int lastBrace)
        {
            int at = -1;
            for (int i = start; at < 0 && i < part.Length; i++)
            {
                char c = part[i];
                if (c == '{' && lastBrace > i + 1 && (i == start || part[i - 1] == ']' || PyUnicode.IsPythonSpace(part[i - 1])))   // a {uri} opens a name
                {
                    int close = part.IndexOf('}', i + 1);
                    if (close > i + 1)
                        i = close;
                }
                else if (c == '[')
                {
                    int close = PredicateEnd(part, i);
                    i = close < 0 ? part.Length : close;   // no ']': ParseStep refuses the predicate
                }
                else if (PyUnicode.IsPythonSpace(c))
                {
                    at = i;
                }
            }
            return at;
        }

        // The last position of each closer: an opener after it has no closer, so it is not searched for
        // ('{' * 150000 searched the rest of the path for every brace).
        private struct Closers
        {
            public readonly int Brace, Single, Double;

            public Closers(string path)
            {
                Brace = path.LastIndexOf('}');
                Single = path.LastIndexOf('\'');
                Double = path.LastIndexOf('"');
            }
        }

        // A '/' that separates steps: not one inside {uri} or a quoted predicate value.
        private static int NextSlash(string path, int start, Closers closers)
        {
            bool inPredicate = false;
            for (int i = start; i < path.Length; i++)
            {
                char c = path[i];
                if (c == '/')
                    return i;
                if (c == '{')
                {
                    if (closers.Brace > i + 1)
                    {
                        int close = path.IndexOf('}', i + 1);
                        if (close > i + 1)
                            i = close;
                    }
                }
                else if (c == '[')
                    inPredicate = true;
                else if (c == ']')
                    inPredicate = false;
                else if (inPredicate && (c == '\'' || c == '"'))
                {
                    if ((c == '\'' ? closers.Single : closers.Double) > i)
                        i = path.IndexOf(c, i + 1);
                }
            }
            return -1;
        }

        // The ']' of the predicate opened at `open`: not one inside a quoted value.
        private static int PredicateEnd(string part, int open)
        {
            for (int i = open + 1; i < part.Length; i++)
            {
                char c = part[i];
                if (c == ']')
                    return i;
                if (c == '\'' || c == '"')
                {
                    int close = part.IndexOf(c, i + 1);
                    if (close > 0)
                        i = close;
                }
            }
            return -1;
        }

        private static Step ParseStep(EvalContext ctx, string part, Axis axis, Dictionary<string, string> ns)
        {
            var step = new Step { Axis = axis };
            int uriEnd = part.Length > 0 && part[0] == '{' ? part.IndexOf('}') : -1;   // a uri's own [ and ( are text
            int br = part.IndexOf('[', uriEnd + 1);
            string test = br < 0 ? part : part.Substring(0, br);
            string local = test.Substring(uriEnd + 1);
            if (test == ".")
                step.Axis = Axis.Self;
            else if (test == "..")
                step.Axis = Axis.Parent;
            else if (local.IndexOf('(') >= 0 || local.IndexOf(')') >= 0)
                throw BadPath(ctx, "unsupported path syntax", part);   // text(), node(), ...
            step.Test = step.Axis == Axis.Parent ? "*" : ExpandPrefix(ctx, test, ns, true);
            int i = br;
            while (i >= 0 && i < part.Length && part[i] == '[')
            {
                int close = PredicateEnd(part, i);
                if (close < 0)
                    throw Raise.ValueError(ctx, "invalid predicate");
                ctx.Budget.Step();
                ctx.Values.PreCharge(PredicateBytes + 2L * (close - i));
                Predicate p = ParsePredicate(ctx, part.Substring(i + 1, close - i - 1), ns);
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
            {
                if (pos == 0)
                    throw Raise.Make(ctx, PyExceptionTypes.SyntaxError, "XPath position >= 1 expected");
                if (pos == int.MinValue)
                    throw Raise.Make(ctx, PyExceptionTypes.SyntaxError, "XPath offset from last() must be negative");
                return new Predicate { Position = pos };
            }
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
                pos = Saturated(body);   // past any child count: matches nothing, as CPython's IndexError
                return true;
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
                int back = Saturated(rest.Substring(1).Trim());
                pos = back == 0 ? int.MinValue : -1 - System.Math.Min(back, int.MaxValue - 1);   // [last()-0]: CPython's "must be negative"
                return true;
            }
            return false;
        }

        // Digits read as an int, capped at int.MaxValue.
        private static int Saturated(string digits)
        {
            long v = 0;
            for (int i = 0; i < digits.Length; i++)
                v = System.Math.Min(v * 10 + (digits[i] - '0'), int.MaxValue);
            return (int)v;
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
        // An undefined prefix is CPython's SyntaxError; an empty one (':b') its KeyError.
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
                {
                    if (prefix.Length == 0)
                        throw Raise.KeyError(ctx, ctx.Values.Str(":"));
                    throw Raise.Make(ctx, PyExceptionTypes.SyntaxError, "prefix " + ctx.Values.Str(prefix).Repr(ctx) + " not found in prefix map");
                }
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
        private readonly ElementValue _root;
        private ElementWalk _walk;   // lent at the start, given back at the end
        private readonly string _tagFilter;
        private bool _started;

        internal ElementIterIterator(ElementValue root, string tagFilter)
        {
            _root = root;
            _tagFilter = tagFilter;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (!_started)
            {
                _started = true;
                _walk = ElementXPath.RentWalk(ctx);
                _walk.Reset(ctx, _root);
                if (Wanted(ctx, _root))
                {
                    value = _root;
                    return true;
                }
            }
            while (_walk != null && _walk.Depth > 0)
            {
                ElementValue e = _walk.NextChild();
                if (e == null)
                {
                    _walk.Ascend();
                    continue;
                }
                // Charged per node: a tree may contain itself (a.append(a)), and a filter that matches
                // nothing walked it forever inside one call; the walk's depth cap ends that.
                ctx.Budget.StepTreeNode();
                _walk.Descend(ctx, e);
                if (Wanted(ctx, e))
                {
                    value = e;
                    return true;
                }
            }
            if (_walk != null)
            {
                ElementXPath.ReturnWalk(ctx, _walk);   // a drained iter() reads nothing more; a dropped one keeps its walk
                _walk = null;
            }
            value = null;
            return false;
        }

        // iter(tag) is `e.tag == tag`: a tag that is not a str answers with its own __eq__ (review 9, C-5)
        private bool Wanted(EvalContext ctx, ElementValue e)
        {
            if (_tagFilter == null || _tagFilter == "*")
                return true;
            StrValue tag = e.Tag as StrValue;
            return tag != null ? tag.Value == _tagFilter : ElementXPath.OddTagIs(ctx, e.Tag, _tagFilter);
        }
    }
}
