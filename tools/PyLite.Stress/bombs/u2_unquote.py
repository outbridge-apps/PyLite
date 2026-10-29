# deadline: 20000
# allocmb: 1024
import urllib.parse as up
print(len(up.unquote('%20a' * 2000000)))
