# Adapted from CPython's test suite (Lib/test/test_random.py: TestBasicOps.test_sample/test_choice/test_shuffle/
# test_randrange_errors/test_choices/test_getrandbits/test_randbytes, TestDistributions.test_avg_std/
# test_constant, SystemRandom_TestBasicOps). Python Software Foundation, PSF License Version 2 -- see README.
# Dialect: the generator is System.Random, so seeded SEQUENCES differ from MT19937; the API contract
# (ranges, per-seed determinism, distribution invariants) is what this file asserts.

import random

# ---- seeding and determinism ----
random.seed(12345)
a = [random.random() for _ in range(5)]
random.seed(12345)
b = [random.random() for _ in range(5)]
assert a == b and len(set(a)) > 1
random.seed('text-seed')
c1 = random.random()
random.seed('text-seed')
assert random.random() == c1
random.seed(1.5)
f1 = random.randint(0, 10 ** 6)
random.seed(1.5)
assert random.randint(0, 10 ** 6) == f1
random.seed(2 ** 100)
big1 = random.random()
random.seed(2 ** 100)
assert random.random() == big1
random.seed(None)                       # reseed from entropy: still a valid generator
assert 0.0 <= random.random() < 1.0
random.seed()
assert 0.0 <= random.random() < 1.0
random.seed(0)
first = random.random()
random.seed(1)
assert random.random() != first          # different seeds diverge

random.seed(42)

# ---- random / uniform / getrandbits ----
for _ in range(1000):
    r = random.random()
    assert 0.0 <= r < 1.0 and isinstance(r, float)
for _ in range(500):
    u = random.uniform(-2.5, 7.5)
    assert -2.5 <= u <= 7.5
    v = random.uniform(3, 3)
    assert v == 3.0
    w = random.uniform(10, 1)           # reversed bounds are allowed
    assert 1 <= w <= 10
for k in (1, 7, 8, 31, 32, 63, 64, 100, 200):
    for _ in range(50):
        bits = random.getrandbits(k)
        assert 0 <= bits < 2 ** k and isinstance(bits, int)
assert random.getrandbits(0) == 0
assert any(random.getrandbits(64) >= 2 ** 63 for _ in range(200))    # the top bit does get set
try:
    random.getrandbits(-1)
    assert False
except ValueError:
    pass
try:
    random.getrandbits(1.5)
    assert False
except TypeError:
    pass

# ---- randrange / randint ----
for _ in range(500):
    assert 0 <= random.randrange(10) < 10
    assert 5 <= random.randrange(5, 10) < 10
    assert random.randrange(0, 100, 10) % 10 == 0 and random.randrange(0, 100, 10) < 100
    assert random.randrange(-50, -40) in range(-50, -40)
    assert random.randrange(1, 10, 3) in (1, 4, 7)
    assert 1 <= random.randint(1, 6) <= 6
    assert random.randint(-3, -3) == -3
    assert isinstance(random.randint(1, 6), int)
assert random.randrange(1) == 0 and random.randrange(7, 8) == 7 and random.randint(5, 5) == 5
assert 0 <= random.randrange(10 ** 30) < 10 ** 30 and 2 ** 80 <= random.randint(2 ** 80, 2 ** 81) <= 2 ** 81
assert {random.randrange(3) for _ in range(300)} == {0, 1, 2}
assert {random.randint(1, 2) for _ in range(200)} == {1, 2}
assert {random.randrange(0, 10, 5) for _ in range(200)} == {0, 5}
for bad in (lambda: random.randrange(0), lambda: random.randrange(5, 5), lambda: random.randrange(10, 1),
            lambda: random.randrange(0, 10, 0), lambda: random.randint(10, 1), lambda: random.randrange(-1)):
    try:
        bad()
        assert False
    except ValueError:
        pass
# 3.12+: a non-integer bound is a TypeError
for bad in (lambda: random.randrange(1.5), lambda: random.randint(1, 2.5), lambda: random.randrange('5'), lambda: random.randrange(0, 10, 1.5)):
    try:
        bad()
        assert False
    except TypeError:
        pass

# ---- choice / choices ----
pool = ['a', 'b', 'c', 'd']
for _ in range(300):
    assert random.choice(pool) in pool
    assert random.choice('xyz') in 'xyz'
    assert random.choice((1, 2)) in (1, 2)
    assert random.choice(range(5)) in range(5)
assert random.choice([7]) == 7 and {random.choice(pool) for _ in range(300)} == set(pool)
try:
    random.choice([])
    assert False
except IndexError:
    pass
try:
    random.choice(5)
    assert False
except TypeError:
    pass
