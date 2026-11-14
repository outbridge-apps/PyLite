using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // The second sandbox review (2026-10): an __eq__, __lt__, key function or iterator - or the __bool__ of
    // what a comparison returned - that clears or rebuilds the container being walked made an internal engine
    // error: the walk kept an index or a count from before the callback. Each walk now does what CPython's
    // does: reads the live size, checks the container's state after a comparison, snapshots the subject, or
    // starts a hash probe over. The expected answers are CPython 3.14's (run in Pyodide), byte for byte.
    [Trait("Category", "Compatibility")]
    public class MutationDuringCallbackTests
    {
        [Theory]
        [InlineData(@"
L = []; M = []
class A:
    def __eq__(self, o):
        L.clear()
        return False
    def __lt__(self, o):
        return True
L.extend([A() for i in range(5)]); M.extend([A() for i in range(5)])
r = (L < M, len(L))
", "(True, 0)")]
        [InlineData(@"
L = []; M = []
class A:
    def __eq__(self, o):
        L.clear()
        return True
L.extend([A() for i in range(5)]); M.extend([A() for i in range(5)])
r = (L < M, L <= M, M > L, len(L))
", "(True, True, True, 0)")]
        [InlineData(@"
L = []; M = []
class B:
    def __bool__(self):
        L.clear(); M.clear()
        return False
class A:
    def __eq__(self, o):
        return B()
    def __lt__(self, o):
        return B()
L.extend([A() for i in range(5)]); M.extend([A() for i in range(5)])
r = (L < M, len(L), len(M))
", "(False, 0, 0)")]
        [InlineData(@"
L = []
class A:
    def __eq__(self, o):
        L.clear()
        return True
L.extend([A(), A(), A()])
r = (L.remove(5), len(L))
", "(None, 0)")]
        public void ListOrderAndRemove(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData(@"
L = []
class A:
    def __eq__(self, o):
        L.clear()
        return True
L.extend([A(), A(), A()])
match L:
    case [1, 2, 3]:
        r = 'matched'
    case _:
        r = 'no'
r = (r, len(L))
", "('matched', 0)")]
        [InlineData(@"
L = []
class A:
    def __eq__(self, o):
        L.clear()
        return True
L.extend([A() for i in range(10)])
match L:
    case [1, *rest]:
        r = ('matched', len(rest))
    case _:
        r = 'no'
r = (r, len(L))
", "(('matched', 9), 0)")]
        public void MatchUnpacksTheSubjectFirst(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void DequeWalksCheckTheirState()
        {
            const string src = @"
from collections import deque
out = []
for op in ['in', 'count', 'index', 'remove', 'eq', 'lt']:
    q = deque(); p = deque()
    class A:
        def __eq__(self, o):
            q.clear()
            return False
        def __lt__(self, o):
            return True
    q.extend([A() for i in range(5)]); p.extend([A() for i in range(5)])
    try:
        if op == 'in': v = 7 in q
        elif op == 'count': v = q.count(7)
        elif op == 'index': v = q.index(7)
        elif op == 'remove': v = q.remove(7)
        elif op == 'eq': v = q == p
        else: v = q < p
        out.append((op, v))
    except Exception as e:
        out.append((op, type(e).__name__, str(e)))
r = out
";
            Assert.Equal("[('in', 'RuntimeError', 'deque mutated during iteration'), ('count', 'RuntimeError', 'deque mutated during iteration'), "
                + "('index', 'RuntimeError', 'deque mutated during iteration'), ('remove', 'IndexError', 'deque mutated during iteration'), "
                + "('eq', False), ('lt', True)]", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void HeapqChecksTheHeapSize()
        {
            const string src = @"
import heapq
out = []
for op in ['heapify', 'heappush', 'heappop', 'heappushpop', 'heapreplace']:
    h = []
    class A:
        def __init__(self, v): self.v = v
        def __lt__(self, o):
            h.clear()
            return self.v < o.v
    h.extend([A(i) for i in range(10, 0, -1)])
    try:
        if op == 'heapify': heapq.heapify(h)
        elif op == 'heappush': heapq.heappush(h, A(0))
        elif op == 'heappop': heapq.heappop(h)
        elif op == 'heappushpop': heapq.heappushpop(h, A(20))
        else: heapq.heapreplace(h, A(20))
        out.append((op, 'ok', len(h)))
    except Exception as e:
        out.append((op, type(e).__name__, str(e)))
r = out
";
            Assert.Equal("[('heapify', 'RuntimeError', 'list changed size during iteration'), ('heappush', 'RuntimeError', 'list changed size during iteration'), "
                + "('heappop', 'RuntimeError', 'list changed size during iteration'), ('heappushpop', 'IndexError', 'index out of range'), "
                + "('heapreplace', 'RuntimeError', 'list changed size during iteration')]", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void BisectReadsTheLiveList()
        {
            const string src = @"
import bisect
out = []
for op in ['bisect_left', 'bisect_right', 'insort', 'key']:
    L = []
    class A:
        def __init__(self, v): self.v = v
        def __lt__(self, o):
            L.clear()
            return self.v < o.v
    L.extend([A(i) for i in range(10)])
    try:
        if op == 'bisect_left': v = bisect.bisect_left(L, A(5))
        elif op == 'bisect_right': v = bisect.bisect_right(L, A(5))
        elif op == 'insort': v = bisect.insort(L, A(5))
        else:
            M = list(range(10))
            def k(x):
                M.clear()
                return x
            v = bisect.bisect(M, 5, key=k)
        out.append((op, v, len(L)))
    except Exception as e:
        out.append((op, type(e).__name__, str(e)))
r = out
";
            Assert.Equal("[('bisect_left', 'IndexError', 'list index out of range'), ('bisect_right', 'IndexError', 'list index out of range'), "
                + "('insort', 'IndexError', 'list index out of range'), ('key', 'IndexError', 'list index out of range')]", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void SliceAssignmentTakesTheValueFirst()
        {
            const string src = @"
out = []
L = list(range(10))
class It:
    def __init__(self): self.i = 0
    def __iter__(self): return self
    def __next__(self):
        self.i += 1
        if self.i > 5: raise StopIteration
        del L[2:]
        return self.i
try:
    L[::2] = It()
    out.append(('ext', L))
except Exception as e:
    out.append(('ext', type(e).__name__, str(e), L))
L2 = list(range(10))
class It2:
    def __init__(self): self.i = 0
    def __iter__(self): return self
    def __next__(self):
        self.i += 1
        if self.i > 3: raise StopIteration
        L2.clear()
        return self.i
L2[5:8] = It2()
out.append(('simple', L2))
r = out
";
            Assert.Equal("[('ext', 'ValueError', 'attempt to assign sequence of size 5 to extended slice of size 1', [0, 1]), ('simple', [1, 2, 3])]",
                Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void ChainMapTakesItsMapsFirst()
        {
            const string src = @"
import collections
st = [0]
a = {}
class K:
    def __hash__(self):
        return 1
    def __eq__(self, o):
        if st[0]:
            cm.maps.clear()
        return False
for i in range(5):
    a[K()] = 1
cm = collections.ChainMap({K(): 2}, a, {})
st[0] = 1
r = (len(cm), len(cm.maps))
";
            Assert.Equal("(6, 0)", Ev.Run(src).Repr("r"));
        }

        // A key's __eq__ that rebuilt the table: a set lookup was an engine fault, and a dict delete went on
        // probing the old index - it marked another key's slot, or never ended.
        [Fact]
        public void AHashProbeStartsOverOnARebuiltTable()
        {
            const string dict = @"
st = [0]
d = {}
class K:
    def __init__(self, n): self.n = n
    def __hash__(self): return 1
    def __eq__(self, o):
        if st[0] == 1:
            st[0] = 2
            keep = list(d.items())
            d.clear()
            for k, v in keep:
                d[k] = v
            for i in range(100):
                d[1000 + i] = i
        return isinstance(o, K) and o.n == self.n
for i in range(20):
    d[K(i)] = i
st[0] = 1
del d[K(5)]
r = (len(d), sum(1 for k in d if isinstance(k, K)), all(d[k] == k.n for k in d if isinstance(k, K)))
";
            Assert.Equal("(119, 19, True)", Ev.Run(dict).Repr("r"));
            const string set = @"
st = [0]
s = set()
class K:
    def __init__(self, n): self.n = n
    def __hash__(self): return 1
    def __eq__(self, o):
        if st[0] == 1:
            st[0] = 2
            keep = list(s)
            s.clear()
            for k in keep:
                s.add(k)
            for i in range(50):
                s.add(1000 + i)
        return isinstance(o, K) and o.n == self.n
for i in range(20):
    s.add(K(i))
st[0] = 1
s.discard(K(4))
r = (len(s), sum(1 for k in s if isinstance(k, K)), K(7) in s, K(4) in s)
";
            Assert.Equal("(69, 19, True, False)", Ev.Run(set).Repr("r"));
        }
    }
}
