# deadline: 3000
# allocmb: 1024
from urllib.parse import parse_qsl
x = parse_qsl('a' * 16000000, separator='a' * 1000 + 'b')
