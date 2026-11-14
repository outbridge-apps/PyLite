# deadline: 20000
# allocmb: 64
import urllib.parse
x = urllib.parse.parse_qs('a&' * 8000000)
