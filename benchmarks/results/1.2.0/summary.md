# Benchmarks: PyLite 1.2.0 and IronPython 3.4.2

3 passes of `tools/PyLite.PerfBench --full`, each benchmark warmed up 3 times and measured 15 times per pass. The samples are in the `pass-*.json` files next to this page.

| | |
|---|---|
| PyLite | 1.2.0, commit `e5b2908` |
| IronPython | 3.4.2 (NuGet) |
| OS | Windows Server 2022 Datacenter Evaluation, build 20348.5870 |
| CPU | Intel(R) Xeon(R) CPU E5-2650 v2 @ 2.60GHz, 8 logical processors |
| .NET | .NET Framework 4.8.4811.0 |
| passes | 2026-11-15T00:41:04Z, 2026-11-15T00:43:56Z, 2026-11-15T00:46:43Z |

Each cell gives the range across the passes. The median is the middle sample of a pass; the spread
is the fastest and the slowest single sample over all passes; allocations are the bytes the whole
process allocated per run. The verdict compares the medians pass by pass. Rows where PyLite is slower
stay in the table.

| benchmark | PyLite median | PyLite spread | PyLite allocated | IronPython median | IronPython spread | IronPython allocated | verdict |
|---|---:|---:|---:|---:|---:|---:|---|
| `cold_start` | 391–959 ms | 391–959 ms | 0.40 MB | 3024–4519 ms | 3024–4519 ms | 9.5 MB | ×4.7–7.7 faster |
| `engine_spinup` | 0.50–0.61 ms | 0.40–3.0 ms | 0.03 MB | 90.5–114 ms | 84.2–190 ms | 6.8 MB | ×166–228 faster |
| `micro_arith_loop` | 547–556 ms | 530–611 ms | 122 MB | 985–999 ms | 954–1250 ms | 245 MB | ×1.77–1.83 faster |
| `micro_func_calls` | 254–264 ms | 241–280 ms | 30.4 MB | 352–368 ms | 337–513 ms | 22.9 MB | ×1.33–1.45 faster |
| `micro_dict_ops` | 243–250 ms | 216–283 ms | 45.9 MB | 433–469 ms | 396–530 ms | 47.1 MB | ×1.73–1.91 faster |
| `micro_str_join` | 142–152 ms | 95.0–224 ms | 18.3–18.4 MB | 152–166 ms | 137–228 ms | 13.8 MB | ×1.06–1.15 faster |
| `micro_list_comp` | 89.6–101 ms | 66.9–139 ms | 11.6 MB | 175–255 ms | 146–390 ms | 34.0 MB | ×1.9–2.5 faster |
| `micro_sorted_key` | 65.0–72.4 ms | 49.3–94.1 ms | 11.1 MB | 62.8–69.6 ms | 41.4–94.4 ms | 8.7 MB | about equal, direction varies |
| `micro_decimal_money` | 26.9–33.0 ms | 25.2–41.5 ms | 6.7 MB | 2107–2218 ms | 1789–2601 ms | 70.9 MB | ×66–78 faster |
| `macro_json_orders` | 122–137 ms | 96.0–172 ms | 16.9 MB | 2069–2170 ms | 1846–2548 ms | 147 MB | ×15–18 faster |
| `macro_text_report` | 74.9–83.7 ms | 70.0–124 ms | 13.4 MB | 92.0–95.8 ms | 88.6–136 ms | 19.4 MB | ×1.1–1.26 faster |
| `macro_regex_log` | 41.2–45.9 ms | 34.7–76.2 ms | 7.4 MB | 42.4–50.1 ms | 39.1–56.0 ms | 12.5 MB | about equal, direction varies |
| `macro_datetime_calc` | 57.1–59.8 ms | 53.6–70.9 ms | 8.5 MB | 49.9–54.1 ms | 47.8–66.0 ms | 4.0 MB | ×1.06–1.18 slower |
| `macro_csv_roundtrip` | 45.5–50.8 ms | 40.3–89.3 ms | 16.4 MB | 198–204 ms | 186–228 ms | 20.5 MB | ×4.0–4.5 faster |

`cold_start` is one sample per pass: the first run in a fresh process, JIT included.
