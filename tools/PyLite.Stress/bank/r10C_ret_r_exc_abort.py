# repeat: 2
# allocmb: 60
class E(Exception):
    pass
keep = []
def f(n):
    try:
        keep.append("y" * 1000)
        return f(n + 1)
    except RecursionError as e:
        raise E(keep) from e
while True:
    try:
        f(0)
    except E as e:
        e.add_note("n")
