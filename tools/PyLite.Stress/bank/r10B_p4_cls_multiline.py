class B:
    def f(self): return 1
class A(
    B  # base
): pass
print(A().f())
