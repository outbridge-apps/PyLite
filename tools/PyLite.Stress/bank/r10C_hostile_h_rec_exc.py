# repeat: 2
class E(Exception):
    pass
def f(n):
    try:
        return f(n + 1)
    except RecursionError as e:
        raise E("x") from e
f(0)
