# deadline: 1500
x = 1.0
for i in range(30):
    s = repr(x * 10.0 ** (i * 10 - 150))
print(s, float.fromhex("0x1." + "f" * 4000000 + "p0"))
