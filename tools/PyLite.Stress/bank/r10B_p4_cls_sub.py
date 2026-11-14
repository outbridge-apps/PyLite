class B:
    def f(self): return 1
d = {"k": B}
class A(d["k"]): pass
print(A().f())
