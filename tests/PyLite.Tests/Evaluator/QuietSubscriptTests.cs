using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    // A subscript that is a statement's whole value (`v = d[k]`, `d[k]`) hands a KeyError or IndexError back as a
    // Raised signal instead of a .NET throw (review 3, PF6). Each script runs twice: with @ as that statement, and
    // with @ as the same subscript inside a conditional expression, which takes the throwing path. Everything the
    // script and the host can see must be the same: the output, the traceback text, the error and its line.
    [Trait("Category", "Regression")]
    public class QuietSubscriptTests
    {
        private static string Outcome(string src)
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run(src, new RunRequest());
            return r.PrintOutput + (r.Succeeded ? "" : "\nERROR " + r.Error.PythonType + ": " + r.Error.Message
                + " @" + r.Error.Line + "\n" + r.Error.ScriptTraceback);
        }

        [Theory]
        [InlineData("import traceback\nd = {}\ntry:\n    v = @\nexcept KeyError as e:\n    print(repr(e), e.args, traceback.format_exc())\n", "d['k']")]
        [InlineData("import traceback\nd = {}\ntry:\n    @\nexcept KeyError:\n    print(traceback.format_exc())\n", "d['k']")]
        [InlineData("d = {}\nx = 1\nv = @\n", "d[x]")]
        [InlineData("import traceback\ndef f(d):\n    v = @\n    return v\ntry:\n    f({})\nexcept KeyError:\n    print(traceback.format_exc())\n", "d['a']")]
        [InlineData("def f(d):\n    v = @\n    return v\ndef g():\n    return f({})\ng()\n", "d['a']")]
        [InlineData("try:\n    1 / 0\nexcept ZeroDivisionError:\n    v = @\n", "{}[0]")]
        [InlineData("try:\n    v = @\nexcept KeyError as e:\n    raise ValueError('wrapped') from e\n", "{}[0]")]
        [InlineData("class C:\n    def __enter__(self):\n        return self\n    def __exit__(self, t, v, tb):\n        print('exit', t.__name__, v.args)\n        return True\nwith C():\n    v = @\nprint('after')\n", "{}['z']")]
        [InlineData("try:\n    try:\n        v = @\n    finally:\n        print('finally')\nexcept LookupError as e:\n    print(type(e).__name__)\n", "[1, 2][5]")]
        [InlineData("for i in range(3):\n    try:\n        v = @\n        print(v)\n    except IndexError as e:\n        print(i, e)\n", "(10, 20)[i - 1]")]
        [InlineData("l = [1, 2, 3]\nprint(@)\nv = @\nprint(v)\n", "l[-1]")]
        [InlineData("l = [1, 2, 3]\nv = @\n", "l[-4]")]
        [InlineData("l = [1, 2, 3]\nv = @\nprint(v)\n", "l[True]")]
        [InlineData("l = [1, 2, 3]\nv = @\nprint(v)\n", "l[1:]")]
        [InlineData("l = [1, 2, 3]\nv = @\n", "l['a']")]
        [InlineData("t = (1,)\nv = @\n", "t[2**70]")]
        [InlineData("from collections import defaultdict, Counter\nd = defaultdict(list)\nv = @\nprint(dict(d), Counter()['x'])\n", "d['k']")]
        [InlineData("from collections import Counter\nc = Counter()\nv = @\nprint(v)\n", "c['x']")]
        [InlineData("d = {}\nv = @\n", "d[[1]]")]
        [InlineData("class R:\n    def __init__(self):\n        self.d = {}\n    def get(self, k):\n        try:\n            v = @\n        except KeyError:\n            v = None\n        return v\nprint(R().get('x'))\n", "self.d[k]")]
        public void TheStatementAnswersAsTheExpression(string template, string subscript)
        {
            string quiet = template.Replace("@", subscript);
            string thrown = template.Replace("@", "(" + subscript + " if 1 else 0)");
            Assert.Equal(Outcome(thrown), Outcome(quiet));
        }

        // The texts are CPython's.
        [Theory]
        [InlineData("d = {}\nv = d['k']\n", "KeyError", "'k'")]
        [InlineData("v = [1][3]\n", "IndexError", "list index out of range")]
        [InlineData("v = (1,)[3]\n", "IndexError", "tuple index out of range")]
        public void TheTextsAreCPythons(string src, string type, string message)
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run(src, new RunRequest());
            Assert.Equal(type, r.Error.PythonType);
            Assert.Equal(message, r.Error.Message);
        }
    }
}
