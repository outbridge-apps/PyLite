# PyLite

[![build](https://github.com/outbridge-apps/PyLite/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/outbridge-apps/PyLite/actions/workflows/build.yml)

An embeddable interpreter of a Python dialect for .NET Framework hosts, written in C#.

PyLite runs scripts written in a modern-syntax Python dialect (f-strings, `match`/`case`, the
walrus operator, decorators, `@dataclass`) inside a .NET Framework 4.7.2 process, with no native
code, no AppDomain and no access to the file system, the network or reflection. Every run is
bounded by resource limits the host sets: steps, wall-clock deadline, allocated memory, data depth
and call depth. When a limit is crossed the run stops with a typed error and the host keeps its
process.

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
  dunder methods (`__init__`, `__eq__`, `__repr__`, and the ones `@dataclass` generates);
- `import` of arbitrary modules: only the modules the engine ships and the modules the host
  registers are importable;
- file, network and process access.

## Modules

`math`, `decimal`, `fractions`, `statistics`, `random`, `secrets`, `json`, `re`, `datetime`, `time`,
`calendar`, `zoneinfo`, `collections`, `itertools`, `functools`, `operator`, `copy`, `copyreg`,
`dataclasses`, `typing`, `string`, `textwrap`, `unicodedata`, `html`, `base64`, `binascii`,
`hashlib`, `hmac`, `struct`, `zlib`, `gzip`, `csv`, `io`, `uuid`, `bisect`, `heapq`, `urllib.parse`,
`xml.etree.ElementTree`, `traceback`.

`io` is `StringIO` only, and `zlib`/`gzip` are one-shot; [docs/modules.md](docs/modules.md) lists
every name each module exposes.

## Documentation

- [docs/modules.md](docs/modules.md) — every builtin, type method and module member that exists,
  read off a running engine rather than written by hand.
- [docs/deviations.md](docs/deviations.md) — where the dialect knowingly behaves differently from
  CPython: UTF-16 strings, the range of `decimal`, record classes, and what a resource limit does
  to your `except`.
- [docs/performance.md](docs/performance.md) — measured against IronPython 3.4.2: money in
  `decimal` ×66–91 faster, JSON ×8.5–10.1 faster, a new engine in a warm process ×73–173 faster,
  `sorted` with a `key` ×2.6–3.3 slower.

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
many times.

## Building

```
dotnet build src/PyLite/PyLite.csproj -c Release
```

The engine targets .NET Framework 4.7.2 and depends on Newtonsoft.Json 13.0.3 only.

## Status

Version 1.0.0. The engine is exercised by an internal suite of more than 6000 tests, a CPython
conformance corpus, a differential corpus against a reference interpreter and an adversarial
sandbox corpus; these live outside this repository.
