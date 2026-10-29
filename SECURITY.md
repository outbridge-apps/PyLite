# Security

PyLite runs scripts that the host did not write, so its threat model is part of its contract. Every
guarantee below names the tests and the adversarial probes that hold it. `tools/run-gates.ps1` runs all of
them, and the [test report](docs/test-report.md) of each release shows that run on the release commit.
[`TESTING.md`](TESTING.md) says how to repeat it.

## Threat model

- **The attacker is the script author.** They choose the source text and whatever the host passes in as
  input. They can call every builtin and module the engine ships or the host registers, and they will try
  to exhaust time, memory, stack, threads and output, to reach past the engine, or to make it fail in a way
  the host cannot handle.
- **What is protected** is the host process: its CPU, its memory and its threads, its stability (no crash,
  no hang), and everything the host does not hand to the script (files, network, processes, the
  environment, the .NET runtime).
- **The boundary** is the set of functions and modules the host registers. Those run with the host's
  privileges; see what is not guaranteed.

## Guarantees and what holds them

Test classes are under `tests/PyLite.Tests/`; probes are the files of `tools/PyLite.Stress/bombs`, grouped
by prefix in its README.

| guarantee | held by |
|---|---|
| **No files, sockets, processes or environment.** The engine ships no module that reaches them, an `import` of anything not registered fails, and `eval`, `exec`, `compile`, `open`, `input`, `globals` and `locals` do not exist. The modules are listed in [`docs/modules.md`](docs/modules.md). | `Builtins/BuiltinsInventoryTests` (the builtin surface, pinned), `Builtins/HostFunctionTableTests`, `Runtime/ModuleRegistryTests`, `Evaluator/EvaluatorPhase5Tests` (unknown imports) |
| **No reach into .NET.** Script values are the engine's own value model. Introspection does not lead out of it: `__subclasses__`, `__globals__`, `__code__`, `__builtins__`, `__import__` and the format-string attribute walk find nothing to climb, and no attribute or call yields a CLR object, type or method. | `Hosting/IntrospectionEscapeTests` |
| **Every run is bounded** by `ResourceLimits`: steps, wall-clock deadline, allocated bytes, call depth, data depth, string and collection size, integer size, output bytes, regex time, sleep time. Crossing a limit ends the run with a `BudgetExceeded` result naming the limit; the script cannot catch it. | `Runtime/BudgetTests`, `Runtime/ResourceLimitsTests`, `Hosting/ResourceBoundTests` (bombs of every kind end near their deadline, no thread leaked), the `Stress/` classes, [`examples/limits`](examples/limits) with `Hosting/LimitExamplesTests` (one script per limit), every probe |
| **The deadline holds inside long operations too**: big-int arithmetic, regex, sort, string building, parsing, compression. | `Hosting/ResourceBoundTests`, `Values/BigOpsTests`, probes `h`, `r`, `l` |
| **No stack overflow.** Deep recursion ends the run on `MaxCallDepth`; deep data raises `RecursionError` at `MaxDataDepth`; the C# stack is probed on every recursive descent, at compile time as well, so a script cannot take the process down with `StackOverflowException`. | `Runtime/StackGuardTests`, `Syntax/CompileStackSafetyTests`, `Syntax/SyntaxLimitsTests`, `Stress/ValueModelStressTests`, probes `d` and `l1`-`l4` |
| **No engine fault from script content.** Whatever the script does, the host receives a typed result (`RuntimeError`, `SyntaxError`, `BudgetExceeded`, `HostError`), never an exception. | `Hosting/HostBoundaryTests` (a hostile corpus through `Run` and `TryCompile`), `Hosting/RunnerThreadTests`, `Runtime/OverflowNetTests`, `Modules/ArgumentFaultTests`, `Modules/ModuleBoundaryTests`, the `Fuzz` category |
| **The memory of a stopped run is given back.** | `Hosting/ResourceBoundTests` (`AbortedMemoryBombGraphIsCollected`), `Hosting/MemoryHygieneTests` |
| **Concurrency is bounded.** At most `MaxConcurrentRuns` runs at once (8 by default, 32 at most), and the `MaxAllocBytes` of the running runs together stay under `GlobalMemoryCeilingBytes` (1 GB by default). A run past either is refused at once (`BudgetExceeded`, `MaxConcurrentRuns` or `GlobalMemoryCeiling`), never queued. | `Hosting/ScriptEngineGateTests`, `Hosting/RunGateTests` |

## What can outlive a run, stated exactly

- **Script code always stops at a budget check.** `Run` returns at about `DeadlineMs`, plus a terminating
  grace of 250 ms in which `finally` blocks may run but cannot un-fail the run.
- **A host function that blocks** is the one thing that can keep a run's thread past deadline + grace +
  5 s. `Run` then returns a `HostError`, the runner thread is leaked (its gate slot and its memory stay held
  until the host function returns), the engine raises a `ThreadLeaked` event, and once
  `CircuitBreakerLeakedThreads` threads (4 by default) are leaked, the circuit breaker opens: every `Run` is
  refused until they finish. Held by `Hosting/RunGateWatchdogTests` and `Hosting/RunnerThreadTests`.
- **A long big-int operation** (a multiply, division, gcd, power, `pow(b, e, m)` or decimal conversion that
  would take more than about 10 ms) runs on a helper thread while the run's thread polls the deadline. A
  stopped run returns on time; that one call then finishes on its own: seconds of CPU at the default
  `MaxIntBits` of 1,000,000 bits, about 16 times that at its 4,000,000-bit hard cap. The helper holds no gate
  slot. Held by `Values/BigOpsTests` and probes `li`-`lm`.

## What it does not guarantee

- A host function or module the host registers runs with the host's privileges. Everything the host hands
  to a script is the host's responsibility; PyLite fences those calls so that an exception in them becomes
  a `HostError` result, nothing more, and a host function that blocks is the leaked-thread case above.
- The CPU a stopped run's last big-int call still uses, as above.
- Timing side channels and resource-usage inference are out of scope.
- The default limits are generous. A host that runs untrusted scripts should set its own `ResourceLimits`
  per run and a ceiling per engine in `EngineOptions`; the engine's hard caps (deadline 300 s, 2·10⁹ steps,
  1 GB allocated, call depth 512, data depth 10,000, int size 4,000,000 bits, 32 concurrent runs) apply on
  top of both.
- Denial of service against the host by a flood of runs is the host's concern: the engine bounds each run
  and refuses runs past its gate, but it does not throttle callers.

## Reporting a vulnerability

Please report security issues privately through GitHub's "Report a vulnerability" form on this
repository (Security → Advisories) rather than as a public issue. Include a script that reproduces
the behaviour and the limits it ran under. We aim to acknowledge reports within five working days.
