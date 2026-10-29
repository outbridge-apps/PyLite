using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // v2 E16/E17/E19: deepcopy(x, memo), UUID(bytes_le=/fields=), copyreg extension registry.
    [Trait("Category", "Compatibility")]
    public class V2TailsTests
    {
        private static string Body(string body)
        {
            return Ev.Run(body + "\n").Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(body + "\n"));
        }

        // ---- E16: deepcopy memo ----

        [Fact]
        public void DeepCopySharedMemoDedupsAcrossCalls()
        {
            string src = "import copy\n"
                + "shared = [1, 2]\n"
                + "memo = {}\n"
                + "a = copy.deepcopy([shared], memo)\n"
                + "b = copy.deepcopy([shared, shared], memo)\n"
                + "r = (a[0] is b[0], b[0] is b[1], a[0] is shared)\n";
            Assert.Equal("(True, True, False)", Body(src));
        }

        [Fact]
        public void DeepCopyMemoNoneIsFresh()
        {
            Assert.Equal("(True, False)", Body("import copy\nl = [1, [2]]\nc = copy.deepcopy(l, None)\nr = (c == l, c[1] is l[1])"));
        }

        [Fact]
        public void DeepCopyMemoWrongTypeRaises()
        {
            Assert.Equal("TypeError", Raises("import copy\nx = copy.deepcopy([1], 5)").Value.ExcType.Name);
        }

        // ---- E17: uuid bytes_le / fields ----

        [Theory]
        [InlineData("from uuid import UUID\nu = UUID('12345678-1234-5678-1234-567812345678')\nr = str(UUID(bytes_le=u.bytes_le))",
            "'12345678-1234-5678-1234-567812345678'")]
        [InlineData("from uuid import UUID\nu = UUID('12345678-1234-5678-1234-567812345678')\nr = (u.bytes_le == u.bytes)", "False")]
        [InlineData("from uuid import UUID\nr = str(UUID(fields=(0x12345678, 0x1234, 0x5678, 0x12, 0x34, 0x567812345678)))",
            "'12345678-1234-5678-1234-567812345678'")]
        [InlineData("from uuid import UUID\nu = UUID('12345678-1234-5678-1234-567812345678')\nr = (UUID(fields=u.fields) == u)", "True")]
        [InlineData("from uuid import UUID\nu = UUID(int=0)\nr = (UUID(bytes_le=u.bytes_le) == u)", "True")]
        public void UuidConstructors(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("from uuid import UUID\nx = UUID(fields=(2**32, 0, 0, 0, 0, 0))", "ValueError")]
        [InlineData("from uuid import UUID\nx = UUID(fields=(0, 0, 0, 256, 0, 0))", "ValueError")]
        [InlineData("from uuid import UUID\nx = UUID(fields=(0, 0, 0, 0, 0, 2**48))", "ValueError")]
        [InlineData("from uuid import UUID\nx = UUID(fields=(1, 2, 3))", "ValueError")]
        [InlineData("from uuid import UUID\nx = UUID(bytes_le=b'short')", "ValueError")]
        [InlineData("from uuid import UUID\nu = UUID(int=0)\nx = UUID(bytes=u.bytes, int=0)", "TypeError")]
        public void UuidErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void UuidFieldMessage()
        {
            string src = "from uuid import UUID\nmsg = ''\ntry:\n    UUID(fields=(2**32, 0, 0, 0, 0, 0))\nexcept ValueError as e:\n    msg = str(e)\n";
            Assert.Equal("'field 1 out of range (need a 32-bit value)'", Ev.Run(src).Repr("msg"));
        }

        // ---- E19: copyreg extension registry ----

        [Fact]
        public void AddAndRemoveExtension()
        {
            string src = "import copyreg\n"
                + "copyreg.add_extension('mod', 'name', 7)\n"
                + "a = ('mod', 'name') in copyreg._extension_registry\n"
                + "b = 7 in copyreg._inverted_registry\n"
                + "copyreg.remove_extension('mod', 'name', 7)\n"
                + "c = ('mod', 'name') in copyreg._extension_registry\n"
                + "r = (a, b, c)\n";
            Assert.Equal("(True, True, False)", Body(src));
        }

        [Fact]
        public void AddExtensionIsIdempotent()
        {
            string src = "import copyreg\n"
                + "copyreg.add_extension('m', 'n', 5)\n"
                + "copyreg.add_extension('m', 'n', 5)\n"   // same pair: no error
                + "r = copyreg._extension_registry[('m', 'n')]\n";
            Assert.Equal("5", Body(src));
        }

        [Theory]
        [InlineData("copyreg.add_extension('m', 'n', 0)", "ValueError")]
        [InlineData("copyreg.add_extension('m', 'n', 2**31)", "ValueError")]
        [InlineData("copyreg.add_extension('m', 'n', 1)\ncopyreg.add_extension('m', 'n', 2)", "ValueError")]
        [InlineData("copyreg.add_extension('a', 'b', 3)\ncopyreg.add_extension('c', 'd', 3)", "ValueError")]
        [InlineData("copyreg.remove_extension('no', 'pe', 9)", "ValueError")]
        public void ExtensionErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises("import copyreg\n" + body).Value.ExcType.Name);
        }

        [Fact]
        public void ClearExtensionCache()
        {
            string src = "import copyreg\n"
                + "copyreg._extension_cache[1] = 'x'\n"
                + "copyreg.clear_extension_cache()\n"
                + "r = len(copyreg._extension_cache)\n";
            Assert.Equal("0", Body(src));
        }
    }
}
