# CPython 3.7+ (and PyLite) leave '~' unquoted, as RFC 3986 lists it among the unreserved characters;
# IronPython 3.4.2 writes '%7E'.
from urllib.parse import quote
result = quote('~user/a b')
