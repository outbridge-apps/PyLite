a = 3
b = 4
w = 8
s = "xy"
d = {"k": 1}
L = [1, 2, 3]
out = []
out.append(f"{a + b}")
out.append(f"{a=}")
out.append(f"{a = }")
out.append(f"{a + b = }")
out.append(f"{a!r}")
out.append(f"{s!r:>6}")
out.append(f"{a:>{w}}")
out.append(f"{a if b else 0}")
out.append(f"{ {'k': 2}['k'] }")
out.append(f"{d['k']}")
out.append(f"{L[1:]}")
out.append(f"{L[-1]}")
out.append(f"{-a}")
out.append(f"{not a}")
out.append(f"{(lambda: 7)()}")
out.append(f"{'a' 'b'}")
out.append(f"{f'{a}'}")
out.append(f"{a:{'>'}{w}}")
out.append(f"{a*b:x}")
out.append(f"{a:=^9}")
out.append(f"{a!s:=^9}")
out.append(f"{a<b}")
out.append(f"{a>=b}")
out.append(f"{a != b}")
out.append(f"{a==b}")
out.append(f"{s.upper()}")
out.append(f"{...}")
out.append(f"{None}")
out.append(f"{a,}")
out.append(f"{a, b}")
out.append(f"{*L,}")
out.append(f"{[x for x in L]}")
out.append(f"{3.5:{b}.{a}}")
out.append(f"{3.5:.2f}")
out.append(f"{(a := 9)}")
out.append(f"{a}")
out.append(f"""{
a
+
b
}""")
print(out)
