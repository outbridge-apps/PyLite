namespace Outbridge.PyLite.Runtime.Values
{
    // A depth-first walk over an element subtree on a stack of (element, next child) frames, so its memory
    // follows the depth, not the pending siblings: pushing every child held 1800 entries for 900 shared
    // copies of a 900-child element, past a 1000-item cap that CPython's walk never meets. Children are
    // read live by index, as CPython's iteration reads them, so a mutation during a lazy walk is seen and
    // never faults. A tree that contains itself deepens without end: the depth is capped by
    // MaxCollectionItems and each frame past the first arrays is charged once, at the high-water mark.
    // The first eight frames are the walk's own arrays, part of whatever made it: charged per walk they were
    // 12 B a candidate of a text predicate that nothing gave back (review 12, row 12). A walk that ends with its
    // call is lent by the run's scratch (ParentMapCache.RentWalk), so its deeper frames are paid once, not per
    // walk: 1,500 lookups over 300 rows 60 deep were 256 MB (review 13, row 24).
    internal sealed class ElementWalk
    {
        private const int FreeFrames = 8;
        private ElementValue[] _el = new ElementValue[FreeFrames];
        private int[] _next = new int[FreeFrames];
        private int _depth;
        private long _paid = FreeFrames;

        internal int Depth { get { return _depth; } }

        // The element whose children are being read.
        internal ElementValue Top { get { return _el[_depth - 1]; } }

        internal void Reset(EvalContext ctx, ElementValue root)
        {
            _depth = 0;
            Descend(ctx, root);
        }

        // The top frame's next child, or null when its children are done.
        internal ElementValue NextChild()
        {
            int top = _depth - 1;
            var kids = _el[top].Children;
            return _next[top] < kids.Count ? (ElementValue)kids[_next[top]++] : null;
        }

        // Read e's children next.
        internal void Descend(EvalContext ctx, ElementValue e)
        {
            if (_depth == _el.Length)
            {
                ctx.Values.EnsureCollectionSize(_depth + 1L);
                int size = (int)System.Math.Min(2L * _el.Length, ctx.Limits.MaxCollectionItems);
                System.Array.Resize(ref _el, size);
                System.Array.Resize(ref _next, size);
            }
            _el[_depth] = e;
            _next[_depth] = 0;
            _depth++;
            if (_depth > _paid)
            {
                ctx.Values.PreCharge(12L * (_depth - _paid));
                _paid = _depth;
            }
        }

        // No frame left, the arrays (and what they were paid) kept for the next walk.
        internal void Clear()
        {
            System.Array.Clear(_el, 0, _depth);
            _depth = 0;
        }

        // Done with the top element's children: its frame is dropped and the element returned.
        internal ElementValue Ascend()
        {
            ElementValue e = _el[--_depth];
            _el[_depth] = null;
            return e;
        }
    }
}
