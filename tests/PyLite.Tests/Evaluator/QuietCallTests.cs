using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    // A call of int, float or next that is a statement's whole value (`v = int(s)`, `t += int(s)`, `return int(s)`,
    // `next(it)`) hands a malformed literal's ValueError or an exhausted iterator's StopIteration back as a Raised
    // signal instead of a .NET throw. Each script runs twice: with @ as that call, and with @ as the same call
    // inside a conditional expression, which takes the throwing path. Everything the script and the host can see
    // must be the same: the output, the traceback text, the error and its line.
    [Trait("Category", "Regression")]
    public class QuietCallTests
    {
        private static string Outcome(string src)
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run(src, new RunRequest());
            return r.PrintOutput + (r.Succeeded ? "" : "\nERROR " + r.Error.PythonType + ": " + r.Error.Message
                + " @" + r.Error.Line + " " + r.Error.LimitName + "\n" + r.Error.ScriptTraceback);
        }

        private static void SameBothWays(string template, string call)
        {
            string quiet = template.Replace("@", call);
            string thrown = template.Replace("@", "(" + call + " if 1 else 0)");
            Assert.Equal(Outcome(thrown), Outcome(quiet));
        }

        [Theory]
        [InlineData("import traceback\ntry:\n    v = @\nexcept ValueError as e:\n    print(repr(e), e.args, traceback.format_exc())\n", "int('x1')")]
        [InlineData("s = 'x'\nv = @\n", "int(s)")]
        [InlineData("t = 0\nt += @\n", "int('1.5')")]
        [InlineData("t = 0\nfor s in ['1', 'a', '2']:\n    try:\n        t += @\n    except ValueError as e:\n        print('bad', e)\nprint(t)\n", "int(s)")]
        [InlineData("def f(s):\n    try:\n        return @\n    except ValueError as e:\n        return repr(e)\nprint(f('x'), f('5'), f(' 7 '))\n", "int(s)")]
        [InlineData("import traceback\ndef f(s):\n    v = @\n    return v\ntry:\n    f('q')\nexcept ValueError:\n    print(traceback.format_exc())\n", "int(s)")]
        [InlineData("def f(s):\n    x = 1\n    return @\ndef g():\n    return f('q')\ng()\n", "int(s)")]
        [InlineData("try:\n    1 / 0\nexcept ZeroDivisionError:\n    v = @\n", "float('?')")]
        [InlineData("try:\n    v = @\nexcept ValueError as e:\n    raise KeyError('wrapped') from e\n", "float('')")]
        [InlineData("class C:\n    def __enter__(self):\n        return self\n    def __exit__(self, t, v, tb):\n        print('exit', t.__name__, v.args)\n        return True\nwith C():\n    v = @\nprint('after')\n", "int('z')")]
        [InlineData("try:\n    try:\n        v = @\n    finally:\n        print('finally')\nexcept StopIteration as e:\n    print(type(e).__name__, e.args)\n", "next(iter(()))")]
        [InlineData("it = iter([1])\nv = @\nprint(v)\ntry:\n    @\nexcept StopIteration:\n    print('end')\n@\n", "next(it)")]
        [InlineData("v = @\nprint(v)\n", "next(iter([]), 'dflt')")]
        [InlineData("for i in range(4):\n    try:\n        v = @\n        print(v)\n    except ValueError as e:\n        print(i, e)\n", "int(str(i) + 'x' * (i % 2))")]
        [InlineData("d = {'a': 1}\ntry:\n    d['a'] += @\nexcept ValueError as e:\n    print(e)\nprint(d)\nd['b'] += @\n", "int('z')")]
        [InlineData("class C:\n    def __init__(self):\n        self.n = 1\nc = C()\ntry:\n    c.n += @\nexcept ValueError:\n    pass\nprint(c.n)\nc.n += @\n", "int('z')")]
        [InlineData("a = b = @\n", "int('0x10')")]
        [InlineData("a = b = @\nprint(a, b)\n", "int('0x10', 0)")]
        [InlineData("v = @\n", "int('12', 37)")]
        [InlineData("v = @\nprint(v)\nw = int('1G', 16)\n", "int('zz', 36)")]
        [InlineData("v = @\n", "int('7' * 5000)")]
        [InlineData("print(@, float('nan'), float('-inf'), float(b'1.5'))\nv = @\nprint(v)\nw = @\n", "float('1e400')")]
        [InlineData("v = @\n", "float('  ')")]
        [InlineData("v = @\n", "int(None)")]
        [InlineData("v = @\n", "next(5)")]
        [InlineData("v = @\n", "next(iter([]), 1, 2)")]
        [InlineData("v = @\n", "next(iter([]), default=1)")]
        [InlineData("v = @\n", "int('5', base=1)")]
        [InlineData("int = float\nv = @\nprint(v)\n", "int('1.5')")]
        [InlineData("def int(s):\n    return 'mine ' + s\nv = @\nprint(v)\n", "int('x')")]
        [InlineData("import io\nf = io.StringIO('a\\n')\nprint(repr(next(f)))\ntry:\n    v = @\nexcept StopIteration:\n    print('eof')\nv = @\n", "next(f)")]
        [InlineData("g = (1 // x for x in [1, 0])\nv = next(g)\ntry:\n    v = @\nexcept ZeroDivisionError as e:\n    print(e)\nv = @\nprint(v)\n", "next(g)")]
        [InlineData("it = iter([1, 2])\ndef cb(x):\n    y = x\n    return @\nprint(list(map(cb, range(5))))\n", "next(it)")]
        [InlineData("class It:\n    def __init__(self):\n        self.src = iter([1, 2])\n    def __iter__(self):\n        return self\n    def __next__(self):\n        v = @\n        return v * 10\nprint(list(It()))\nfor x in It():\n    print(x)\n", "next(self.src)")]
        [InlineData("class R:\n    def __init__(self):\n        self.d = {}\n    def get(self, k):\n        try:\n            v = @\n        except ValueError:\n            v = None\n        return v\nprint(R().get('x'), R().get('3'))\n", "int(k)")]
        [InlineData("def f():\n    try:\n        return @\n    finally:\n        print('cleanup')\nf()\n", "float('x')")]
        [InlineData("def f():\n    try:\n        return @\n    finally:\n        return 'finally wins'\nprint(f())\n", "int('x')")]
        [InlineData("import traceback\ndef f():\n    try:\n        v = @\n    except ValueError:\n        raise\ntry:\n    f()\nexcept ValueError:\n    print(traceback.format_exc())\n", "int('x')")]
        public void TheStatementAnswersAsTheExpression(string template, string call)
        {
            SameBothWays(template, call);
        }

        // A loop of failing calls still ends on the step limit and the deadline (the two forms count different
        // nodes, so the limit falls on a different line).
        [Theory]
        [InlineData("while True:\n    try:\n        v = int('x')\n    except ValueError:\n        pass\n")]
        [InlineData("while True:\n    try:\n        next(iter(()))\n    except StopIteration:\n        pass\n")]
        public void ALoopOfFailuresStillHitsTheStepLimit(string src)
        {
            var request = new RunRequest { Limits = ResourceLimits.Default.With(maxSteps: 200_000) };
            RunResult r = new ScriptEngine(new EngineOptions()).Run(src, request);
            Assert.Equal("MaxSteps", r.Error.LimitName);
        }

        [Fact]
        public void ALoopOfFailuresStillHitsTheDeadline()
        {
            var request = new RunRequest { Limits = ResourceLimits.Default.With(deadlineMs: 300) };
            RunResult r = new ScriptEngine(new EngineOptions()).Run(
                "while True:\n    try:\n        v = int('x')\n    except ValueError:\n        pass\n", request);
            Assert.Equal("DeadlineMs", r.Error.LimitName);
        }

        // Only a call by one of HasQuietTwin's names looks for a twin: the names and the builtins registered with
        // one must agree.
        [Fact]
        public void TheQuietNamesAreTheBuiltinsWithATwin()
        {
            foreach (var kv in Outbridge.PyLite.Modules.BuiltinsTable.CreateCopy())
            {
                var fn = kv.Value as Outbridge.PyLite.Runtime.Values.BuiltinFunctionValue;
                var type = kv.Value as Outbridge.PyLite.Runtime.Values.TypeValue;
                bool hasTwin = (fn != null && fn.Quiet != null) || (type != null && type.Quiet != null);
                Assert.True(hasTwin == Outbridge.PyLite.Modules.Builtins.HasQuietTwin(kv.Key), kv.Key);
            }
        }

        // A host's own int is called as the host wrote it, never through the engine's quiet twin.
        [Fact]
        public void AHostBuiltinIsCalledAsItself()
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            t.Set("int", (self, args, kw, c) => { throw new System.InvalidOperationException("host int"); });
            RunResult r = new ScriptEngine(new EngineOptions()).Run(
                "try:\n    v = int('x')\nexcept ValueError:\n    pass\n", new RunRequest { Builtins = t });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
        }

        // The texts are CPython's.
        [Theory]
        [InlineData("v = int('x1')\n", "ValueError", "invalid literal for int() with base 10: 'x1'")]
        [InlineData("v = int('12', 1)\n", "ValueError", "int() base must be >= 2 and <= 36, or 0")]
        [InlineData("v = float('1.5x')\n", "ValueError", "could not convert string to float: '1.5x'")]
        [InlineData("v = next(iter([]))\n", "StopIteration", "")]
        public void TheTextsAreCPythons(string src, string type, string message)
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run(src, new RunRequest());
            Assert.Equal(type, r.Error.PythonType);
            Assert.Equal(message, r.Error.Message);
        }
    }
}
