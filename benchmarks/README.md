# Benchmarks

PyLite against IronPython 3.4.2 on the same scripts, in the same .NET Framework process. The numbers and
their reading are in [`docs/performance.md`](../docs/performance.md); this folder holds what they come from.

## What runs

The workloads are the scripts in [`tools/PyLite.PerfBench/Program.Corpus.cs`](../tools/PyLite.PerfBench/Program.Corpus.cs):
integer arithmetic, function calls, a dictionary, string building, a list comprehension, `sorted` with a
key, money in `decimal`, JSON orders, a delimited text report, a regex over log lines, datetime arithmetic
and a CSV round trip. They are written in 3.4-era syntax so that IronPython runs them too. Two startup rows
come first: the first run in a fresh process (one sample, JIT included) and a new engine in a warm process.

Each pass runs in a fresh process. A benchmark is warmed up 3 times and measured 15 times; a pass records
every sample, the median, and the bytes the process allocated per run, for both engines.

## How to run it

```
powershell -ExecutionPolicy Bypass -File benchmarks/run.ps1 -Passes 3
```

This builds `tools/PyLite.PerfBench` in Release and writes `results/<version>/pass-1.json` ... and
`results/<version>/summary.md`: for every row the range of the medians across the passes, the fastest and
slowest single sample, the allocations, and a verdict that compares the medians pass by pass. Rows where
PyLite is slower stay in the table.

Give the machine nothing else to do while it runs. A single pass on a shared machine moves individual rows
by up to a factor of two between runs of the same code; that is why there are several passes and ranges
rather than one number, and why the ratios matter more than the milliseconds.

## Results

| version | where |
|---|---|
| 1.1.0 | [results/1.1.0/summary.md](results/1.1.0/summary.md): three passes on the release commit, 2026-10-29 |
