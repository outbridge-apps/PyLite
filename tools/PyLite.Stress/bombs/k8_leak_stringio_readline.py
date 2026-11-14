# deadline: 3000
# allocmb: 1024
import io
f = io.StringIO()
for i in range(2000):
    f.write('a' * 8000)
while True:
    f.seek(0)
    f.readline()
