# deadline: 20000
# allocmb: 1024
import urllib.parse as up
print(len(up.urlencode({str(i): 'x y' for i in range(300000)})))
