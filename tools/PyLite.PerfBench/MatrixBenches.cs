using System.Collections.Generic;

namespace Outbridge.PyLite.PerfBench
{
    // The fine-grained function-level battery (--matrix): which library and which FUNCTIONS are slower
    // or faster vs IronPython, not just whole-scenario numbers. Every source is 3.4-compatible so both
    // engines run the identical script; each defines `result`. Sizes aim for ~20-80ms per PyLite run.
    internal static class MatrixBenches
    {
        internal sealed class MBench
        {
            public string Cat;
            public string Name;
            public string Source;
            public MBench(string cat, string name, string source) { Cat = cat; Name = name; Source = source; }
        }

        internal static IEnumerable<MBench> All()
        {
            // ---------------- str ----------------
            yield return new MBench("str", "split", Loop10k("s = 'alpha-beta-gamma-delta-epsilon'", "parts = s.split('-')", "len(parts)"));
            yield return new MBench("str", "join", "lst = ['alpha', 'beta', 'gamma', 'delta', 'epsilon']\nn = 0\nfor i in range(10000):\n    t = '-'.join(lst)\n    n = n + len(t)\nresult = n\n");
            yield return new MBench("str", "replace", Loop10k("s = 'alpha-beta-gamma-delta-epsilon'", "t = s.replace('-', '_')", "len(t)"));
            yield return new MBench("str", "find", Loop10k("s = 'alpha-beta-gamma-delta-epsilon'", "k = s.find('delta')", "k"));
            yield return new MBench("str", "startswith", Loop10k("s = 'alpha-beta-gamma'", "b = s.startswith('alp')", "1 if b else 0"));
            yield return new MBench("str", "upper", Loop10k("s = 'alpha-beta-gamma-delta'", "t = s.upper()", "len(t)"));
            yield return new MBench("str", "strip", Loop10k("s = '   alpha-beta-gamma   '", "t = s.strip()", "len(t)"));
            yield return new MBench("str", "format", Loop10k("s = 'val'", "t = '{0}:{1}'.format(i, s)", "len(t)"));
            yield return new MBench("str", "percent_fmt", Loop10k("s = 'val'", "t = '%d:%s' % (i, s)", "len(t)"));
            yield return new MBench("str", "concat", Loop10k("s = 'alpha-beta'", "t = s + '-tail'", "len(t)"));
            yield return new MBench("str", "slice", Loop10k("s = 'alpha-beta-gamma-delta'", "t = s[3:14]", "len(t)"));
            yield return new MBench("str", "contains", Loop10k("s = 'alpha-beta-gamma-delta'", "b = 'gamma' in s", "1 if b else 0"));
            yield return new MBench("str", "str_of_int", Loop10k(null, "t = str(i)", "len(t)"));
            yield return new MBench("str", "int_of_str", Loop10k("s = '123456'", "v = int(s)", "v"));

            // ---------------- int / float ----------------
            yield return new MBench("num", "int_add", "s = 0\nfor i in range(30000):\n    s = s + i\nresult = s\n");
            yield return new MBench("num", "int_mul", "s = 1\nfor i in range(30000):\n    s = i * 7 + 1\nresult = s\n");
            yield return new MBench("num", "int_floordiv", "s = 0\nfor i in range(30000):\n    s = (i * 13 + 7) // 5\nresult = s\n");
            yield return new MBench("num", "int_mod", "s = 0\nfor i in range(30000):\n    s = i % 97\nresult = s\n");
            yield return new MBench("num", "int_pow", "s = 0\nfor i in range(10000):\n    s = i ** 2\nresult = s\n");
            yield return new MBench("num", "bigint_mul", "s = 10 ** 40\nfor i in range(5000):\n    v = s * (i + 1)\nresult = len(str(v))\n");
            yield return new MBench("num", "float_arith", "x = 0.0\nfor i in range(30000):\n    x = x + i * 0.5 - 1.25\nresult = int(x)\n");
            yield return new MBench("num", "float_div", "x = 1.0\nfor i in range(30000):\n    x = (i + 1.0) / 3.0\nresult = int(x)\n");
            yield return new MBench("num", "round2", Loop10k("x = 3.14159", "v = round(x + i, 2)", "int(v)"));
            yield return new MBench("num", "abs", Loop10k(null, "v = abs(1 - i)", "v"));
            yield return new MBench("num", "divmod", Loop10k(null, "q, r = divmod(i, 7)", "q + r"));

            // ---------------- list ----------------
            yield return new MBench("list", "append", "lst = []\nfor i in range(20000):\n    lst.append(i)\nresult = len(lst)\n");
            yield return new MBench("list", "append_pop", "lst = []\nfor i in range(20000):\n    lst.append(i)\n    x = lst.pop()\nresult = x\n");
            yield return new MBench("list", "index_read", "lst = list(range(100))\ns = 0\nfor i in range(20000):\n    s = s + lst[i % 100]\nresult = s\n");
            yield return new MBench("list", "index_write", "lst = list(range(100))\nfor i in range(20000):\n    lst[i % 100] = i\nresult = len(lst)\n");
            yield return new MBench("list", "contains", "lst = list(range(100))\nn = 0\nfor i in range(5000):\n    if (i % 150) in lst:\n        n = n + 1\nresult = n\n");
            yield return new MBench("list", "slice", "lst = list(range(100))\nn = 0\nfor i in range(10000):\n    t = lst[10:20]\n    n = n + len(t)\nresult = n\n");
            yield return new MBench("list", "extend", "n = 0\nsrc = [1, 2, 3, 4, 5]\nfor i in range(5000):\n    lst = []\n    lst.extend(src)\n    n = n + len(lst)\nresult = n\n");
            yield return new MBench("list", "reverse", "lst = list(range(100))\nfor i in range(5000):\n    lst.reverse()\nresult = lst[0]\n");
            yield return new MBench("list", "sort_100", "import random\nrandom.seed(7)\nbase = [random.randint(0, 1000) for i in range(100)]\nfor i in range(500):\n    lst = list(base)\n    lst.sort()\nresult = lst[0]\n");
            yield return new MBench("list", "count", "lst = list(range(50)) * 2\nn = 0\nfor i in range(2000):\n    n = lst.count(25)\nresult = n\n");

            // ---------------- dict ----------------
            yield return new MBench("dict", "set_item", "d = {}\nfor i in range(20000):\n    d[i % 500] = i\nresult = len(d)\n");
            yield return new MBench("dict", "get_item", "d = {}\nfor i in range(500):\n    d[i] = i\ns = 0\nfor i in range(20000):\n    s = s + d[i % 500]\nresult = s\n");
            yield return new MBench("dict", "get_method", "d = {}\nfor i in range(500):\n    d[i] = i\ns = 0\nfor i in range(20000):\n    s = s + d.get(i % 700, 0)\nresult = s\n");
            yield return new MBench("dict", "contains", "d = {}\nfor i in range(500):\n    d[i] = i\nn = 0\nfor i in range(20000):\n    if (i % 700) in d:\n        n = n + 1\nresult = n\n");
            yield return new MBench("dict", "str_keys", "d = {}\nkeys = [str(k) for k in range(200)]\nfor i in range(10000):\n    d[keys[i % 200]] = i\nresult = len(d)\n");
            yield return new MBench("dict", "iter_keys", "d = {}\nfor i in range(200):\n    d[i] = i\ns = 0\nfor i in range(500):\n    for k in d:\n        s = s + k\nresult = s\n");
            yield return new MBench("dict", "items", "d = {}\nfor i in range(200):\n    d[i] = i\ns = 0\nfor i in range(500):\n    for kv in d.items():\n        s = s + kv[1]\nresult = s\n");
            yield return new MBench("dict", "update", "src = {1: 1, 2: 2, 3: 3, 4: 4, 5: 5}\nfor i in range(5000):\n    d = {}\n    d.update(src)\nresult = len(d)\n");

            // ---------------- set / tuple ----------------
            yield return new MBench("set", "add", "s = set()\nfor i in range(20000):\n    s.add(i % 500)\nresult = len(s)\n");
            yield return new MBench("set", "contains", "s = set(range(500))\nn = 0\nfor i in range(20000):\n    if (i % 700) in s:\n        n = n + 1\nresult = n\n");
            yield return new MBench("set", "union", "a = set(range(50))\nb = set(range(25, 75))\nfor i in range(3000):\n    c = a | b\nresult = len(c)\n");
            yield return new MBench("tuple", "pack_unpack", "s = 0\nfor i in range(20000):\n    t = (i, i + 1, i + 2)\n    a, b, c = t\n    s = a + c\nresult = s\n");

            // ---------------- builtins ----------------
            yield return new MBench("builtin", "len_str", Loop10k("s = 'alpha-beta-gamma'", "n = len(s)", "n"));
            yield return new MBench("builtin", "enumerate", "lst = list(range(100))\ns = 0\nfor i in range(1000):\n    for j, v in enumerate(lst):\n        s = s + j\nresult = s\n");
            yield return new MBench("builtin", "zip", "a = list(range(100))\nb = list(range(100))\ns = 0\nfor i in range(1000):\n    for x, y in zip(a, b):\n        s = x + y\nresult = s\n");
            yield return new MBench("builtin", "map_lambda", "lst = list(range(100))\nn = 0\nfor i in range(1000):\n    r = list(map(lambda x: x + 1, lst))\n    n = len(r)\nresult = n\n");
            yield return new MBench("builtin", "filter_lambda", "lst = list(range(100))\nn = 0\nfor i in range(1000):\n    r = list(filter(lambda x: x % 2 == 0, lst))\n    n = len(r)\nresult = n\n");
            yield return new MBench("builtin", "sum_list", "lst = list(range(100))\ns = 0\nfor i in range(3000):\n    s = sum(lst)\nresult = s\n");
            yield return new MBench("builtin", "max_list", "lst = list(range(100))\nfor i in range(3000):\n    m = max(lst)\nresult = m\n");
            yield return new MBench("builtin", "sorted_100", "import random\nrandom.seed(7)\nbase = [random.randint(0, 1000) for i in range(100)]\nfor i in range(500):\n    r = sorted(base)\nresult = r[0]\n");
            yield return new MBench("builtin", "all_list", "lst = [1] * 100\nn = 0\nfor i in range(3000):\n    if all(lst):\n        n = n + 1\nresult = n\n");
            yield return new MBench("builtin", "isinstance", Loop10k(null, "b = isinstance(i, int)", "1 if b else 0"));

            // ---------------- calls ----------------
            yield return new MBench("call", "def_0arg", "def f():\n    return 1\ns = 0\nfor i in range(20000):\n    s = s + f()\nresult = s\n");
            yield return new MBench("call", "def_2arg", "def f(a, b):\n    return a + b\ns = 0\nfor i in range(20000):\n    s = f(i, 1)\nresult = s\n");
            yield return new MBench("call", "def_kwargs", "def f(a=0, b=0):\n    return a + b\ns = 0\nfor i in range(20000):\n    s = f(a=i, b=1)\nresult = s\n");
            yield return new MBench("call", "def_defaults", "def f(a, b=10, c=20):\n    return a + b + c\ns = 0\nfor i in range(20000):\n    s = f(i)\nresult = s\n");
            yield return new MBench("call", "lambda_call", "f = lambda x: x + 1\ns = 0\nfor i in range(20000):\n    s = f(i)\nresult = s\n");
            yield return new MBench("call", "nested_call", "def g(x):\n    return x + 1\ndef f(x):\n    return g(x) + 1\ns = 0\nfor i in range(10000):\n    s = f(i)\nresult = s\n");
            yield return new MBench("call", "recursion_fib15", "def fib(n):\n    if n < 2:\n        return n\n    return fib(n - 1) + fib(n - 2)\ns = 0\nfor i in range(30):\n    s = fib(15)\nresult = s\n");
            yield return new MBench("call", "try_no_raise", "s = 0\nfor i in range(20000):\n    try:\n        s = s + 1\n    except ValueError:\n        s = 0\nresult = s\n");
            yield return new MBench("call", "raise_catch", "n = 0\nfor i in range(5000):\n    try:\n        raise ValueError('x')\n    except ValueError:\n        n = n + 1\nresult = n\n");

            // ---------------- comprehensions ----------------
            yield return new MBench("comp", "listcomp", "n = 0\nfor i in range(100):\n    r = [x * 2 for x in range(200)]\n    n = len(r)\nresult = n\n");
            yield return new MBench("comp", "listcomp_if", "n = 0\nfor i in range(100):\n    r = [x for x in range(200) if x % 2 == 0]\n    n = len(r)\nresult = n\n");
            yield return new MBench("comp", "dictcomp", "n = 0\nfor i in range(100):\n    d = {x: x * 2 for x in range(200)}\n    n = len(d)\nresult = n\n");
            yield return new MBench("comp", "setcomp", "n = 0\nfor i in range(100):\n    s = {x % 50 for x in range(200)}\n    n = len(s)\nresult = n\n");
            yield return new MBench("comp", "genexp_sum", "s = 0\nfor i in range(100):\n    s = sum(x * 2 for x in range(200))\nresult = s\n");

            // ---------------- json ----------------
            yield return new MBench("json", "dumps_small", "import json\nobj = {'id': 7, 'name': 'order', 'qty': 3, 'price': 19.5, 'ok': True}\nn = 0\nfor i in range(2000):\n    t = json.dumps(obj)\n    n = len(t)\nresult = n\n");
            yield return new MBench("json", "loads_small", "import json\ntxt = '{\"id\": 7, \"name\": \"order\", \"qty\": 3, \"price\": 19.5, \"ok\": true}'\nn = 0\nfor i in range(2000):\n    obj = json.loads(txt)\n    n = obj['qty']\nresult = n\n");
            yield return new MBench("json", "loads_nested", "import json\ntxt = '{\"a\": [1, 2, 3, {\"b\": [4, 5, {\"c\": \"deep\"}]}], \"d\": {\"e\": {\"f\": [7, 8, 9]}}}'\nn = 0\nfor i in range(1500):\n    obj = json.loads(txt)\n    n = len(obj['a'])\nresult = n\n");

            // ---------------- re ----------------
            yield return new MBench("re", "match_compiled", "import re\np = re.compile('([a-z]+)([0-9]+)')\nn = 0\nfor i in range(5000):\n    m = p.match('abc123')\n    if m:\n        n = n + 1\nresult = n\n");
            yield return new MBench("re", "group", "import re\np = re.compile('([a-z]+)([0-9]+)')\nn = 0\nfor i in range(5000):\n    m = p.match('abc123')\n    n = len(m.group(1))\nresult = n\n");
            yield return new MBench("re", "findall", "import re\np = re.compile('[0-9]+')\ns = 'a1 b22 c333 d4444 e55555'\nn = 0\nfor i in range(3000):\n    r = p.findall(s)\n    n = len(r)\nresult = n\n");
            yield return new MBench("re", "sub", "import re\np = re.compile('[0-9]+')\ns = 'a1 b22 c333 d4444'\nn = 0\nfor i in range(2000):\n    t = p.sub('#', s)\n    n = len(t)\nresult = n\n");
            yield return new MBench("re", "search_uncompiled", "import re\ns = 'alpha beta 42 gamma'\nn = 0\nfor i in range(3000):\n    m = re.search('[0-9]+', s)\n    if m:\n        n = n + 1\nresult = n\n");

            // ---------------- datetime ----------------
            yield return new MBench("datetime", "dt_ctor", "import datetime\nfor i in range(5000):\n    d = datetime.datetime(2026, 1 + i % 12, 1 + i % 28)\nresult = d.year\n");
            yield return new MBench("datetime", "td_ctor_kw", "import datetime\nfor i in range(5000):\n    t = datetime.timedelta(days=i % 365, hours=i % 24)\nresult = t.days\n");
            yield return new MBench("datetime", "dt_add_td", "import datetime\nbase = datetime.datetime(2026, 1, 1)\ntd = datetime.timedelta(days=3, hours=7)\nfor i in range(5000):\n    d = base + td\nresult = d.day\n");
            yield return new MBench("datetime", "strftime", "import datetime\nd = datetime.datetime(2026, 7, 8, 12, 30, 45)\nn = 0\nfor i in range(2000):\n    t = d.strftime('%Y-%m-%d %H:%M:%S')\n    n = len(t)\nresult = n\n");
            yield return new MBench("datetime", "isoformat", "import datetime\nd = datetime.datetime(2026, 7, 8, 12, 30, 45)\nn = 0\nfor i in range(5000):\n    t = d.isoformat()\n    n = len(t)\nresult = n\n");
            yield return new MBench("datetime", "weekday", "import datetime\nd = datetime.datetime(2026, 7, 8)\nn = 0\nfor i in range(10000):\n    n = n + d.weekday()\nresult = n\n");

            // ---------------- math / random ----------------
            yield return new MBench("math", "sqrt", "import math\nx = 0.0\nfor i in range(20000):\n    x = math.sqrt(i + 1.0)\nresult = int(x)\n");
            yield return new MBench("math", "floor", "import math\nn = 0\nfor i in range(20000):\n    n = math.floor(i * 0.5)\nresult = n\n");
            yield return new MBench("math", "sin", "import math\nx = 0.0\nfor i in range(20000):\n    x = math.sin(i * 0.01)\nresult = int(x * 100)\n");
            yield return new MBench("random", "random", "import random\nrandom.seed(42)\nx = 0.0\nfor i in range(20000):\n    x = random.random()\nresult = int(x * 100)\n");
            yield return new MBench("random", "randint", "import random\nrandom.seed(42)\nn = 0\nfor i in range(10000):\n    n = random.randint(1, 100)\nresult = n\n");
            yield return new MBench("random", "choice", "import random\nrandom.seed(42)\nlst = list(range(100))\nn = 0\nfor i in range(10000):\n    n = random.choice(lst)\nresult = n\n");

            // ---------------- collections / itertools ----------------
            yield return new MBench("collections", "counter_ctor", "import collections\ns = 'abcabcabcxyz' * 5\nn = 0\nfor i in range(2000):\n    c = collections.Counter(s)\n    n = c['a']\nresult = n\n");
            yield return new MBench("collections", "defaultdict", "import collections\nfor i in range(3000):\n    d = collections.defaultdict(list)\n    d['k'].append(i)\nresult = len(d)\n");
            yield return new MBench("collections", "deque_rotate", "import collections\ndq = collections.deque(range(100))\nfor i in range(10000):\n    dq.append(i)\n    x = dq.popleft()\nresult = x\n");
            yield return new MBench("itertools", "chain_sum", "import itertools\na = list(range(50))\nb = list(range(50))\ns = 0\nfor i in range(2000):\n    s = sum(itertools.chain(a, b))\nresult = s\n");
            yield return new MBench("itertools", "product", "import itertools\nr = list(range(10))\nn = 0\nfor i in range(500):\n    n = len(list(itertools.product(r, r)))\nresult = n\n");

            // ---------------- binary / hash ----------------
            yield return new MBench("hash", "md5_1kb", "import hashlib\ndata = b'x' * 1024\nn = 0\nfor i in range(1000):\n    h = hashlib.md5(data).hexdigest()\n    n = len(h)\nresult = n\n");
            yield return new MBench("hash", "b64encode", "import base64\ndata = b'payload-bytes-here!' * 10\nn = 0\nfor i in range(2000):\n    t = base64.b64encode(data)\n    n = len(t)\nresult = n\n");

            // ---------------- zlib / gzip (10 KB CSV-ish payload) ----------------
            yield return new MBench("zlib", "compress_10kb", "import zlib\ndata = b'field1;field2;99.50;2026-07-10;' * 320\nn = 0\nfor i in range(300):\n    n = len(zlib.compress(data))\nresult = n\n");
            yield return new MBench("zlib", "decompress_10kb", "import zlib\nblob = zlib.compress(b'field1;field2;99.50;2026-07-10;' * 320)\nn = 0\nfor i in range(1000):\n    n = len(zlib.decompress(blob))\nresult = n\n");
            yield return new MBench("zlib", "crc32_10kb", "import zlib\ndata = b'field1;field2;99.50;2026-07-10;' * 320\nn = 0\nfor i in range(2000):\n    n = zlib.crc32(data)\nresult = n\n");
            yield return new MBench("zlib", "adler32_10kb", "import zlib\ndata = b'field1;field2;99.50;2026-07-10;' * 320\nn = 0\nfor i in range(2000):\n    n = zlib.adler32(data)\nresult = n\n");
            yield return new MBench("zlib", "gzip_compress_10kb", "import gzip\ndata = b'field1;field2;99.50;2026-07-10;' * 320\nn = 0\nfor i in range(300):\n    n = len(gzip.compress(data))\nresult = n\n");
            yield return new MBench("zlib", "gzip_decompress_10kb", "import gzip\nblob = gzip.compress(b'field1;field2;99.50;2026-07-10;' * 320)\nn = 0\nfor i in range(1000):\n    n = len(gzip.decompress(blob))\nresult = n\n");

            foreach (MBench b in Recent())
                yield return b;
            foreach (MBench b in Tail())
                yield return b;
        }

