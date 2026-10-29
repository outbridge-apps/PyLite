using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // RFC-003 modern-syntax (Python 3.14-era) features, end-to-end through lexer -> parser -> evaluator.
    // Grows one section per landed feature.
    [Trait("Category", "Compatibility")]
    public class ModernSyntaxTests
    {
        private static string R(string expr)
        {
            return Ev.ReprOf(expr);
        }

        // ---- §2.1 PEP 515: underscores in numeric literals ----

        [Theory]
        [InlineData("1_000_000", "1000000")]
        [InlineData("0xFF_FF", "65535")]
        [InlineData("0b1010_0101", "165")]
        [InlineData("1_000.5", "1000.5")]
        [InlineData("1_0e1_0", "100000000000.0")]
        public void UnderscoreLiterals(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void UnderscoreLiteralInScript()
        {
            Assert.Equal("2000000", Ev.Run("a = 1_000_000\nb = a + 1_000_000\n").Repr("b"));
        }

        // ---- §2.5 PEP 584: dict merge | and |= ----

        [Theory]
        [InlineData("{1: 2} | {3: 4}", "{1: 2, 3: 4}")]
        [InlineData("{1: 2, 3: 4} | {3: 9}", "{1: 2, 3: 9}")]  // right wins, position kept
        [InlineData("{} | {1: 1}", "{1: 1}")]
        public void DictUnion(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void DictUnionAssignIsInPlace()
        {
            var r = Ev.Run("d = {1: 2}\ne = d\nd |= {3: 4}\nsame = d is e\n");
            Assert.Equal("{1: 2, 3: 4}", r.Repr("d"));
            Assert.Equal("True", r.Repr("same"));   // PEP 584: |= mutates the same object
        }

        [Fact]
        public void DictUnionAssignAcceptsPairs()
        {
            // |= is update-like: accepts an iterable of key/value pairs
            Assert.Equal("{1: 2, 3: 4}", Ev.Run("d = {1: 2}\nd |= [(3, 4)]\n").Repr("d"));
        }

        [Fact]
        public void DictUnionNonDictRhsIsTypeError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("x = {1: 2} | [1]\n"));
            Assert.Equal("unsupported operand type(s) for |: 'dict' and 'list'", ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- §2.3 PEP 572: walrus := ----

        [Fact]
        public void WalrusYieldsValue()
        {
            Assert.Equal("5", R("(x := 5)"));
        }

        [Fact]
        public void WalrusBindsTheName()
        {
            var r = Ev.Run("y = (x := 41) + 1\n");
            Assert.Equal("41", r.Repr("x"));
            Assert.Equal("42", r.Repr("y"));
        }

        [Fact]
        public void WalrusInIfCondition()
        {
            var r = Ev.Run("data = [1, 2, 3, 4]\nif (n := len(data)) > 3:\n    big = n\n");
            Assert.Equal("4", r.Repr("big"));
            Assert.Equal("4", r.Repr("n"));
        }

        [Fact]
        public void WalrusInWhileCondition()
        {
            var r = Ev.Run("items = [3, 2, 1, 0]\nout = []\ni = 0\nwhile (v := items[i]):\n    out.append(v)\n    i += 1\n");
            Assert.Equal("[3, 2, 1]", r.Repr("out"));
        }

        [Fact]
        public void WalrusInComprehensionFilter()
        {
            Assert.Equal("[4, 16]", R("[y for x in [1, 2, 3, 4] if (y := x * x) % 2 == 0]"));
        }

        [Fact]
        public void WalrusIsRightAssociative()
        {
            var r = Ev.Run("a = (b := (c := 7))\n");
            Assert.Equal("7", r.Repr("a"));
            Assert.Equal("7", r.Repr("b"));
            Assert.Equal("7", r.Repr("c"));
        }

        [Fact]
        public void WalrusTargetMustBeName()
        {
            Assert.Throws<PySyntaxErrorException>(() => Ev.Run("obj = []\nobj.x := 5\n"));
        }

        // ---- §2.7 PEP 3107/526: annotations parsed and discarded ----

        [Fact]
        public void AnnotatedParamsAndReturnRunNormally()
        {
            var r = Ev.Run("def add(a: int, b: int = 2) -> int:\n    return a + b\nx = add(40)\ny = add(1, b=5)\n");
            Assert.Equal("42", r.Repr("x"));
            Assert.Equal("6", r.Repr("y"));
        }

        [Fact]
        public void AnnotatedVarAssignmentBinds()
        {
            Assert.Equal("7", Ev.Run("n: int = 7\n").Repr("n"));
        }

        [Fact]
        public void BareAnnotationDoesNotBind()
        {
            // 'x: int' declares nothing at runtime -> reading x is a NameError
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("x: int\nprint(x)\n"));
            Assert.Contains("name 'x' is not defined", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void StarArgsAnnotationDiscarded()
        {
            var r = Ev.Run("def f(*args: int, **kw: str):\n    return len(args)\nn = f(1, 2, 3)\n");
            Assert.Equal("3", r.Repr("n"));
        }

        // ---- §2.6 PEP 570: positional-only parameters ----

        [Fact]
        public void PositionalOnlyBindsPositionally()
        {
            var r = Ev.Run("def f(a, b, /, c):\n    return a + b + c\nx = f(1, 2, 3)\ny = f(1, 2, c=3)\n");
            Assert.Equal("6", r.Repr("x"));
            Assert.Equal("6", r.Repr("y"));
        }

        [Fact]
        public void PositionalOnlyRejectsKeyword()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("def f(a, /):\n    return a\nf(a=1)\n"));
            Assert.Equal("f() got some positional-only arguments passed as keyword arguments: 'a'",
                ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void PositionalOnlyNameFallsIntoKwargs()
        {
            // 'a' is positional-only, so 'a=2' lands in **kw, not the parameter
            var r = Ev.Run("def f(a, /, **kw):\n    return (a, kw)\nres = f(1, a=2)\n");
            Assert.Equal("(1, {'a': 2})", r.Repr("res"));
        }

        [Fact]
        public void LambdaPositionalOnly()
        {
            Assert.Equal("5", Ev.Run("g = lambda a, /, b: a + b\nr = g(2, b=3)\n").Repr("r"));
        }

        // ---- §2.2 PEP 448: unpacking generalizations in displays ----

        [Theory]
        [InlineData("[*[1, 2], 3, *[4, 5]]", "[1, 2, 3, 4, 5]")]
        [InlineData("[*range(3)]", "[0, 1, 2]")]
        [InlineData("(*[1, 2], *[3])", "(1, 2, 3)")]
        [InlineData("{*[1, 2], *[2, 3]}", "{1, 2, 3}")]
        [InlineData("{**{1: 2}, **{3: 4}}", "{1: 2, 3: 4}")]
        [InlineData("{**{1: 2, 3: 4}, 3: 99}", "{1: 2, 3: 99}")]
        [InlineData("{1: 1, **{2: 2}}", "{1: 1, 2: 2}")]
        public void UnpackingInDisplays(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void StarUnpackNonIterableIsTypeError()
        {
            Assert.Throws<ScriptException>(() => Ev.Run("x = [*5]\n"));
        }

        [Fact]
        public void DoubleStarUnpackNonMappingIsTypeError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("x = {**[1, 2]}\n"));
            Assert.Equal("'list' object is not a mapping", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void StringUnpacksIntoSet()
        {
            Assert.Equal("{'a', 'b', 'c'}", R("{*'abc'}"));
        }

        [Fact]
        public void CallUnpackingStillWorks()
        {
            // PEP 448 in call args (already supported) alongside the new display unpacking
            var r = Ev.Run("def f(a, b, c, d):\n    return a + b + c + d\nnums = [1, 2]\nx = f(*nums, *[3, 4])\n");
            Assert.Equal("10", r.Repr("x"));
        }

        // ---- §2.4 PEP 501/701: f-string '=' self-documenting + same-quote nesting ----

        [Fact]
        public void FStringDebugNoSpace()
        {
            Assert.Equal("'x=5'", Ev.Run("x = 5\nr = f'{x=}'\n").Repr("r"));
        }

        [Fact]
        public void FStringDebugPreservesSpaces()
        {
            Assert.Equal("'x = 5'", Ev.Run("x = 5\nr = f'{x = }'\n").Repr("r"));
        }

        [Fact]
        public void FStringDebugDefaultsToRepr()
        {
            // default conversion is repr, so a str value keeps its quotes
            Assert.Equal("\"s='hi'\"", Ev.Run("s = 'hi'\nr = f'{s=}'\n").Repr("r"));
        }

        [Fact]
        public void FStringDebugWithSpecUsesFormat()
        {
            Assert.Equal("'x=  5'", Ev.Run("x = 5\nr = f'{x=:>3}'\n").Repr("r"));
        }

        [Fact]
        public void FStringDebugWithConversion()
        {
            Assert.Equal("'x=5'", Ev.Run("x = 5\nr = f'{x=!s}'\n").Repr("r"));
        }

        [Fact]
        public void FStringDebugArbitraryExpression()
        {
            Assert.Equal("'a + b=7'", Ev.Run("a = 3\nb = 4\nr = f'{a + b=}'\n").Repr("r"));
        }

        [Fact]
        public void FStringSameQuoteNesting()
        {
            // PEP 701: the replacement field may reuse the outer quote character
            Assert.Equal("'VALUE'", Ev.Run("d = {'k': 'VALUE'}\nr = f'{d['k']}'\n").Repr("r"));
        }

        // ---- §2.8 PEP 614: decorators on def ----

        [Fact]
        public void SimpleDecorator()
        {
            var r = Ev.Run("def twice(f):\n    return lambda x: f(f(x))\n@twice\ndef inc(x):\n    return x + 1\ny = inc(10)\n");
            Assert.Equal("12", r.Repr("y"));
        }

        [Fact]
        public void DecoratorAppliesBottomUp()
        {
            // order proof: @a over @b means a(b(f)); the innermost wraps first
            var src = "log = []\n"
                + "def a(f):\n    log.append('a')\n    return f\n"
                + "def b(f):\n    log.append('b')\n    return f\n"
                + "@a\n@b\ndef g():\n    return 0\n";
            Assert.Equal("['b', 'a']", Ev.Run(src).Repr("log"));
        }

        [Fact]
        public void DecoratorFactoryWithArgs()
        {
            var src = "def repeat(n):\n    def deco(f):\n        return lambda x: f(x) * n\n    return deco\n"
                + "@repeat(3)\ndef ident(x):\n    return x\nr = ident(5)\n";
            Assert.Equal("15", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void DecoratorExpressionEvalOrderTopDown()
        {
            // PEP 614: decorator expressions evaluate top-to-bottom before the calls apply bottom-up
            var src = "seq = []\n"
                + "def mk(tag):\n    seq.append('eval:' + tag)\n    def deco(f):\n        seq.append('call:' + tag)\n        return f\n    return deco\n"
                + "@mk('A')\n@mk('B')\ndef h():\n    return 1\n";
            Assert.Equal("['eval:A', 'eval:B', 'call:B', 'call:A']", Ev.Run(src).Repr("seq"));
        }

        // ---- §2.9 PEP 634: match / case (structural pattern matching) ----

        [Fact]
        public void MatchLiteralAndWildcard()
        {
            var src = "def classify(x):\n"
                + "    match x:\n"
                + "        case 0:\n            return 'zero'\n"
                + "        case 1:\n            return 'one'\n"
                + "        case _:\n            return 'many'\n"
                + "a = classify(0)\nb = classify(1)\nc = classify(9)\n";
            var r = Ev.Run(src);
            Assert.Equal("'zero'", r.Repr("a"));
            Assert.Equal("'one'", r.Repr("b"));
            Assert.Equal("'many'", r.Repr("c"));
        }

        [Fact]
        public void MatchCaptureBinds()
        {
            Assert.Equal("42", Ev.Run("match 42:\n    case n:\n        got = n\n").Repr("got"));
        }

        [Fact]
        public void MatchSequenceWithStar()
        {
            var r = Ev.Run("match [1, 2, 3, 4]:\n    case [first, *rest]:\n        f = first\n        r = rest\n");
            Assert.Equal("1", r.Repr("f"));
            Assert.Equal("[2, 3, 4]", r.Repr("r"));
        }

        [Fact]
        public void MatchSequenceExactLength()
        {
            var src = "def f(p):\n"
                + "    match p:\n"
                + "        case [x, y]:\n            return x + y\n"
                + "        case [x, y, z]:\n            return x * z\n"
                + "        case _:\n            return -1\n"
                + "a = f([2, 3])\nb = f([2, 3, 4])\nc = f([1])\n";
            var r = Ev.Run(src);
            Assert.Equal("5", r.Repr("a"));
            Assert.Equal("8", r.Repr("b"));
            Assert.Equal("-1", r.Repr("c"));
        }

        [Fact]
        public void MatchTupleSubject()
        {
            var src = "def q(a, b):\n"
                + "    match a, b:\n"
                + "        case [0, 0]:\n            return 'origin'\n"
                + "        case [0, y]:\n            return ('yaxis', y)\n"
                + "        case [x, 0]:\n            return ('xaxis', x)\n"
                + "        case _:\n            return 'other'\n"
                + "r1 = q(0, 0)\nr2 = q(0, 5)\nr3 = q(7, 0)\nr4 = q(3, 4)\n";
            var r = Ev.Run(src);
            Assert.Equal("'origin'", r.Repr("r1"));
            Assert.Equal("('yaxis', 5)", r.Repr("r2"));
            Assert.Equal("('xaxis', 7)", r.Repr("r3"));
            Assert.Equal("'other'", r.Repr("r4"));
        }

        [Fact]
        public void MatchMappingPattern()
        {
            var src = "def h(d):\n"
                + "    match d:\n"
                + "        case {'type': 'point', 'x': x, 'y': y}:\n            return x + y\n"
                + "        case {'type': t}:\n            return t\n"
                + "        case _:\n            return None\n"
                + "a = h({'type': 'point', 'x': 3, 'y': 4})\nb = h({'type': 'circle'})\nc = h({})\n";
            var r = Ev.Run(src);
            Assert.Equal("7", r.Repr("a"));
            Assert.Equal("'circle'", r.Repr("b"));
            Assert.Equal("None", r.Repr("c"));
        }

        [Fact]
        public void MatchMappingRest()
        {
            var r = Ev.Run("match {'a': 1, 'b': 2, 'c': 3}:\n    case {'a': x, **rest}:\n        v = x\n        others = rest\n");
            Assert.Equal("1", r.Repr("v"));
            Assert.Equal("{'b': 2, 'c': 3}", r.Repr("others"));
        }

        [Fact]
        public void MatchOrPattern()
        {
            var src = "def f(x):\n"
                + "    match x:\n"
                + "        case 1 | 2 | 3:\n            return 'small'\n"
                + "        case _:\n            return 'big'\n"
                + "a = f(2)\nb = f(9)\n";
            var r = Ev.Run(src);
            Assert.Equal("'small'", r.Repr("a"));
            Assert.Equal("'big'", r.Repr("b"));
        }

        [Fact]
        public void MatchGuard()
        {
            var src = "def f(x):\n"
                + "    match x:\n"
                + "        case n if n < 0:\n            return 'neg'\n"
                + "        case 0:\n            return 'zero'\n"
                + "        case _:\n            return 'pos'\n"
                + "a = f(-5)\nb = f(0)\nc = f(3)\n";
            var r = Ev.Run(src);
            Assert.Equal("'neg'", r.Repr("a"));
            Assert.Equal("'zero'", r.Repr("b"));
            Assert.Equal("'pos'", r.Repr("c"));
        }

        [Fact]
        public void MatchAsPattern()
        {
            var r = Ev.Run("match [1, 2]:\n    case [1, y] as whole:\n        yy = y\n        w = whole\n");
            Assert.Equal("2", r.Repr("yy"));
            Assert.Equal("[1, 2]", r.Repr("w"));
        }

        [Fact]
        public void MatchGuardFallsToNextCase()
        {
            // pattern matches but guard is false -> next case
            var src = "def f(x):\n"
                + "    match x:\n"
                + "        case n if n > 100:\n            return 'huge'\n"
                + "        case n:\n            return 'normal'\n"
                + "a = f(5)\nb = f(500)\n";
            var r = Ev.Run(src);
            Assert.Equal("'normal'", r.Repr("a"));
            Assert.Equal("'huge'", r.Repr("b"));
        }

        [Fact]
        public void MatchNoMatchIsNoOp()
        {
            Assert.Equal("'none'", Ev.Run("hit = 'none'\nmatch 5:\n    case 1:\n        hit = 'one'\n    case 2:\n        hit = 'two'\n").Repr("hit"));
        }

        [Fact]
        public void MatchStringDoesNotMatchSequence()
        {
            // a str is NOT a sequence for pattern purposes
            var src = "def f(x):\n"
                + "    match x:\n"
                + "        case [a, b]:\n            return 'seq'\n"
                + "        case _:\n            return 'other'\n"
                + "r = f('ab')\n";
            Assert.Equal("'other'", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void MatchAndCaseUsableAsIdentifiers()
        {
            // soft keywords: outside a match statement, 'match'/'case' are ordinary names
            var r = Ev.Run("match = 10\ncase = 20\nmatch += case\n");
            Assert.Equal("30", r.Repr("match"));
        }

        [Fact]
        public void MatchStarSubjectRejectedBare()
        {
            // a bare star subject without a comma is not valid
            Assert.Throws<PySyntaxErrorException>(() => Ev.Run("match *x:\n    case _:\n        pass\n"));
        }

        // ---- §2.11 functools ----

        [Fact]
        public void FunctoolsReduceWithInit()
        {
            Assert.Equal("10", Ev.Run("import functools\nr = functools.reduce(lambda a, b: a + b, [1, 2, 3, 4], 0)\n").Repr("r"));
        }

        [Fact]
        public void FunctoolsReduceNoInit()
        {
            Assert.Equal("24", Ev.Run("import functools\nr = functools.reduce(lambda a, b: a * b, [1, 2, 3, 4])\n").Repr("r"));
        }

        [Fact]
        public void FunctoolsPartialPositional()
        {
            var src = "import functools\n"
                + "def add(a, b, c):\n    return a + b + c\n"
                + "add5 = functools.partial(add, 5)\n"
                + "r = add5(10, 20)\n";
            Assert.Equal("35", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void FunctoolsPartialKeywords()
        {
            var src = "import functools\n"
                + "def greet(greeting, name):\n    return greeting + ', ' + name\n"
                + "hi = functools.partial(greet, greeting='Hi')\n"
                + "r = hi(name='Bob')\n";
            Assert.Equal("'Hi, Bob'", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void FunctoolsLruCacheBare()
        {
            var src = "import functools\n"
                + "calls = []\n"
                + "@functools.lru_cache\n"
                + "def sq(x):\n    calls.append(x)\n    return x * x\n"
                + "a = sq(4)\nb = sq(4)\nc = sq(5)\nn = len(calls)\n";
            var r = Ev.Run(src);
            Assert.Equal("16", r.Repr("a"));
            Assert.Equal("16", r.Repr("b"));
            Assert.Equal("25", r.Repr("c"));
            Assert.Equal("2", r.Repr("n"));   // sq(4) computed once (hit on the second call), sq(5) once
        }

        [Fact]
        public void FunctoolsLruCacheEvictsLeastRecentlyUsed()
        {
            var src = "import functools\n"
                + "calls = []\n"
                + "@functools.lru_cache(maxsize=2)\n"
                + "def f(x):\n    calls.append(x)\n    return x\n"
                + "f(1)\nf(2)\nf(1)\nf(3)\nf(1)\n";   // touch 1, add 3 -> evicts 2 (LRU), 1 survives
            Assert.Equal("[1, 2, 3]", Ev.Run(src).Repr("calls"));
        }

        [Fact]
        public void FunctoolsLruCacheUnhashableArgRaises()
        {
            Assert.Throws<ScriptException>(() => Ev.Run("import functools\n@functools.lru_cache\ndef f(x):\n    return x\nf([1, 2])\n"));
        }

        [Fact]
        public void FunctoolsWrapsKeepsWrapperWorking()
        {
            var src = "import functools\n"
                + "def deco(f):\n"
                + "    @functools.wraps(f)\n"
                + "    def inner(x):\n        return f(x) + 1\n"
                + "    return inner\n"
                + "@deco\ndef g(x):\n    return x * 2\n"
                + "r = g(10)\n";
            Assert.Equal("21", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void FunctoolsCmpToKeySortsByComparator()
        {
            var src = "import functools\n"
                + "def cmp(a, b):\n    return b - a\n"   // descending
                + "r = sorted([3, 1, 2, 5, 4], key=functools.cmp_to_key(cmp))\n";
            Assert.Equal("[5, 4, 3, 2, 1]", Ev.Run(src).Repr("r"));
        }

        // PEP 479 (RFC-003 §4): a StopIteration escaping a comprehension/genexp body -> RuntimeError,
        // not a silent end-of-iteration. Both the eager comprehension and the lazy genexp drive the same
        // ComprehensionIterator.MoveNext.
        [Fact]
        public void StopIterationInComprehensionBodyBecomesRuntimeError()
        {
            var e1 = Assert.Throws<ScriptException>(() => Ev.Run(
                "def f():\n    raise StopIteration\nr = [f() for _ in range(1)]\n"));
            Assert.Equal("RuntimeError", e1.Value.ExcType.Name);

            var e2 = Assert.Throws<ScriptException>(() => Ev.Run(
                "def f():\n    raise StopIteration\nr = list(f() for _ in range(1))\n"));
            Assert.Equal("RuntimeError", e2.Value.ExcType.Name);
        }

        // 3.8+ (RFC-003 §4): continue is legal inside finally; its signal overrides the pending exception.
        [Fact]
        public void ContinueInFinallySwallowsPendingException()
        {
            var src = "r = []\n"
                + "for i in range(3):\n"
                + "    try:\n"
                + "        if i == 1:\n"
                + "            raise ValueError('x')\n"
                + "        r.append(i)\n"
                + "    finally:\n"
                + "        continue\n"
                + "    r.append(99)\n";   // never reached: finally's continue restarts the loop
            Assert.Equal("[0, 2]", Ev.Run(src).Repr("r"));
        }
    }
}
