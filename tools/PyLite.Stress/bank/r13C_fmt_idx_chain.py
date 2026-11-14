a = []
x = a
for i in range(100000):
    y = [0]
    x.append(y)
    x = y
try:
    print(len(("{0" + "[0]" * 100000 + "}").format([a])))
except Exception as e:
    print(type(e).__name__, str(e)[:80])
