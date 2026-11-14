# Gates

What checks the engine, what runs each check, and what nothing runs yet. This repository holds no pipeline
definitions: the gates run when someone runs `tools/run-gates.ps1`, which writes `docs/test-report.md`, and a
release is cut only from a commit whose report passes every gate (see `TESTING.md`).

## `tools/run-gates.ps1`

| gate | what it runs | fails on |
|---|---|---|
| build | `PyLite.sln`, Release | any error; the engine builds with warnings as errors |
| xUnit, everyday | `Category!=MachineSensitive` | any failed test |
| xUnit, MachineSensitive | the tests that compare wall-clock time or heap growth | any failed test (give it a quiet machine) |
| conformance | `conformance/PyLite.Conformance`: cases from CPython's `Lib/test` | any failed case, or none run |
| differential | `tools/PyLite.DiffHarness` against IronPython 3.4.2 on `tools/diff-corpus` | a difference not listed in `known-differences.txt`, an engine error |
| adversarial | `tools/PyLite.Stress` on its `bombs` corpus | a probe that ends in a host error, an engine fault or a throw; returns more than 500 ms past its deadline; leaks its runner thread; grows the process past four times its memory limit plus 512 MB; keeps a core busy in the quarter second after it returned; or ends in another outcome than `bombs/expected.txt` pins |
| performance (`-Perf` only) | `tools/PyLite.PerfBench --full` against `tools/perf-baseline.json` | slower than the baseline x1.5, or a baseline of another scale; the baseline must come from the same machine |

## Inside the xUnit gates

The five categories (`Compatibility`, `Sandbox`, `ResourceLimits`, `Regression`, `Fuzz`) and `MachineSensitive`
are described in `TESTING.md`. Some classes hold checks that once were run apart from the suite:

| check | class | what it holds |
|---|---|---|
| CPython oracle | `Spec/OracleCorpusTests` | the scripts in `tests/PyLite.Tests/Oracle` print exactly what CPython 3.14 printed for them |
| caller culture | `Spec/OracleCorpusTests`, `Hosting/HostContractTests` | the same output under a culture with a U+2212 minus and a comma point; a run leaves the caller's culture as it was |
| parallel runs | `Spec/OracleCorpusTests`, `Hosting/UnlimitedRunsTests`, `Hosting/ScriptEngineGateTests` | sixteen runs of each oracle script at once print what one prints; runs side by side without a limit on their number, and aborted host threads |
| an ASP.NET host's .NET 4.5 mode | `Spec/OracleCorpusTests` | the oracle corpus in an AppDomain whose target framework is 4.5, as a web host's with `httpRuntime targetFramework="4.5"` |
| FIPS | `Infra/FipsSafeCryptoTests` | the engine names no algorithm a FIPS-enforcing Windows refuses (md5 is the managed `Md5Managed`) |
| compile-and-run fuzz | `Fuzz/FuzzTests` | generated and mutated programs compiled and run: no throw from `Run`, no engine fault |
| host boundary | `Hosting/HostIsolationTests`, `HostContractTests`, `HostBoundaryTests` | host failures, shared exceptions, the compile cache, fenced host objects and services |

## Not run by anything yet

- A machine with the FIPS policy on (`FipsSafeCryptoTests` checks the source, not the policy).
- A 1,000-run endurance check (memory back to its baseline within 20 %); `MemoryHygieneTests` runs 100
  and pins retention, not totals.
- Long fuzzing: `FuzzTests` runs a few thousand programs per suite run.
- PerfBench unless `-Perf` is given.
