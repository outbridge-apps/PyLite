# Adapted from CPython's test suite (Lib/test/test_random.py) as PROPERTY tests.
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# NOTE (documented deviation): PyLite's random is NOT MT19937, so seeded SEQUENCES do not match
# CPython's. The API contract (ranges, determinism per seed, distribution invariants) does.

import random

# determinism: same seed => same sequence (within this engine)
random.seed(42)
first = [random.random() for _ in range(5)]
random.seed(42)
second = [random.random() for _ in range(5)]
assert first == second

# random() is in [0, 1)
random.seed(1)
for _ in range(100):
    v = random.random()
    assert 0.0 <= v < 1.0

# randint is INCLUSIVE on both ends and covers the range
random.seed(7)
seen = set()
for _ in range(200):
    n = random.randint(1, 3)
    assert 1 <= n <= 3
    seen.add(n)
assert seen == {1, 2, 3}

# randrange EXCLUDES the stop
random.seed(7)
for _ in range(100):
    assert 0 <= random.randrange(5) < 5
    assert random.randrange(2, 4) in (2, 3)
step_vals = set()
random.seed(3)
for _ in range(100):
    step_vals.add(random.randrange(0, 10, 3))
assert step_vals <= {0, 3, 6, 9}

# choice returns an element; sample is unique and unordered-subset
items = ['a', 'b', 'c', 'd']
random.seed(5)
for _ in range(50):
    assert random.choice(items) in items
picked = random.sample(items, 3)
assert len(picked) == 3
assert len(set(picked)) == 3
assert set(picked) <= set(items)

# choices allows repeats and honors k
random.seed(5)
many = random.choices(items, k=10)
assert len(many) == 10
assert set(many) <= set(items)

# shuffle permutes in place: same multiset, same length
deck = list(range(10))
random.seed(9)
random.shuffle(deck)
assert sorted(deck) == list(range(10))

# uniform stays in the closed-ish interval
random.seed(2)
for _ in range(100):
    u = random.uniform(10, 20)
    assert 10 <= u <= 20

# getrandbits fits the requested width
random.seed(4)
for _ in range(50):
    assert 0 <= random.getrandbits(8) < 256
    assert 0 <= random.getrandbits(1) < 2
