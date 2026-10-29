using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // A wrong argument is the script's TypeError/ValueError, never a C# exception reaching the host as an
    // EngineFault (which no `except` can catch). Each case below used to fault: an unchecked cast, a throwing
    // UTF-8 decoder, or an arity the module never checked (found by the 2026-10 code review, B2).
    [Trait("Category", "Regression")]
    public class ArgumentFaultTests
    {
        [Theory]
        [InlineData("b'aaa'.replace(b'a', b'b', None)", "TypeError")]
        [InlineData("b'aaa'.replace(b'a', b'b', 2**80)", "b'bbb'")]
        [InlineData("b'a b c'.split(None, 2**40)", "[b'a', b'b', b'c']")]
        [InlineData("b'x'.decode('utf-8', 5)", "TypeError")]
        [InlineData("ET.fromstring(b'\\xff')", "UnicodeDecodeError")]
        [InlineData("json.load(io.BytesIO(b'\\xff'))", "UnicodeDecodeError")]
        [InlineData("uuid.UUID(int=1, version='4')", "TypeError")]
        [InlineData("uuid.UUID(fields=itertools.count())", "ValueError")]
        [InlineData("random.choice()", "TypeError")]
        [InlineData("random.shuffle()", "TypeError")]
        [InlineData("random.randint(1)", "TypeError")]
        [InlineData("random.uniform(1)", "TypeError")]
        [InlineData("random.gammavariate(1)", "TypeError")]
        [InlineData("random.random(1)", "TypeError")]
        public void AWrongArgumentIsAScriptError(string expr, string expected)
        {
            Assert.Equal(expected.EndsWith("Error") ? "'" + expected + "'" : expected, Eval(expr));
        }

        // An argument is never dropped silently (B10): a positional-only builtin or method refuses a keyword
        // and an extra argument as CPython does, and a keyword CPython accepts is used.
        [Theory]
        [InlineData("math.sqrt(4, 5)", "TypeError")]
        [InlineData("math.sqrt(x=4)", "TypeError")]
        [InlineData("math.floor(2.5, 1)", "TypeError")]
        [InlineData("'xax'.strip(chars='x')", "TypeError")]
        [InlineData("'abc'.find('c', start=1)", "TypeError")]
        [InlineData("'a'.upper(1)", "TypeError")]
        [InlineData("'a'.center(5, '*', 1)", "TypeError")]
        [InlineData("[3, 1].sort(len)", "TypeError")]
        [InlineData("[1].clear(5)", "TypeError")]
        [InlineData("{}.get('a', x=1)", "TypeError")]
        [InlineData("{1}.add(2, 3)", "TypeError")]
        [InlineData("abs(1, foo=1)", "TypeError")]
        [InlineData("len([], x=1)", "TypeError")]
        [InlineData("list(map(str, [1], x=1))", "TypeError")]
        [InlineData("list(enumerate('a', 1, start=2))", "TypeError")]
        [InlineData("statistics.quantiles([1, 2, 3, 4], N=10)", "TypeError")]
        [InlineData("pow(2, 10, mod=7)", "2")]
        [InlineData("pow(base=2, exp=10)", "1024")]
        [InlineData("pow(2, 10, None)", "1024")]
        [InlineData("bytes('x', encoding='ascii')", "b'x'")]
        [InlineData("'aaa'.replace('a', 'b', count=1)", "'baa'")]
        [InlineData("b'aaa'.replace(b'a', b'b', count=2)", "b'bba'")]
        [InlineData("list(enumerate('ab', start=2))", "[(2, 'a'), (3, 'b')]")]
        [InlineData("set().union([1], [2], [3])", "{1, 2, 3}")]
        [InlineData("math.gcd(12, 18, 27)", "3")]
        // the rest of the modules and engine types (B10 tail)
        [InlineData("datetime.date(2024, 1, 2).weekday(1)", "TypeError")]
        [InlineData("datetime.date(2024, 1, 2).weekday(x=1)", "TypeError")]
        [InlineData("datetime.date.today(1)", "TypeError")]
        [InlineData("heapq.heappush([], 1, x=2)", "TypeError")]
        [InlineData("time.sleep(secs=0)", "TypeError")]
        [InlineData("struct.unpack('b', b'x', 1)", "TypeError")]
        [InlineData("io.StringIO('ab').read(1, 2)", "TypeError")]
        [InlineData("operator.methodcaller('upper')('a', 'b')", "TypeError")]
        [InlineData("hashlib.sha256().update(x=1)", "TypeError")]
        [InlineData("dict.fromkeys([1], 2, 3)", "TypeError")]
        [InlineData("(5).bit_length(1)", "TypeError")]
        [InlineData("re.match('(x)', 'x').span(1, 2)", "TypeError")]
        [InlineData("re.match('(x)(y)?', 'x').groups(default='-')", "('x', '-')")]
        [InlineData("re.match('(?P<a>x)(?P<b>y)?', 'x').groupdict(default='-')", "{'a': 'x', 'b': '-'}")]
        [InlineData("fractions.Fraction(1, 3).limit_denominator(max_denominator=2)", "Fraction(1, 2)")]
        [InlineData("base64.b32decode(b'ME======', map01=b'L')", "b'a'")]
        [InlineData("base64.b16decode(b'ff', casefold=True)", "b'\\xff'")]
        [InlineData("base64.b16decode(b'ff', x=1)", "TypeError")]
        [InlineData("typing.NamedTuple('P', x=int, y=str)(1, 2)", "P(x=1, y=2)")]
        [InlineData("typing.NamedTuple('P', [('x', int)], y=str)", "TypeError")]
        [InlineData("typing.get_type_hints(len, globalns=None, include_extras=True)", "{}")]
        [InlineData("typing.get_type_hints(len, nope=1)", "TypeError")]
        [InlineData("zoneinfo.ZoneInfo.clear_cache(only_keys=['UTC'])", "None")]
        [InlineData("zoneinfo.ZoneInfo.clear_cache(keys=['UTC'])", "TypeError")]
        [InlineData("functools.wraps(len, assigned=())(lambda: 0).__name__", "'<lambda>'")]
        [InlineData("functools.wraps(len)(lambda: 0).__name__", "'len'")]
        [InlineData("functools.wraps(len, nope=1)", "TypeError")]
        public void NoArgumentIsDroppedSilently(string expr, string expected)
        {
            Assert.Equal(expected.EndsWith("Error") ? "'" + expected + "'" : expected, Eval(expr));
        }

        private static string Eval(string expr)
        {
            string src = "import base64, datetime, fractions, functools, hashlib, heapq, io, itertools, json, math, operator, random, re,"
                + " statistics, struct, time, typing, uuid, zoneinfo\nimport xml.etree.ElementTree as ET\n"
                + "try:\n    r = " + expr + "\nexcept Exception as e:\n    r = type(e).__name__\n";
            return Outbridge.PyLite.Tests.Evaluator.Ev.Run(src).Repr("r");
        }
    }
}
