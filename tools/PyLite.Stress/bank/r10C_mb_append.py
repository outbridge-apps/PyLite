# repeat: 2
# deadline: 60000
import time
t = time.perf_counter()
x = []
i = 0
while i < 300000:
    x.append("a" * 10)
    i += 1
print(int((time.perf_counter() - t) * 1000))
