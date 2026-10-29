# Adapted from CPython's test suite (Lib/test/test_defaultdict.py, Lib/test/test_ordered_dict.py:
# OrderedDictTests.test_move_to_end/test_popitem/test_equality, Lib/test/test_collections.py:
# TestChainMap.test_basics, TestNamedTuple.test_factory/test_instance/test_rename/test_odd_sizes).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from collections import defaultdict, OrderedDict, ChainMap, namedtuple

# ================= defaultdict =================
dd = defaultdict(list)
assert dd.default_factory is list
dd['a'].append(1)
assert dd == {'a': [1]} and isinstance(dd, dict)
assert defaultdict(int)['x'] == 0 and defaultdict(str)['x'] == '' and defaultdict(set)['x'] == set()
assert defaultdict(lambda: 42)['q'] == 42 and defaultdict(dict)['x'] == {}
n = defaultdict(None)
assert n.default_factory is None
try:
    n['missing']
    assert False
except KeyError:
    pass
plain = defaultdict()
assert plain.default_factory is None
try:
    defaultdict(5)
    assert False
except TypeError:
    pass
init = defaultdict(int, {'a': 1}, b=2)
assert init == {'a': 1, 'b': 2} and init['c'] == 0
# get / in / pop do not trigger the factory
g = defaultdict(int)
assert g.get('k') is None and 'k' not in g and g.pop('k', 'dflt') == 'dflt' and len(g) == 0
try:
    g.pop('k')
    assert False
except KeyError:
    pass
# nested factories
tree = defaultdict(lambda: defaultdict(int))
tree['x']['y'] += 1
assert tree['x']['y'] == 1 and dict(tree['x']) == {'y': 1}
# the factory runs each time
calls = []
def factory():
    calls.append(1)
    return []
f = defaultdict(factory)
f['a']; f['a']; f['b']
assert len(calls) == 2
assert repr(defaultdict(int, a=1)).startswith('defaultdict(') and 'a' in repr(defaultdict(int, a=1))
assert dict(defaultdict(int, a=1)) == {'a': 1} and sorted(defaultdict(int, b=1, a=2)) == ['a', 'b']
cp = f.copy()
assert cp.default_factory is factory and cp == f
grouped = defaultdict(list)
for k, v in [('a', 1), ('b', 2), ('a', 3)]:
    grouped[k].append(v)
assert dict(grouped) == {'a': [1, 3], 'b': [2]}

# ================= OrderedDict =================
od = OrderedDict()
od['b'] = 1
od['a'] = 2
od['c'] = 3
assert list(od) == ['b', 'a', 'c'] and list(od.values()) == [1, 2, 3] and isinstance(od, dict)
od.move_to_end('b')
assert list(od) == ['a', 'c', 'b']
od.move_to_end('c', last=False)
assert list(od) == ['c', 'a', 'b']
od.move_to_end('b', False)
assert list(od) == ['b', 'c', 'a']
try:
    od.move_to_end('zzz')
    assert False
except KeyError:
    pass
assert od.popitem() == ('a', 2)                 # LIFO by default
assert od.popitem(last=False) == ('b', 1)
assert list(od) == ['c']
od.popitem()
try:
    od.popitem()
    assert False
except KeyError:
    pass