        // The tail the parity audit closed: the bytes method surface, itertext, the numeric tower,
        // Decimal scaleb/shift, Match.expand, pbkdf2_hmac and iter_unpack.
        private static IEnumerable<MBench> Tail()
        {
            const string Payload = "data = b'field one;field two;99.50;2026-07-10;' * 5000\n";
            yield return new MBench("bytes", "translate", Payload + "t = bytes.maketrans(b';', b'|')\nn = 0\nfor i in range(10):\n    n = len(data.translate(t))\nresult = n\n");
            yield return new MBench("bytes", "translate_delete", Payload + "n = 0\nfor i in range(10):\n    n = len(data.translate(None, b';'))\nresult = n\n");
            yield return new MBench("bytes", "splitlines", "data = (b'a line of text here' + bytes([10])) * 20000\nn = 0\nfor i in range(10):\n    n = len(data.splitlines())\nresult = n\n");
            // partition returns copies of both halves, so these use a ROW rather than the payload: the
            // point is the operation, not moving 200 KB per call.
            const string Row = "row = b'field one;field two;99.50;2026-07-10'\n";
            yield return new MBench("bytes", "partition", Row + "n = 0\nfor i in range(20000):\n    n = len(row.partition(b';')[2])\nresult = n\n");
            yield return new MBench("bytes", "rpartition", Row + "n = 0\nfor i in range(20000):\n    n = len(row.rpartition(b';')[0])\nresult = n\n");
            yield return new MBench("bytes", "title", "data = b'hello world of bytes ' * 5000\nn = 0\nfor i in range(20):\n    n = len(data.title())\nresult = n\n");
            yield return new MBench("bytes", "isascii", Payload + "n = 0\nfor i in range(200):\n    n = 1 if data.isascii() else 0\nresult = n\n");
            yield return new MBench("bytes", "zfill", "n = 0\nfor i in range(20000):\n    n = len(b'42'.zfill(40))\nresult = n\n");
            yield return new MBench("bytes", "center", "n = 0\nfor i in range(20000):\n    n = len(b'ab'.center(40, b'-'))\nresult = n\n");
            yield return new MBench("bytes", "removeprefix", Row + "n = 0\nfor i in range(20000):\n    n = len(row.removeprefix(b'field'))\nresult = n\n");

            const string Tree = "import xml.etree.ElementTree as ET\n"
                + "root = ET.Element('r')\n"
                + "for i in range(2000):\n"
                + "    e = ET.SubElement(root, 'c')\n"
                + "    e.text = 'value'\n"
                + "    e.tail = 'tail'\n";
            yield return new MBench("xml", "itertext", Tree + "n = 0\nfor i in range(50):\n    n = len(list(root.itertext()))\nresult = n\n");
            yield return new MBench("xml", "itertext_join", Tree + "n = 0\nfor i in range(50):\n    n = len(''.join(root.itertext()))\nresult = n\n");
            yield return new MBench("xml", "comment_build", "import xml.etree.ElementTree as ET\nn = 0\nfor i in range(200):\n    r = ET.Element('r')\n    for j in range(20):\n        r.append(ET.Comment('note'))\n    n = len(ET.tostring(r, encoding='unicode'))\nresult = n\n");

            yield return new MBench("num", "int_real", "n = 0\nfor i in range(20000):\n    n = i.real + i.imag\nresult = n\n");
            yield return new MBench("num", "int_numerator", "n = 0\nfor i in range(20000):\n    n = i.numerator + i.denominator\nresult = n\n");

            const string Dec = "from decimal import Decimal\nd = Decimal('19.99')\n";
            yield return new MBench("decimal", "scaleb", Dec + "n = 0\nfor i in range(10000):\n    n = d.scaleb(2)\nresult = int(n)\n");
            yield return new MBench("decimal", "shift", Dec + "n = 0\nfor i in range(10000):\n    n = d.shift(1)\nresult = int(n)\n");
            yield return new MBench("decimal", "as_integer_ratio", Dec + "n = 0\nfor i in range(10000):\n    n = d.as_integer_ratio()[0]\nresult = n\n");
            yield return new MBench("decimal", "quantize", Dec + "q = Decimal('0.01')\nn = 0\nfor i in range(10000):\n    n = d.quantize(q)\nresult = int(n)\n");

            yield return new MBench("re", "expand", "import re\nm = re.match(r'(\\w+)-(\\w+)', 'alpha-beta')\nn = 0\nfor i in range(20000):\n    n = len(m.expand(r'\\2:\\1'))\nresult = n\n");
            yield return new MBench("re", "lastindex", "import re\nm = re.match(r'(a)(b)(c)', 'abc')\nn = 0\nfor i in range(20000):\n    n = m.lastindex\nresult = n\n");

            yield return new MBench("hash", "pbkdf2_sha256", "import hashlib\nn = 0\nfor i in range(10):\n    n = len(hashlib.pbkdf2_hmac('sha256', b'password', b'salt', 1000))\nresult = n\n");
            yield return new MBench("struct", "iter_unpack", "import struct\ndata = struct.pack('>4h', 1, 2, 3, 4) * 5000\nn = 0\nfor i in range(20):\n    n = len(list(struct.iter_unpack('>h', data)))\nresult = n\n");
            yield return new MBench("struct", "unpack_loop", "import struct\ndata = struct.pack('>4h', 1, 2, 3, 4)\nn = 0\nfor i in range(20000):\n    n = struct.unpack('>4h', data)[3]\nresult = n\n");
        }

