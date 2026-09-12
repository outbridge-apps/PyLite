# Security

PyLite runs scripts that the host did not write, so its threat model is part of its contract.

## What the sandbox guarantees

A script running under PyLite cannot:

- read or write files, open sockets, start processes, or reach the host's environment: the engine
  ships no module that does, and there is no `import` of anything the host did not register;
- reach the .NET runtime through reflection: script values are the engine's own value model, and
  no builtin exposes a CLR object, type or method;
- run longer, allocate more, nest deeper or print more than the host allows: every run is bounded
  by `ResourceLimits` (steps, wall-clock deadline, allocated bytes, call depth, data depth, string
  and collection sizes, integer size, output bytes, regex time, sleep time), and crossing a limit
  ends the run with a typed `BudgetExceeded` error;
- hang the host thread: a run executes on its own thread, a deadline is enforced cooperatively and,
  past a grace period, the thread is abandoned and reported rather than joined forever;
- crash the host process with an engine fault: a C# exception escaping the engine is reported as
  an `EngineFault` result, and an arithmetic overflow escaping a builtin is the script's own
  `OverflowError`.

The engine is exercised by an adversarial corpus (deep nesting, huge literals, regex backtracking,
quadratic string operations, hash flooding, allocation and recursion bombs) that must hold every
limit, and by a differential corpus against a reference interpreter.

## What it does not guarantee

- A host function or module the host registers runs with the host's privileges. Everything the
  host hands to a script is the host's responsibility; PyLite fences those calls so that an
  exception in them becomes a `HostError` result, nothing more.
- Timing side channels and resource-usage inference are out of scope.
- The default limits are generous. A host that runs untrusted scripts should set its own
  `ResourceLimits` per run and a per-engine ceiling in `EngineOptions`.
- Denial of service against the host by a flood of runs is the host's concern: the engine bounds
  each run, and its concurrency gate refuses runs past the configured count, but it does not
  throttle callers.

## Reporting a vulnerability

Please report security issues privately through GitHub's "Report a vulnerability" form on this
repository (Security → Advisories) rather than as a public issue. Include a script that reproduces
the behaviour and the limits it ran under. We aim to acknowledge reports within five working days.
