using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // enum (1.1.0). An IntEnum/StrEnum member is a real int/str object that the run's EnumValues map
    // knows; what only the engine can get wrong - the map's reach, the budget, the dialect refusals.
    [Trait("Category", "Compatibility")]
    public class EnumTests
    {
        private static string R(string src)
        {
            return Ev.Run("from enum import Enum, IntEnum, StrEnum, auto, unique\n" + src).Repr("r");
        }

        private static RunResult Run(string src, ResourceLimits limits)
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = limits });
            return engine.Run("from enum import Enum\n" + src, new RunRequest { ScalarOutputNames = new[] { "r" } });
        }

        [Fact]
        public void AMemberIsNeverASharedIntOrStr()
        {
            // the member's int/str is its own object: the cached 1 and the shared '' stay plain
            Assert.Equal("('1', \"''\", '<N.A: 1>', \"<E.EMPTY: ''>\", '1', \"''\")",
                R("class N(IntEnum):\n    A = 1\nclass E(StrEnum):\n    EMPTY = ''\n"
                + "r = (repr(1), repr(''), repr(N.A), repr(E.EMPTY), repr(N.A.value), repr(E.EMPTY.value))\n"));
        }

        [Fact]
        public void ArithmeticAndStringMethodsGivePlainValues()
        {
            Assert.Equal("('2', '1', \"'XY'\", 'True')",
                R("class N(IntEnum):\n    A = 1\nclass S(StrEnum):\n    X = 'x'\n"
                + "r = (repr(N.A + 1), repr(int(N.A)), repr(S.X.upper() + 'Y'), repr(type(N.A * 1) is int))\n"));
        }

        [Fact]
        public void UnhashableValuesAreFoundByEquality()
        {
            Assert.Equal("(True, True, 2)", R("class L(Enum):\n    A = [1]\n    B = [2]\n    C = [1]\n"
                + "r = (L([1]) is L.A, L.C is L.A, len(L))\n"));
        }

        [Fact]
        public void AHundredThousandMembersAreChargedAndCapped()
        {
            const string src = "Big = Enum('Big', ['M%d' % i for i in range(100000)])\nr = len(Big)\n";
            RunResult ok = Run(src, ResourceLimits.Default);
            Assert.True(ok.Succeeded, ok.Error?.Message);
            Assert.Equal("100000", ok.ScalarsOut["r"]);
            Assert.True(ok.Stats.AllocatedBytes > 10_000_000, ok.Stats.AllocatedBytes.ToString());

            RunResult capped = Run(src, ResourceLimits.Default.With(maxCollectionItems: 50000));
            Assert.Equal("MaxCollectionItems", capped.Error.LimitName);
            RunResult memory = Run(src, ResourceLimits.Default.With(maxAllocBytes: 8_000_000));
            Assert.Equal("MaxAllocBytes", memory.Error.LimitName);
        }

        [Theory]
        [InlineData("class C(Enum):\n    A = 1\n    def __init__(self):\n        pass\n", "TypeError")]
        [InlineData("class C(Enum):\n    A: int = 1\n", "TypeError")]
        [InlineData("class C(Enum):\n    _x_ = 1\n", "ValueError")]
        [InlineData("class C(Enum):\n    A = 1\nC.A.B\n", "AttributeError")]
        [InlineData("class C(Enum):\n    A = 1\nC()\n", "TypeError")]
        [InlineData("class R:\n    pass\nclass C(str, R):\n    A = 'a'\n", "TypeError")]
        [InlineData("class C(IntEnum):\n    A = 'x'\n", "ValueError")]
        [InlineData("class C(IntEnum):\n    A = [1]\n", "TypeError")]
        [InlineData("class C(Enum):\n    A = 1\nclass D(C):\n    B = 2\n", "TypeError")]
        public void DialectAndCPythonErrors(string src, string type)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("from enum import Enum, IntEnum\n" + src));
            Assert.Equal(type, ex.Value.ExcType.Name);
        }

        [Fact]
        public void OnlyStrOrIntMayBeMixedIn()
        {
            Assert.Throws<PySyntaxErrorException>(() => Ev.Run("from enum import Enum\nclass C(float, Enum):\n    A = 1.0\n"));
            Assert.Throws<PySyntaxErrorException>(() => Ev.Run("from enum import Enum\nclass C(str, Enum, int):\n    A = 'a'\n"));
        }

        [Fact]
        public void AClassCheckedAgainstABuiltinTypeIsNotASubclass()
        {
            // was an InvalidCastException (an engine fault) before 1.1.0
            Assert.Equal("(False, False, True)", R("class Rec:\n    pass\nclass E(Enum):\n    A = 1\n"
                + "r = (issubclass(Rec, int), issubclass(E, int), issubclass(E, (int, Enum)))\n"));
        }

        [Fact]
        public void EnumsSurviveAJsonRoundTripAsKeys()
        {
            Assert.Equal("(True, True)", R("import json\nclass Code(IntEnum):\n    OK = 200\n    MISSING = 404\n"
                + "class Kind(StrEnum):\n    A = 'a'\n"
                + "back = json.loads(json.dumps({Code.OK: Kind.A}))\n"
                + "r = (Code(int(list(back)[0])) is Code.OK, Kind(back['200']) is Kind.A)\n"));
        }
    }
}
