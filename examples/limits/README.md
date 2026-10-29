# Resource limits, one script each

Each script here runs into one member of `ResourceLimits`. Its header names the limits the host sets for
the run and the result the host gets back; `tests/PyLite.Tests/Hosting/LimitExamplesTests.cs` runs every
script that way, checks the result, and then runs an ordinary script on the same engine to show that
nothing of the stopped run is left behind.

| script | limit set for the run | result |
|---|---|---|
| [`steps.py`](steps.py) | `MaxSteps = 1,000,000` | `BudgetExceeded`, `MaxSteps` |
| [`deadline.py`](deadline.py) | `DeadlineMs = 500` | `BudgetExceeded`, `DeadlineMs` |
| [`allocated_bytes.py`](allocated_bytes.py) | `MaxAllocBytes = 8 MB` | `BudgetExceeded`, `MaxAllocBytes` |
| [`call_depth.py`](call_depth.py) | `MaxCallDepth = 50` | `BudgetExceeded`, `MaxCallDepth` |
| [`data_depth.py`](data_depth.py) | `MaxDataDepth = 32` | `RuntimeError`, Python `RecursionError` |
| [`string_length.py`](string_length.py) | `MaxStrChars = 1,000,000` | `BudgetExceeded`, `MaxStrChars` |
| [`collection_size.py`](collection_size.py) | `MaxCollectionItems = 100,000` | `BudgetExceeded`, `MaxCollectionItems` |
| [`int_size.py`](int_size.py) | `MaxIntBits = 10,000` | `BudgetExceeded`, `MaxIntBits` |
| [`output.py`](output.py) | `MaxOutputBytes = 10,000` | `BudgetExceeded`, `MaxOutputBytes` |
| [`regex_time.py`](regex_time.py) | `RegexPerOpTimeoutMs = 50` | `BudgetExceeded`, `RegexPerOpTimeout` |
| [`sleep.py`](sleep.py) | `SleepTotalMs = 200` | `BudgetExceeded`, `SleepTotalMs` |

A `BudgetExceeded` result names the limit in `ScriptError.LimitName`; the script cannot catch it, and its
`finally` blocks get a short grace to run but cannot turn the run into a success. `MaxDataDepth` is the one
limit that surfaces as a Python exception, so a script may catch it like any `RecursionError`.

## Setting the limits

```csharp
var engine = new ScriptEngine(new EngineOptions());
RunResult r = engine.Run(source, new RunRequest
{
    Limits = ResourceLimits.Default.With(maxSteps: 1_000_000),
});
if (!r.Succeeded && r.Error.Kind == ScriptErrorKind.BudgetExceeded)
    Console.WriteLine(r.Error.LimitName);   // MaxSteps
```

The limits of a `RunRequest` can only lower the engine's: each one is clamped to
`EngineOptions.DefaultLimits` (`ResourceLimits.Default` unless the host sets it), which is itself clamped
to the engine's hard caps. To allow more than the defaults, raise `EngineOptions.DefaultLimits` when
creating the engine. [`SECURITY.md`](../../SECURITY.md) lists the hard caps and what each limit
guarantees.
