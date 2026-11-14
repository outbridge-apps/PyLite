x = 1
y = 2
def f() -> (yield):
    return 1
print(f(), 1 if x else -1, 1 if x else +2, 1 if x else ~3)
