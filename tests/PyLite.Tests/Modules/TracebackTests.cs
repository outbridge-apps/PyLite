using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // traceback: format_exc/print_exc for the exception being handled, format_exception(_only) for an
    // exception object; the text is the host's traceback shape ("<script>", line, function).
    [Trait("Category", "Compatibility")]
    public class TracebackTests
    {
        private const string Imp = "import traceback\n";

        [Theory]
        [InlineData("def f():\n    raise ValueError('bad')\ntry:\n    f()\nexcept ValueError:\n    r = traceback.format_exc()",
            "'Traceback (most recent call last):\\n  File \"<script>\", line 5, in <module>\\n  File \"<script>\", line 3, in f\\nValueError: bad\\n'")]
        [InlineData("try:\n    {}['k']\nexcept KeyError:\n    r = traceback.format_exc()",
            "'Traceback (most recent call last):\\n  File \"<script>\", line 3, in <module>\\nKeyError: \\'k\\'\\n'")]
        [InlineData("r = traceback.format_exc()", "'NoneType: None\\n'")]
        [InlineData("try:\n    try:\n        1 / 0\n    except ZeroDivisionError as e:\n        raise ValueError('outer') from e\nexcept ValueError:\n    r = traceback.format_exc()",
            "'Traceback (most recent call last):\\n  File \"<script>\", line 4, in <module>\\nZeroDivisionError: division by zero\\n\\nThe above exception was the direct cause of the following exception:\\n\\nTraceback (most recent call last):\\n  File \"<script>\", line 6, in <module>\\nValueError: outer\\n'")]
        [InlineData("try:\n    try:\n        1 / 0\n    except ZeroDivisionError as e:\n        raise ValueError('outer') from e\nexcept ValueError:\n    r = traceback.format_exc(chain=False)",
            "'Traceback (most recent call last):\\n  File \"<script>\", line 6, in <module>\\nValueError: outer\\n'")]
        [InlineData("try:\n    raise RuntimeError('x')\nexcept RuntimeError as e:\n    err = e\nr = traceback.format_exception_only(err)", "['RuntimeError: x\\n']")]
        [InlineData("try:\n    raise RuntimeError('x')\nexcept RuntimeError as e:\n    err = e\nr = traceback.format_exception(err)",
            "['Traceback (most recent call last):\\n', '  File \"<script>\", line 3, in <module>\\n', 'RuntimeError: x\\n']")]
        [InlineData("try:\n    raise RuntimeError('x')\nexcept RuntimeError as e:\n    err = e\nr = traceback.format_exception(type(err), err, None)",
            "['Traceback (most recent call last):\\n', '  File \"<script>\", line 3, in <module>\\n', 'RuntimeError: x\\n']")]
        [InlineData("r = traceback.format_exception_only(ValueError())", "['ValueError\\n']")]
        [InlineData("e = ValueError('n')\ne.add_note('note 1')\nr = traceback.format_exception_only(e)", "['ValueError: n\\n', 'note 1\\n']")]
        [InlineData("r = ''.join(traceback.format_exception(None))", "'NoneType: None\\n'")]
        public void Formats(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(Imp + body + "\n").Repr("r"));
        }

        [Fact]
        public void PrintExcWritesToTheSink()
        {
            var r = Ev.Run(Imp + "try:\n    raise ValueError('p')\nexcept ValueError:\n    traceback.print_exc()\nprint('after')\n");
            Assert.Equal("Traceback (most recent call last):\n  File \"<script>\", line 3, in <module>\nValueError: p\nafter\n", r.PrintOutput);
        }

        // the error-reporting idiom: a message plus the type name, without any host plumbing
        [Fact]
        public void FormatExcInsideAHandlerFunction()
        {
            var r = Ev.Run(Imp + "def report():\n    return traceback.format_exc().strip().split('\\n')[-1]\n"
                + "def work(x):\n    return 1 / x\ntry:\n    work(0)\nexcept ZeroDivisionError:\n    r = report()\n");
            Assert.Equal("'ZeroDivisionError: division by zero'", r.Repr("r"));
        }

        [Theory]
        [InlineData("traceback.format_exception(1)", "TypeError")]
        [InlineData("traceback.format_exception_only()", "TypeError")]
        [InlineData("traceback.format_exc(1, True, 3)", "TypeError")]
        public void Errors(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }
    }
}
