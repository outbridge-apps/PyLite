using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Xunit;

namespace Outbridge.PyLite.Tests.Infra
{
    // docs/modules.md is read off a running engine, never written by hand. In a tree that has the file (the
    // public one) it must be what the engine reports now; PYLITE_WRITE_DOCS=1 rewrites it from the engine.
    [Trait("Category", "Regression")]
    public class ModulesDocTests
    {
        // The README's order. Every module the engine registers by default is here exactly once.
        private static readonly string[] Modules =
        {
            "math", "decimal", "fractions", "statistics", "random", "secrets", "json", "re", "datetime", "time",
            "calendar", "zoneinfo", "collections", "itertools", "functools", "operator", "copy", "copyreg",
            "dataclasses", "typing", "enum", "contextlib", "string", "textwrap", "unicodedata", "html", "base64",
            "binascii", "hashlib", "hmac", "struct", "zlib", "gzip", "zipfile", "csv", "io", "uuid", "bisect",
            "heapq", "urllib.parse", "email.utils", "xml.etree.ElementTree", "traceback",
        };

        // CPython 3.12's builtins module, without the exceptions and without what site adds (help, exit, ...).
        private static readonly string[] CPythonBuiltins =
        {
            "abs", "aiter", "all", "anext", "any", "ascii", "bin", "bool", "breakpoint", "bytearray", "bytes",
            "callable", "chr", "classmethod", "compile", "complex", "delattr", "dict", "dir", "divmod", "enumerate",
            "eval", "exec", "filter", "float", "format", "frozenset", "getattr", "globals", "hasattr", "hash", "hex",
            "id", "input", "int", "isinstance", "issubclass", "iter", "len", "list", "locals", "map", "max",
            "memoryview", "min", "next", "object", "oct", "open", "ord", "pow", "print", "property", "range",
            "repr", "reversed", "round", "set", "setattr", "slice", "sorted", "staticmethod", "str", "sum", "super",
            "tuple", "type", "vars", "zip", "__import__",
        };

        private static readonly string[] CPythonExceptions =
        {
            "BaseException", "BaseExceptionGroup", "GeneratorExit", "KeyboardInterrupt", "SystemExit", "Exception",
            "ArithmeticError", "FloatingPointError", "OverflowError", "ZeroDivisionError", "AssertionError",
            "AttributeError", "BufferError", "EOFError", "ExceptionGroup", "ImportError", "ModuleNotFoundError",
            "LookupError", "IndexError", "KeyError", "MemoryError", "NameError", "UnboundLocalError", "OSError",
            "BlockingIOError", "ChildProcessError", "ConnectionError", "BrokenPipeError", "ConnectionAbortedError",
            "ConnectionRefusedError", "ConnectionResetError", "FileExistsError", "FileNotFoundError",
            "InterruptedError", "IsADirectoryError", "NotADirectoryError", "PermissionError", "ProcessLookupError",
            "TimeoutError", "ReferenceError", "RuntimeError", "NotImplementedError", "RecursionError",
            "StopAsyncIteration", "StopIteration", "SyntaxError", "IndentationError", "TabError", "SystemError",
            "TypeError", "ValueError", "UnicodeError", "UnicodeDecodeError", "UnicodeEncodeError",
            "UnicodeTranslateError", "Warning", "BytesWarning", "DeprecationWarning", "EncodingWarning",
            "FutureWarning", "ImportWarning", "PendingDeprecationWarning", "ResourceWarning", "RuntimeWarning",
            "SyntaxWarning", "UnicodeWarning", "UserWarning",
        };

        private static readonly string[] Types = { "str", "bytes", "list", "dict", "set", "tuple", "int", "float" };

        [Fact]
        public void EveryDefaultModuleIsListed()
        {
            var registry = new ModuleRegistry();
            DefaultModules.RegisterBuiltins(registry);
            Assert.Equal(registry.Names.OrderBy(n => n, StringComparer.Ordinal),
                Modules.OrderBy(n => n, StringComparer.Ordinal));
        }

