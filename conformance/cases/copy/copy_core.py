# Adapted from CPython's test suite (Lib/test/test_copy.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import copy

# shallow copy: a new top-level container, SHARED inner objects
src = [[1, 2], {'k': 'v'}]
sh = copy.copy(src)
assert sh == src and sh is not src
sh.append('new')
assert len(src) == 2                      # top level independent
sh[0].append(3)
assert src[0] == [1, 2, 3]                # inner list shared

# deep copy: fully independent
src = [[1, 2], {'k': [10]}]
dp = copy.deepcopy(src)
assert dp == src
dp[0].append(3)
dp[1]['k'].append(20)
assert src == [[1, 2], {'k': [10]}]

# dicts
d = {'a': [1]}
sd = copy.copy(d)
sd['a'].append(2)
assert d['a'] == [1, 2]
dd = copy.deepcopy(d)
dd['a'].append(3)
assert d['a'] == [1, 2]

# nested mixed structures
tree = {'children': [{'name': 'x', 'tags': ['t1']}]}
clone = copy.deepcopy(tree)
clone['children'][0]['tags'].append('t2')
assert tree['children'][0]['tags'] == ['t1']

# immutable values come back equal
assert copy.copy(42) == 42
assert copy.copy('s') == 's'
assert copy.deepcopy((1, 2)) == (1, 2)

# deepcopy handles shared references without duplicating them into inequality
inner = [1]
pair = [inner, inner]
pc = copy.deepcopy(pair)
pc[0].append(2)
assert pc[1] == [1, 2]                    # the copy PRESERVES the sharing
assert inner == [1]
