import functools
for n in (90, 95, 99, 100, 101, 150, 190):
    @functools.lru_cache(maxsize=None)
    def fib(k):
        return k if k < 2 else fib(k - 1) + fib(k - 2)
    try:
        print(n, fib(n) > 0)
    except RecursionError as e:
        print(n, 'RE')
