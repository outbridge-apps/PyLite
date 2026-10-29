# CPython 3.5+ (and PyLite) raise json.JSONDecodeError, a ValueError subclass with msg/pos/lineno/colno;
# IronPython 3.4.2 raises a plain ValueError.
import json
try:
    json.loads('{"a" 1}')
except ValueError as e:
    result = (type(e).__name__, isinstance(e, ValueError))
