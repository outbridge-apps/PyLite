# Testing PyLite

PyLite is tested by five suites, all in this repository and all run locally. There is no test run on every
pull request (the build workflow compiles everything, warnings as errors); instead every release carries
the report of the full gate run on its own commit, [`docs/test-report.md`](docs/test-report.md), written by
[`tools/run-gates.ps1`](tools/run-gates.ps1). Anyone can repeat it.

You need Windows, the .NET SDK 8 or later and .NET Framework 4.7.2 or later (the engine targets net472).
xUnit and IronPython 3.4.2 come from NuGet.

## The suites

| suite | where | size | what it shows |
|---|---|---|---|
| unit and behaviour tests (xUnit) | `tests/PyLite.Tests` | 7,007 tests | the language, the values, the builtins and the modules behave as Python's; the host API, the sandbox and every resource limit hold; defects found earlier stay fixed |
| conformance | `conformance/` | 174 cases | CPython's own tests (`Lib/test`), adapted case by case, module by module |
| differential | `tools/PyLite.DiffHarness`, `tools/diff-corpus` | 87 scripts | the same script gives the same result under PyLite and under IronPython 3.4.2 |
| adversarial | `tools/PyLite.Stress` | 130 probes | hostile one-operation scripts (endless loops, recursion, huge numbers, regex backtracking, memory, deep nesting, zip bombs, the limits audit): each must end on a budget, never hang the host or crash it |
| benchmarks | `tools/PyLite.PerfBench`, `tools/perf-baseline.json` | 14 corpus benchmarks, 165 matrix rows | speed against IronPython 3.4.2, and a ×1.5 regression gate against the baseline |

`tools/PyLite.UnicodeGen` is not a test: it regenerates the Unicode tables in `src` from the Unicode
Character Database, so that generated code can be reproduced.

## Running them

From the repository root:

```
dotnet test tests/PyLite.Tests -c Release --filter "Category!=MachineSensitive"
dotnet test tests/PyLite.Tests -c Release --filter "Category=MachineSensitive"
cd conformance/PyLite.Conformance && dotnet run -c Release && cd ../..
dotnet run --project tools/PyLite.DiffHarness -c Release -- --corpus tools/diff-corpus --fail-on-bug
cd tools/PyLite.Stress && dotnet run -c Release -- bombs && cd ../..
dotnet run --project tools/PyLite.PerfBench -c Release -- --full
```

or all of them at once, with the report: `powershell -File tools/run-gates.ps1`.

The first command is the everyday one (about 1.5 minutes). The second runs the tests that measure time or
heap growth; give it a quiet machine (see below). `--filter Category=Sandbox` and the like run one category.

## Categories

Every test class carries exactly one of five categories as an xUnit trait (`Infra/TestCategoryTests` fails
on a class that does not):

| category | tests | what it covers |
|---|---:|---|
| `Compatibility` | 5,775 | syntax, evaluation, values, builtins, modules, the spec files: the dialect behaves as Python |
| `Sandbox` | 476 | the host boundary: the hosting API, engine reuse, the concurrency gate, runner threads and a leaked thread, the error contract, the host function table; nothing of the host reachable from a script, by any of the classic introspection escapes |
| `ResourceLimits` | 321 | steps, deadline, memory, call and data depth, sizes, output, regex time, sleep, with one script per limit in [`examples/limits`](examples/limits); the stack guards, the compile-time limits, the big-int helper that keeps a long operation inside the deadline |
| `Regression` | 418 | defects found by reviews and fuzzing, kept fixed (arguments that once reached the host as engine faults, module boundary cases, wrong XML answers); fast paths that must change no result; the public API surface, so it cannot drift unnoticed |
| `Fuzz` | 17 | seeded grammar, lexer and parser fuzzers: no foreign exception, no hang |

`MachineSensitive` comes on top of a category for 175 tests in seven classes that compare wall-clock time
or heap growth: a deadline that must fire within a margin, the memory an aborted run gives back, catalog
stress runs. They are right on a quiet machine and can fail on a busy or slow one, so the everyday command
leaves them out and they run on their own. Nothing is skipped and no test is hidden; the release report
runs both.

## The reference interpreter

The differential suite compares with IronPython 3.4.2 from NuGet, not with CPython: everything stays on .NET
and anyone can repeat it. IronPython follows Python 3.4, so what came later (`match`, `:=`, f-string `=`,
PEP 515 underscores in `Decimal` and `Fraction`, ...) is covered by the conformance cases and the xUnit
tests only. Known differences are listed with their reason in
`tools/PyLite.DiffHarness/known-differences.txt`.

The corpus has a folder per module (`re/`, `datetime/`, `collections/`, `csv/`, `xml/`, ...); error paths are
compared by exception class inside the script (`type(e).__name__` goes into `result`). Where IronPython
3.4.2 answers as Python 3.4 did, or as no CPython ever did, and PyLite follows current CPython, the case
sits alone in `iron34/` with a header saying which side is which, so a known difference never hides
anything else. A float that IronPython prints with 17 digits (`3.7000000000000002`) is compared by value.

## What stays internal

- a scan of proprietary client scripts that guided which modules and methods PyLite carries (its report is
  statistics about client code);
- a benchmark of a host migration that mirrors an internal integration;
- the planning and decision documents.

Comments in the tests carry internal requirement and plan numbers (`T-TST-11`, `impl/06`, `RFC-001`, `B13`,
...). They trace a test to the decision that asked for it; the documents they name are not published.

## Adding a test

Put it beside the tests of the same area and give its class one category (plus `MachineSensitive` when it
measures time or heap growth). An adversarial probe goes into `tools/PyLite.Stress/gen_bombs.sh` (its README
says how), a differential script into `tools/diff-corpus`.
