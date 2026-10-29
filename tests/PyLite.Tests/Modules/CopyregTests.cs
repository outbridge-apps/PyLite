using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-33: copyreg stub.
    [Trait("Category", "Compatibility")]
    public class CopyregTests
    {
        private static string Run(string src)
        {
            return Ev.Run(src).Repr("r");
        }

        private static ScriptException Raises(string src)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(src));
        }

        [Fact]
        public void ImportSucceeds()
        {
            Assert.Equal("True", Run("import copyreg\nr = True\n"));
        }

        [Fact]
        public void FromImportSucceeds()
        {
            Assert.Equal("True", Run("from copyreg import pickle, constructor\nr = True\n"));
        }

        [Fact]
        public void PickleReturnsNone()
        {
            Assert.Equal("None", Run("import copyreg\nr = copyreg.pickle(int, lambda x: x)\n"));
        }

        [Fact]
        public void ConstructorReturnsNone()
        {
            Assert.Equal("None", Run("import copyreg\nr = copyreg.constructor(lambda: 0)\n"));
        }

        [Fact]
        public void DispatchTableEmptyAtStart()
        {
            Assert.Equal("0", Run("import copyreg\nr = len(copyreg.dispatch_table)\n"));
        }

        [Fact]
        public void PickleWritesDispatchTable()
        {
            Assert.Equal("True", Run("import copyreg\ncopyreg.pickle(int, lambda x: x)\nr = int in copyreg.dispatch_table\n"));
        }

        [Theory]
        [InlineData("import copyreg\ncopyreg.pickle(int, 5)\n", "TypeError")]
        [InlineData("import copyreg\ncopyreg.constructor(5)\n", "TypeError")]
        [InlineData("import pickle\n", "ImportError")]
        public void Errors(string src, string excType)
        {
            Assert.Equal(excType, Raises(src).Value.ExcType.Name);
        }

        [Fact]
        public void PickleNonCallableMessage()
        {
            string src = "import copyreg\nmsg = ''\ntry:\n    copyreg.pickle(int, 5)\nexcept TypeError as e:\n    msg = str(e)\n";
            Assert.Equal("'reduction functions must be callable.'", Ev.Run(src).Repr("msg"));
        }

        [Fact]
        public void ConstructorNonCallableMessage()
        {
            string src = "import copyreg\nmsg = ''\ntry:\n    copyreg.constructor(5)\nexcept TypeError as e:\n    msg = str(e)\n";
            Assert.Equal("'constructors must be callable.'", Ev.Run(src).Repr("msg"));
        }
    }
}
