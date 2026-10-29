using System.Collections.Generic;

namespace Outbridge.PyLite.PerfBench
{
    // The cases behind the performance work of release 1.1.0 (--focus): each one isolates a
    // gap that survived re-measurement, sized so a single run takes tens to hundreds of milliseconds.
    // They are the A/B benchmarks of P1-P5: run `--focus --no-iron` on the baseline and on the change.
    internal static class FocusBenches
    {
        private const string Shuffled = "import random\nrandom.seed(7)\ndata = list(range(100000))\nrandom.shuffle(data)\n";

        internal static IEnumerable<MatrixBenches.MBench> All()
        {
            // ---- P1 sorting: the setup row is subtracted by eye from the rows that share it ----
            yield return new MatrixBenches.MBench("sort", "shuffled_setup_only", Shuffled + "result = data[0]\n");
            yield return new MatrixBenches.MBench("sort", "shuffled", Shuffled + "r = sorted(data)\nresult = r[0]\n");
            yield return new MatrixBenches.MBench("sort", "shuffled_key_lambda", Shuffled + "r = sorted(data, key=lambda x: -x)\nresult = r[0]\n");
            yield return new MatrixBenches.MBench("sort", "shuffled_key_builtin", Shuffled + "r = sorted(data, key=abs)\nresult = r[0]\n");
            yield return new MatrixBenches.MBench("sort", "shuffled_keys_precomputed", Shuffled + "keys = [-x for x in data]\nr = sorted(keys)\nresult = r[0]\n");
            yield return new MatrixBenches.MBench("sort", "shuffled_reverse", Shuffled + "r = sorted(data, reverse=True)\nresult = r[0]\n");
            yield return new MatrixBenches.MBench("sort", "presorted", "data = list(range(100000))\nr = sorted(data)\nresult = r[0]\n");
            yield return new MatrixBenches.MBench("sort", "reversed", "data = list(range(100000, 0, -1))\nr = sorted(data)\nresult = r[0]\n");
            yield return new MatrixBenches.MBench("sort", "range_key_neg", "r = sorted(range(100000), key=lambda x: -x)\nresult = r[0]\n");
            yield return new MatrixBenches.MBench("sort", "records_half_presorted", "import random\nrandom.seed(7)\ndata = [{'d': i if i % 2 == 0 else random.randint(0, 100000)} for i in range(50000)]\nr = sorted(data, key=lambda o: o['d'])\nresult = r[0]['d']\n");
            yield return new MatrixBenches.MBench("sort", "strings_shuffled", "import random\nrandom.seed(7)\ndata = ['k%07d' % i for i in range(50000)]\nrandom.shuffle(data)\nr = sorted(data)\nresult = r[0]\n");
            yield return new MatrixBenches.MBench("sort", "small_lists_x5k", "import random\nrandom.seed(7)\nbase = [random.randint(0, 1000) for i in range(100)]\nfor i in range(5000):\n    lst = list(base)\n    lst.sort()\nresult = lst[0]\n");

            // ---- P2 callbacks from builtins ----
            yield return new MatrixBenches.MBench("callback", "map_lambda", Shuffled + "r = list(map(lambda x: -x, data))\nresult = r[0]\n");
            yield return new MatrixBenches.MBench("callback", "filter_lambda", "lst = list(range(100))\nn = 0\nfor i in range(5000):\n    n = len(list(filter(lambda x: x % 2 == 0, lst)))\nresult = n\n");
            yield return new MatrixBenches.MBench("callback", "filter_def", "def even(x):\n    return x % 2 == 0\nlst = list(range(100))\nn = 0\nfor i in range(5000):\n    n = len(list(filter(even, lst)))\nresult = n\n");
            yield return new MatrixBenches.MBench("callback", "filter_none", "lst = list(range(100))\nn = 0\nfor i in range(5000):\n    n = len(list(filter(None, lst)))\nresult = n\n");
            yield return new MatrixBenches.MBench("callback", "filterfalse_lambda", "import itertools\nlst = list(range(100))\nn = 0\nfor i in range(5000):\n    n = len(list(itertools.filterfalse(lambda x: x % 2 == 0, lst)))\nresult = n\n");
            yield return new MatrixBenches.MBench("callback", "listcomp_if_same_work", "lst = list(range(100))\nn = 0\nfor i in range(5000):\n    n = len([x for x in lst if x % 2 == 0])\nresult = n\n");
            yield return new MatrixBenches.MBench("callback", "list_copy", "lst = list(range(100))\nn = 0\nfor i in range(5000):\n    n = len(list(lst))\nresult = n\n");
            yield return new MatrixBenches.MBench("callback", "min_max_key","lst = [(i * 7919) % 1000 for i in range(1000)]\nn = 0\nfor i in range(300):\n    n = min(lst, key=lambda x: -x) + max(lst, key=lambda x: -x)\nresult = n\n");

            // ---- P3 regex wrapper ----
            yield return new MatrixBenches.MBench("re", "sub_compiled", "import re\np = re.compile('[0-9]+')\ns = 'a1 b22 c333 d4444'\nn = 0\nfor i in range(20000):\n    n = len(p.sub('#', s))\nresult = n\n");
            yield return new MatrixBenches.MBench("re", "findall_compiled", "import re\np = re.compile('[0-9]+')\ns = 'a1 b22 c333 d4444'\nn = 0\nfor i in range(20000):\n    n = len(p.findall(s))\nresult = n\n");
            yield return new MatrixBenches.MBench("re", "match_groups", "import re\np = re.compile('([a-z]+)([0-9]+)')\nn = 0\nfor i in range(50000):\n    m = p.match('abc123')\n    n = n + len(m.group(1)) + len(m.group(2))\nresult = n\n");
            yield return new MatrixBenches.MBench("re", "match_fail_long", "import re\np = re.compile('[0-9]+x')\ns = 'abc' * 3000 + '1x'\nn = 0\nfor i in range(3000):\n    if p.match(s):\n        n = n + 1\nresult = n\n");
            yield return new MatrixBenches.MBench("re", "sub_groups", "import re\np = re.compile('([a-z])([0-9]+)')\ns = 'a1 b22 c333 d4444'\nn = 0\nfor i in range(20000):\n    n = len(p.sub(r'\\2\\1', s))\nresult = n\n");
            yield return new MatrixBenches.MBench("re", "match_compiled","import re\np = re.compile('([a-z]+)([0-9]+)')\nn = 0\nfor i in range(50000):\n    if p.match('abc123'):\n        n = n + 1\nresult = n\n");

            // ---- P4 building sets ----
            yield return new MatrixBenches.MBench("set", "setcomp_200_x2k", "n = 0\nfor i in range(2000):\n    n = len({x % 50 for x in range(200)})\nresult = n\n");
            yield return new MatrixBenches.MBench("set", "set_of_range_100k", "s = set(range(100000))\nresult = len(s)\n");
            yield return new MatrixBenches.MBench("set", "listcomp_200_x2k", "n = 0\nfor i in range(2000):\n    n = len([x for x in range(200)])\nresult = n\n");
            yield return new MatrixBenches.MBench("set", "dictcomp_range_200k", "d = {i: i for i in range(200000)}\nresult = len(d)\n");
            yield return new MatrixBenches.MBench("set", "setcomp_range_200k", "s = {i for i in range(200000)}\nresult = len(s)\n");
            yield return new MatrixBenches.MBench("set", "dictcomp_items_100k", "d = dict.fromkeys(range(100000), 1)\nr = {k: v + 1 for k, v in d.items()}\nresult = len(r)\n");
            yield return new MatrixBenches.MBench("set", "dictcomp_rows_50k", "rows = [{'id': i, 'v': i} for i in range(50000)]\nr = {row['id']: row for row in rows}\nresult = len(r)\n");
            yield return new MatrixBenches.MBench("set", "dict_loop_200k", "d = {}\nfor i in range(200000):\n    d[i] = i\nresult = len(d)\n");
            yield return new MatrixBenches.MBench("set", "set_of_dupes_200k", "lst = [i % 100 for i in range(200000)]\ns = set(lst)\nresult = len(s)\n");
            yield return new MatrixBenches.MBench("set", "listcomp_list_200k", "lst = list(range(200000))\nr = [x + 1 for x in lst]\nresult = len(r)\n");

            // ---- P5 calls and recursion ----
            yield return new MatrixBenches.MBench("call", "fib_20", "def fib(n):\n    if n < 2:\n        return n\n    return fib(n - 1) + fib(n - 2)\nresult = fib(20)\n");
            yield return new MatrixBenches.MBench("call", "one_line_helper_300k", "def helper(x):\n    return x * 2 + 1\ndef run():\n    s = 0\n    for i in range(300000):\n        s = helper(s) % 1000\n    return s\nresult = run()\n");
            yield return new MatrixBenches.MBench("call", "two_statement_helper_300k", "def helper(x):\n    y = x * 2\n    return y + 1\ndef run():\n    s = 0\n    for i in range(300000):\n        s = helper(s) % 1000\n    return s\nresult = run()\n");
            yield return new MatrixBenches.MBench("call", "flat_calls_200k","def f(n):\n    return n + 1\nx = 0\nfor i in range(200000):\n    x = f(i)\nresult = x\n");

            // ---- loops: the trailing number is the iteration count, so per-run ms / count = cost per iteration ----
            yield return new MatrixBenches.MBench("loop", "for_range_pass_500k", "for i in range(500000):\n    pass\nresult = 0\n");
            yield return new MatrixBenches.MBench("loop", "for_range_assign_500k", "x = 0\nfor i in range(500000):\n    x = i\nresult = x\n");
            yield return new MatrixBenches.MBench("loop", "for_range_int_add_500k", "s = 0\nfor i in range(500000):\n    s = s + i\nresult = s\n");
            yield return new MatrixBenches.MBench("loop", "for_range_int_augadd_500k", "s = 0\nfor i in range(500000):\n    s += i\nresult = s\n");
            yield return new MatrixBenches.MBench("loop", "for_range_float_add_500k", "x = 0.0\nfor i in range(500000):\n    x = x + 0.5\nresult = int(x)\n");
            yield return new MatrixBenches.MBench("loop", "while_counter_500k", "i = 0\nwhile i < 500000:\n    i = i + 1\nresult = i\n");
            yield return new MatrixBenches.MBench("loop", "for_list_500k", "lst = list(range(500000))\ns = 0\nfor v in lst:\n    s = s + v\nresult = s\n");
            yield return new MatrixBenches.MBench("loop", "for_dict_items_200k", "d = dict.fromkeys(range(200000), 1)\ns = 0\nfor k, v in d.items():\n    s = s + v\nresult = s\n");
            yield return new MatrixBenches.MBench("loop", "enumerate_300k", "lst = [1] * 300000\ns = 0\nfor i, v in enumerate(lst):\n    s = s + v\nresult = s\n");
            yield return new MatrixBenches.MBench("loop", "zip_300k", "a = [1] * 300000\nb = [2] * 300000\ns = 0\nfor x, y in zip(a, b):\n    s = s + x + y\nresult = s\n");
            yield return new MatrixBenches.MBench("loop", "nested_700x700", "s = 0\nfor i in range(700):\n    for j in range(700):\n        s = s + 1\nresult = s\n");
            yield return new MatrixBenches.MBench("loop", "if_elif_else_500k", "a = 0\nb = 0\nc = 0\nfor i in range(500000):\n    if i % 3 == 0:\n        a = a + 1\n    elif i % 3 == 1:\n        b = b + 1\n    else:\n        c = c + 1\nresult = a + b + c\n");
            yield return new MatrixBenches.MBench("loop", "continue_half_500k", "s = 0\nfor i in range(500000):\n    if i % 2:\n        continue\n    s = s + 1\nresult = s\n");
            yield return new MatrixBenches.MBench("loop", "list_index_rw_300k", "lst = list(range(100))\nfor i in range(300000):\n    lst[i % 100] = lst[(i + 1) % 100] + 1\nresult = lst[0]\n");
            yield return new MatrixBenches.MBench("loop", "dict_get_set_300k", "d = {}\nfor i in range(300000):\n    d[i % 100] = d.get(i % 100, 0) + 1\nresult = d[0]\n");
            yield return new MatrixBenches.MBench("loop", "attr_rw_300k", "class C:\n    def __init__(self):\n        self.x = 0\no = C()\nfor i in range(300000):\n    o.x = o.x + 1\nresult = o.x\n");
            yield return new MatrixBenches.MBench("loop", "builtin_len_300k", "s = 'abc'\nn = 0\nfor i in range(300000):\n    n = len(s)\nresult = n\n");
            yield return new MatrixBenches.MBench("loop", "builtin_abs_300k", "n = 0\nfor i in range(300000):\n    n = abs(i)\nresult = n\n");
            yield return new MatrixBenches.MBench("loop", "user_func_300k", "def f(n):\n    return n + 1\nx = 0\nfor i in range(300000):\n    x = f(i)\nresult = x\n");
            yield return new MatrixBenches.MBench("loop", "listcomp_500k", "r = [i * 2 for i in range(500000)]\nresult = len(r)\n");
            yield return new MatrixBenches.MBench("loop", "genexp_sum_500k", "result = sum(i * 2 for i in range(500000))\n");
            yield return new MatrixBenches.MBench("loop", "dictcomp_200k", "d = {i: i for i in range(200000)}\nresult = len(d)\n");

            // ---- recursion: the trailing number is the call count ----
            yield return new MatrixBenches.MBench("recur", "fib_20_21891", "def fib(n):\n    if n < 2:\n        return n\n    return fib(n - 1) + fib(n - 2)\nresult = fib(20)\n");
            yield return new MatrixBenches.MBench("recur", "fib_24_150049", "def fib(n):\n    if n < 2:\n        return n\n    return fib(n - 1) + fib(n - 2)\nresult = fib(24)\n");
            yield return new MatrixBenches.MBench("recur", "linear_depth150_x1000_151000", "def down(n):\n    if n == 0:\n        return 0\n    return 1 + down(n - 1)\ns = 0\nfor i in range(1000):\n    s = s + down(150)\nresult = s\n");
            yield return new MatrixBenches.MBench("recur", "mutual_depth150_x1000_151000", "def even(n):\n    if n == 0:\n        return True\n    return odd(n - 1)\ndef odd(n):\n    if n == 0:\n        return False\n    return even(n - 1)\ns = 0\nfor i in range(1000):\n    if even(150):\n        s = s + 1\nresult = s\n");
            yield return new MatrixBenches.MBench("recur", "default_arg_depth150_x1000_151000", "def total(n, acc=0):\n    if n == 0:\n        return acc\n    return total(n - 1, acc + n)\ns = 0\nfor i in range(1000):\n    s = total(150)\nresult = s\n");
            yield return new MatrixBenches.MBench("recur", "keyword_args_depth150_x1000_151000", "def total(n, acc=0):\n    if n == 0:\n        return acc\n    return total(n=n - 1, acc=acc + n)\ns = 0\nfor i in range(1000):\n    s = total(n=150)\nresult = s\n");
            yield return new MatrixBenches.MBench("recur", "method_depth150_x1000_151000", "class Node:\n    def __init__(self):\n        self.k = 1\n    def depth(self, n):\n        if n == 0:\n            return 0\n        return self.k + self.depth(n - 1)\no = Node()\ns = 0\nfor i in range(1000):\n    s = s + o.depth(150)\nresult = s\n");
            yield return new MatrixBenches.MBench("recur", "closure_depth150_x1000_151000", "def outer():\n    def go(n):\n        if n == 0:\n            return 0\n        return 1 + go(n - 1)\n    return go(150)\ns = 0\nfor i in range(1000):\n    s = s + outer()\nresult = s\n");
            yield return new MatrixBenches.MBench("recur", "tree_walk_3pow9_x5_147620", "def build(d):\n    if d == 0:\n        return 1\n    return [build(d - 1), build(d - 1), build(d - 1)]\ndef walk(t):\n    if isinstance(t, list):\n        s = 0\n        for x in t:\n            s = s + walk(x)\n        return s\n    return t\ntree = build(9)\ns = 0\nfor i in range(5):\n    s = s + walk(tree)\nresult = s\n");
        }
    }
}
