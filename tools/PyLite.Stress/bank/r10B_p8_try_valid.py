out = []
try:
    out.append(1)
except ValueError:
    out.append(2)
else:
    out.append(3)
finally:
    out.append(4)
try:
    out.append(5)
finally:
    out.append(6)
try:
    raise KeyError
except (KeyError, ValueError) as e:
    out.append(7)
else:
    out.append(8)
for i in range(1):
    try:
        pass
    except Exception:
        pass
    else:
        out.append(9)
else:
    out.append(10)
print(out)
