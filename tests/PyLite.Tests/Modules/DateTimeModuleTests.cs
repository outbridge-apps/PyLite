using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/09 T-MDT-12: datetime module assembly (constants, class attrs, isinstance, tzinfo).
    [Trait("Category", "Compatibility")]
    public class DateTimeModuleTests
    {
        private static string R(string body, string expr)
        {
            return Ev.ReprOf(body, expr);
        }

        [Theory]
        [InlineData("import datetime\n", "datetime.MINYEAR", "1")]
        [InlineData("import datetime\n", "datetime.MAXYEAR", "9999")]
        [InlineData("from datetime import date, timedelta\n", "(date.max - date.min) // timedelta(days=1)", "3652058")]
        [InlineData("import datetime\n", "repr(datetime.datetime.min)", "'datetime.datetime(1, 1, 1, 0, 0)'")]
        [InlineData("import datetime\n", "repr(datetime.timedelta.resolution)", "'datetime.timedelta(microseconds=1)'")]
        [InlineData("import datetime\n", "repr(datetime.timezone.utc)", "'datetime.timezone.utc'")]
        [InlineData("import datetime\n", "isinstance(datetime.timezone.utc, datetime.tzinfo)", "True")]
        [InlineData("import datetime\n", "isinstance(datetime.date(2011,1,1), datetime.date)", "True")]
        [InlineData("import datetime\n", "isinstance(datetime.timezone.utc, datetime.timezone)", "True")]
        [InlineData("import datetime\n", "repr(datetime.date(year=2011, month=1, day=2))", "'datetime.date(2011, 1, 2)'")]
        public void ModuleBehavior(string body, string expr, string expected)
        {
            Assert.Equal(expected, R(body, expr));
        }

        [Fact]
        public void TzInfoNotInstantiable()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import datetime\nx = datetime.tzinfo()\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void DateMissingArgsIsTypeError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import datetime\nx = datetime.date(2011)\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void FloatFieldMessage()
        {
            string src = "import datetime\nmsg=''\ntry:\n    datetime.datetime(2011.0, 1, 1)\nexcept TypeError as e:\n    msg = str(e)\n";
            Assert.Equal("'integer argument expected, got float'", Ev.Run(src).Repr("msg"));
        }

        [Fact]
        public void ClassMethodIsCallableValue()
        {
            Assert.Equal("True", Ev.Run("import datetime\nr = callable(datetime.date.today)\n").Repr("r"));
        }
    }
}
