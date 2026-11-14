# PyLite Hosting Guide — embedding the engine in C#

How to embed the engine through its host API (`Outbridge.PyLite.Hosting`): creating the engine, running
scripts, passing data in and out, writing custom modules (C# and Python), injecting host delegates,
resource limits, error handling, concurrency and memory lifecycle. Signatures and per-member
semantics of every public type are in the XML documentation the package ships (IntelliSense shows it).

All examples are C# 7.3 / .NET Framework 4.7.2, compile against the current `PyLite` assembly, and
use only the public API. Namespaces used throughout:

```csharp
using Outbridge.PyLite.Hosting;          // ScriptEngine, EngineOptions, RunRequest, RunResult, ResourceLimits, ...
using Outbridge.PyLite.Runtime;          // EvalContext, ValueFactory
using Outbridge.PyLite.Runtime.Evaluator;   // KwArgs (when you name the delegate's parameter types)
using Outbridge.PyLite.Runtime.Values;   // ScriptValue, StrValue, IntValue, ModuleValue, BuiltinDelegate, ...
using Outbridge.PyLite.Syntax;           // CompiledScript
```

---

## 1. Quick start

```csharp
var engine = new ScriptEngine(new EngineOptions());

RunResult result = engine.Run(
    "total = price * (1 + rate)\n" +
    "label = 'net %.2f' % total\n",
    new RunRequest
    {
        ScalarsIn = new Dictionary<string, string> { ["price"] = "100", ["rate"] = "0.2" },
        ScalarOutputNames = new[] { "total", "label" },
    });

if (result.Succeeded)
{
    string total = result.ScalarsOut["total"];   // "120.0"
    string label = result.ScalarsOut["label"];   // "net 120.00"
}
else
{
    Console.WriteLine(result.Error.ToString());  // kind, line, python type, message
}
```

`ScriptEngine.Run` **never throws** — every outcome (script error, budget exhaustion, host bug,
engine fault) comes back as a `RunResult` with `Succeeded == false` and a populated `Error`.
The only exceptions out of the hosting API are `ArgumentException`-family throws for host coding
errors at engine construction time (e.g. a syntax error in a registered Python module).

### Engine lifecycle

Create **one** `ScriptEngine` per configuration and keep it for the process lifetime. Construction snapshots `EngineOptions` — mutating the options object
afterwards has no effect. The engine is thread-safe: any number of threads may call `Run`
concurrently, as many as it likes (§8).

There is nothing to dispose. Dropping the last reference makes the engine, its module registry and
its compiled-script cache ordinary garbage.

---

## 2. Running scripts

Two entry points:

```csharp
RunResult Run(string source, RunRequest request);          // compile (cached) + run
RunResult Run(CompiledScript compiled, RunRequest request); // run a pre-compiled script
```

### Pre-compiling

`Compile`/`TryCompile` parse and resolve a script without running it — useful to validate user
scripts at save time, or to compile once and run many times:

```csharp
CompiledScript compiled;
ScriptError error;
if (!engine.TryCompile(sourceText, "invoice-hook", out compiled, out error))
{
    Console.WriteLine("bad script: line " + error.Line + ": " + error.Message);
    return;
}
// later, many times, possibly concurrently:
RunResult r = engine.Run(compiled, new RunRequest());
```

`Compile(source)` (the throwing variant) raises `PyLiteSyntaxException` carrying the same
`ScriptError`. A `CompiledScript` is immutable and safe to share across threads and runs.

The engine also caches compilation internally, keyed by the source text itself and the script name
(`EngineOptions.CompiledScriptCacheEntries`, default 256; `0` disables; the cache also holds at most
2 million characters of source in all; on overflow it is fully cleared and rebuilt on demand). So
`Run(string, ...)` in a loop does **not** re-parse an unchanged script.

### Print output

`print()` output is captured per run:

```csharp
// Option A: buffered — read it from the result afterwards.
RunResult r = engine.Run("print('hello')\nprint('world')\n", new RunRequest());
string text = r.PrintOutput;                    // "hello\nworld\n"

// Option B: live callback, one call per line, line WITHOUT the trailing newline.
var req = new RunRequest { PrintSink = line => log.Info("[script] " + line) };
```

When `PrintSink` is set, `RunResult.PrintOutput` is `null` (the host already received the output).
Total print volume is capped by `ResourceLimits.MaxOutputBytes`. On a failed run the output
produced before the error is still delivered.

---

## 3. Variables in, variables out

The primary data channel is **string-based and safe by construction**: the host never touches
script values directly, and the script never sees host objects.

### Inputs

```csharp
var req = new RunRequest
{
    // Scalars: each string is coerced to a Python value (rules below).
    ScalarsIn = new Dictionary<string, string>
    {
        ["customer_id"] = "C-1042",
        ["qty"]         = "17",          // -> int
        ["rate"]        = "0.25",        // -> float
        ["is_test"]     = "true",        // -> bool
        ["comment"]     = null,          // -> None
    },
    // Structured data: each string is parsed as JSON into dict/list/str/int/float/bool/None.
    JsonIn = new Dictionary<string, string>
    {
        ["lines"] = "[{\"item\": \"A1\", \"qty\": 2}, {\"item\": \"B7\", \"qty\": 5}]",
    },
};
```

Every input becomes a **global variable** of the script (`qty`, `lines`, ...). Coercion rules for
`ScalarsIn` values:

| host string | script value |
|---|---|
| `null` | `None` |
| `""` | `''` (empty str) |
| integer literal (optional sign, ≤ 4300 digits) | `int` |
| float literal (not inf/nan) | `float` |
| `"true"` / `"false"` (any case) | `bool` |
| anything else | `str` |

If a value must stay a string even when it looks numeric (an account number `"0042"`), pass it via
`JsonIn` as `"\"0042\""` instead.

Variable names must be ASCII identifiers, must not be Python keywords, and a name may not appear in
both `ScalarsIn` and `JsonIn` — violations fail the run with a `HostError` before anything executes.
An invalid JSON input value fails the run the same way.

### Outputs

Outputs are **declared before the run** by name; after the run the engine marshals those globals
out of the script:

```csharp
var req = new RunRequest
{
    ScalarOutputNames = new[] { "status", "total" },   // str(value) per variable
    JsonOutputNames   = new[] { "result_lines" },      // json.dumps(value), compact
};
RunResult r = engine.Run(src, req);

string status = r.ScalarsOut["status"];
string json   = r.JsonOut["result_lines"];   // e.g. [{"item":"A1","ok":true}]
```

Semantics:

- A declared output the script never assigned is simply **absent** from the dictionary
  (use `TryGetValue`).
- `ScalarsOut` uses Python `str()` semantics — `str(3.5) == "3.5"`, `str(True) == "True"`,
  `str([1, 2]) == "[1, 2]"`.
- `JsonOut` uses compact JSON (`ensure_ascii=false`, `allow_nan=false`). A non-serializable value
  (a function, a set, a NaN, a cyclic structure) fails the run with `HostError` naming the variable.
- On any failure both dictionaries are empty (never null).

There is no other channel: the script cannot reach host memory, files, network or environment
unless the host explicitly hands it a function (§4) or a module (§5).

---

## 4. Host functions: injecting a C# delegate as a builtin

`RunRequest.Builtins` gives the script extra global functions implemented by the host. This is the
right mechanism for **per-run** capabilities — the delegate may close over the current business
context (session, company, buffer):

```csharp
HostFunctionTable builtins = HostFunctionTable.CreateDefault();   // standard builtins + exception types

// Delegate signature: (ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx) -> ScriptValue.
// For a free function `self` is always null.
builtins.Set("get_balance", (self, args, kw, ctx) =>
{
    if (args.Length != 1 || kw.Count > 0)
        throw ScriptExceptions.TypeError("get_balance() takes exactly 1 positional argument");
    var account = args[0] as StrValue;
    if (account == null)
        throw ScriptExceptions.TypeError("get_balance() argument must be str");

    decimal balance = ledger.GetBalance(account.Value);           // captured host state
    return ctx.Values.Float((double)balance);
});

RunResult r = engine.Run(
    "b = get_balance('4010')\n" +
    "ok = b >= 0\n",
    new RunRequest { Builtins = builtins, ScalarOutputNames = new[] { "ok", "b" } });
```

Key facts:

- `CreateDefault()` returns a fresh mutable copy of the standard builtins; `Set` adds or replaces,
  `Remove(name)` deletes (standard builtins can be removed to tighten the sandbox further).
- The engine **clones and freezes** the table at run start, so one `HostFunctionTable` instance can
  be safely reused and even mutated between runs — a running script never sees a change.
- Host delegate calls are fenced by the engine (`HostCallGuard`):
  - throwing `ScriptExceptions.TypeError/ValueError/KeyError/RuntimeError/PyError(type, msg)`
    raises an ordinary Python exception **the script can catch** with `try/except`;
  - any other .NET exception escaping the delegate aborts the run with an **uncatchable**
    `HostError` (the script cannot swallow host bugs);
  - returning `null` is tolerated and becomes `None`, but prefer an explicit `ctx.Values.None`.
- Time spent inside the delegate **counts against the run deadline** and is reported in
  `RunResult.Stats.HostCallTime`. A delegate that blocks forever leaks the runner thread (§8) —
  never call indefinitely-blocking APIs from a script delegate.

### Reading arguments

Arguments arrive as `ScriptValue`s. Pattern-match the concrete types:

```csharp
var s  = args[0] as StrValue;    // s.Value  : string
var i  = args[0] as IntValue;    // i.Value  : BigInteger — range-check before (long)i.Value
var f  = args[0] as FloatValue;  // f.Value  : double
var b  = args[0] as BoolValue;   // b.Value  : bool
var d  = args[0] as DictValue;   // d.TryGet(key, ctx, out v), d.SetItem(key, v, ctx), d.Count

// Any value -> display string (Python str()/repr() semantics):
string text = args[0].Str(ctx);
string dbg  = args[0].Repr(ctx);
bool truthy = args[0].IsTruthy(ctx);
string type = args[0].PyTypeName;         // "str", "int", "list", ...
```

Keyword arguments come in `kw`:

```csharp
ScriptValue sep;
string sepText = kw.TryGet("sep", out sep) ? ((StrValue)sep).Value : ",";
// A function that accepts no kwargs should reject them explicitly:
if (kw.Count > 0)
    throw ScriptExceptions.TypeError("frobnicate() takes no keyword arguments");
```

### Building return values

All construction goes through `ctx.Values` (the run's `ValueFactory` — it charges the memory
budget, so a hostile script cannot use your function to bypass limits):

```csharp
ctx.Values.None                     // None
ctx.Values.Bool(true)               // True
ctx.Values.Int(42L)                 // int (also Int(BigInteger))
ctx.Values.Float(3.14)              // float
ctx.Values.Str("text")              // str
ctx.Values.Bytes(new byte[] {1,2})  // bytes

var d = ctx.Values.Dict(4);         // dict
d.SetItem(ctx.Values.Str("id"), ctx.Values.Str("C-1042"), ctx);
d.SetItem(ctx.Values.Str("qty"), ctx.Values.Int(17), ctx);

var list = ctx.Values.ListFrom(new List<ScriptValue>
{
    ctx.Values.Int(1), ctx.Values.Int(2), ctx.Values.Int(3),
});                                 // list (adopts the List<> — do not keep using it)

var t = ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Str("a"), ctx.Values.Int(1) });
```

For arbitrarily-shaped data the pragmatic pattern is **JSON at the boundary**: accept a `str`
argument, `Newtonsoft`-parse it host-side, and return a `str` the script feeds to `json.loads` —
element-level access to script lists from host code is deliberately not part of the public surface.
Nested `dict`s built via `SetItem` cover most structured returns directly.

---

## 5. Custom modules

Four mechanisms, all configured on `EngineOptions` (i.e. fixed at engine construction, before any
script starts). Decision matrix:

| you need | use |
|---|---|
| a per-run function bound to current business context | `RunRequest.Builtins` (§4) |
| a stable importable API implemented in C# | `ExtraModules` (`IModuleProvider`) |
| a helper library written in the script dialect itself | `PythonModules` |
| names available without any `import` line | `PreludeModules` |
| patching or removing members of an existing module | `ModuleOverrides` |

Name collisions across sources resolve **last-wins per whole module**:
built-ins < `ExtraModules` < `PythonModules`. Duplicates within one source are an error.

### 5.1 C# module (`IModuleProvider`)

```csharp
public sealed class ErpModule : IModuleProvider
{
    private readonly IErpGateway _gateway;   // engine-lifetime service, must be thread-safe

    public ErpModule(IErpGateway gateway) { _gateway = gateway; }

    public string Name { get { return "erp"; } }

    // Called once per run on the first `import erp`, on the runner thread, under the run's budget.
    public ModuleValue CreateInstance(EvalContext ctx)
    {
        var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);

        m["VERSION"] = ctx.Values.Str("1.0");            // plain data member

        m["find_customer"] = ctx.Values.BuiltinFunction("erp.find_customer", (self, args, kw, c) =>
        {
            var id = args.Length == 1 ? args[0] as StrValue : null;
            if (id == null)
                throw ScriptExceptions.TypeError("find_customer(id: str) expects one str argument");
            Customer cust;
            try
            {
                cust = _gateway.Find(id.Value);
            }
            catch (Exception ex)                          // see the fencing note below
            {
                throw ScriptExceptions.RuntimeError("erp lookup failed: " + ex.Message);
            }
            if (cust == null)
                return c.Values.None;
            var d = c.Values.Dict(2);
            d.SetItem(c.Values.Str("id"), c.Values.Str(cust.Id), c);
            d.SetItem(c.Values.Str("name"), c.Values.Str(cust.Name), c);
            return d;
        });

        return ctx.Values.Module("erp", m);
    }
}
```

Registration and use:

```csharp
var engine = new ScriptEngine(new EngineOptions
{
    ExtraModules = new IModuleProvider[] { new ErpModule(gateway) },
});

RunResult r = engine.Run(
    "import erp\n" +
    "c = erp.find_customer(customer_id)\n" +
    "name = c['name'] if c is not None else '<missing>'\n",
    new RunRequest
    {
        ScalarsIn = new Dictionary<string, string> { ["customer_id"] = "C-1042" },
        ScalarOutputNames = new[] { "name" },
    });
```

Rules for provider code:

- `CreateInstance` runs **once per run** (module state is never shared between runs); the provider
  instance itself lives for the engine lifetime and must be thread-safe.
- **Fencing**: `CreateInstance` and what host code makes with `ctx.Values.BuiltinFunction`,
  `ctx.Values.Type` and `ctx.Values.Iterator` (wherever the script gets it from) are fenced like
  `RunRequest.Builtins` and `ModuleOverrides` delegates: an arbitrary .NET exception
  escaping one ends the run as `HostError`, and so does a `null` module. Throw `ScriptExceptions.*` for
  an error the script should be able to catch (as above).
- Don't capture `ctx` or any `ScriptValue` beyond the run — they belong to the run and pinning them
  defeats the per-run memory reclamation (§9).

### 5.2 Python-source modules (`PythonModules`)

A host-shipped library written in the script dialect. Compiled and syntax-checked at engine build
(a syntax error throws `ArgumentException` naming the module); the module body executes once per
run on first import, **under the run's budget** — a Python module cannot do anything a script
cannot.

```csharp
var engine = new ScriptEngine(new EngineOptions
{
    PythonModules = new Dictionary<string, string>
    {
        ["fin"] =
            "TAX_RATE = 0.25\n" +
            "def net_to_gross(amount):\n" +
            "    return round(amount * (1 + TAX_RATE), 2)\n" +
            "def _helper():\n" +          // '_' prefix: hidden from prelude injection
            "    pass\n",
    },
});

engine.Run("import fin\ng = fin.net_to_gross(100)\n", ...);   // g == 125.0
```

### 5.3 Preludes: names visible without `import`

Any registered module (C# or Python) can be auto-loaded before the main script; its public
top-level names (not starting with `_`) are injected into the script globals — an implicit
`from mod import *`:

```csharp
var engine = new ScriptEngine(new EngineOptions
{
    PythonModules  = new Dictionary<string, string> { ["fin"] = finSource },
    ExtraModules   = new IModuleProvider[] { new ErpModule(gateway) },
    PreludeModules = new[] { "fin", "erp" },
});

// Scripts now simply say:  g = net_to_gross(100); c = find_customer('C-1')
```

A script assignment to an injected name shadows it for that run only. Referencing an unregistered
name in `PreludeModules` fails engine construction.

### 5.4 Patching stdlib: `ModuleOverrides`

Member-level replace/add/remove on any registered module, applied when the module materializes in
a run. Overriding delegates are host-fenced (like §4). A `null` delegate **removes** the member:

```csharp
var engine = new ScriptEngine(new EngineOptions
{
    ModuleOverrides = new Dictionary<string, IReadOnlyDictionary<string, BuiltinDelegate>>
    {
        ["time"] = new Dictionary<string, BuiltinDelegate>
        {
            ["sleep"] = (self, args, kw, ctx) => ctx.Values.None,   // neuter time.sleep
        },
        ["random"] = new Dictionary<string, BuiltinDelegate>
        {
            ["seed"] = null,                                        // remove random.seed
        },
    },
});
```

An unknown module name in `ModuleOverrides` fails engine construction (host bug, not a runtime
surprise).

### Built-in modules

Available for `import` out of the box: `json`, `re`, `datetime`, `time`, `math`, `random`,
`decimal`, `fractions`, `statistics`, `itertools`, `collections`, `functools`, `copy`, `copyreg`,
`dataclasses`, `typing`, `html`, `uuid`, `xml.etree.ElementTree`, `binascii`, `struct`, `io`,
`csv`, `base64`, `hashlib`, `hmac`, `zlib`, `gzip`, `zipfile`, `enum`, `contextlib`, `string`,
`email.utils` (the date helpers), `bisect`, `heapq`, `operator`, `unicodedata`, `zoneinfo`, `calendar`,
`urllib.parse`, `traceback`, `textwrap`, `secrets`, `__future__` (the features are no-ops; `barry_as_FLUFL` is refused).
Anything else raises `ImportError` in the script. There is no
filesystem/network import — the registry above is the whole world.

`zlib`/`gzip` are one-shot (`compress`/`decompress` + `crc32`/`adler32`); there is no
`compressobj`/`GzipFile` streaming API. Decompression is budget-guarded: a compressed bomb aborts
the run on `MaxAllocBytes` before the payload materializes.

`zipfile` reads and writes archives in an `io.BytesIO` (`ZipFile(io.BytesIO(data))`); a path is
refused. A member is inflated in charged chunks up to its recorded size, the entry count is capped by
`MaxCollectionItems`, and overlapped entries (a zip bomb's shared stream) are refused.

---

## 6. Resource limits

Every run executes under a `ResourceLimits` budget. Three layers:

1. **Engine hard caps** (non-negotiable): deadline ≤ 300 s, steps ≤ 2·10⁹, memory ≤ 1 GB,
   call depth ≤ 512, data depth ≤ 10 000, int size ≤ 4 000 000 bits.
2. **Server ceiling** — `EngineOptions.DefaultLimits` (also used when a request sets nothing),
   itself clamped to the hard caps.
3. **Per-run request** — `RunRequest.Limits`, clamped **downward** onto the ceiling
   (a request can only tighten, never exceed).

```csharp
// Server ceiling for this engine:
var engine = new ScriptEngine(new EngineOptions
{
    DefaultLimits = ResourceLimits.Default.With(deadlineMs: 10_000, maxAllocBytes: 128L << 20),
});

// One specific run, tighter still:
var req = new RunRequest
{
    Limits = ResourceLimits.Default.With(deadlineMs: 2_000, maxSteps: 10_000_000),
    CancellationToken = cts.Token,     // host-side cooperative cancel, polled by the budget
};
```

`ResourceLimits` is immutable; derive variants with `.With(...)`. Presets: `Default`,
`Interactive` (5 s / 64 MB), `Batch` (60 s / 512 MB).

| field | default | limits |
|---|---|---|
| `MaxSteps` | 200 000 000 | evaluator steps (≈ interpreter operations) |
| `DeadlineMs` | 20 000 | wall-clock, includes host-delegate time and marshaling |
| `MaxAllocBytes` | 268 435 456 | cumulative charged allocations |
| `MaxCallDepth` | 200 | Python call depth (recursion) |
| `MaxDataDepth` | 128 | nesting depth of data a comparison, hash or repr descends (hard cap 10 000); past it the script gets a catchable `RecursionError`, not a budget abort |
| `MaxStrChars` | 16 000 000 | length of any single str/bytes |
| `MaxCollectionItems` | 5 000 000 | size of any single list/dict/set/tuple |
| `MaxIntBits` | 1 000 000 | magnitude of any single int (hard cap 4 000 000) |
| `MaxOutputBytes` | 16 777 216 | total print output |
| `RegexPerOpTimeoutMs` | 250 | per regex operation (plus a total regex budget) |
| `SleepTotalMs` | 5 000 | total `time.sleep` per run; a sleep past it fails the run before waiting |

A violated limit fails the run with `Error.Kind == BudgetExceeded` and `Error.LimitName` naming the
limit (`"MaxSteps"`, `"DeadlineMs"`, ...). On deadline/steps the script first gets a short
*terminating grace* window (fixed: 100 000 steps / 250 ms) so `finally` blocks can run — but it
cannot un-fail the run.

**Calibrating the two hard budgets** (measured against a battery of ~30 CPU / native-op / memory
bombs, pinned by `ResourceBoundTests`):

- **Time.** No bomb runs past its deadline: `Run` returns at roughly `DeadlineMs` plus a small
  detection + marshaling overhead (a `while True: pass` under a 2.5 s deadline returned in ~2.76 s;
  the deadline is polled on a step cadence *and* inside every long native operation — big-int math,
  regex, sort, `str(bigint)`, `int(str)`, `join`). A long big-int multiply, division, gcd, power,
  modular power or decimal conversion runs in the engine's own kernel on the run thread, which checks the
  deadline every few milliseconds: when the run is aborted nothing of it computes on. A script can
  only make `Run` block longer than
  `DeadlineMs + grace` by getting stuck **inside a host delegate** (the leaked-thread path,
  §8) — so budget your injected builtins. In short: set `DeadlineMs` to your real ceiling (2000–3000
  ms is typical) and expect the caller to unblock within a few hundred ms of it.
- **Memory.** `MaxAllocBytes` is a **logical, cumulative** charge on script-visible allocations, not
  the process RSS: every new int, float, str, list growth … is charged when it is made, and almost nothing is
  credited back when it dies (a compiled regex is, once collected). It therefore grows with the work a script does, not only with the data
  it keeps — a loop iteration that makes one new int (`x = x + 1`) is charged about 70 bytes, so
  3 million iterations charge ≈ 210 MB, and a 10–15 MB budget ends such a loop after ~140–210 thousand
  iterations. Size it from the data the script builds *and* its loop volume: the default 256 MB covers
  ~3.8 million simple iterations; for long batch scripts raise it (up to the 1 GB hard cap) rather than
  cutting it down to the data size. The charge fires *before* the CLR allocation, so a bomb never
  overshoots the budget — but the process can exceed it: a warm engine sits at a ~27 MB working-set
  floor, and growing one large list holds the old and new backing arrays plus large-object garbage
  that waits for a full GC (peaks measured at 2.6–3.2 × `MaxAllocBytes`: a 64 MB budget peaked at
  166–207 MB private bytes). All of it is returned after the run (verified leak-free over 400
  back-to-back bomb+success runs: managed memory returned to baseline, working set flat).

Related knobs on `EngineOptions`:

- `Clock` (`IScriptClock`) — the time source for `datetime`/`time` (the engine never calls
  `DateTime.Now` itself). `SystemClock(offset, name)` for a fixed company timezone;
  `FrozenClock(...)` for deterministic tests.
- `SleepPolicy` — `DefaultSleepPolicy` (real waits; the total is the run's `SleepTotalMs`) or
  `NoOpSleepPolicy` (every `time.sleep` returns instantly and is not counted; recommended on a
  production server).
- Both are host code, read under the same fence as a host function: a clock or policy that throws, a
  `UtcNow` that is not `DateTimeKind.Utc`, a `LocalOffset` that is not whole minutes under a day, a null
  `LocalName`, a `Quantum` outside 1 ms to 1 s or a negative `Clamp` ends the run as `HostError`.
- `StrHashSeed` — the string-hash seed. FIXED by default, so `hash(s)` is the same number in every run and after a process restart. Set it to `null` for a fresh cryptographic seed per engine: that buys hash-flooding resistance for dict keys that come from outside and costs the reproducibility. A flood is bounded either way, since every table probe charges a step.

---

## 7. Error handling

```csharp
RunResult r = engine.Run(src, req);
if (!r.Succeeded)
{
    ScriptError e = r.Error;
    switch (e.Kind)
    {
        case ScriptErrorKind.SyntaxError:     // bad script text (line/column set)
        case ScriptErrorKind.RuntimeError:    // uncaught Python exception; e.PythonType = "ValueError", ...
        case ScriptErrorKind.BudgetExceeded:  // limit hit; e.LimitName = "DeadlineMs", ...
        case ScriptErrorKind.HostError:       // host delegate threw / bad input JSON / non-serializable output
        case ScriptErrorKind.EngineFault:     // engine bug — report it
            break;
    }
    Console.WriteLine(e.Message);          // capped at 2048 chars
    Console.WriteLine(e.ScriptTraceback);  // CPython-format script frames, or null
}
```

The numeric values of `ScriptErrorKind` (0–5) are a stable contract: a host that marshals the kind as an integer can rely on them.
`RunStatistics` (`r.Stats`) accompanies every result: `Steps`, `AllocatedBytes`, `Elapsed`,
`HostCallTime`, `RegexTime`, `PeakCallDepth`, `ModulesImported`, `TerminatingEntered`, and more —
cheap to log per run.

---

## 8. Concurrency and protection

Each run executes on a **dedicated runner thread with a 32 MB stack**, and a source not in the compile cache is
compiled there too. `Compile` and `TryCompile` compile a source not in the cache, and the engine its `PythonModules`, on a 32 MB thread of their own (about
a millisecond to start one, on a cache miss only). That thread passes the same free-memory check as a run's (its stack, 128 bytes a source character, the reserve): a refusal is a `HostError` / `"FreeMemory"` from `TryCompile`, and the `PyLiteSyntaxException` of `Compile` carries it. Parser and evaluator never run on the caller's stack: on .NET Framework the
parser's stack probe refuses once half of a thread's stack is used, so a host deep in its own stack got a
`SyntaxError` for `print(1)`, and with under about 78 KB left the process ended.

- **The engine does not limit how many runs execute at once.** `Run` is synchronous, so your own threads
  bound it: as many callers, as many runs. A host that wants a limit keeps it around its own calls to `Run`.
  `EngineOptions.MaxConcurrentRuns` and `GlobalMemoryCeilingBytes` are obsolete and ignored.
- **Size the limits for your parallelism.** Each run's limits hold for that run; nothing bounds their sum, so
  N runs at once may hold up to N × `MaxAllocBytes` of memory and take N cores until their deadlines. Set
  `DefaultLimits` for the most runs you start at once. A request's limits can only lower the engine's.
- A run started from **inside** a run (a host function that calls `Run`) is a run like any other, on a thread
  of its own while its caller waits. Recursion through such a function (script → host function → `Run` →
  script) is yours to prevent: a thread per level until the deadline, and nothing in the engine stops it.
- **A deadline needs a core.** It is read by the run's own thread, so keep CPU-bound parallelism within a few
  times your cores.
- **The first runs of a process are slower.** .NET compiles the engine's code as it is first used: in a fresh process,
  32 copies at once of a script that takes 0.4 s alone all ended on a 1 s deadline. Give the runs of the first seconds
  a longer deadline, or start the load after one small warm-up run of the scripts you use.
- Clock, sleep policy, providers and `EventSink` are called
  from the run threads, at the same time: they must be thread-safe.
- If a run is stuck **inside host code** (a host function, module, iterator, print callback, clock, sleep policy, or a method or property built with
  `SlotDescriptor`)
  past `deadline + grace` and makes no progress for 5 s, its thread is declared **leaked**: `Run` returns a `HostError`
  and the thread stays until it actually dies. The 5 s grow with the runs sharing a core: 0.5 s per run on a core,
  so 11.5 s for 180 runs on 8 cores. A run in the engine's own code is never leaked before 2 minutes, however short of
  CPU it is, and so is script code a host function calls back (a `__repr__` it reaches through `ScriptValue.Repr`): that code always ends (on one core, 154 CPU-bound runs at once all returned their own results, the
  last after 59 s). Past 2 minutes any run counts as leaked. After `CircuitBreakerLeakedThreads`
  simultaneous leaks (default 4) the engine refuses all new runs at once until leaks recover, because a hung
  service would otherwise pile up threads until the process dies.
- A new run is refused with a `HostError` / `"FreeMemory"` when free memory is below what it may need: its stack, its
  `MaxAllocBytes` and 200 MB (488 MB with the default limits). A run that compiles its source (one not compiled or cached yet) also needs 128 bytes a source character for the syntax tree, up to 64 MB. It counts what Windows can still promise, physical memory plus page file: .NET Framework takes each run's whole 32 MB
  stack from it at start, so about 680 runs fill 22 GB while physical memory stays nearly free. The check keeps memory
  for the host's other threads, where an out-of-memory error would end the process. The stacks of runs admitted and not
  yet started are counted too, in every engine of the process: 160 callers released at once all passed the check
  before. Past it, a thread that cannot start
  gives a `HostError` / `"ThreadStart"`. Treat both as "try later". The check does not see a job object's memory cap, and
  it is not a guarantee: memory can still run out after a run has started.
- A `RunRequest.CancellationToken` already cancelled when `Run` is called runs nothing (`BudgetExceeded` /
  `"CancellationToken"`).
- `EventSink` receives `RunCompleted` / `ThreadLeaked` / `CircuitOpened` / `CircuitClosed` /
  `EngineFault` telemetry; the sink is called inside try/catch and can never affect a run.

```csharp
var engine = new ScriptEngine(new EngineOptions
{
    DefaultLimits = ResourceLimits.Default.With(maxAllocBytes: 64L << 20),
    EventSink = ev => log.Info("pylite " + ev.Kind + " run=" + ev.RunId + " " + ev.Message),
});
```

### The boundary guarantee: `Run` never throws

**No script content can raise an exception on your thread.** This is the load-bearing safety
property for embedding in a shared process. Concretely:

- `engine.Run(source, request)` **always returns a `RunResult`** — for *any* input, however hostile.
  It never propagates a CLR exception to the caller.
  A null source or script is a `HostError` too, and so is a request whose collections throw while
  the run copies them.
- A host failure — an exception from a host function, a host module, the print callback, the clock or
  the sleep policy — ends the run as `HostError` whatever the script does: a `finally` that returns
  or a `with` block whose `__exit__` suppresses cannot turn it into success. Only `ScriptExceptions.*`
  errors are the script's to catch, and each run raises its own copy of one, even one kept in a static field.
- A run executes in the invariant culture, whatever the calling thread's, and the compiler reads none:
  numbers, minus signs and .NET's message texts never depend on the user's or the machine's locale. The
  calling thread's culture is left as it was (nothing is set on it, so no AsyncLocal lands on its context).
- `engine.TryCompile(...)` **never throws** (a null source is a failed result); `engine.Compile(...)` throws
  `PyLiteSyntaxException` for a syntax error and `ArgumentNullException` for a null source, as `new ScriptEngine(null)`
  does for null options: those are the host's mistakes, not script content.
- Under a job object's memory cap, which the free-memory check cannot see, a runner stack that cannot be committed is
  a `HostError` / `"ThreadStart"` refusal; size such a cap above the parallel runs × (`MaxAllocBytes` + 32 MB).

Every failure mode maps to a `ScriptError.Kind`, never to a throw or a crash:

| Hostile input | Result |
|---|---|
| Malformed/garbage source, unterminated string | `SyntaxError` |
| Pathologically deep nesting (`[[[…]]]`, `1+1+…`, deep `match` patterns) | `SyntaxError` "too deeply nested / too complex" — **not** a stack overflow |
| Infinite loop, runaway recursion, memory/int/string bomb | `BudgetExceeded` — and it is **uncatchable by the script**: a script `except BaseException` cannot swallow a budget abort |
| Uncaught Python exception | `RuntimeError` (with `PythonType` + traceback) |

The engine defends its own C# stack: script code runs on a 32 MB runner thread, and **compilation
runs on a thread of the engine and stack-probes**, so a deeply nested but syntactically legal program
becomes a `SyntaxError`, never a process-killing `StackOverflowException` (which .NET cannot catch), and the
answer does not depend on how deep in its own stack the host called.

`EngineFault` is the one kind a *script* can never cause — it signals an engine bug; if you see it,
report it. The single thing outside this guarantee is a **host delegate that blocks forever**
(see the leaked-thread bullet above): give your injected builtins their own timeouts, no longer than
`ctx.Budget.TimeRemaining`, and the run ends on time even when the call behind them blocks. This contract
is pinned by an adversarial test battery (`HostBoundaryTests`) that asserts, over dozens of attack
scripts, that `Run` never throws and never faults.

---

## 9. Memory lifecycle and cleanup

There is **no per-run cleanup call and no Dispose** — by design:

- Every script value is allocated per run and rooted only by the run's context; when `Run` returns,
  the whole run graph (values, module instances, environments) is unreferenced .NET garbage. No
  AppDomain, nothing to unload.
- Consequently the one rule for host code: **never store `ScriptValue`/`EvalContext` references
  beyond the run** (in delegates, providers or sinks). Convert to `string`/CLR types at the
  boundary.
- The GC reclaims that garbage on its own schedule. If you want RAM returned to the OS promptly
  after large runs, opt into the hygiene knobs:

```csharp
var engine = new ScriptEngine(new EngineOptions
{
    MemoryHygiene = new MemoryHygieneOptions
    {
        // After a run that charged > 64 MB, the dying runner thread performs
        // GC.Collect(2, GCCollectionMode.Optimized) — the CLR may decline; callers never pause.
        PostRunGcHintThresholdBytes = 64L << 20,
        // Every 8th such hint additionally compacts the Large Object Heap
        // (defragments after big-string workloads).
        LohCompactionEveryNHints = 8,
    },
});
```

Defaults are **off** — on a shared server an unconditional collect per run would trade RAM for pauses
in unrelated workloads.

The only engine-lifetime caches are the compiled-script cache (bounded by
`CompiledScriptCacheEntries`, self-clearing on overflow; set `0` to disable entirely) and the
frozen module registry (fixed size). Long-lived memory growth beyond those is a bug — report it.

---

## 10. The flat facade: `ScriptRunner`

For hosts that can't consume the object API comfortably (an interop layer that marshals only strings and
integers), `ScriptRunner` wraps a
shared default engine behind a flat, exception-free surface:

```csharp
var runner = new ScriptRunner();
runner.SetScalar("qty", "17");
runner.SetJson("lines", linesJson);
runner.DeclareScalarOutput("total");
runner.SetLimitTimeoutMs(5000);
runner.SetLimitMemoryBytes(64L << 20);
runner.SetLimitIntBits(65536);          // ints up to ~19,700 digits
runner.SetPrintCallback(line => info(line));

int code = runner.Run(source);          // 0 = OK, else (int)ScriptErrorKind 1..5; NEVER throws
if (code == 0)
    total = runner.GetScalar("total");  // GetScalar/GetJson/GetPrintOutput
else
    error(runner.GetErrorType() + ": " + runner.GetErrorMessage());

runner.Reset();                          // clear inputs/outputs/limits for reuse
```

The shared engine is built on first use with the default `EngineOptions`. To configure it (limits,
`Clock`, `SleepPolicy`, for instance `NoOpSleepPolicy` on a server), call
`ScriptRunner.ConfigureSharedEngine(options)` once, before the first `new ScriptRunner()`; it returns
`false` once the engine exists. A null name passed to a setter is reported by the next `Run` as an
invalid variable name, not thrown.

The `SetLimit*` setters only lower the engine's defaults; a larger value is clamped to them.
`SetLimitIntBits` is the cheapest bound on the time one big-int operation can take: business data
rarely needs more than 64K bits.

Configuration persists across `Run` calls until `Reset()`. Prefer `ScriptEngine` from C# — the
facade exists for such boundaries.

---

## Appendix: what scripts can and cannot do

The language is a Python dialect with modern (3.14-era) syntax: f-strings, `match/case`, walrus,
starred unpacking, decorators, dataclasses/record classes (single inheritance). Architecturally
excluded: `async/await`, `yield` generators, full class machinery, metaclasses, `eval/exec`,
arbitrary imports, any filesystem/network/process access. What differs from CPython is in
[deviations.md](deviations.md); every builtin, type method and module member that exists is listed in
[modules.md](modules.md), read off a running engine.