        // The surface added over the last cycles. A bench IronPython cannot run at all is worth as much
        // as a timing: the report marks it FAIL rather than dropping the row.
        private static IEnumerable<MBench> Recent()
        {
            // ---------------- datetime: parsing and time zones ----------------
            yield return new MBench("datetime", "fromisoformat", "import datetime\ns = '2026-09-10T12:34:56.123456'\nn = 0\nfor i in range(10000):\n    d = datetime.datetime.fromisoformat(s)\n    n = d.day\nresult = n\n");
            yield return new MBench("datetime", "fromisoformat_tz", "import datetime\ns = '2026-09-10T12:34:56.123456+03:00'\nn = 0\nfor i in range(10000):\n    d = datetime.datetime.fromisoformat(s)\n    n = d.day\nresult = n\n");
            yield return new MBench("datetime", "isoformat_out", "import datetime\nd = datetime.datetime(2026, 9, 10, 12, 34, 56, 123456)\nn = 0\nfor i in range(10000):\n    n = len(d.isoformat())\nresult = n\n");
            yield return new MBench("datetime", "strptime_tz", "import datetime\nn = 0\nfor i in range(3000):\n    d = datetime.datetime.strptime('2026-09-10 12:34:56 +0300', '%Y-%m-%d %H:%M:%S %z')\n    n = d.day\nresult = n\n");
            yield return new MBench("datetime", "strftime_wide", "import datetime\nd = datetime.datetime(2026, 9, 10, 12, 34, 56)\nf = '%Y-%m-%dT%H:%M:%S %a %A %b %B %j %U %W'\nn = 0\nfor i in range(3000):\n    n = len(d.strftime(f))\nresult = n\n");
            yield return new MBench("datetime", "astimezone_fixed", "import datetime\ntz = datetime.timezone(datetime.timedelta(hours=3))\nd = datetime.datetime(2026, 7, 1, 12, 0, tzinfo=datetime.timezone.utc)\nn = 0\nfor i in range(5000):\n    n = d.astimezone(tz).hour\nresult = n\n");

            // ---------------- zoneinfo ----------------
            yield return new MBench("zoneinfo", "ctor_cached", "import zoneinfo\nn = 0\nfor i in range(5000):\n    tz = zoneinfo.ZoneInfo('Europe/Amsterdam')\n    n = n + 1\nresult = n\n");
            yield return new MBench("zoneinfo", "astimezone", "import zoneinfo, datetime\ntz = zoneinfo.ZoneInfo('Europe/Amsterdam')\nd = datetime.datetime(2026, 7, 1, 12, 0, tzinfo=datetime.timezone.utc)\nn = 0\nfor i in range(3000):\n    n = d.astimezone(tz).hour\nresult = n\n");
            yield return new MBench("zoneinfo", "utcoffset", "import zoneinfo, datetime\ntz = zoneinfo.ZoneInfo('Europe/Amsterdam')\nd = datetime.datetime(2026, 7, 1, 12, 0, tzinfo=tz)\nn = 0\nfor i in range(3000):\n    n = int(d.utcoffset().total_seconds())\nresult = n\n");

            // ---------------- unicodedata ----------------
            yield return new MBench("unicodedata", "normalize_nfc", "import unicodedata\ns = 'Cre\\u0301me Bru\\u0302le\\u0301e de la maison' * 4\nn = 0\nfor i in range(3000):\n    n = len(unicodedata.normalize('NFC', s))\nresult = n\n");
            yield return new MBench("unicodedata", "normalize_nfkd", "import unicodedata\ns = 'Cr\\u00e8me Br\\u00fbl\\u00e9e \\ufb01n \\u2460' * 4\nn = 0\nfor i in range(3000):\n    n = len(unicodedata.normalize('NFKD', s))\nresult = n\n");
            yield return new MBench("unicodedata", "category", "import unicodedata\ns = 'Alpha Beta 123 \\u00e9\\u4e2d'\nn = 0\nfor i in range(2000):\n    for c in s:\n        n = n + len(unicodedata.category(c))\nresult = n\n");
            yield return new MBench("unicodedata", "east_asian_width", "import unicodedata\ns = 'Alpha \\u4e2d\\u6587 \\uff21\\uff22'\nn = 0\nfor i in range(2000):\n    for c in s:\n        n = n + len(unicodedata.east_asian_width(c))\nresult = n\n");
            yield return new MBench("unicodedata", "name", "import unicodedata\ns = 'Alpha\\u00e9\\u20ac'\nn = 0\nfor i in range(1000):\n    for c in s:\n        n = n + len(unicodedata.name(c))\nresult = n\n");

            // ---------------- urllib.parse ----------------
            yield return new MBench("urllib", "urlsplit", "from urllib.parse import urlsplit\nu = 'https://user:pw@example.com:8443/a/b/c?x=1&y=2#frag'\nn = 0\nfor i in range(10000):\n    n = len(urlsplit(u).netloc)\nresult = n\n");
            yield return new MBench("urllib", "urljoin", "from urllib.parse import urljoin\nn = 0\nfor i in range(5000):\n    n = len(urljoin('http://example.com/a/b/c?q=1#f', '../d/e'))\nresult = n\n");
            yield return new MBench("urllib", "quote", "from urllib.parse import quote\ns = 'a value/with spaces & \\u043a\\u0438\\u0440\\u0438\\u043b\\u043b\\u0438\\u0446\\u0430'\nn = 0\nfor i in range(10000):\n    n = len(quote(s))\nresult = n\n");
            yield return new MBench("urllib", "unquote", "from urllib.parse import unquote\ns = 'a%20value%2Fwith%20spaces%20%26%20%D0%BA%D0%B8%D1%80'\nn = 0\nfor i in range(10000):\n    n = len(unquote(s))\nresult = n\n");
            yield return new MBench("urllib", "urlencode", "from urllib.parse import urlencode\nd = {'one': 'value 1', 'two': 'value/2', 'three': 42}\nn = 0\nfor i in range(5000):\n    n = len(urlencode(d))\nresult = n\n");
            yield return new MBench("urllib", "parse_qsl", "from urllib.parse import parse_qsl\nq = 'one=value+1&two=value%2F2&three=42&four=&five=x'\nn = 0\nfor i in range(5000):\n    n = len(parse_qsl(q))\nresult = n\n");

            // ---------------- calendar / textwrap / secrets / traceback ----------------
            yield return new MBench("calendar", "monthrange", "import calendar\nn = 0\nfor i in range(10000):\n    n = calendar.monthrange(2026, (i % 12) + 1)[1]\nresult = n\n");
            yield return new MBench("calendar", "monthcalendar", "import calendar\nn = 0\nfor i in range(2000):\n    n = len(calendar.monthcalendar(2026, (i % 12) + 1))\nresult = n\n");
            yield return new MBench("textwrap", "wrap_paragraph", "import textwrap\nt = 'The quick brown fox jumps over the lazy dog. ' * 20\nn = 0\nfor i in range(500):\n    n = len(textwrap.wrap(t, width=70))\nresult = n\n");
            yield return new MBench("textwrap", "dedent", "import textwrap\nt = ('    indented line of text' + chr(10)) * 40\nn = 0\nfor i in range(2000):\n    n = len(textwrap.dedent(t))\nresult = n\n");
            yield return new MBench("textwrap", "shorten", "import textwrap\nt = 'The quick brown fox jumps over the lazy dog. ' * 20\nn = 0\nfor i in range(1000):\n    n = len(textwrap.shorten(t, width=60))\nresult = n\n");
            yield return new MBench("secrets", "token_hex", "import secrets\nn = 0\nfor i in range(3000):\n    n = len(secrets.token_hex(16))\nresult = n\n");
            yield return new MBench("secrets", "randbelow", "import secrets\nn = 0\nfor i in range(3000):\n    n = secrets.randbelow(1000000)\nresult = n\n");
            yield return new MBench("traceback", "format_exc", "import traceback\nn = 0\nfor i in range(2000):\n    try:\n        raise ValueError('boom')\n    except ValueError:\n        n = len(traceback.format_exc())\nresult = n\n");

            // ---------------- xml.etree ----------------
            yield return new MBench("xml", "fromstring", "import xml.etree.ElementTree as ET\ndoc = '<root>' + '<row a=\"1\"><c>text</c></row>' * 20 + '</root>'\nn = 0\nfor i in range(500):\n    n = len(ET.fromstring(doc))\nresult = n\n");
            yield return new MBench("xml", "tostring", "import xml.etree.ElementTree as ET\nroot = ET.Element('root')\nfor i in range(20):\n    e = ET.SubElement(root, 'row', {'a': '1'})\n    ET.SubElement(e, 'c').text = 'text'\nn = 0\nfor i in range(500):\n    n = len(ET.tostring(root, encoding='unicode'))\nresult = n\n");
            yield return new MBench("xml", "findall", "import xml.etree.ElementTree as ET\ndoc = '<root>' + '<row a=\"1\"><c>text</c></row>' * 20 + '</root>'\nroot = ET.fromstring(doc)\nn = 0\nfor i in range(2000):\n    n = len(root.findall('.//c'))\nresult = n\n");

            // ---------------- csv.Sniffer ----------------
            yield return new MBench("csv", "sniffer_sniff", "import csv\nsample = ('name,amount,date' + chr(10) + 'alpha,10.50,2026-07-01' + chr(10)) * 20\nn = 0\nfor i in range(200):\n    n = len(csv.Sniffer().sniff(sample).delimiter)\nresult = n\n");
            yield return new MBench("csv", "sniffer_has_header", "import csv\nsample = ('name,amount,date' + chr(10) + 'alpha,10.50,2026-07-01' + chr(10)) * 20\nn = 0\nfor i in range(200):\n    n = 1 if csv.Sniffer().has_header(sample) else 0\nresult = n\n");

            // ---------------- json / collections additions ----------------
            yield return new MBench("json", "dumps_default", "import json, datetime\nobj = {'when': datetime.datetime(2026, 9, 10, 12, 0), 'n': 1, 's': 'text'}\nn = 0\nfor i in range(3000):\n    n = len(json.dumps(obj, default=str))\nresult = n\n");
            yield return new MBench("json", "loads_parse_float", "import json\nfrom decimal import Decimal\ns = '{\"a\": 1.5, \"b\": [2.25, 3.125], \"c\": 4.0625}'\nn = 0\nfor i in range(3000):\n    n = len(json.loads(s, parse_float=Decimal))\nresult = n\n");
            yield return new MBench("collections", "counter_total", "import collections\nc = collections.Counter('abcabcabcxyz' * 5)\nn = 0\nfor i in range(5000):\n    n = c.total()\nresult = n\n");
            yield return new MBench("collections", "namedtuple_defaults", "import collections\nP = collections.namedtuple('P', 'x y z', defaults=(5, 6))\nn = 0\nfor i in range(5000):\n    n = P(i).z\nresult = n\n");
        }

        private static string Loop10k(string setup, string body, string resultExpr)
        {
            string head = setup == null ? "" : setup + "\n";
            return head + "for i in range(10000):\n    " + body + "\nresult = " + resultExpr + "\n";
        }
    }
}
