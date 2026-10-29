# Adapted from CPython's test suite (Lib/test/test_csv.py, TestDialectExcel... test_write_escape).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import csv
from io import StringIO


def write(fields, **kwargs):
    buf = StringIO()
    w = csv.writer(buf, **kwargs)
    w.writerow(fields)
    return buf.getvalue()[:-len(w.dialect.lineterminator)]


assert write(['a', 1, 'p,q'], escapechar='\\') == 'a,1,"p,q"'
try:
    write(['a', 1, 'p,"q"'], escapechar=None, doublequote=False)
    assert False, 'no error'
except csv.Error:
    pass
assert write(['a', 1, 'p,"q"'], escapechar='\\', doublequote=False) == 'a,1,"p,\\"q\\""'
assert write(['"'], escapechar='\\', quoting=csv.QUOTE_MINIMAL) == '""""'
assert write(['"'], escapechar='\\', quoting=csv.QUOTE_MINIMAL, doublequote=False) == '\\"'
assert write(['"'], escapechar='\\', quoting=csv.QUOTE_NONE) == '\\"'
assert write(['a', 1, 'p,q'], escapechar='\\', quoting=csv.QUOTE_NONE) == 'a,1,p\\,q'
assert write(['\\', 'a'], escapechar='\\', quoting=csv.QUOTE_NONE) == '\\\\,a'
assert write(['\\', 'a'], escapechar='\\', quoting=csv.QUOTE_MINIMAL) == '\\\\,a'
assert write(['\\', 'a'], escapechar='\\', quoting=csv.QUOTE_ALL) == '"\\\\","a"'
assert write(['\\ ', 'a'], escapechar='\\', quoting=csv.QUOTE_MINIMAL) == '\\\\ ,a'
assert write(['\\,', 'a'], escapechar='\\', quoting=csv.QUOTE_MINIMAL) == '"\\\\,",a'
assert write([',\\', 'a'], escapechar='\\', quoting=csv.QUOTE_MINIMAL) == '",\\\\",a'
assert write(['C\\', '6', '7', 'X"'], escapechar='\\', quoting=csv.QUOTE_MINIMAL) == 'C\\\\,6,7,"X"""'

# a character of the line terminator quotes the field (or is escaped under QUOTE_NONE)
assert write(['a|b'], lineterminator='|') == '"a|b"'
assert write(['a|b'], lineterminator='|', quoting=csv.QUOTE_NONE, escapechar='\\') == 'a\\|b'
