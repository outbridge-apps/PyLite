using System;
using System.Collections.Generic;
using System.Text;

namespace Outbridge.PyLite.Tests.Fuzz
{
    // T-TST-08: a generator of random syntactically-plausible PyLite programs. Goal = path coverage with
    // inputs no one writes by hand, not validity. Deterministic by seed (R-12). Depth is bounded BEFORE
    // descent so the GENERATOR itself never overflows the C# stack.
    internal sealed class FuzzConfig
    {
        public int Seed;
        public int MaxDepth = 5;
        public int MaxStatements = 25;
        public int MaxStringLen = 24;
        public double PPreferValidNames = 0.8;
        public bool AllowForbidden = false;   // occasionally emit class/yield/with -> a clear-SyntaxError check
    }

    internal sealed class GrammarFuzzer
    {
        private static readonly string[] Modules =
            { "math", "json", "re", "datetime", "itertools", "collections", "random", "functools", "statistics" };
        private static readonly string[] Builtins =
            { "len", "range", "sum", "min", "max", "abs", "sorted", "str", "int", "float", "list", "dict", "print" };
        private static readonly string[] BinOps = { "+", "-", "*", "//", "%", "**", "<<", ">>", "&", "|", "^" };
        private static readonly string[] CmpOps = { "<", "<=", ">", ">=", "==", "!=" };

        private readonly Random _rng;
        private readonly FuzzConfig _cfg;

        internal GrammarFuzzer(FuzzConfig cfg)
        {
            _cfg = cfg;
            _rng = new Random(cfg.Seed);
        }

        // A fresh scope tracks "declared" names so references look plausible.
        private sealed class Scope
        {
            public readonly List<string> Names = new List<string>();
            private int _counter;
            public string Fresh() { string n = "v" + _counter++; Names.Add(n); return n; }
        }

        internal string GenModule()
        {
            var scope = new Scope();
            var sb = new StringBuilder();
            int n = 1 + _rng.Next(_cfg.MaxStatements);
            for (int i = 0; i < n; i++)
                sb.Append(GenStmt(scope, 0, 0));
            return sb.ToString();
        }

        private string GenStmt(Scope scope, int depth, int indent)
        {
            string pad = new string(' ', indent * 4);
            if (depth >= _cfg.MaxDepth)
                return pad + GenSimpleStmt(scope, depth) + "\n";

            if (_cfg.AllowForbidden && _rng.NextDouble() < 0.05)
                return pad + Pick("yield 1", "with x:", "class C:", "async def f():") + "\n";

            switch (_rng.Next(12))
            {
                case 0:
                case 1:
                    return pad + scope.Fresh() + " = " + GenExpr(scope, depth + 1) + "\n";
                case 2:
                    return pad + scope.Fresh() + " " + Pick("+=", "-=", "*=", "//=") + " " + GenExpr(scope, depth + 1) + "\n";
                case 3:
                    return pad + "if " + GenExpr(scope, depth + 1) + ":\n" + GenBlock(scope, depth + 1, indent + 1)
                        + (_rng.NextDouble() < 0.4 ? pad + "else:\n" + GenBlock(scope, depth + 1, indent + 1) : "");
                case 4:
                    return pad + "while " + GenExpr(scope, depth + 1) + ":\n"
                        + new string(' ', (indent + 1) * 4) + "break\n";   // guarded: no accidental infinite loop
                case 5:
                    return pad + "for " + scope.Fresh() + " in " + GenIterable(scope, depth + 1) + ":\n"
                        + GenBlock(scope, depth + 1, indent + 1);
                case 6:
                    return GenFuncDef(scope, depth, indent);
                case 7:
                    return pad + "try:\n" + GenBlock(scope, depth + 1, indent + 1)
                        + pad + "except " + Pick("Exception", "ValueError", "KeyError", "") + ":\n"
                        + new string(' ', (indent + 1) * 4) + "pass\n";
                case 8:
                    return pad + "import " + Pick(Modules) + "\n";
                case 9:
                    return pad + "assert " + GenExpr(scope, depth + 1) + "\n";
                default:
                    return pad + GenSimpleStmt(scope, depth) + "\n";
            }
        }

