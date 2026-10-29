using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // THE host-boundary contract: no script content may surface an exception on the
    // calling thread. Run(source, request) returns a RunResult for ANY input, and hostile input maps
    // to SyntaxError / RuntimeError / BudgetExceeded / HostError — never EngineFault (EngineFault
    // means an engine defect) and never a throw. TryCompile likewise never throws.
    [Trait("Category", "Sandbox")]
    public class HostBoundaryTests
    {
        private static readonly ScriptEngine Engine = new ScriptEngine(new EngineOptions());

        // Small budget so the bombs die fast; generous enough for the benign cases.
        private static readonly ResourceLimits Tight = ResourceLimits.Default.With(
            maxSteps: 2_000_000, deadlineMs: 5_000, maxAllocBytes: 32L * 1024 * 1024,
            maxStrChars: 1_000_000, maxCollectionItems: 200_000, maxIntBits: 64_000);

        private static string Repeat(string unit, int count)
        {
            var sb = new StringBuilder(unit.Length * count);
            for (int i = 0; i < count; i++)
                sb.Append(unit);
            return sb.ToString();
        }

        private static string NestedIfs(int depth)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < depth; i++)
                sb.Append(Repeat(" ", i * 4)).Append("if 1:\n");
            sb.Append(Repeat(" ", depth * 4)).Append("pass\n");
            return sb.ToString();
        }

        private static IEnumerable<string[]> Hostile()
        {
            // --- lexer / source-form attacks ---
            yield return new[] { "empty", "" };
            yield return new[] { "whitespace-only", " \n\t \n" };
            yield return new[] { "comment-only", "# nothing\n" };
            yield return new[] { "token garbage", "$$$ @@@ ??? !!!\n" };
            yield return new[] { "NUL char in source", "x = 1\0\ny = 2\n" };
            yield return new[] { "lone high surrogate", "s = '\ud800'\n" };
            yield return new[] { "BOM prefix", "﻿x = 1\n" };
            yield return new[] { "CR / CRLF mix", "x = 1\r\ny = 2\rz = 3\n" };
            yield return new[] { "astral + RTL + zero-width", "s = '\U0001F600​‮abc'\nr = len(s)\n" };
            yield return new[] { "source cap", Repeat("x = 1\n", 100_000) };
            yield return new[] { "token cap", Repeat(";", 520_000) };
            yield return new[] { "numeric literal cap", "x = " + Repeat("9", 10_000) + "\n" };

            // --- parser depth / AST-shape attacks (each once crashed or would crash a stack) ---
            yield return new[] { "deep parens", Repeat("(", 300) + "1" + Repeat(")", 300) + "\n" };
            yield return new[] { "flat + chain", "r = " + Repeat("1+", 5000) + "1\n" };
            yield return new[] { "flat attr chain", "r = a" + Repeat(".b", 3000) + "\n" };
            yield return new[] { "deep stmt nesting", NestedIfs(120) };
            yield return new[] { "deep match dotted value", "match x:\n    case a" + Repeat(".b", 2000) + ":\n        pass\n" };
            // Pattern NESTING once crashed the process (uncatchable parser StackOverflow): every level
            // must pass through the guarded ParsePattern.
            yield return new[] { "deep match seq pattern", "match x:\n    case " + Repeat("[", 5000) + "1" + Repeat("]", 5000) + ":\n        pass\n" };
            yield return new[] { "deep match group pattern", "match x:\n    case " + Repeat("(", 5000) + "1" + Repeat(")", 5000) + ":\n        pass\n" };
            yield return new[] { "deep match mapping pattern", "match x:\n    case " + Repeat("{1: ", 5000) + "1" + Repeat("}", 5000) + ":\n        pass\n" };
            yield return new[] { "deep match class pattern", "match x:\n    case " + Repeat("C(", 5000) + "1" + Repeat(")", 5000) + ":\n        pass\n" };
            yield return new[] { "wide or pattern", "match x:\n    case " + Repeat("1 | ", 5000) + "1:\n        pass\n" };
            yield return new[] { "deep target tuple", Repeat("(", 3000) + "a" + Repeat(")", 3000) + " = x\n" };
            yield return new[] { "unterminated string", "s = 'abc\n" };
            yield return new[] { "unterminated fstring brace", "s = f'{\n" };

            // --- budget bombs (EngineAbort must become BudgetExceeded, uncatchable by the script) ---
            yield return new[] { "infinite loop", "while True:\n    pass\n" };
            yield return new[] { "recursion bomb", "def f():\n    return f()\nf()\n" };
            yield return new[] { "except cannot swallow abort", "try:\n    while True:\n        pass\nexcept BaseException:\n    pass\n" };
            yield return new[] { "finally loop after abort", "try:\n    while True:\n        pass\nfinally:\n    x = 1\n" };
            yield return new[] { "string doubling bomb", "x = 'a'\nwhile True:\n    x = x + x\n" };
            yield return new[] { "list doubling bomb", "x = [0]\nwhile True:\n    x = x + x\n" };
            yield return new[] { "int bits bomb", "x = 1 << 100000\n" };
            yield return new[] { "wide kwargs merge", "def f(**kw):\n    return len(kw)\nd = {}\nfor i in range(30000):\n    d['k' + str(i)] = i\nr = f(**d)\n" };

            // --- stdlib argument type/size confusion: a wrong-typed or huge int size/count/offset/seed
            //     arg once reached an unchecked (IntValue)v cast or a (int)BigInteger overflow and leaked
            //     an InvalidCastException/OverflowException as an EngineFault. Must be a clean script error. ---
            yield return new[] { "csv quoting bad type", "import csv, io\ncsv.writer(io.StringIO(), quoting='x')\n" };
            yield return new[] { "csv quoting huge", "import csv, io\ncsv.writer(io.StringIO(), quoting=10 ** 100)\n" };
            yield return new[] { "uuid clock_seq bad type", "import uuid\nuuid.uuid1(clock_seq='x')\n" };
            yield return new[] { "uuid node bad type", "import uuid\nuuid.uuid1(node=[1])\n" };
            yield return new[] { "uuid node huge", "import uuid\nuuid.uuid1(node=10 ** 100)\n" };
            yield return new[] { "struct offset bad type", "import struct\nstruct.unpack_from('i', b'xxxx', 'x')\n" };
            yield return new[] { "struct offset huge", "import struct\nstruct.unpack_from('i', b'xxxx', 10 ** 100)\n" };
            yield return new[] { "statistics quantiles n bad type", "import statistics\nstatistics.quantiles([1, 2, 3, 4], n='x')\n" };
            yield return new[] { "statistics quantiles n huge", "import statistics\nstatistics.quantiles([1, 2, 3, 4], n=10 ** 100)\n" };
            yield return new[] { "re flags huge", "import re\nre.compile('a', 10 ** 100)\n" };
            yield return new[] { "re sub count huge", "import re\nre.sub('a', 'b', 'aaa', 10 ** 100)\n" };
            yield return new[] { "random getrandbits huge", "import random\nrandom.getrandbits(10 ** 100)\n" };
            yield return new[] { "to_bytes length huge", "(1).to_bytes(10 ** 100, 'big')\n" };
            yield return new[] { "to_bytes length big alloc", "(1).to_bytes(10 ** 12, 'big')\n" };
            yield return new[] { "to_bytes length bad type", "(1).to_bytes('x', 'big')\n" };
            yield return new[] { "translate huge codepoint", "r = 'aaa'.translate({97: 10 ** 100})\n" };
            yield return new[] { "fromhex huge exponent", "r = float.fromhex('0x1p' + '9' * 100)\n" };
            yield return new[] { "fromhex digit flood", "r = float.fromhex('0x' + 'f' * 100000)\n" };

            // --- broad module/method fuzz: a huge size/count/exponent once reached an unchecked (int)cast
            //     or an uncharged BCL alloc and leaked OverflowException/OutOfMemoryException as EngineFault. ---
            yield return new[] { "round neg huge ndigits", "round(12345, -(10 ** 100))\n" };
            yield return new[] { "round int huge ndigits", "round(10 ** 50, -(10 ** 100))\n" };
            yield return new[] { "bytes count huge", "bytes(10 ** 100)\n" };
            yield return new[] { "expandtabs huge", "'\\ta'.expandtabs(10 ** 100)\n" };
            yield return new[] { "combinations r huge", "import itertools\nlist(itertools.combinations([1,2,3], 10 ** 100))\n" };
            yield return new[] { "permutations r huge", "import itertools\nlist(itertools.permutations([1,2,3], 10 ** 100))\n" };
            yield return new[] { "cwr r huge", "import itertools\nlist(itertools.combinations_with_replacement([1,2], 10 ** 100))\n" };
            yield return new[] { "product repeat huge", "import itertools\nlist(itertools.product([1,2], repeat=10 ** 100))\n" };
            yield return new[] { "deque maxlen huge", "import collections\ncollections.deque([1,2,3], 10 ** 100)\n" };
            yield return new[] { "factorial huge", "import math\nmath.factorial(10 ** 100)\n" };
            yield return new[] { "choices k huge", "import random\nrandom.choices([1,2,3], k=10 ** 100)\n" };
            yield return new[] { "re huge quantifier", "import re\nre.match('a{999999999}', 'a')\n" };
            yield return new[] { "re huge quantifier 2", "import re\nre.match('a{2000000000}', 'aaa')\n" };
            yield return new[] { "float format huge precision", "'{:.999999999f}'.format(1.5)\n" };
            yield return new[] { "float format huge precision e", "'{:.999999999e}'.format(1.5)\n" };
            yield return new[] { "percent huge star precision", "'%.*f' % (999999999, 1.5)\n" };

            // --- trash into parsers/decoders: malformed input must be a clean typed error, never a fault. ---
            yield return new[] { "json truncated array", "import json\njson.loads('[1,2,')\n" };
            yield return new[] { "json truncated object", "import json\njson.loads('{\"a\":1')\n" };
            yield return new[] { "json deep nest", "import json\njson.loads('[' * 500000 + ']' * 500000)\n" };
            yield return new[] { "json control chars", "import json\njson.loads('\\x00\\x01\\x02')\n" };
            yield return new[] { "xml mismatched", "import xml.etree.ElementTree as ET\nET.fromstring('<a></b>')\n" };
            yield return new[] { "xml unterminated", "import xml.etree.ElementTree as ET\nET.fromstring('<a>')\n" };
            yield return new[] { "xml bad entity", "import xml.etree.ElementTree as ET\nET.fromstring('<a>&undefined;</a>')\n" };
            yield return new[] { "xml deep nest", "import xml.etree.ElementTree as ET\nET.fromstring('<a>' * 200000 + '</a>' * 200000)\n" };
            yield return new[] { "strptime mismatch", "import datetime\ndatetime.datetime.strptime('abc', '%Y')\n" };
            yield return new[] { "strptime bad month", "import datetime\ndatetime.datetime.strptime('2020-13-01', '%Y-%m-%d')\n" };
            yield return new[] { "fromisoformat garbage", "import datetime\ndatetime.date.fromisoformat('not-a-date')\n" };
            yield return new[] { "decimal garbage", "import decimal\ndecimal.Decimal('1.2.3')\n" };
            yield return new[] { "fraction garbage", "import fractions\nfractions.Fraction('1/2/3')\n" };
            yield return new[] { "struct bad format", "import struct\nstruct.calcsize('z')\n" };
            yield return new[] { "struct short buffer", "import struct\nstruct.unpack('i', b'x')\n" };
            yield return new[] { "re invalid pattern", "import re\nre.compile('(?P<n>)(?P<n>)')\n" };
            yield return new[] { "decode bad utf8", "b'\\xff\\xfe'.decode('utf-8')\n" };
            yield return new[] { "decode unknown codec", "b'ab'.decode('not-a-codec')\n" };
            yield return new[] { "uuid garbage", "import uuid\nuuid.UUID('not-a-uuid')\n" };
            yield return new[] { "hashlib bad algo", "import hashlib\nhashlib.new('not-an-algo')\n" };

            // --- circular references + reentrant callbacks: a cyclic record repr once recursed into an
            //     uncatchable StackOverflow that killed the host; callbacks that raise/mutate must be clean. ---
            yield return new[] { "self-ref record repr", "class A:\n def __init__(self):\n  self.x = None\na = A()\na.x = a\nrepr(a)\n" };
            yield return new[] { "self-ref list repr", "l = []\nl.append(l)\nrepr(l)\n" };
            yield return new[] { "self-ref json dumps", "import json\nl = []\nl.append(l)\njson.dumps(l)\n" };
            yield return new[] { "self-ref deepcopy", "import copy\nl = []\nl.append(l)\ncopy.deepcopy(l)\n" };
            yield return new[] { "eq infinite recursion", "class A:\n def __eq__(self, o):\n  return self == o\nA() == A()\n" };
            yield return new[] { "sort key infinite recursion", "def f(x):\n return sorted([1], key=f)\nsorted([1], key=f)\n" };
            yield return new[] { "sort key mutates list", "a = [3,1,2]\na.sort(key=lambda x: a.pop())\n" };
            yield return new[] { "dict mutated in sorted key", "d = {1:1,2:2,3:3}\nsorted(d, key=lambda k: d.pop(k))\n" };
            yield return new[] { "cmp_to_key non-int", "import functools\nsorted([3,1,2], key=functools.cmp_to_key(lambda a,b: 'x'))\n" };
            yield return new[] { "re.sub repl non-str", "import re\nre.sub('a', lambda m: 42, 'aaa')\n" };
            yield return new[] { "repr returns non-str", "class A:\n def __repr__(self):\n  return 42\nrepr(A())\n" };

            // --- serialization / numeric specials / slice pathology: huge slice stride once overflowed the
            //     (int)step cast; the rest must stay clean typed errors. ---
            yield return new[] { "slice huge step", "[1,2,3][::10 ** 100]\n" };
            yield return new[] { "slice huge neg step", "[1,2,3][::-(10 ** 100)]\n" };
            yield return new[] { "slice assign huge step", "a = [1,2,3]\na[::10 ** 100] = [9]\n" };
            yield return new[] { "int of inf", "int(float('inf'))\n" };
            yield return new[] { "int of nan", "int(float('nan'))\n" };
            yield return new[] { "unhashable dict key", "{[1]: 2}\n" };
            yield return new[] { "star non-iterable", "[*5]\n" };
            yield return new[] { "json dumps non-str key", "import json\njson.dumps({(1,2): 3})\n" };
            yield return new[] { "json dumps circular", "import json\nl = []\nl.append(l)\njson.dumps(l)\n" };
            yield return new[] { "decimal snan arith", "import decimal\ndecimal.Decimal('snan') + 1\n" };
            yield return new[] { "date max overflow", "import datetime\ndatetime.date.max + datetime.timedelta(days=1)\n" };
            yield return new[] { "statistics empty", "import statistics\nstatistics.mean([])\n" };

            // --- SOE audit: a deeply nested lazy-iterator chain or comprehension once recursed the C#
            //     stack past overflow (fatal, uncatchable). Each must now abort cleanly / reject cleanly. ---
            yield return new[] { "deep genexp chain", "g=iter([1])\nfor i in range(150000):\n g=(x for x in g)\nr=list(g)\n" };
            yield return new[] { "deep map chain", "it=iter([1])\nfor i in range(150000):\n it=map(lambda x:x, it)\nr=list(it)\n" };
            yield return new[] { "deep islice chain", "import itertools\nit=iter([1])\nfor i in range(150000):\n it=itertools.islice(it, 5)\nr=list(it)\n" };
            yield return new[] { "deep filter chain", "it=iter([1])\nfor i in range(150000):\n it=filter(lambda x:True, it)\nr=list(it)\n" };
            yield return new[] { "deep comprehension parse", "r=" + Repeat("[x for x in ", 5000) + "[1]" + Repeat("]", 5000) + "\n" };

            // --- runtime errors that must stay ordinary script errors ---
            yield return new[] { "uncaught raise", "raise ValueError('boom')\n" };
            yield return new[] { "huge exception message", "raise ValueError('x' * 900000)\n" };
            yield return new[] { "deep data repr", "l = []\nfor i in range(500):\n    l = [l]\nr = repr(l)\n" };
            yield return new[] { "unknown import", "import nonexistent_module\n" };
            yield return new[] { "unsupported builtin", "eval('1')\n" };
            yield return new[] { "PEP 479", "def g():\n    for i in [1]:\n        yield i\nr = 1\n" };
        }

        public static IEnumerable<object[]> HostileCases()
        {
            foreach (string[] c in Hostile())
                yield return new object[] { c[0], c[1] };
        }

        [Theory]
        [MemberData(nameof(HostileCases))]
        public void RunNeverThrowsAndNeverFaults(string label, string src)
        {
            RunResult r = Engine.Run(src, new RunRequest { Limits = Tight });   // must not throw
            Assert.NotNull(r);
            if (!r.Succeeded)
            {
                Assert.False(r.Error.Kind == ScriptErrorKind.EngineFault,
                    label + " produced EngineFault: " + r.Error.Message);
            }
        }

        [Theory]
        [MemberData(nameof(HostileCases))]
        public void TryCompileNeverThrows(string label, string src)
        {
            CompiledScript compiled;
            ScriptError error;
            bool ok = Engine.TryCompile(src, "<hostile>", out compiled, out error);   // must not throw
            if (ok)
            {
                Assert.NotNull(compiled);
            }
            else
            {
                Assert.NotNull(error);
                Assert.False(error.Kind == ScriptErrorKind.EngineFault,
                    label + " compile produced EngineFault: " + error.Message);
            }
        }

        // A host-provided builtin throwing a raw C# exception is the HOST's bug: it must come back as
        // a HostError RunResult (HostCallGuard, R-10) — never as an exception on the calling thread.
        [Fact]
        public void ThrowingHostBuiltinBecomesHostError()
        {
            HostFunctionTable builtins = HostFunctionTable.CreateDefault();
            builtins.Set("boom", (self, args, kw, ctx) => { throw new InvalidOperationException("host bug"); });
            RunResult r = Engine.Run("r = boom()\n", new RunRequest { Limits = Tight, Builtins = builtins });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
        }

        // Oversized declared outputs must be bounded by MaxOutputBytes, not thrown.
        [Fact]
        public void HugeDeclaredOutputIsBounded()
        {
            var req = new RunRequest
            {
                Limits = Tight,
                JsonOutputNames = new[] { "x" },
            };
            RunResult r = Engine.Run("x = ['y' * 1000] * 100000\n", req);   // must not throw
            Assert.NotNull(r);
            if (!r.Succeeded)
            {
                Assert.NotEqual(ScriptErrorKind.EngineFault, r.Error.Kind);
            }
        }
    }
}
