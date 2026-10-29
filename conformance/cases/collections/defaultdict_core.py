# Adapted from CPython's test suite (Lib/test/test_defaultdict.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from collections import defaultdict

# list factory: missing access CREATES the entry
groups = defaultdict(list)
groups['a'].append(1)
groups['a'].append(2)
groups['b'].append(3)
assert dict(groups) == {'a': [1, 2], 'b': [3]}
assert len(groups) == 2

# int factory: the counting idiom
counts = defaultdict(int)
for ch in 'abracadabra':
    counts[ch] += 1
assert counts['a'] == 5
assert counts['r'] == 2

# reading via [] materializes, get() does NOT
d = defaultdict(int)
assert d.get('missing') is None
assert 'missing' not in d
_ = d['made']
assert 'made' in d and d['made'] == 0

# a lambda factory
fmt = defaultdict(lambda: '<unset>')
fmt['known'] = 'v'
assert fmt['known'] == 'v'
assert fmt['other'] == '<unset>'

# behaves as a dict otherwise
dd = defaultdict(list)
dd['k'] = [9]
assert dict(dd) == {'k': [9]}
assert list(dd.keys()) == ['k']
