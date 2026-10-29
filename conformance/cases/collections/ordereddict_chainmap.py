# Adapted from CPython's test suite (Lib/test/test_ordered_dict.py, ChainMap cases in test_collections.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from collections import OrderedDict, ChainMap

# OrderedDict keeps insertion order and behaves like a dict
od = OrderedDict()
od['b'] = 1
od['a'] = 2
assert list(od) == ['b', 'a']
assert od['b'] == 1
od['b'] = 9                       # overwrite keeps position
assert list(od) == ['b', 'a']
assert dict(od) == {'a': 2, 'b': 9}

# ChainMap: lookup walks the maps left to right
defaults = {'color': 'red', 'size': 1}
user = {'size': 2}
cm = ChainMap(user, defaults)
assert cm['size'] == 2            # found in the first map
assert cm['color'] == 'red'       # falls through to the second
assert len(cm) == 2               # union of keys
assert sorted(cm) == ['color', 'size']

# writes go to the FIRST map only
cm['color'] = 'blue'
assert user == {'size': 2, 'color': 'blue'}
assert defaults['color'] == 'red'

# missing everywhere raises KeyError
raised = False
try:
    _ = cm['nope']
except KeyError:
    raised = True
assert raised
