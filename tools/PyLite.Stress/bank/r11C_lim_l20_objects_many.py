# allocmb: 64
# deadline: 20000
class C:
    def __init__(s, i):
        s.a = i
        s.b = i
l = [C(i) for i in range(2000000)]
print(len(l))