        private string GenFuncDef(Scope scope, int depth, int indent)
        {
            string pad = new string(' ', indent * 4);
            string name = "f" + _rng.Next(1000);
            var inner = new Scope();
            int paramCount = _rng.Next(3);
            var ps = new List<string>();
            for (int i = 0; i < paramCount; i++)
                ps.Add(inner.Fresh());
            scope.Names.Add(name);
            return pad + "def " + name + "(" + string.Join(", ", ps) + "):\n"
                + GenBlock(inner, depth + 1, indent + 1)
                + new string(' ', (indent + 1) * 4) + "return " + GenExpr(inner, depth + 1) + "\n";
        }

        private string GenBlock(Scope scope, int depth, int indent)
        {
            int n = 1 + _rng.Next(2);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
                sb.Append(GenStmt(scope, depth, indent));
            return sb.ToString();
        }

        private string GenSimpleStmt(Scope scope, int depth)
        {
            switch (_rng.Next(5))
            {
                case 0: return "pass";
                case 1: return scope.Fresh() + " = " + GenAtom(scope);
                case 2: return "print(" + GenAtom(scope) + ")";
                case 3: return GenAtom(scope);
                default: return scope.Names.Count > 0 ? "del " + Ref(scope) : "pass";
            }
        }

        private string GenExpr(Scope scope, int depth)
        {
            if (depth >= _cfg.MaxDepth || _rng.NextDouble() < 0.35)
                return GenAtom(scope);
            switch (_rng.Next(10))
            {
                case 0:
                case 1:
                    return GenExpr(scope, depth + 1) + " " + Pick(BinOps) + " " + GenExpr(scope, depth + 1);
                case 2:
                    return GenExpr(scope, depth + 1) + " " + Pick(CmpOps) + " " + GenExpr(scope, depth + 1);
                case 3:
                    return Pick(Builtins) + "(" + GenArgs(scope, depth) + ")";
                case 4:
                    return "[" + GenArgs(scope, depth) + "]";
                case 5:
                    return "{" + GenAtom(scope) + ": " + GenExpr(scope, depth + 1) + "}";
                case 6:
                    return "(" + GenExpr(scope, depth + 1) + " if " + GenExpr(scope, depth + 1) + " else " + GenExpr(scope, depth + 1) + ")";
                case 7:
                    return "[x for x in " + GenIterable(scope, depth + 1) + (_rng.NextDouble() < 0.5 ? " if x" : "") + "]";
                case 8:
                    return "lambda " + Pick("", "a", "a, b") + ": " + GenAtom(scope);
                default:
                    return Pick("not ", "-", "~") + GenAtom(scope);
            }
        }

        private string GenArgs(Scope scope, int depth)
        {
            int n = _rng.Next(4);
            var parts = new List<string>();
            for (int i = 0; i < n; i++)
                parts.Add(GenExpr(scope, depth + 1));
            return string.Join(", ", parts);
        }

        private string GenIterable(Scope scope, int depth)
        {
            switch (_rng.Next(4))
            {
                case 0: return "range(" + _rng.Next(20) + ")";
                case 1: return "[" + GenArgs(scope, depth) + "]";
                case 2: return "'" + RandString() + "'";
                default: return Ref(scope);
            }
        }

        private string GenAtom(Scope scope)
        {
            switch (_rng.Next(8))
            {
                case 0: return _rng.Next(1000).ToString(System.Globalization.CultureInfo.InvariantCulture);
                case 1: return Pick("0", "-1", "10**9", "2**63", "0.5", "1e300", "True", "False", "None");
                case 2: return "'" + RandString() + "'";
                case 3: return "f'{" + Ref(scope) + "}'";
                case 4: return Ref(scope);
                case 5: return "[" + _rng.Next(9) + ", " + _rng.Next(9) + "]";
                case 6: return "(" + _rng.Next(9) + ",)";
                default: return Ref(scope);
            }
        }

        private string Ref(Scope scope)
        {
            if (scope.Names.Count > 0 && _rng.NextDouble() < _cfg.PPreferValidNames)
                return scope.Names[_rng.Next(scope.Names.Count)];
            return Pick(Builtins);   // an existing builtin is a safe fallback reference
        }

        private string RandString()
        {
            int len = _rng.Next(_cfg.MaxStringLen);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
            {
                char c = (char)('a' + _rng.Next(26));
                if (_rng.NextDouble() < 0.1)
                    c = Pick("\\n", "\\t", " ", "0")[0];
                sb.Append(c);
            }
            return sb.ToString();
        }

        private string Pick(params string[] options) { return options[_rng.Next(options.Length)]; }
    }
}
