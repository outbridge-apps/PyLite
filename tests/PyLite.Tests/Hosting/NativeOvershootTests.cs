using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // One .NET call a script can make long - number formatting, an engine-side regex, a multi-char split,
    // a key derivation, a big-int parse or square - must still end its run on the deadline and leave
    // nothing computing. The scripts are the 1.1.0 audit's, at default limits:
    // each one ran on past the deadline there, most of them for good. A call that outlives its run returns
    // only at deadline + 5.25 s with the runner thread leaked, far past the ceiling.
    [Collection("TimeSensitive")]   // wall-clock and process-CPU bounds; must not race the rest of the suite
    [Trait("Category", "ResourceLimits")]
    [Trait("Category", "MachineSensitive")]
    public class NativeOvershootTests
    {
        private const int DeadlineMs = 1000;
        private const int CeilingMs = 2500;

        public static IEnumerable<object[]> Calls()
        {
            yield return new object[] { "float-format-precision", "format(1.0, '.1000000f')" };
            yield return new object[] { "decimal-format-precision", "from decimal import Decimal\nformat(Decimal(1), '.2000000000f')" };
            yield return new object[] { "decimal-zero-exponent", "from decimal import Decimal\nround(Decimal('0E-999999999'))" };
            yield return new object[] { "csv-sniff-quadratic", "import csv\ncsv.Sniffer().sniff(',\"a' * 21000)" };
            // the value's int() now stops on the 4,300-digit limit at once, as CPython's
            yield return new object[] { "intenum-str-value", "import enum\ntry:\n    class E(enum.IntEnum):\n        A = '1' * 1000000\nexcept ValueError:\n    pass\n" };
            yield return new object[] { "capwords-long-sep", "import string\nstring.capwords('a' * 16000000, 'a' * 1000 + 'b')" };
            yield return new object[] { "pbkdf2-long-salt", "import hashlib\nhashlib.pbkdf2_hmac('sha256', b'k', b's' * 16000000, 1, dklen=3200)" };
            yield return new object[] { "fraction-str", "from fractions import Fraction\ntry:\n    str(Fraction(2**999999, 3))\nexcept ValueError:\n    pass\n" };
            yield return new object[] { "textwrap-split", "import textwrap\ntextwrap.wrap(' a' * 8000000, break_on_hyphens=False)" };
            yield return new object[] { "random-seed-str", "import random\nrandom.seed('x' * 16000000)\nwhile True:\n    pass\n" };
            yield return new object[] { "qsl-long-sep", "from urllib.parse import parse_qsl\nparse_qsl('a' * 16000000, separator='a' * 1000 + 'b')" };
            yield return new object[] { "regex-long-repeat", "import re\nre.compile('a{100000}')\nm = re.match('a{100000}', 'a')\nf = re.fullmatch('a{100000}', 'b')" };
            yield return new object[] { "range-big-step", "r = range(0, 2**999000, 3**300000)\nwhile True:\n    n = r[-1]\n    b = 5 in r\n" };   // r[-1] divides for the length; len(r) is an OverflowError
            yield return new object[] { "statistics-big-square", "import statistics\nstatistics.variance([2**999999, 0])" };
        }

        [Theory]
        [MemberData(nameof(Calls))]
        public void NativeCallEndsWithItsRun(string label, string src)
        {
            int leaks = 0;
            var engine = new ScriptEngine(new EngineOptions
            {
                DefaultLimits = ResourceLimits.Default.With(deadlineMs: DeadlineMs),
                EventSink = ev => { if (ev.Kind == EngineEventKind.ThreadLeaked) Interlocked.Increment(ref leaks); },
            });
            engine.Run("x = 1\n", new RunRequest());
            WaitQuiet();   // whatever an earlier case left running (its garbage collection) must not count here

            var sw = Stopwatch.StartNew();
            RunResult r = engine.Run(src, new RunRequest());
            sw.Stop();
            TimeSpan cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
            Thread.Sleep(1000);
            double cpuAfter = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalSeconds;

            Assert.True(r.Succeeded || r.Error.Kind == ScriptErrorKind.BudgetExceeded,
                label + " ended " + r.Error?.Kind + ": " + r.Error?.Message);
            Assert.True(sw.ElapsedMilliseconds < CeilingMs,
                label + " returned after " + sw.ElapsedMilliseconds + " ms (deadline " + DeadlineMs + " ms)");
            Assert.Equal(0, Volatile.Read(ref leaks));
            Assert.True(cpuAfter < 0.5, label + " left " + cpuAfter.ToString("F2") + " s of CPU in the second after its run");
        }

        // The 2026-10 sandbox review: work a call does for the one step it charges - a method or a constructor
        // (no deadline check after either), a long str compare or search, a quadratic algorithm - looped until
        // the runner thread was leaked. Each now ends near its deadline, on its own result, error or limit.
        public static IEnumerable<object[]> Loops()
        {
            yield return new object[] { "float-parse-loop", "s = '1' * 16000000\nwhile True:\n    float(s)\n" };
            yield return new object[] { "decimal-parse-loop", "from decimal import Decimal\ns = 'x' * 16000000\nwhile True:\n    try:\n        Decimal(s)\n    except Exception:\n        pass\n" };
            yield return new object[] { "bigint-mod-small", "x = (1 << 999000) - 12345\nwhile True:\n    x % 7\n" };
            yield return new object[] { "fraction-compare", "from fractions import Fraction\nf = Fraction(1, 3) ** 600000\nwhile True:\n    f < 5\n" };
            yield return new object[] { "str-in-loop", "s = 'a' * 8000000\nwhile 'ab' not in s:\n    pass\n" };
            yield return new object[] { "bytes-in-loop", "s = b'a' * 8000000\nwhile b'ab' not in s:\n    pass\n" };
            yield return new object[] { "str-eq-loop", "a = 'a' * 16000000\nb = 'a' * 15999999 + 'a'\nwhile a == b:\n    pass\n" };
            yield return new object[] { "tuple-eq", "a = 'a' * 16000000\nb = 'a' * 15999999 + 'a'\nx = (a,) * 1000 == (b,) * 1000\n" };
            yield return new object[] { "sort-long-str", "a = 'a' * 16000000\nb = 'a' * 15999999 + 'b'\nl = [a, b] * 1000\nl.sort()\n" };
            yield return new object[] { "set-subset-loop", "a = set(range(1000000))\nb = a.copy()\nwhile a <= b:\n    pass\n" };
            yield return new object[] { "chainmap-contains", "from collections import ChainMap\ncm = ChainMap(*([{}] * 4000000))\nwhile 'k' not in cm:\n    pass\n" };
            yield return new object[] { "deque-del-front", "from collections import deque\nd = deque([0]) * 2000000\nwhile d:\n    del d[0]\n" };
            yield return new object[] { "isalnum-astral", "s = chr(0x10107) * 8000000\nwhile True:\n    s.isalnum()\n" };
            yield return new object[] { "swapcase-full-mapping", "x = (chr(0xFB17) * 16000000).swapcase()\n" };
            yield return new object[] { "stringio-readline", "import io\nf = io.StringIO()\nfor i in range(2000):\n    f.write('a' * 8000)\nwhile True:\n    f.seek(0)\n    f.readline()\n" };
            yield return new object[] { "tee-many-clones", "import itertools\nts = itertools.tee(range(5000), 1500000)\nx = list(ts[0])\n" };
            yield return new object[] { "kw-binding", "def f(" + string.Join(", ", Names("a", 10000)) + ", **kw):\n    pass\nf(**{str(i): 0 for i in range(300000)})\n" };
            yield return new object[] { "dataclass-astuple", "from dataclasses import make_dataclass, astuple\nC = make_dataclass('C', ['f%d' % i for i in range(40000)])\nx = astuple(C(*range(40000)))\n" };
            yield return new object[] { "notes-format", "import traceback\ne = ValueError('x')\nfor i in range(100000):\n    e.add_note('n')\nx = traceback.format_exception_only(e)\n" };
            yield return new object[] { "compiled-or-chain", "y = 5\nc = [0 for x in range(10**9) if " + string.Join(" or ", Repeat("y<0", 70000)) + "]\n" };
            yield return new object[] { "xpath-predicates", "import xml.etree.ElementTree as ET\nr = ET.fromstring('<r>' + '<a/>' * 500 + '</r>')\nx = r.findall('*/.' + '[1]' * 1000000)\n" };
            yield return new object[] { "xml-many-xmlns", "import xml.etree.ElementTree as ET\nx = ET.fromstring('<r ' + ' '.join(['xmlns:p%d=\"u\"' % i for i in range(400000)]) + '/>')\n" };
            yield return new object[] { "hmac-long-key", "import hmac\nh = hmac.new(b'k' * 16000000, digestmod='sha256')\nwhile True:\n    h.update(b'')\n    h.digest()\n" };
            yield return new object[] { "uuid-parse-loop", "import uuid\nh = 'urn:' * 4000000\nwhile True:\n    try:\n        uuid.UUID(h)\n    except ValueError:\n        pass\n" };
            yield return new object[] { "csv-long-terminator", "import csv, io\ncsv.writer(io.StringIO(), lineterminator='x' * 200000).writerow(['y' * 200000])\n" };
            yield return new object[] { "namedtuple-fields", "from collections import namedtuple\nP = namedtuple('P', 'a ' * 3000000, rename=True)\n" };
            yield return new object[] { "re-escape", "import re\nwhile True:\n    re.escape('a' * 16000000)\n" };
            yield return new object[] { "normalize-lone-surrogates", "import unicodedata\nx = unicodedata.normalize('NFC', 'e\\u0301\\ud800' * 5000000)\n" };
            yield return new object[] { "bytes-split", "x = (b'a ' * 8000000).split()\n" };
            yield return new object[] { "codec-namereplace", "x = (chr(0xFDFA) * 8000000).encode('ascii', 'namereplace')\n" };
            yield return new object[] { "repeat-repr-bigint", "import itertools\nx = repr(itertools.repeat(0, 2 ** 999000))\n" };
            yield return new object[] { "isinstance-wide", "big = (str,) * 4000000\nx = isinstance(1, (big,) * 15)\n" };
            yield return new object[] { "typevar-hash", "import typing\nt = typing.TypeVar('x' * 16000000)\nwhile True:\n    t in set()\n" };
            // The second review (2026-10): its probes, and the big divisions found while fixing them.
            yield return new object[] { "bigint-floordiv-loop", "a = 2**999999\nb = 3**630700\nwhile True:\n    a // b\n" };
            yield return new object[] { "bigint-truediv-loop", "a = 2**999999\nb = 3**630700\nwhile True:\n    a / b\n" };
            yield return new object[] { "slice-indices-big", "n = 2**999999\ns = slice(None, None, 3**315000)\nwhile True:\n    s.indices(n)\n" };
            yield return new object[] { "class-init-walk", "while True:\n    class C:\n        def __init__(self):\n            "
                + string.Join("\n            ", Repeat("self.a = 0", 10000)) + "\n" };
            yield return new object[] { "closure-capture", "def outer():\n    " + string.Join("\n    ", Names("v", 20000).Select(v => v + " = 0"))
                + "\n    def inner():\n        return (" + string.Join(", ", Names("v", 20000)) + ")\n    return inner\nwhile True:\n    outer()\n" };
            yield return new object[] { "html-unescape-legacy", "import html\nwhile True:\n    html.unescape(('&' + 'q' * 31) * 100000)\n" };
            yield return new object[] { "capwords-words", "import string\nx = string.capwords('a ' * 8000000)\n" };
            yield return new object[] { "deque-imul-maxlen", "from collections import deque\nd = deque([0] * 2000000, maxlen=2000000)\nwhile True:\n    d *= 10**18\n" };
            yield return new object[] { "startswith-prefixes", "s = 'a' * 8000000\nt = (s[:-1] + 'b',) * 3000\nx = s.startswith(t)\n" };
            yield return new object[] { "bytes-tuple-eq", "a = b'a' * 16000000\nb = b'a' * 15999999 + b'a'\nx = (a,) * 1000 == (b,) * 1000\n" };
            yield return new object[] { "list-del-front", "L = [0] * 5000000\nwhile L:\n    del L[0]\n" };
            yield return new object[] { "asdict-dag", "from dataclasses import dataclass, asdict\n@dataclass\nclass N:\n    a: object\n    b: object\n"
                + "x = N(None, None)\nfor i in range(40):\n    x = N(x, x)\nasdict(x)\n" };
            yield return new object[] { "randbytes-big", "import random\nwhile True:\n    random.randbytes(15000000)\n" };
            yield return new object[] { "attrgetter-long-path", "import operator\nclass C:\n    def __init__(self):\n        self.a = self\nc = C()\n"
                + "f = operator.attrgetter('a.' * 2999999 + 'a')\nwhile True:\n    f(c)\n" };
        }

        private static IEnumerable<string> Names(string prefix, int n)
        {
            for (int i = 0; i < n; i++)
                yield return prefix + i;
        }

        private static IEnumerable<string> Repeat(string s, int n)
        {
            for (int i = 0; i < n; i++)
                yield return s;
        }

        [Theory]
        [MemberData(nameof(Loops))]
        public void ALoopOfChargedCallsEndsWithItsRun(string label, string src)
        {
            int leaks = 0;
            var engine = new ScriptEngine(new EngineOptions
            {
                DefaultLimits = ResourceLimits.Default.With(deadlineMs: DeadlineMs),
                EventSink = ev => { if (ev.Kind == EngineEventKind.ThreadLeaked) Interlocked.Increment(ref leaks); },
            });
            engine.Run("x = 1\n", new RunRequest());
            WaitQuiet();

            var sw = Stopwatch.StartNew();
            RunResult r = engine.Run(src, new RunRequest());
            sw.Stop();
            TimeSpan cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
            Thread.Sleep(1000);
            double cpuAfter = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalSeconds;

            Assert.True(r.Succeeded || r.Error.Kind == ScriptErrorKind.BudgetExceeded || r.Error.Kind == ScriptErrorKind.RuntimeError,
                label + " ended " + r.Error?.Kind + ": " + r.Error?.Message);
            Assert.True(sw.ElapsedMilliseconds < CeilingMs,
                label + " returned after " + sw.ElapsedMilliseconds + " ms (deadline " + DeadlineMs + " ms)");
            Assert.Equal(0, Volatile.Read(ref leaks));
            Assert.True(cpuAfter < 0.5, label + " left " + cpuAfter.ToString("F2") + " s of CPU in the second after its run");
        }

        // Until the process spends under 10% of a core over 300 ms (at most 20 s).
        private static void WaitQuiet()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var limit = Stopwatch.StartNew();
            while (limit.ElapsedMilliseconds < 20000)
            {
                TimeSpan c0 = Process.GetCurrentProcess().TotalProcessorTime;
                Thread.Sleep(300);
                if ((Process.GetCurrentProcess().TotalProcessorTime - c0).TotalSeconds < 0.03)
                    return;
            }
        }
    }
}
