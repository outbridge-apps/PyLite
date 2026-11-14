using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Modules.Support;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // Review 6, P2 wrong answers: IGNORECASE as sre folds case (e1-08, f1-10, f1-11), \w \b \B by the engine's own Unicode
    // tables (f1-02), traceback's limit (t1-01), Unicode digits and spaces in Decimal and Fraction (e1-09), the x86 float
    // digits (e1-03). Every expected output is CPython 3.14.2's (Pyodide, Unicode 16.0).
    [Trait("Category", "Compatibility")]
    public class Review6AnswersTests
    {
        private static string Out(string src)
        {
            RunResult r = TestEngines.WithCeiling(TestEngines.Unhurried).Run(src, new RunRequest());
            Assert.True(r.Succeeded, r.Succeeded ? null : r.Error.Message);
            return r.PrintOutput;
        }

        [Fact]
        public void IgnoreCaseFoldsAsSre()
        {
            Assert.Equal("[True, True, True, True, True, True, True]\n", Out("import re\n"
                + "print([re.match('(?i)i', 'I') is not None, re.match('(?i)I', 'i') is not None, re.match('(?i)i', '\\u0130') is not None, "
                + "re.match('(?i)I', '\\u0131') is not None, re.match('(?i)\\u0130', 'i') is not None, re.match('(?i)k', '\\u212a') is not None, "
                + "re.match('(?i)s', '\\u017f') is not None])\n"));
        }

        // every ordered pair of re/_casefix.py's families, as a literal and as a class
        [Fact]
        public void TheCasefixFamiliesMatchEachOther()
        {
            Assert.Equal("0 []\n", Out("import re\n"
                + "fam = [(0x69, 0x131), (0x73, 0x17f), (0xb5, 0x3bc), (0x345, 0x3b9, 0x1fbe), (0x390, 0x1fd3), (0x3b0, 0x1fe3), (0x3b2, 0x3d0), "
                + "(0x3b5, 0x3f5), (0x3b8, 0x3d1), (0x3ba, 0x3f0), (0x3c0, 0x3d6), (0x3c1, 0x3f1), (0x3c2, 0x3c3), (0x3c6, 0x3d5), (0x432, 0x1c80), "
                + "(0x434, 0x1c81), (0x43e, 0x1c82), (0x441, 0x1c83), (0x442, 0x1c84, 0x1c85), (0x44a, 0x1c86), (0x463, 0x1c87), (0xa64b, 0x1c88), "
                + "(0x1e61, 0x1e9b), (0xfb05, 0xfb06)]\nbad = []\nfor t in fam:\n    for a in t:\n        for b in t:\n"
                + "            if re.fullmatch('(?i)' + chr(a), chr(b)) is None:\n                bad.append('%04X/%04X' % (a, b))\n"
                + "            if re.fullmatch('(?i)[' + chr(a) + ']', chr(b)) is None:\n                bad.append('[%04X]/%04X' % (a, b))\n"
                + "print(len(bad), bad[:5])\n"));
        }

        [Fact]
        public void DigraphsSigmasGeorgianRangesAndScopes()
        {
            Assert.Equal("[True, True, True, True, True, True, True, True, True]\n[True, True, True, True, True, True, True, True, True]\n"
                + "[True, True]\n[True, True, True, True]\n[False, False, False, False]\n[True, False, True]\n[True, True, True, True]\n",
                Out("import re\n"
                + "print([re.fullmatch('(?i)' + a, b) is not None for a in '\\u01c4\\u01c5\\u01c6' for b in '\\u01c4\\u01c5\\u01c6'])\n"
                + "print([re.fullmatch('(?i)' + a, b) is not None for a in '\\u03a3\\u03c3\\u03c2' for b in '\\u03a3\\u03c3\\u03c2'])\n"
                + "print([re.fullmatch('(?i)\\u1c90', '\\u10d0') is not None, re.fullmatch('(?i)\\u10d0', '\\u1c90') is not None])\n"
                + "print([re.fullmatch('(?i)[a-z]', c) is not None for c in '\\u212a\\u017f\\u0130\\u0131'])\n"
                + "print([re.fullmatch('(?i)[^a-z]', c) is not None for c in '\\u212a\\u017f\\u0130\\u0131'])\n"
                + "print([re.fullmatch('(?i:k)', '\\u212a') is not None, re.fullmatch('(?i)(?-i:k)', '\\u212a') is not None, "
                + "re.fullmatch('k(?i:K)', 'k\\u212a') is not None])\n"
                + "print([re.fullmatch('(?i)' + a, b) is not None for a, b in (('\\u00df', '\\u1e9e'), ('\\u1e9e', '\\u00df'), "
                + "('\\u00c5', '\\u212b'), ('\\u212b', '\\u00e5'))])\n"));
        }

        // f1-10: under re.A sre folds ASCII letters only, and only against ASCII
        [Fact]
        public void AsciiIgnoreCaseFoldsAsciiOnly()
        {
            Assert.Equal("[None, None, None, None]\n[True, 'ABC', None, None, True]\n[None, True]\n", Out("import re\n"
                + "print([re.match('(?ai)\\u00e9', '\\u00c9'), re.match('(?ai)k', '\\u212a'), re.match('(?ai)[a-z]', '\\u212a'), "
                + "re.match('(?ai)[\\u00e9]', '\\u00c9')])\n"
                + "print([re.match('(?ai)K', 'k') is not None, re.match('(?ai)[a-z]+', 'ABC').group(), re.match('(?ai)[^a-z]', 'Q'), "
                + "re.match('(?ai)i', '\\u0130'), re.match('(?ai)[k-m]', 'L') is not None])\n"
                + "print([re.match('\\u00e9', '\\u00c9', re.A | re.I), re.match('(?i)\\u00e9', '\\u00c9') is not None])\n"));
        }

        // From the diff review: a last '-' stays a literal when sre's extra letters follow it, and under re.A a class with an
        // escaped member keeps its folding.
        [Fact]
        public void IgnoreCaseClassesKeepTheirLiteralsAndEscapes()
        {
            Assert.Equal("[None, True, True, True, None, True, True]\n", Out("import re\n"
                + "print([re.fullmatch(r'[a-z0-9.+-]+', 'a@b', re.I), re.fullmatch(r'[a-z0-9.+-]+', 'a-B.1', re.I) is not None, "
                + "re.fullmatch('[s-]', '\\u017f', re.I) is not None, re.fullmatch('[s-]', '-', re.I) is not None, re.fullmatch('[s-]', 't', re.I), "
                + "re.match(r'[\\x41\\u00e9]', 'a', re.A | re.I) is not None, re.match(r'[+i-]', 'I', re.A | re.I) is not None])\n"));
        }

        // A translated pattern far longer than its source (a Unicode \b is three copies of the word class) is charged for
        // what .NET builds from it: when a subject first needs the exact translation (here a combining mark), held at 2 B
        // a translated character inside a class (review 14; it was 32 paid by every compile).
        [Fact]
        public void ALongTranslationIsCharged()
        {
            RunResult r = TestEngines.WithCeiling(TestEngines.Unhurried).Run("import re\nre.compile(r'\\b' * 2000).search(chr(0x301))\n",
                new RunRequest());
            Assert.True(r.Succeeded);
            Assert.True(r.Stats.AllocatedBytes > 2L << 20, r.Stats.AllocatedBytes.ToString());
            Assert.True(r.Stats.Steps > 50000, r.Stats.Steps.ToString());
        }

        // From review round 3: the usual word search compiles one pattern per word and must not run the memory budget dry
        // (each pattern holds two Unicode \b); under re.A with IGNORECASE \w and \b take ASCII only; an escape's digits in a
        // class are no range ([\x7a-~] does not take the long s). CPython 3.14.2's answers.
        [Fact]
        public void WordSearchesFitAndAsciiWordSetsStayAscii()
        {
            RunResult r = TestEngines.WithCeiling(TestEngines.Unhurried.With(maxAllocBytes: 64L << 20)).Run("import re\n"
                + "text = 'x ' * 10\nn = 0\nfor k in range(6000):\n    if re.search(r'\\b' + re.escape('w%d' % k) + r'\\b', text):\n        n += 1\n",
                new RunRequest());
            Assert.True(r.Succeeded, r.Succeeded ? null : r.Error.LimitName + " " + r.Error.Message);
            Assert.Equal("[None, True, None, True, None, True]\n", Out("import re\n"
                + "print([re.fullmatch(r'\\w', '\\u212a', re.A | re.I), re.fullmatch(r'\\W', '\\u212a', re.A | re.I) is not None, "
                + "re.fullmatch(r'[\\w]', '\\u212a', re.A | re.I), re.fullmatch(r'[\\w]', 'K', re.A | re.I) is not None, "
                + "re.fullmatch(r'[\\x7a-~]', '\\u017f', re.I), re.fullmatch(r'[\\x7a-~]', 'Z', re.I) is not None])\n"));
        }

        // f1-02: \w is str.isalnum() or '_' by Unicode 16 (Georgian Mtavruli in, the two Mongolian marks out), \b and \B
        // follow it (a combining mark is not a word character)
        [Fact]
        public void WordClassesFollowTheEngineTables()
        {
            Assert.Equal("[True, True, False, False, True, True, True, False]\n[False, True, True, True]\n"
                + "['', '', '', ''] [] ['\u1c90\u1c91', 'ab']\n.-\u1885-. ['\u1c90'] ['\u1c90', 'a']\n", Out("import re\n"
                + "print([re.fullmatch(r'\\w', c) is not None for c in '\\u1c90\\u0560\\u1885\\u1886a_9\\u0301'])\n"
                + "print([re.fullmatch(r'\\W', c) is not None for c in '\\u1c90\\u1885\\u0301 '])\n"
                + "print(re.findall(r'\\b', 'e\\u0301x'), re.findall(r'\\B', 'e\\u0301x'), re.findall(r'\\b\\w+\\b', '\\u1c90\\u1c91 ab'))\n"
                + "print(re.sub(r'\\w', '.', '\\u1c90-\\u1885-\\u00b2'), re.findall(r'[\\w]', '\\u1c90\\u1885'), re.findall(r'[^\\W]', '\\u1c90\\u1885a'))\n"));
        }

        // Round 4 review: a pattern runs .NET's own \w and \b on a subject without the characters where they differ from
        // the exact word set, the exact translation on one with them; one pattern object switches both ways, groups included.
        [Fact]
        public void APatternSwitchesBetweenItsWordTranslations()
        {
            Assert.Equal("[['ab', 'cd'], '. .', ['', ' ', ''], [(0, 2), (3, 5)], True, False, 'ab']\n"
                + "[['e', 'x', '\u00b2'], '.\u0301. .', ['', '\u0301', ' ', ''], [(0, 1), (2, 3), (4, 5)], True, False, 'e']\n"
                + "[['ab'], '.', ['', ''], [(0, 2)], True, True, 'ab']\n"
                + "[['x', 'y'], '.\u203f.', ['', '\u203f', ''], [(0, 1), (2, 3)], True, False, 'x']\n"
                + "('ab', 'cd') cd (3, 5)\n('a\u00b2', 'b') b (3, 4)\n('ab', 'cd') cd (3, 5)\n"
                + "['a-b', 'c'] ['a', '\u00b2b'] ['b', '\u0300']\n"
                + "['K\u212a', 's\u017f'] ['a', 'b'] ['a\u00b2', 'b\u00b2']\n", Out("import re\n"
                + "A, SQ, TIE, KEL, LS, GR = chr(0x301), chr(0xb2), chr(0x203f), chr(0x212a), chr(0x17f), chr(0x300)\n"
                + "p = re.compile(r'\\b\\w+\\b')\n"
                + "for s in ('ab cd', 'e' + A + 'x ' + SQ, 'ab', 'x' + TIE + 'y'):\n"
                + "    print([p.findall(s), p.sub('.', s), p.split(s), [m.span() for m in p.finditer(s)], p.match(s) is not None, "
                + "p.fullmatch(s) is not None, p.search(s).group()])\n"
                + "q = re.compile(r'(\\w+)\\W(?P<b>\\w+)')\n"
                + "for s in ('ab cd', 'a' + SQ + ' b', 'ab cd'):\n    m = q.match(s)\n    print(m.groups(), m.group('b'), m.span(2))\n"
                + "print(re.findall(r'[\\w-]+', 'a-b ' + A + 'c'), re.findall(r'[^\\W\\d]+', 'a1' + SQ + 'b'), re.findall(r'\\B.', 'ab ' + GR + 'c'))\n"
                + "print(re.findall(r'(?i)\\w+', 'K' + KEL + ' s' + LS), re.findall(r'(?a)\\w+', 'a' + SQ + 'b'), "
                + "re.findall(r'(?a:\\w)\\w', 'a' + SQ + 'b' + SQ))\n"));
        }

        // t1-01: limit keeps the outermost n frames, -n the innermost, 0 none, for each exception of a chain
        [Fact]
        public void TracebackLimitSelectsFrames()
        {
            const string setup = "import traceback\ndef f():\n    1 / 0\ndef g():\n    f()\ntry:\n    g()\nexcept ZeroDivisionError as e:\n    exc = e\n";
            Assert.Equal("['ZeroDivisionError: division by zero\\n']\n"
                + "['Traceback (most recent call last):\\n', '  File \"<script>\", line 7, in <module>\\n', 'ZeroDivisionError: division by zero\\n']\n"
                + "['Traceback (most recent call last):\\n', '  File \"<script>\", line 3, in f\\n', 'ZeroDivisionError: division by zero\\n']\n"
                + "['Traceback (most recent call last):\\n', '  File \"<script>\", line 7, in <module>\\n', '  File \"<script>\", line 5, in g\\n', "
                + "'  File \"<script>\", line 3, in f\\n', 'ZeroDivisionError: division by zero\\n']\n"
                + "['Traceback (most recent call last):\\n', '  File \"<script>\", line 7, in <module>\\n', 'ZeroDivisionError: division by zero\\n']\n",
                Out(setup + "for lim in (0, 1, -1, -10, True):\n    print(repr(traceback.format_exception(exc, limit=lim)))\n"));
            Assert.Equal("['Traceback (most recent call last):\\n', '  File \"<script>\", line 12, in <module>\\n', 'ZeroDivisionError: division by zero\\n', "
                + "'\\nDuring handling of the above exception, another exception occurred:\\n\\n', 'Traceback (most recent call last):\\n', "
                + "'  File \"<script>\", line 14, in <module>\\n', 'ValueError: v\\n']\n"
                + "TypeError '>=' not supported between instances of 'str' and 'int'\n"
                + "ValueError Stop argument for islice() must be None or an integer: 0 <= x <= sys.maxsize.\n"
                + "TypeError an integer is required\n",
                Out(setup + "try:\n    try:\n        g()\n    except ZeroDivisionError:\n        raise ValueError('v')\n"
                + "except ValueError as e2:\n    print(repr(traceback.format_exception(e2, limit=1)))\n"
                + "for bad in ('x', 1.5, -1.5):\n    try:\n        traceback.format_exception(exc, limit=bad)\n"
                + "    except Exception as e3:\n        print(type(e3).__name__, e3)\n"));
        }

        // e1-09: any Unicode decimal digit reads as its ASCII twin and Unicode spaces are trimmed, as float() already did
        [Fact]
        public void DecimalAndFractionReadUnicodeDigits()
        {
            Assert.Equal("12.5 7 1.2E+2 1/2 7/2\nInvalidOperation\nInvalidOperation\nInvalidOperation\n", Out(
                "from decimal import Decimal\nfrom fractions import Fraction\n"
                + "print(Decimal('\\u0661\\u0662.\\u0665'), Decimal('\\u3000 7\\u2003'), Decimal('\\u0967\\u0968e\\u0661'), "
                + "Fraction('\\u0661/\\u0662'), Fraction(' \\u0663.\\u0665 '))\n"
                + "for s in ('\\u0661\\u0662.\\u0665x', '1\\u00b2', '\\u2155'):\n    try:\n        print(Decimal(s))\n"
                + "    except Exception as e:\n        print(type(e).__name__)\n"));
        }

        [Fact]
        public void BytesPatternsAreRefusedWithTheirOwnText()
        {
            RunResult r = TestEngines.WithCeiling(TestEngines.Unhurried).Run("import re\nre.search(b'a', b'xa')\n", new RunRequest());
            Assert.Equal("TypeError", r.Error.PythonType);
            Assert.Equal("bytes patterns are not supported in this dialect", r.Error.Message);
        }

        // e1-03: the exact 17 digits (used on x86, where .NET's "E16" is wrong for 0.5% of doubles) equal x64 .NET's,
        // which are exact
        [Fact]
        public void TheExactSeventeenDigitsAreTheBclsOnX64()
        {
            if (IntPtr.Size != 8)
                return;
            var rnd = new Random(20261109);
            var buf = new byte[8];
            for (int n = 0; n < 20000; n++)
            {
                rnd.NextBytes(buf);
                long bits = BitConverter.ToInt64(buf, 0) & long.MaxValue;
                if (n % 10 == 0)
                    bits &= 0x000FFFFFFFFFFFFFL;   // subnormals too
                double d = BitConverter.Int64BitsToDouble(bits);
                if (d == 0 || double.IsNaN(d) || double.IsInfinity(d))
                    continue;
                string sci = d.ToString("E16", System.Globalization.CultureInfo.InvariantCulture);
                ulong d17;
                int e10;
                ReprDigits.Exact17(bits, out d17, out e10);
                string mine = d17.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(sci.Substring(0, 1) + sci.Substring(2, 16) + " " + int.Parse(sci.Substring(19), System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture), mine + " " + e10);
            }
        }

        // The whole x86 repr path (Exact17 in place of "E16") gives the digits of the x64 one: the suite runs x64 only.
        [Fact]
        public void TheX86ReprPathGivesTheX64Digits()
        {
            var rnd = new Random(20261110);
            var buf = new byte[8];
            for (int n = 0; n < 60000; n++)
            {
                double d;
                switch (n % 4)
                {
                    case 0:
                        rnd.NextBytes(buf);
                        d = BitConverter.ToDouble(buf, 0);
                        break;
                    case 1:
                        d = rnd.Next(-1000000, 1000000) / Math.Pow(10, rnd.Next(0, 12));
                        break;
                    case 2:
                        d = BitConverter.Int64BitsToDouble(rnd.Next(1, int.MaxValue) * (long)rnd.Next(1, 3));   // subnormal
                        break;
                    default:
                        d = (rnd.NextDouble() - 0.5) * Math.Pow(10, rnd.Next(-300, 300));
                        break;
                }
                if (double.IsNaN(d) || double.IsInfinity(d) || d == 0.0)
                    continue;
                bool neg, neg86;
                int exp, exp86;
                string x64 = ReprDigits.Shortest(d, out neg, out exp, false);
                string x86 = ReprDigits.Shortest(d, out neg86, out exp86, true);
                Assert.Equal(x64 + "e" + exp + (neg ? "-" : "+"), x86 + "e" + exp86 + (neg86 ? "-" : "+"));
            }
            bool n1;
            int e1;
            Assert.Equal("5", ReprDigits.Shortest(double.Epsilon, out n1, out e1, true));   // 5e-324
            Assert.Equal(-324, e1);
            Assert.Equal("17976931348623157", ReprDigits.Shortest(double.MaxValue, out n1, out e1, true));
        }
    }
}
