using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Tests;
using Xunit;

namespace Outbridge.PyLite.Tests.Builtins
{
    // impl/06 T-BLT-25 §3: pin the v1 public API surface (builtins + type methods). An intentional API
    // change updates these lists in the same commit.
    [Trait("Category", "Regression")]
    public class BuiltinsInventoryTests
    {
        private static readonly string[] Functions =
        {
            "print", "len", "range", "abs", "min", "max", "sum", "sorted", "reversed", "enumerate", "zip",
            "map", "filter", "any", "all", "int", "float", "str", "bool", "list", "dict", "tuple", "set",
            "frozenset", "round", "divmod", "pow", "isinstance", "issubclass", "type", "repr", "format",
            "chr", "ord", "hex", "oct", "bin", "hash", "iter", "next", "slice", "callable", "dir",
            "getattr", "hasattr", "setattr", "id"
        };

        private static readonly string[] Exceptions =
        {
            "BaseException", "Exception", "ArithmeticError", "ZeroDivisionError", "OverflowError",
            "ValueError", "TypeError", "KeyError", "IndexError", "LookupError", "AttributeError",
            "NameError", "RuntimeError", "StopIteration", "NotImplementedError", "AssertionError",
            "ImportError", "UnboundLocalError"
        };

        private static readonly string[] Unsupported =
        {
            "eval", "exec", "compile", "open", "input", "globals", "locals", "delattr"
        };

        [Fact]
        public void DefaultTableHasAllV1Names()
        {
            var t = HostFunctionTable.CreateDefault();
            foreach (string name in Functions)
                Assert.True(t.Contains(name), "missing builtin: " + name);
            foreach (string name in Exceptions)
                Assert.True(t.Contains(name), "missing exception: " + name);
        }

        [Fact]
        public void UnsupportedNamesAbsent()
        {
            var t = HostFunctionTable.CreateDefault();
            foreach (string name in Unsupported)
                Assert.False(t.Contains(name), "unexpectedly present: " + name);
        }

        [Theory]
        [InlineData("find")]
        [InlineData("rfind")]
        [InlineData("split")]
        [InlineData("rsplit")]
        [InlineData("join")]
        [InlineData("replace")]
        [InlineData("strip")]
        [InlineData("upper")]
        [InlineData("lower")]
        [InlineData("title")]
        [InlineData("startswith")]
        [InlineData("endswith")]
        [InlineData("format")]
        [InlineData("center")]
        [InlineData("zfill")]
        [InlineData("isdigit")]
        [InlineData("isalpha")]
        public void StrHasMethod(string name)
        {
            var ctx = Rt.Ctx();
            Assert.True(ctx.Values.Str("x").TypeInfo.Slots.ContainsKey(name), "str missing " + name);
        }

        [Fact]
        public void StrGainedP2Methods()
        {
            var ctx = Rt.Ctx();
            var slots = ctx.Values.Str("x").TypeInfo.Slots;
            Assert.True(slots.ContainsKey("encode"));       // RFC-001 §2 added str.encode -> bytes
            Assert.True(slots.ContainsKey("format_map"));   // P2 gap-closure
            Assert.True(slots.ContainsKey("translate"));
            Assert.True(slots.ContainsKey("isascii"));
        }

        [Theory]
        [InlineData("append")]
        [InlineData("extend")]
        [InlineData("insert")]
        [InlineData("remove")]
        [InlineData("pop")]
        [InlineData("sort")]
        [InlineData("index")]
        [InlineData("count")]
        [InlineData("copy")]
        [InlineData("reverse")]
        public void ListHasMethod(string name)
        {
            var ctx = Rt.Ctx();
            Assert.True(ctx.Values.List(0).TypeInfo.Slots.ContainsKey(name), "list missing " + name);
        }

        [Theory]
        [InlineData("get")]
        [InlineData("pop")]
        [InlineData("popitem")]
        [InlineData("setdefault")]
        [InlineData("update")]
        [InlineData("keys")]
        [InlineData("values")]
        [InlineData("items")]
        public void DictHasMethod(string name)
        {
            var ctx = Rt.Ctx();
            Assert.True(ctx.Values.Dict(0).TypeInfo.Slots.ContainsKey(name), "dict missing " + name);
        }

        [Theory]
        [InlineData("add")]
        [InlineData("remove")]
        [InlineData("discard")]
        [InlineData("union")]
        [InlineData("intersection")]
        [InlineData("difference")]
        [InlineData("issubset")]
        public void SetHasMethod(string name)
        {
            var ctx = Rt.Ctx();
            Assert.True(ctx.Values.Set(0).TypeInfo.Slots.ContainsKey(name), "set missing " + name);
        }

        [Fact]
        public void FrozenSetLacksMutators()
        {
            var ctx = Rt.Ctx();
            var fs = ctx.Values.FrozenSet(new OrderedTable(ctx, 0));
            Assert.False(fs.TypeInfo.Slots.ContainsKey("add"));
            Assert.True(fs.TypeInfo.Slots.ContainsKey("union"));
        }
    }
}