        [Fact]
        public void ModulesMdIsWhatTheEngineReports()
        {
            string text = Generate();
            string path = Path.Combine(RepoRoot(), "docs", "modules.md");
            if (System.Environment.GetEnvironmentVariable("PYLITE_WRITE_DOCS") == "1")
                File.WriteAllText(path, text, new UTF8Encoding(false));
            else if (File.Exists(path))
                Assert.True(text == File.ReadAllText(path).Replace("\r\n", "\n"),
                    "docs/modules.md differs from the engine; rerun this test with PYLITE_WRITE_DOCS=1 and commit the file");
        }

        private static string Generate()
        {
            var src = new StringBuilder();
            foreach (string name in CPythonBuiltins.Concat(CPythonExceptions))
                src.Append("try:\n    ").Append(name).Append("\n    print('B ").Append(name)
                    .Append("')\nexcept NameError as e:\n    print('N ").Append(name).Append(" ' + str(e))\n");
            foreach (string t in Types)
                src.Append("print('T ").Append(t).Append(" ' + ' '.join(n for n in dir(").Append(t)
                    .Append(") if not n.startswith('_')))\n");
            foreach (string m in Modules)
                src.Append("import ").Append(m).Append(" as m\nprint('M ").Append(m)
                    .Append(" ' + ' '.join(n for n in dir(m) if not n.startswith('_') or n.startswith('__')))\n");
            RunResult r = new ScriptEngine(new EngineOptions()).Run(src.ToString(), new RunRequest());
            Assert.True(r.Succeeded, r.Succeeded ? null : r.Error.Message);

            var present = new HashSet<string>(StringComparer.Ordinal);
            var unsupported = new List<string>();
            var lines = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in r.PrintOutput.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0))
            {
                string kind = line.Substring(0, 1), rest = line.Substring(2);
                int sp = rest.IndexOf(' ');
                string name = sp < 0 ? rest : rest.Substring(0, sp), tail = sp < 0 ? "" : rest.Substring(sp + 1);
                if (kind == "B")
                    present.Add(name);
                else if (kind == "N" && tail.Contains("not supported in this environment"))
                    unsupported.Add(name);
                else if (kind == "T" || kind == "M")
                    lines[kind + name] = tail;
            }

            var sb = new StringBuilder();
            sb.Append("# What is implemented\n\n");
            sb.Append("The surface below was read off a running engine at version ")
                .Append(typeof(ScriptEngine).Assembly.GetName().Version.ToString(3)).Append(" — every name in it is what\n");
            sb.Append("`dir()` returns for that module, private `_names` left out — so it says what exists, not what was\n");
            sb.Append("intended. It does not say how each name behaves; where behaviour differs from CPython, that\n");
            sb.Append("belongs in `deviations.md`. `tests/PyLite.Tests/Infra/ModulesDocTests.cs` writes this file and\n");
            sb.Append("fails when it falls behind the engine.\n\n");
            sb.Append("## Builtins\n\n");
            sb.Append(Ticks(CPythonBuiltins.Where(present.Contains))).Append("\n\n");
            sb.Append("Absent by design. Using one raises `NameError: name 'eval' is not defined: this builtin is not\n");
            sb.Append("supported in this environment`:\n\n");
            sb.Append(Ticks(unsupported.Where(n => CPythonBuiltins.Contains(n)))).Append("\n\n");
            sb.Append("Not defined at all (a plain `NameError`): ")
                .Append(Ticks(CPythonBuiltins.Where(n => !present.Contains(n) && !unsupported.Contains(n)))).Append(".\n\n");
            sb.Append("Exception classes: ").Append(Ticks(CPythonExceptions.Where(present.Contains))).Append(".\n\n");
            sb.Append("`import` reaches the modules listed below and the modules the host registers, nothing else.\n\n");
            sb.Append("## Methods on the built-in types\n\n");
            foreach (string t in Types)
                sb.Append("**").Append(t).Append("** — ").Append(Ticks(lines["T" + t].Split(' '))).Append("\n\n");
            sb.Append("## Modules\n");
            foreach (string m in Modules)
                sb.Append("\n### ").Append(m).Append("\n\n").Append(Ticks(lines["M" + m].Split(' '))).Append("\n");
            return sb.ToString();
        }

        private static string Ticks(IEnumerable<string> names)
        {
            return string.Join(" ", names.Where(n => n.Length > 0).OrderBy(n => n, StringComparer.Ordinal).Select(n => "`" + n + "`"));
        }

        private static string RepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "PyLite.sln")))
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            Assert.NotNull(dir);
            return dir;
        }
    }
}
