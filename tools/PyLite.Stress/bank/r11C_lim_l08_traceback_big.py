# allocmb: 64
# deadline: 20000
import traceback
def f(n):
    if n == 0:
        raise ValueError("x" * 4000000)
    return f(n - 1)
try:
    f(190)
except ValueError:
    s = traceback.format_exc()
    print(len(s))
    l = traceback.format_exception(ValueError("y" * 4000000))
    print(len(l))
