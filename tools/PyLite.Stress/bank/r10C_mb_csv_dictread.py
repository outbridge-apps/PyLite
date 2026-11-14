# repeat: 2
# deadline: 60000
# allocmb: 2000
import time, csv,io
t = time.perf_counter()
rows = list(csv.DictReader(io.StringIO("a,b,c,d" + chr(10) + ("1,abc,\"q,x\",4.5" + chr(10)) * 30000)))
print("ms=%d" % ((time.perf_counter() - t) * 1000))