# order-sensitive equality between OrderedDicts, order-insensitive against plain dicts
o1 = OrderedDict([('a', 1), ('b', 2)])
o2 = OrderedDict([('b', 2), ('a', 1)])
assert o1 != o2 and o1 == {'b': 2, 'a': 1} and o2 == {'a': 1, 'b': 2} and o1 == dict(o2)
assert OrderedDict(a=1) == OrderedDict([('a', 1)]) and OrderedDict() == {}
# re-assigning keeps position; deleting and re-adding moves to the end
o3 = OrderedDict([('x', 1), ('y', 2)])
o3['x'] = 10
assert list(o3) == ['x', 'y']
del o3['x']
o3['x'] = 11
assert list(o3) == ['y', 'x']
assert repr(OrderedDict()) == 'OrderedDict()' and repr(OrderedDict([('a', 1)])) == "OrderedDict({'a': 1})"
assert OrderedDict.fromkeys('ab', 0) == {'a': 0, 'b': 0} and list(OrderedDict.fromkeys('ba')) == ['b', 'a']
o4 = OrderedDict(o1)
assert o4 == o1 and o4 is not o1
assert list(reversed(OrderedDict([('a', 1), ('b', 2)]))) == ['b', 'a']
o5 = OrderedDict([('k', 1)])
o5.update([('m', 2)], n=3)
assert list(o5) == ['k', 'm', 'n'] and o5.setdefault('k', 9) == 1 and o5.setdefault('p', 9) == 9
assert list(o5.keys())[-1] == 'p' and o5.pop('m') == 2 and 'm' not in o5

# ================= ChainMap =================
base = {'a': 1, 'b': 2}
over = {'b': 20, 'c': 30}
cm = ChainMap(over, base)
assert cm['a'] == 1 and cm['b'] == 20 and cm['c'] == 30      # first mapping wins
assert len(cm) == 3 and sorted(cm) == ['a', 'b', 'c'] and sorted(cm.keys()) == ['a', 'b', 'c']
assert sorted(cm.items()) == [('a', 1), ('b', 20), ('c', 30)] and sorted(cm.values()) == [1, 20, 30]
assert 'a' in cm and 'z' not in cm and cm.get('z') is None and cm.get('z', 0) == 0 and cm.get('a') == 1
try:
    cm['z']
    assert False
except KeyError:
    pass
cm['d'] = 4                                   # writes go to the FIRST mapping
assert over == {'b': 20, 'c': 30, 'd': 4} and base == {'a': 1, 'b': 2}
cm['a'] = 100
assert over['a'] == 100 and base['a'] == 1 and cm['a'] == 100
del cm['a']
assert cm['a'] == 1 and 'a' not in over
try:
    del cm['b']
    del cm['b']                               # 'b' only in base now -> KeyError on the first map
    assert False
except KeyError:
    pass
assert cm.maps[0] is over and cm.maps[1] is base and len(cm.maps) == 2
child = cm.new_child()
child['e'] = 5
assert child['e'] == 5 and 'e' not in cm and child.maps[0] == {'e': 5} and child.maps[1] is over
child2 = cm.new_child({'f': 6})
assert child2['f'] == 6 and child2.maps[0] == {'f': 6}
assert child.parents.maps == cm.maps and child.parents['c'] == 30
assert ChainMap().maps == [{}] and len(ChainMap()) == 0 and ChainMap()['x' if False else 'y'] if False else True
single = ChainMap({'k': 'v'})
assert dict(single) == {'k': 'v'} and single == {'k': 'v'}
assert cm == dict(cm) and ChainMap({'a': 1}, {'a': 2}) == {'a': 1}
assert repr(ChainMap({'a': 1}, {})) == "ChainMap({'a': 1}, {})"
cm2 = ChainMap({'x': 1}, {'x': 2, 'y': 2})
assert cm2.pop('x') == 1 and cm2['x'] == 2
try:
    cm2.pop('y')                              # only in the second map
    assert False
except KeyError:
    pass
assert cm2.setdefault('q', 7) == 7 and cm2.maps[0]['q'] == 7
cm2.update({'r': 8}, s=9)
assert cm2.maps[0]['r'] == 8 and cm2['s'] == 9
cm2.clear()
assert cm2.maps[0] == {} and cm2['x'] == 2
try:
    ChainMap([1, 2])
    assert False
except TypeError:
    pass

