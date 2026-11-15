# PyLite

[![build](https://github.com/outbridge-apps/PyLite/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/outbridge-apps/PyLite/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/PyLite.svg)](https://www.nuget.org/packages/PyLite)

An embeddable interpreter of a Python dialect for .NET Framework hosts, written in C#.

PyLite runs scripts written in a modern-syntax Python dialect (f-strings, `match`/`case`, the
walrus operator, decorators, `@dataclass`, `with`, `super()`, `enum`) inside a .NET Framework 4.7.2
process, with no native code, no AppDomain and no access to the file system, the network or
reflection. Every run is bounded by resource limits the host sets: steps, wall-clock deadline,
allocated memory, call and data depth, string, collection and integer size, output, regex time and
total sleep; the host bounds how many runs go at once (see [SECURITY.md](https://github.com/outbridge-apps/PyLite/blob/main/SECURITY.md)).
When a limit is crossed the run stops with a typed error and the host keeps its process.

```
dotnet add package PyLite
```

It was built to replace IronPython inside a large business application, where scripts written by
users and integrators run on a shared server and must not be able to hang it, exhaust it or reach
outside their sandbox.

The code was written by Anthropic's Claude models (Opus and Fable) under the direction and review
of a human engineer.

## What it is not

PyLite is a dialect, not CPython. It targets the same public API and behaviour of the language and
of the standard-library modules it ships, but not byte-exact parity. The following are excluded by
design:

- generators (`yield`), `async`/`await`;
- the full class machinery: classes are record classes with single inheritance and a fixed set of
  protocol methods (`__init__`, `__eq__`, `__repr__`, `__iter__`, `__enter__`/`__exit__`, ...;
  see [docs/deviations.md](https://github.com/outbridge-apps/PyLite/blob/main/docs/deviations.md));
- `import` of arbitrary modules: only the modules the engine ships and the modules the host
  registers are importable;
- file, network and process access.

## Modules

`math`, `decimal`, `fractions`, `statistics`, `random`, `secrets`, `json`, `re`, `datetime`, `time`,
`calendar`, `zoneinfo`, `collections`, `itertools`, `functools`, `operator`, `copy`, `copyreg`,
`dataclasses`, `typing`, `enum`, `contextlib`, `string`, `textwrap`, `unicodedata`, `html`, `base64`,
`binascii`, `hashlib`, `hmac`, `struct`, `zlib`, `gzip`, `zipfile`, `csv`, `io`, `uuid`, `bisect`,
`heapq`, `urllib.parse`, `email.utils`, `xml.etree.ElementTree`, `traceback`.

`io` has `StringIO` and `BytesIO`, `zlib`/`gzip` are one-shot, `zipfile` reads and writes archives in
a `BytesIO`, and `email.utils` has the date helpers only; [docs/modules.md](https://github.com/outbridge-apps/PyLite/blob/main/docs/modules.md) lists
every name each module exposes.

## Documentation

- [docs/HOSTING-GUIDE.md](https://github.com/outbridge-apps/PyLite/blob/main/docs/HOSTING-GUIDE.md) — embedding the engine from C#: running scripts, data in and
  out, host functions and modules, resource limits, errors, concurrency and memory.
- [docs/modules.md](https://github.com/outbridge-apps/PyLite/blob/main/docs/modules.md) — every builtin, type method and module member that exists,
  read off a running engine rather than written by hand.
- [docs/deviations.md](https://github.com/outbridge-apps/PyLite/blob/main/docs/deviations.md) — where the dialect knowingly behaves differently from
  CPython: UTF-16 strings, the range of `decimal`, record classes, and what a resource limit does
  to your `except`.
- [docs/performance.md](https://github.com/outbridge-apps/PyLite/blob/main/docs/performance.md) — measured against IronPython 3.4.2: money in
  `decimal` ×66–78 faster, JSON ×15–18 faster, a new engine in a warm process ×166–228 faster,
  datetime arithmetic ×1.06–1.18 slower.

## Using it from C#

```csharp
using Outbridge.PyLite.Hosting;

var engine = new ScriptEngine(new EngineOptions());   // build once, share across runs

var request = new RunRequest
{
    ScalarsIn = new Dictionary<string, string> { { "qty", "12" }, { "price", "9.5" } },
    ScalarOutputNames = new[] { "total" },
    Limits = ResourceLimits.Default.With(maxSteps: 1_000_000, deadlineMs: 2000),
};

RunResult result = engine.Run("total = round(qty * price, 2)", request);

if (result.Succeeded)
    Console.WriteLine(result.ScalarsOut["total"]);    // 114.0
else
    Console.WriteLine(result.Error.Kind + ": " + result.Error.Message);
```

Inputs arrive as strings and are typed by their text (`"12"` is an `int`, `"9.5"` a `float`,
`"true"` a `bool`, anything else a `str`); JSON inputs and outputs are available alongside. The host
can register its own functions and modules, replace `print`, and compile a script once to run it
many times; the [hosting guide](https://github.com/outbridge-apps/PyLite/blob/main/docs/HOSTING-GUIDE.md) shows each.

## Building

```
dotnet build src/PyLite/PyLite.csproj -c Release
```

The engine targets .NET Framework 4.7.2 and depends on Newtonsoft.Json 13.0.3 only.

## Tests

All of them are in this repository, and [TESTING.md](https://github.com/outbridge-apps/PyLite/blob/main/TESTING.md) says what each suite proves and how
to run it:

- `tests/PyLite.Tests`: about 8,600 xUnit tests in five categories (Compatibility, Sandbox,
  ResourceLimits, Regression, Fuzz);
- `conformance/`: cases adapted from CPython's `Lib/test`, one script per area;
- `tools/PyLite.DiffHarness`: scripts run on PyLite and on IronPython 3.4.2, with the results
  compared;
- `tools/PyLite.Stress`: adversarial probes, each of which must end on a limit or a Python
  exception, and the probe bank: about 1,200 probes the pre-release reviewers wrote, each pinned to
  its outcome, output, steps and charged memory.

`tools/run-gates.ps1` runs them all and writes [docs/test-report.md](https://github.com/outbridge-apps/PyLite/blob/main/docs/test-report.md); each
release commits the report of its own run. [SECURITY.md](https://github.com/outbridge-apps/PyLite/blob/main/SECURITY.md) maps every sandbox guarantee
to the tests that hold it, [examples/limits](https://github.com/outbridge-apps/PyLite/tree/main/examples/limits) shows each resource limit firing, and
[docs/fixes.md](https://github.com/outbridge-apps/PyLite/blob/main/docs/fixes.md) lists every defect fixed since 1.0.0 with the test that keeps it fixed.
The performance figures in [docs/performance.md](https://github.com/outbridge-apps/PyLite/blob/main/docs/performance.md) come from
`tools/PyLite.PerfBench`; [benchmarks/](https://github.com/outbridge-apps/PyLite/tree/main/benchmarks) runs it pass after pass and keeps the samples.

## Status

Version 1.2.0: what changed is in the
[release notes](https://github.com/outbridge-apps/PyLite/releases/tag/v1.2.0).