picks = random.choices(pool, k=10)
assert len(picks) == 10 and all(p in pool for p in picks)
assert len(random.choices(pool)) == 1 and random.choices(pool, k=0) == []
assert random.choices([9], k=5) == [9, 9, 9, 9, 9]
weighted = random.choices(['x', 'y'], weights=[1, 0], k=50)
assert weighted == ['x'] * 50
cum = random.choices(['x', 'y'], cum_weights=[1, 1], k=50)
assert cum == ['x'] * 50
assert set(random.choices(['x', 'y', 'z'], weights=[0, 0, 5], k=20)) == {'z'}
assert set(random.choices(['x', 'y', 'z'], cum_weights=[0, 0, 5], k=20)) == {'z'}
skewed = random.choices([0, 1], weights=[9, 1], k=2000)
assert 1600 < skewed.count(0) < 1990
for bad in (lambda: random.choices([]), lambda: random.choices(['a', 'b'], weights=[1]),
            lambda: random.choices(['a'], weights=[1], cum_weights=[1]), lambda: random.choices(['a', 'b'], weights=[0, 0])):
    try:
        bad()
        assert False
    except (ValueError, IndexError, TypeError):
        pass

# ---- shuffle / sample ----
deck = list(range(20))
work = deck[:]
random.shuffle(work)
assert sorted(work) == deck and len(work) == 20
assert random.shuffle(work) is None
distinct = {tuple(random.sample(deck, 20)) for _ in range(5)}
assert len(distinct) > 1                          # a permutation, not the identity every time
single = [1]
random.shuffle(single)
assert single == [1]
empty = []
random.shuffle(empty)
assert empty == []
try:
    random.shuffle((1, 2))
    assert False
except TypeError:
    pass
s = random.sample(deck, 5)
assert len(s) == 5 and len(set(s)) == 5 and all(x in deck for x in s)
assert random.sample(deck, 0) == [] and sorted(random.sample(deck, 20)) == deck
assert len(random.sample(range(1000), 10)) == 10 and len(set(random.sample('abcdef', 3))) == 3
assert random.sample(deck, k=2) != None and len(random.sample(deck, k=2)) == 2
assert sorted(random.sample([1, 1, 2], 3)) == [1, 1, 2]                # duplicates in the population are fine
counted = random.sample(['r', 'b'], 4, counts=[3, 1])
assert len(counted) == 4 and counted.count('r') == 3 and counted.count('b') == 1
for bad in (lambda: random.sample(deck, 21), lambda: random.sample(deck, -1), lambda: random.sample([], 1)):
    try:
        bad()
        assert False
    except ValueError:
        pass
try:
    random.sample({1, 2, 3}, 1)                    # sets are not sequences (3.11+)
    assert False
except TypeError:
    pass
try:
    random.sample(deck)
    assert False
except TypeError:
    pass

# ---- continuous distributions: ranges and rough moments ----
N = 4000
tri = [random.triangular(0, 10, 2) for _ in range(N)]
assert all(0 <= t <= 10 for t in tri) and 2.5 < sum(tri) / N < 5.5
assert 0 <= random.triangular() <= 1 and 1 <= random.triangular(1, 3) <= 3 and 5 <= random.triangular(5, 5) <= 5
exp = [random.expovariate(2.0) for _ in range(N)]
assert all(e >= 0 for e in exp) and 0.35 < sum(exp) / N < 0.65
assert random.expovariate(1.0) >= 0
norm = [random.normalvariate(10, 2) for _ in range(N)]
mean = sum(norm) / N
var = sum((x - mean) ** 2 for x in norm) / N
assert 9.5 < mean < 10.5 and 3 < var < 5.5
gauss = [random.gauss(0, 1) for _ in range(N)]
assert -0.2 < sum(gauss) / N < 0.2 and any(abs(g) > 2 for g in gauss) and all(abs(g) < 8 for g in gauss)
assert isinstance(random.gauss(), float) and isinstance(random.normalvariate(), float)
gam = [random.gammavariate(2.0, 3.0) for _ in range(N)]
assert all(g > 0 for g in gam) and 5 < sum(gam) / N < 7
assert random.gammavariate(1.0, 1.0) > 0 and random.gammavariate(0.5, 1.0) > 0 and random.gammavariate(5.0, 0.5) > 0
beta = [random.betavariate(2.0, 5.0) for _ in range(N)]
assert all(0 <= b <= 1 for b in beta) and 0.2 < sum(beta) / N < 0.37
for bad in (lambda: random.expovariate(0), lambda: random.gammavariate(0, 1), lambda: random.gammavariate(1, 0),
            lambda: random.betavariate(0, 1), lambda: random.triangular(1, 0.5, 2)):
    try:
        bad()
    except ValueError:
        pass
    except ZeroDivisionError:
        pass

# ---- randbytes ----
rb = random.randbytes(16)
assert isinstance(rb, bytes) and len(rb) == 16 and random.randbytes(0) == b''
assert len({random.randbytes(8) for _ in range(20)}) > 1
try:
    random.randbytes(-1)
    assert False
except ValueError:
    pass

# ---- per-seed determinism across the whole API ----
def draw():
    random.seed(777)
    return (random.random(), random.randint(1, 100), random.choice('abc'), random.uniform(0, 1),
            random.getrandbits(20), random.sample(range(50), 5), random.gauss(0, 1), random.randbytes(4),
            random.choices('xyz', k=3), random.randrange(0, 100, 7), random.expovariate(1.5))
assert draw() == draw()
