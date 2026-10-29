# Adapted from CPython's test suite (Lib/test/test_list.py, sort cases from test_sort.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# sorted() copies, .sort() mutates
a = [3, 1, 2]
assert sorted(a) == [1, 2, 3]
assert a == [3, 1, 2]
a.sort()
assert a == [1, 2, 3]
a.sort(reverse=True)
assert a == [3, 2, 1]

# key= and stability
words = ['banana', 'Apple', 'cherry']
assert sorted(words, key=lambda s: s.lower()) == ['Apple', 'banana', 'cherry']
pairs = [(1, 'b'), (0, 'x'), (1, 'a'), (0, 'y')]
assert sorted(pairs, key=lambda t: t[0]) == [(0, 'x'), (0, 'y'), (1, 'b'), (1, 'a')]

# mutation methods
a = [1, 2, 3]
a.append(4)
a.extend([5, 6])
a.insert(0, 0)
assert a == [0, 1, 2, 3, 4, 5, 6]
assert a.pop() == 6
assert a.pop(0) == 0
a.remove(3)                      # removes the FIRST occurrence
assert a == [1, 2, 4, 5]
a.reverse()
assert a == [5, 4, 2, 1]

# append vs extend
a = [1]
a.append([2, 3])
assert a == [1, [2, 3]]
b = [1]
b.extend([2, 3])
assert b == [1, 2, 3]

# index and count
c = [1, 2, 2, 3, 2]
assert c.index(2) == 1
assert c.count(2) == 3

# slicing
s = [0, 1, 2, 3, 4, 5]
assert s[1:4] == [1, 2, 3]
assert s[:3] == [0, 1, 2]
assert s[3:] == [3, 4, 5]
assert s[::2] == [0, 2, 4]
assert s[::-1] == [5, 4, 3, 2, 1, 0]
assert s[10:20] == []
assert s[-2:] == [4, 5]

# concatenation, repetition, comparison
assert [1] + [2] == [1, 2]
assert [0] * 3 == [0, 0, 0]
assert [1, 2] < [1, 3]
assert [1, 2] < [1, 2, 0]
assert ['a'] == ['a']

# the aliasing gotcha: [[]] * n shares ONE inner list
grid = [[]] * 3
grid[0].append(1)
assert grid == [[1], [1], [1]]
safe = [[] for _ in range(3)]
safe[0].append(1)
assert safe == [[1], [], []]

# comprehensions
assert [x * x for x in range(5)] == [0, 1, 4, 9, 16]
assert [x for x in range(10) if x % 3 == 0] == [0, 3, 6, 9]
assert [x + y for x in 'ab' for y in '12'] == ['a1', 'a2', 'b1', 'b2']
assert [[row[i] for row in [[1, 2], [3, 4]]] for i in range(2)] == [[1, 3], [2, 4]]

# builtins over lists
assert sum([1, 2, 3]) == 6
assert min([2, 1, 3]) == 1 and max([2, 1, 3]) == 3
assert len([]) == 0
assert list('abc') == ['a', 'b', 'c']
assert list(range(3)) == [0, 1, 2]

# ---- an unorderable pair reports the operator and both type names, as CPython does ----
try:
    sorted([1, 'a'])
    assert False
except TypeError as e:
    assert str(e) == "'<' not supported between instances of 'str' and 'int'"
try:
    [1] < (1,)
    assert False
except TypeError as e:
    assert str(e) == "'<' not supported between instances of 'list' and 'tuple'"
try:
    1 >= None
    assert False
except TypeError as e:
    assert str(e) == "'>=' not supported between instances of 'int' and 'NoneType'"