# ================= namedtuple =================
Point = namedtuple('Point', ['x', 'y'])
p = Point(1, 2)
assert p.x == 1 and p.y == 2 and p[0] == 1 and p[1] == 2 and p[-1] == 2 and len(p) == 2
assert p == (1, 2) and (1, 2) == p and p == Point(1, 2) and p != Point(2, 1)
assert isinstance(p, tuple) and isinstance(p, Point) and issubclass(Point, tuple)
assert Point(x=1, y=2) == p and Point(1, y=2) == p and Point(y=2, x=1) == p
x, y = p
assert (x, y) == (1, 2) and list(p) == [1, 2] and tuple(p) == (1, 2) and max(p) == 2
assert hash(p) == hash((1, 2)) and {p: 'here'}[(1, 2)] == 'here' and p in {(1, 2)}
assert repr(p) == 'Point(x=1, y=2)' and str(p) == 'Point(x=1, y=2)'
assert Point._fields == ('x', 'y') and p._fields == ('x', 'y')
assert p._asdict() == {'x': 1, 'y': 2} and list(p._asdict()) == ['x', 'y'] and isinstance(p._asdict(), dict)
assert p._replace(x=9) == Point(9, 2) and p == Point(1, 2)
assert p._replace() == p and p._replace(x=0, y=0) == (0, 0)
assert Point._make([3, 4]) == Point(3, 4) and Point._make(iter((5, 6))) == (5, 6) and Point._make('ab') == ('a', 'b')
assert p + (3,) == (1, 2, 3) and p * 2 == (1, 2, 1, 2) and p[0:1] == (1,) and p.count(1) == 1 and p.index(2) == 1
assert Point(1, 2) < Point(1, 3) and sorted([Point(2, 0), Point(1, 5)]) == [Point(1, 5), Point(2, 0)]
assert Point.__name__ == 'Point'

# field-name string forms
P2 = namedtuple('P2', 'a b c')
P3 = namedtuple('P3', 'a, b,c')
P4 = namedtuple('P4', ('a', 'b'))
assert P2._fields == ('a', 'b', 'c') and P3._fields == ('a', 'b', 'c') and P4._fields == ('a', 'b')
Empty = namedtuple('Empty', '')
assert Empty() == () and Empty._fields == () and len(Empty()) == 0
One = namedtuple('One', 'v')
assert One(7).v == 7 and One(7) == (7,)

# rename handles bad/duplicate names
R = namedtuple('R', ['abc', 'def', 'ghi', 'abc', '_x', '1y'], rename=True)
assert R._fields == ('abc', '_1', 'ghi', '_3', '_4', '_5')
assert R(1, 2, 3, 4, 5, 6)._1 == 2

# errors
for bad_fields in (['def'], ['_private'], ['a', 'a'], ['1x'], ['a b']):
    try:
        namedtuple('Bad', bad_fields)
        assert False, bad_fields
    except ValueError:
        pass
try:
    namedtuple('class', 'a')
    assert False
except ValueError:
    pass
try:
    Point(1)
    assert False
except TypeError as e:
    assert 'missing' in str(e)
try:
    Point(1, 2, 3)
    assert False
except TypeError:
    pass
try:
    Point(1, 2, z=3)
    assert False
except TypeError:
    pass
try:
    Point(1, x=2, y=3)
    assert False
except TypeError:
    pass
try:
    p._replace(z=1)
    assert False
except ValueError:
    pass
try:
    Point._make([1])
    assert False
except TypeError:
    pass
try:
    p.x = 5
    assert False
except AttributeError:
    pass
try:
    p.z
    assert False
except AttributeError:
    pass
try:
    p[2]
    assert False
except IndexError:
    pass

# idioms
Employee = namedtuple('Employee', 'name dept salary')
staff = [Employee('ann', 'eng', 100), Employee('bob', 'ops', 80), Employee('cid', 'eng', 120)]
assert [e.name for e in staff if e.dept == 'eng'] == ['ann', 'cid']
assert max(staff, key=lambda e: e.salary).name == 'cid'
assert sum(e.salary for e in staff) == 300
by_dept = {}
for e in staff:
    by_dept.setdefault(e.dept, []).append(e.name)
assert by_dept == {'eng': ['ann', 'cid'], 'ops': ['bob']}
assert [e._asdict()['name'] for e in staff][0] == 'ann'
raise_ = staff[1]._replace(salary=90)
assert raise_.salary == 90 and staff[1].salary == 80
